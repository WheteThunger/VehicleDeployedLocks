using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vehicle Deployed Locks", "WhiteThunder", "1.4.0")]
    [Description("Allows players to deploy code locks and key locks to vehicles.")]
    internal class VehicleDeployedLocks : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private Plugin Clans, Friends;

        private const string Permission_CodeLock_Prefix = "vehicledeployedlocks.codelock";
        private const string Permission_KeyLock_Prefix = "vehicledeployedlocks.keylock";
        private const string Permission_MasterKey = "vehicledeployedlocks.masterkey";

        private const string Prefab_CodeLock_DeployedEffect = "assets/prefabs/locks/keypad/effects/lock-code-deploy.prefab";
        private const string Prefab_CodeLock_DeniedEffect = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
        private const string Prefab_CodeLock_UnlockEffect = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";

        private const float MaxDeployDistance = 3;

        private Configuration _pluginConfig;

        private CooldownManager _craftCodeLockCooldowns;
        private CooldownManager _craftKeyLockCooldowns;

        private readonly VehicleInfoManager _vehicleInfoManager = new VehicleInfoManager();

        private enum PayType { Item, Resources, Free }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(Permission_MasterKey, this);
            permission.RegisterPermission(LockInfo_CodeLock.PermissionFree, this);
            permission.RegisterPermission(LockInfo_CodeLock.PermissionAllVehicles, this);
            permission.RegisterPermission(LockInfo_KeyLock.PermissionFree, this);
            permission.RegisterPermission(LockInfo_KeyLock.PermissionAllVehicles, this);

            _craftKeyLockCooldowns = new CooldownManager(_pluginConfig.CraftCooldownSeconds);
            _craftCodeLockCooldowns = new CooldownManager(_pluginConfig.CraftCooldownSeconds);
        }

        private void OnServerInitialized()
        {
            _vehicleInfoManager.OnServerInitialized(this);
        }

        private bool? CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            // Don't lock taxi modules
            var carSeat = entity as ModularCarSeat;
            if (carSeat != null && !carSeat.associatedSeatingModule.DoorsAreLockable)
                return null;

            return CanPlayerInteractWithParentVehicle(player, entity);
        }

        private bool? CanLootEntity(BasePlayer player, StorageContainer container)
        {
            // Don't lock taxi module shop fronts
            if (container is ModularVehicleShopFront)
                return null;

            return CanPlayerInteractWithParentVehicle(player, container);
        }

        private bool? CanLootEntity(BasePlayer player, ContainerIOEntity container) =>
            CanPlayerInteractWithParentVehicle(player, container);

        private bool? CanLootEntity(BasePlayer player, RidableHorse horse) =>
            CanPlayerInteractWithVehicle(player, horse);

        private bool? CanLootEntity(BasePlayer player, ModularCarGarage carLift)
        {
            if (carLift == null
                || _pluginConfig.ModularCarSettings.AllowEditingWhileLockedOut
                || !carLift.PlatformIsOccupied)
                return null;

            return CanPlayerInteractWithVehicle(player, carLift.carOccupant);
        }

        private bool? OnHorseLead(RidableHorse horse, BasePlayer player) =>
            CanPlayerInteractWithVehicle(player, horse);

        private bool? OnHotAirBalloonToggle(HotAirBalloon hab, BasePlayer player) =>
            CanPlayerInteractWithVehicle(player, hab);

        private bool? OnSwitchToggle(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (electricSwitch == null)
                return null;

            var autoTurret = electricSwitch.GetParentEntity() as AutoTurret;
            if (autoTurret != null)
                return CanPlayerInteractWithParentVehicle(player, autoTurret);

            return null;
        }

        private bool? OnTurretAuthorize(AutoTurret entity, BasePlayer player) =>
            CanPlayerInteractWithParentVehicle(player, entity);

        private bool? OnTurretTarget(AutoTurret autoTurret, BasePlayer player)
        {
            if (autoTurret == null || player == null)
                return null;

            var turretParent = autoTurret.GetParentEntity();
            var vehicle = turretParent as BaseVehicle ?? (turretParent as BaseVehicleModule)?.Vehicle;
            if (vehicle == null)
                return null;

            var baseLock = GetVehicleLock(vehicle);
            if (baseLock == null)
                return null;

            if (CanPlayerBypassLock(player, baseLock))
                return false;

            return null;
        }

        private bool? CanSwapToSeat(BasePlayer player, ModularCarSeat carSeat)
        {
            // Don't lock taxi modules
            if (!carSeat.associatedSeatingModule.DoorsAreLockable)
                return null;

            return CanPlayerInteractWithParentVehicle(player, carSeat, provideFeedback: false);
        }

        // Handle the case where a cockpit is removed but the car remains
        // If a lock is present, either move the lock to another cockpit or destroy it
        private void OnEntityKill(VehicleModuleSeating seatingModule)
        {
            if (seatingModule == null || !seatingModule.HasADriverSeat())
                return;

            var car = seatingModule.Vehicle as ModularCar;
            if (car == null)
                return;

            var baseLock = seatingModule.GetComponentInChildren<BaseLock>();
            if (baseLock == null)
                return;

            baseLock.SetParent(null);
            NextTick(() =>
            {
                if (car == null)
                {
                    baseLock.Kill();
                }
                else
                {
                    var driverModule = FindFirstDriverModule(car);
                    if (driverModule == null)
                        baseLock.Kill();
                    else
                        baseLock.SetParent(driverModule);
                }
            });
        }

        // Allow players to deploy locks directly without any commands.
        private bool? CanDeployItem(BasePlayer basePlayer, Deployer deployer, uint entityId)
        {
            if (basePlayer == null || deployer == null)
                return null;

            var deployable = deployer.GetDeployable();
            if (deployable == null)
                return null;

            var activeItem = basePlayer.GetActiveItem();
            if (activeItem == null)
                return null;

            var itemid = activeItem.info.itemid;

            LockInfo lockInfo;
            if (itemid == LockInfo_CodeLock.ItemId)
                lockInfo = LockInfo_CodeLock;
            else if (itemid == LockInfo_KeyLock.ItemId)
                lockInfo = LockInfo_KeyLock;
            else
                return null;

            var vehicle = GetVehicleFromEntity(BaseNetworkable.serverEntities.Find(entityId) as BaseEntity, basePlayer);
            if (vehicle == null)
                return null;

            var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(vehicle);
            if (vehicleInfo == null)
                return null;

            var player = basePlayer.IPlayer;

            // Trick to make sure the replies are in chat instead of console.
            player.LastCommand = CommandType.Chat;

            PayType payType;
            if (!VerifyCanDeploy(player, vehicle, vehicleInfo, lockInfo, out payType)
                || !VerifyDeployDistance(player, vehicle))
                return false;

            DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, basePlayer, payType);
            return false;
        }

        #endregion

        #region API

        private CodeLock API_DeployCodeLock(BaseEntity vehicle, BasePlayer player, bool isFree = true) =>
            DeployLockForAPI(vehicle, player, LockInfo_CodeLock, isFree) as CodeLock;

        private KeyLock API_DeployKeyLock(BaseEntity vehicle, BasePlayer player, bool isFree = true) =>
            DeployLockForAPI(vehicle, player, LockInfo_KeyLock, isFree) as KeyLock;

        private bool API_CanPlayerDeployCodeLock(BasePlayer player, BaseEntity vehicle) =>
            CanPlayerDeployLockForAPI(player, vehicle, LockInfo_CodeLock);

        private bool API_CanPlayerDeployKeyLock(BasePlayer player, BaseEntity vehicle) =>
            CanPlayerDeployLockForAPI(player, vehicle, LockInfo_KeyLock);

        private bool API_CanAccessVehicle(BasePlayer player, BaseEntity vehicle, bool provideFeedback = true) =>
            CanPlayerInteractWithVehicle(player, vehicle, provideFeedback) == null;

        private void API_RegisterCustomVehicleType(string vehicleType, Vector3 lockPosition, Quaternion lockRotation, string parentBone, Func<BaseEntity, BaseEntity> determineLockParent)
        {
            _vehicleInfoManager.RegisterCustomVehicleType(this, new VehicleInfo()
            {
                VehicleType = vehicleType,
                LockPosition = lockPosition,
                LockRotation = lockRotation,
                ParentBone = parentBone,
                DetermineLockParent = determineLockParent,
            });
        }

        #endregion

        #region Commands

        [Command("vehiclecodelock", "vcodelock", "vlock")]
        private void CodeLockCommand(IPlayer player, string cmd, string[] args) =>
            LockCommand(player, LockInfo_CodeLock);

        [Command("vehiclekeylock", "vkeylock")]
        private void KeyLockCommand(IPlayer player, string cmd, string[] args) =>
            LockCommand(player, LockInfo_KeyLock);

        private void LockCommand(IPlayer player, LockInfo lockInfo)
        {
            if (player.IsServer)
                return;

            var basePlayer = player.Object as BasePlayer;
            VehicleInfo vehicleInfo;
            var vehicle = GetVehicleFromEntity(GetLookEntity(basePlayer, MaxDeployDistance), basePlayer);
            if (vehicle == null || !TryGet(_vehicleInfoManager.GetVehicleInfo(vehicle), out vehicleInfo))
            {
                ReplyToPlayer(player, Lang.DeployErrorNoVehicleFound);
                return;
            }

            PayType payType;
            if (!VerifyCanDeploy(player, vehicle, vehicleInfo, lockInfo, out payType))
                return;

            DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, basePlayer, payType);
        }

        #endregion

        #region Helper Methods - General

        private static bool TryGet<T>(T input, out T output)
        {
            output = input;
            return input != null;
        }

        private static bool HasPermissionAny(IPlayer player, params string[] permissionNames)
        {
            foreach (var perm in permissionNames)
                if (player.HasPermission(perm))
                    return true;

            return false;
        }

        private static BaseLock GetVehicleLock(BaseEntity vehicle) =>
            vehicle.GetSlot(BaseEntity.Slot.Lock) as BaseLock;

        #endregion

        #region Helper Methods - Lock Authorization

        private bool IsPlayerAuthorizedToCodeLock(ulong userID, CodeLock codeLock) =>
            codeLock.whitelistPlayers.Contains(userID)
            || codeLock.guestPlayers.Contains(userID);

        private bool IsPlayerAuthorizedToLock(BasePlayer player, BaseLock baseLock) =>
            (baseLock as KeyLock)?.HasLockPermission(player) ?? IsPlayerAuthorizedToCodeLock(player.userID, baseLock as CodeLock);

        private bool PlayerHasMasterKeyForLock(BasePlayer player, BaseLock baseLock)
        {
            return permission.UserHasPermission(player.UserIDString, Permission_MasterKey);
        }

        private bool IsLockSharedWithPlayer(BasePlayer player, BaseLock baseLock)
        {
            var ownerID = baseLock.OwnerID;
            if (ownerID == 0 || ownerID == player.userID)
                return false;

            // In case the owner was locked out for some reason
            var codeLock = baseLock as CodeLock;
            if (codeLock != null && !IsPlayerAuthorizedToCodeLock(ownerID, codeLock))
                return false;

            var sharingSettings = _pluginConfig.SharingSettings;
            if (sharingSettings.Team && player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(ownerID))
                    return true;
            }

            if (sharingSettings.Friends && Friends != null)
            {
                var friendsResult = Friends.Call("HasFriend", baseLock.OwnerID, player.userID);
                if (friendsResult is bool && (bool)friendsResult)
                    return true;
            }

            if ((sharingSettings.Clan || sharingSettings.ClanOrAlly) && Clans != null)
            {
                var clanMethodName = sharingSettings.ClanOrAlly ? "IsMemberOrAlly" : "IsClanMember";
                var clanResult = Clans.Call(clanMethodName, ownerID.ToString(), player.UserIDString);
                if (clanResult is bool && (bool)clanResult)
                    return true;
            }

            return false;
        }

        private bool CanPlayerBypassLock(BasePlayer player, BaseLock baseLock)
        {
            object hookResult = Interface.CallHook("CanUseLockedEntity", player, baseLock);
            if (hookResult is bool)
                return (bool)hookResult;

            return IsPlayerAuthorizedToLock(player, baseLock)
                || IsLockSharedWithPlayer(player, baseLock)
                || PlayerHasMasterKeyForLock(player, baseLock);
        }

        private bool? CanPlayerInteractWithVehicle(BasePlayer player, BaseEntity vehicle, bool provideFeedback = true)
        {
            if (player == null || vehicle == null)
                return null;

            var baseLock = GetVehicleLock(vehicle);
            if (baseLock == null || !baseLock.IsLocked())
                return null;

            if (!CanPlayerBypassLock(player, baseLock))
            {
                if (provideFeedback)
                {
                    Effect.server.Run(Prefab_CodeLock_DeniedEffect, baseLock, 0, Vector3.zero, Vector3.forward);
                    ChatMessage(player, Lang.GenericErrorVehicleLocked);
                }

                return false;
            }

            if (provideFeedback && baseLock.IsLocked())
                Effect.server.Run(Prefab_CodeLock_UnlockEffect, baseLock, 0, Vector3.zero, Vector3.forward);

            return null;
        }

        private BaseEntity GetParentVehicle(BaseEntity entity)
        {
            var parent = entity.GetParentEntity();
            if (parent == null)
                return null;

            if (parent is HotAirBalloon || parent is BaseVehicle)
                return parent;

            var parentModule = parent as BaseVehicleModule;
            if (parentModule != null)
                return parentModule.Vehicle;

            return _vehicleInfoManager.GetCustomVehicleParent(entity);
        }

        private bool? CanPlayerInteractWithParentVehicle(BasePlayer player, BaseEntity entity, bool provideFeedback = true) =>
            CanPlayerInteractWithVehicle(player, GetParentVehicle(entity), provideFeedback);

        #endregion

        #region Helper Methods - Deploying Locks

        private static bool DeployWasBlocked(BaseEntity vehicle, BasePlayer player, LockInfo lockInfo)
        {
            object hookResult = Interface.CallHook(lockInfo.PreHookName, vehicle, player);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static BaseEntity GetLookEntity(BasePlayer player, float maxDistance)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return null;

            return hit.GetEntity();
        }

        private static bool IsDead(BaseEntity entity) =>
            (entity as BaseCombatEntity)?.IsDead() ?? false;

        private static VehicleModuleSeating FindFirstDriverModule(ModularCar car)
        {
            for (int socketIndex = 0; socketIndex < car.TotalSockets; socketIndex++)
            {
                BaseVehicleModule module;
                if (car.TryGetModuleAt(socketIndex, out module))
                {
                    var seatingModule = module as VehicleModuleSeating;
                    if (seatingModule != null && seatingModule.HasADriverSeat())
                        return seatingModule;
                }
            }
            return null;
        }

        private static bool CanCarHaveLock(ModularCar car) =>
            FindFirstDriverModule(car) != null;

        private static bool CanVehicleHaveALock(BaseEntity vehicle)
        {
            // Only modular cars have restrictions
            var car = vehicle as ModularCar;
            return car == null || CanCarHaveLock(car);
        }

        private static Item GetPlayerLockItem(BasePlayer player, LockInfo lockInfo) =>
            player.inventory.FindItemID(lockInfo.ItemId);

        private static PayType DeterminePayType(IPlayer player, LockInfo lockInfo)
        {
            if (player.HasPermission(lockInfo.PermissionFree))
                return PayType.Free;

            return GetPlayerLockItem(player.Object as BasePlayer, lockInfo) != null
                ? PayType.Item
                : PayType.Resources;
        }

        private static bool CanPlayerAffordLock(BasePlayer player, LockInfo lockInfo, out PayType payType)
        {
            payType = DeterminePayType(player.IPlayer, lockInfo);
            if (payType != PayType.Resources)
                return true;

            return player.inventory.crafting.CanCraft(lockInfo.ItemDefinition, 1);
        }

        private static RidableHorse GetClosestHorse(HitchTrough hitchTrough, BasePlayer player)
        {
            var closestDistance = 1000f;
            RidableHorse closestHorse = null;

            for (var i = 0; i < hitchTrough.hitchSpots.Length; i++)
            {
                var hitchSpot = hitchTrough.hitchSpots[i];
                if (!hitchSpot.IsOccupied())
                    continue;

                var distance = Vector3.Distance(player.transform.position, hitchSpot.spot.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestHorse = hitchSpot.horse.Get(serverside: true) as RidableHorse;
                }
            }

            return closestHorse;
        }

        private static BaseEntity GetVehicleFromEntity(BaseEntity entity, BasePlayer basePlayer)
        {
            if (entity == null)
                return null;

            var module = entity as BaseVehicleModule;
            if (module != null)
                return module.Vehicle;

            var carLift = entity as ModularCarGarage;
            if (!ReferenceEquals(carLift, null))
                return carLift.carOccupant;

            var hitchTrough = entity as HitchTrough;
            if (!ReferenceEquals(hitchTrough, null))
                return GetClosestHorse(hitchTrough, basePlayer);

            return entity;
        }

        private bool AllowNoOwner(BaseEntity vehicle) =>
            _pluginConfig.AllowIfNoOwner
            || vehicle.OwnerID != 0;

        private bool AllowDifferentOwner(IPlayer player, BaseEntity vehicle) =>
            _pluginConfig.AllowIfDifferentOwner
            || vehicle.OwnerID == 0
            || vehicle.OwnerID.ToString() == player.Id;

        private void MaybeChargePlayerForLock(BasePlayer player, LockInfo lockInfo, PayType payType)
        {
            if (payType == PayType.Free)
                return;

            if (payType == PayType.Item)
            {
                // Prefer taking the item they are holding in case they are deploying directly.
                var heldItem = player.GetActiveItem();
                if (heldItem != null && heldItem.info.itemid == lockInfo.ItemId)
                    heldItem.UseItem(1);
                else
                    player.inventory.Take(null, lockInfo.ItemId, 1);

                player.Command("note.inv", lockInfo.ItemId, -1);
                return;
            }

            foreach (var ingredient in lockInfo.Blueprint.ingredients)
            {
                player.inventory.Take(null, ingredient.itemid, (int)ingredient.amount);
                player.Command("note.inv", ingredient.itemid, -ingredient.amount);
                GetCooldownManager(lockInfo).UpdateLastUsedForPlayer(player.UserIDString);
            }
        }

        private BaseLock DeployLock(BaseEntity vehicle, VehicleInfo vehicleInfo, LockInfo lockInfo, ulong ownerID = 0)
        {
            var parentToEntity = vehicleInfo.DetermineLockParent(vehicle);
            if (parentToEntity == null)
                return null;

            var baseLock = GameManager.server.CreateEntity(lockInfo.Prefab, vehicleInfo.LockPosition, vehicleInfo.LockRotation) as BaseLock;
            if (baseLock == null)
                return null;

            var keyLock = baseLock as KeyLock;
            if (keyLock != null)
                keyLock.keyCode = UnityEngine.Random.Range(1, 100000);

            if (ownerID != 0)
                baseLock.OwnerID = ownerID;

            baseLock.SetParent(parentToEntity, vehicleInfo.ParentBone);
            baseLock.Spawn();
            vehicle.SetSlot(BaseEntity.Slot.Lock, baseLock);

            // Auto lock key locks to be consistent with vanilla.
            if (ownerID != 0 && keyLock != null)
                keyLock.SetFlag(BaseEntity.Flags.Locked, true);

            Effect.server.Run(Prefab_CodeLock_DeployedEffect, baseLock.transform.position);
            Interface.CallHook("OnVehicleLockDeployed", vehicle, baseLock);

            return baseLock;
        }

        private BaseLock DeployLockForPlayer(BaseEntity vehicle, VehicleInfo vehicleInfo, LockInfo lockInfo, BasePlayer player, PayType payType)
        {
            var originalVehicleOwnerId = vehicle.OwnerID;

            // Temporarily set the player as the owner of the vehicle, for compatibility with AutoCodeLock (OnItemDeployed).
            vehicle.OwnerID = player.userID;

            var baseLock = DeployLock(vehicle, vehicleInfo, lockInfo, player.userID);
            if (baseLock == null)
            {
                vehicle.OwnerID = originalVehicleOwnerId;
                return null;
            }

            // Allow other plugins to detect the code lock being deployed (e.g., to auto lock).
            var lockItem = GetPlayerLockItem(player, lockInfo);
            if (lockItem != null)
            {
                Interface.CallHook("OnItemDeployed", lockItem.GetHeldEntity(), vehicle, baseLock);
            }
            else
            {
                // Temporarily increase the player inventory capacity to ensure there is enough space.
                player.inventory.containerMain.capacity++;
                var temporaryLockItem = ItemManager.CreateByItemID(lockInfo.ItemId);
                if (player.inventory.GiveItem(temporaryLockItem))
                {
                    Interface.CallHook("OnItemDeployed", temporaryLockItem.GetHeldEntity(), vehicle, baseLock);
                    temporaryLockItem.RemoveFromContainer();
                }
                temporaryLockItem.Remove();
                player.inventory.containerMain.capacity--;
            }

            // Revert the vehicle owner to the original, after OnItemDeployed is called.
            vehicle.OwnerID = originalVehicleOwnerId;

            MaybeChargePlayerForLock(player, lockInfo, payType);
            return baseLock;
        }

        private BaseLock DeployLockForAPI(BaseEntity vehicle, BasePlayer player, LockInfo lockInfo, bool isFree)
        {
            if (vehicle == null || IsDead(vehicle))
                return null;

            var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(vehicle);
            if (vehicleInfo == null
                || GetVehicleLock(vehicle) != null
                || !CanVehicleHaveALock(vehicle))
                return null;

            PayType payType;
            if (isFree)
                payType = PayType.Free;
            else if (!VerifyPlayerCanDeployLock(player.IPlayer, lockInfo, out payType))
                return null;

            if (DeployWasBlocked(vehicle, player, lockInfo))
                return null;

            return player != null
                ? DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, player, payType)
                : DeployLock(vehicle, vehicleInfo, lockInfo);
        }

        private bool CanPlayerDeployLockForAPI(BasePlayer player, BaseEntity vehicle, LockInfo lockInfo)
        {
            PayType payType;
            return vehicle != null
                && !IsDead(vehicle)
                && _vehicleInfoManager.GetVehicleInfo(vehicle) != null
                && AllowNoOwner(vehicle)
                && AllowDifferentOwner(player.IPlayer, vehicle)
                && player.CanBuild()
                && GetVehicleLock(vehicle) == null
                && CanVehicleHaveALock(vehicle)
                && CanPlayerAffordLock(player, lockInfo, out payType)
                && !DeployWasBlocked(vehicle, player, lockInfo);
        }

        #endregion

        #region Helper Methods - Command Checks

        private bool VerifyDeployDistance(IPlayer player, BaseEntity vehicle)
        {
            if (vehicle.Distance(player.Object as BasePlayer) <= MaxDeployDistance)
                return true;

            ReplyToPlayer(player, Lang.DeployErrorDistance);
            return false;
        }

        private bool VerifyPermissionToVehicleAndLockType(IPlayer player, VehicleInfo vehicleInfo, LockInfo lockInfo)
        {
            var vehiclePerm = lockInfo == LockInfo_CodeLock
                ? vehicleInfo.CodeLockPermission
                : vehicleInfo.KeyLockPermission;

            if (vehiclePerm != null && HasPermissionAny(player, lockInfo.PermissionAllVehicles, vehiclePerm))
                return true;

            ReplyToPlayer(player, Lang.GenericErrorNoPermission);
            return false;
        }

        private bool VerifyVehicleIsNotDead(IPlayer player, BaseEntity vehicle)
        {
            if (!IsDead(vehicle))
                return true;

            ReplyToPlayer(player, Lang.DeployErrorVehicleDead);
            return false;
        }

        private bool VerifyNoOwnershipRestriction(IPlayer player, BaseEntity vehicle)
        {
            if (!AllowNoOwner(vehicle))
            {
                ReplyToPlayer(player, Lang.DeployErrorNoOwner);
                return false;
            }

            if (!AllowDifferentOwner(player, vehicle))
            {
                ReplyToPlayer(player, Lang.DeployErrorDifferentOwner);
                return false;
            }

            return true;
        }

        private bool VerifyCanBuild(IPlayer player, BaseEntity vehicle)
        {
            var basePlayer = player.Object as BasePlayer;

            if (vehicle.OwnerID == 0 && _pluginConfig.RequireTCIfNoOwner)
            {
                if (!basePlayer.IsBuildingAuthed() || !basePlayer.IsBuildingAuthed(vehicle.WorldSpaceBounds()))
                {
                    ReplyToPlayer(player, Lang.DeployErrorNoOwnerRequiresTC);
                    return false;
                }
            }
            else if (basePlayer.IsBuildingBlocked() || basePlayer.IsBuildingBlocked(vehicle.WorldSpaceBounds()))
            {
                ReplyToPlayer(player, Lang.GenericErrorBuildingBlocked);
                return false;
            }

            return true;
        }

        private bool VerifyVehicleHasNoLock(IPlayer player, BaseEntity vehicle)
        {
            if (GetVehicleLock(vehicle) == null)
                return true;

            ReplyToPlayer(player, Lang.DeployErrorHasLock);
            return false;
        }

        private bool VerifyVehicleCanHaveALock(IPlayer player, BaseEntity vehicle)
        {
            if (CanVehicleHaveALock(vehicle))
                return true;

            ReplyToPlayer(player, Lang.DeployErrorModularCarNoCockpit);
            return false;
        }

        private bool VerifyPlayerCanAffordLock(BasePlayer player, LockInfo lockInfo, out PayType payType)
        {
            if (CanPlayerAffordLock(player, lockInfo, out payType))
                return true;

            ChatMessage(player, Lang.DeployErrorInsufficientResources, lockInfo.ItemDefinition.displayName.translated);
            return false;
        }

        private bool VerifyOffCooldown(IPlayer player, LockInfo lockInfo, PayType payType)
        {
            if (payType != PayType.Resources)
                return true;

            var secondsRemaining = GetCooldownManager(lockInfo).GetSecondsRemaining(player.Id);
            if (secondsRemaining <= 0)
                return true;

            ChatMessage(player.Object as BasePlayer, Lang.GenericErrorCooldown, Math.Ceiling(secondsRemaining));
            return false;
        }

        private bool VerifyPlayerCanDeployLock(IPlayer player, LockInfo lockInfo, out PayType payType) =>
            VerifyPlayerCanAffordLock(player.Object as BasePlayer, lockInfo, out payType) && VerifyOffCooldown(player, lockInfo, payType);

        private bool VerifyNotMounted(IPlayer player, BaseEntity vehicle, VehicleInfo vehicleInfo)
        {
            if (!vehicleInfo.IsMounted(vehicle))
                return true;

            ReplyToPlayer(player, Lang.DeployErrorMounted);
            return false;
        }

        private bool VerifyCanDeploy(IPlayer player, BaseEntity vehicle, VehicleInfo vehicleInfo, LockInfo lockInfo, out PayType payType)
        {
            var basePlayer = player.Object as BasePlayer;
            payType = PayType.Item;

            return VerifyPermissionToVehicleAndLockType(player, vehicleInfo, lockInfo)
                && VerifyVehicleIsNotDead(player, vehicle)
                && VerifyNoOwnershipRestriction(player, vehicle)
                && VerifyCanBuild(player, vehicle)
                && VerifyVehicleHasNoLock(player, vehicle)
                && VerifyVehicleCanHaveALock(player, vehicle)
                && VerifyPlayerCanDeployLock(player, lockInfo, out payType)
                && VerifyNotMounted(player, vehicle, vehicleInfo)
                && !DeployWasBlocked(vehicle, basePlayer, lockInfo);
        }

        #endregion

        #region Vehicle Info

        private class VehicleInfo
        {
            public string VehicleType;
            public string[] PrefabPaths;
            public Vector3 LockPosition;
            public Quaternion LockRotation;
            public string ParentBone;

            public string CodeLockPermission { get; private set; }
            public string KeyLockPermission { get; private set; }
            public uint[] PrefabIds { get; private set; }

            public Func<BaseEntity, BaseEntity> DetermineLockParent = (entity) => entity;

            public void OnServerInitialized(VehicleDeployedLocks pluginInstance)
            {
                CodeLockPermission = $"{Permission_CodeLock_Prefix}.{VehicleType}";
                KeyLockPermission = $"{Permission_KeyLock_Prefix}.{VehicleType}";

                pluginInstance.permission.RegisterPermission(CodeLockPermission, pluginInstance);
                pluginInstance.permission.RegisterPermission(KeyLockPermission, pluginInstance);

                // Custom vehicles aren't currently allowed to specify prefabs since they reuse existing prefabs.
                if (PrefabPaths != null)
                {
                    var prefabIds = new List<uint>();
                    foreach (var prefabName in PrefabPaths)
                    {
                        var prefabId = StringPool.Get(prefabName);
                        if (prefabId != 0)
                            prefabIds.Add(prefabId);
                        else
                            pluginInstance.LogError($"Invalid prefab. Please alert the plugin maintainer -- {prefabName}");
                    }
                    PrefabIds = prefabIds.ToArray();
                }
            }

            // In the future, custom vehicles may be able to pass in a method to override this.
            public bool IsMounted(BaseEntity entity)
            {
                var vehicle = entity as BaseVehicle;
                if (vehicle != null)
                    return vehicle.AnyMounted();

                var mountable = entity as BaseMountable;
                if (mountable != null)
                    return mountable.IsMounted();

                return false;
            }
        }

        private class VehicleInfoManager
        {
            private readonly Dictionary<uint, VehicleInfo> _prefabIdToVehicleInfo = new Dictionary<uint, VehicleInfo>();
            private readonly Dictionary<string, VehicleInfo> _customVehicleTypes = new Dictionary<string, VehicleInfo>();

            public void OnServerInitialized(VehicleDeployedLocks pluginInstance)
            {
                var allVehicles = new VehicleInfo[]
                {
                    new VehicleInfo
                    {
                        VehicleType = "chinook",
                        PrefabPaths = new string[] { "assets/prefabs/npc/ch47/ch47.entity.prefab" },
                        LockPosition = new Vector3(-1.175f, 2, 6.5f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "duosub",
                        PrefabPaths = new string[] { "assets/content/vehicles/submarine/submarineduo.entity.prefab" },
                        LockPosition = new Vector3(-0.455f, 1.29f, 0.75f),
                        LockRotation = Quaternion.Euler(0, 180, 10),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "hotairballoon",
                        PrefabPaths = new string[] { "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab" },
                        LockPosition = new Vector3(1.45f, 0.9f, 0),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "kayak",
                        PrefabPaths = new string[] { "assets/content/vehicles/boats/kayak/kayak.prefab" },
                        LockPosition = new Vector3(-0.43f, 0.2f, 0.2f),
                        LockRotation = Quaternion.Euler(0, 90, 90),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "magnetcrane",
                        PrefabPaths = new string[] { "assets/content/vehicles/crane_magnet/magnetcrane.entity.prefab" },
                        LockPosition = new Vector3(-1.735f, -1.445f, 0.79f),
                        LockRotation = Quaternion.Euler(0, 0, 90),
                        ParentBone = "Top",
                    },
                    new VehicleInfo
                    {
                        VehicleType = "minicopter",
                        PrefabPaths = new string[] { "assets/content/vehicles/minicopter/minicopter.entity.prefab" },
                        LockPosition = new Vector3(-0.15f, 0.7f, -0.1f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "modularcar",
                        PrefabPaths = new string[]
                        {
                            "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab",
                            "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab",
                            "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab",
                            "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab",
                            "assets/content/vehicles/modularcar/3module_car_spawned.entity.prefab",
                            "assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab",
                        },
                        LockPosition = new Vector3(-0.9f, 0.35f, -0.5f),
                        DetermineLockParent = (vehicle) => FindFirstDriverModule((ModularCar)vehicle),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "rhib",
                        PrefabPaths = new string[] { "assets/content/vehicles/boats/rhib/rhib.prefab" },
                        LockPosition = new Vector3(-0.68f, 2.00f, 0.7f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "ridablehorse",
                        PrefabPaths = new string[] { "assets/rust.ai/nextai/testridablehorse.prefab" },
                        LockPosition = new Vector3(-0.6f, 0.35f, -0.1f),
                        LockRotation = Quaternion.Euler(0, 95, 90),
                        ParentBone = "Horse_RootBone",
                    },
                    new VehicleInfo
                    {
                        VehicleType = "rowboat",
                        PrefabPaths = new string[] { "assets/content/vehicles/boats/rowboat/rowboat.prefab" },
                        LockPosition = new Vector3(-0.83f, 0.51f, -0.57f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "scraptransport",
                        PrefabPaths = new string[] { "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab" },
                        LockPosition = new Vector3(-1.25f, 1.22f, 1.99f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "sedan",
                        PrefabPaths = new string[] { "assets/content/vehicles/sedan_a/sedantest.entity.prefab" },
                        LockPosition = new Vector3(-1.09f, 0.79f, 0.5f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "solosub",
                        PrefabPaths = new string[] { "assets/content/vehicles/submarine/submarinesolo.entity.prefab" },
                        LockPosition = new Vector3(0f, 1.85f, 0f),
                        LockRotation = Quaternion.Euler(0, 90, 90),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "workcart",
                        PrefabPaths = new string[] { "assets/content/vehicles/workcart/workcart.entity.prefab" },
                        LockPosition = new Vector3(-0.2f, 2.35f, 2.7f),
                    },
                };

                foreach (var vehicleInfo in allVehicles)
                {
                    vehicleInfo.OnServerInitialized(pluginInstance);
                    foreach (var prefabId in vehicleInfo.PrefabIds)
                    {
                        _prefabIdToVehicleInfo[prefabId] = vehicleInfo;
                    }
                }
            }

            public void RegisterCustomVehicleType(VehicleDeployedLocks pluginInstance, VehicleInfo vehicleInfo)
            {
                if (_customVehicleTypes.ContainsKey(vehicleInfo.VehicleType))
                    return;

                vehicleInfo.OnServerInitialized(pluginInstance);
                _customVehicleTypes[vehicleInfo.VehicleType] = vehicleInfo;
            }

            public VehicleInfo GetVehicleInfo(BaseEntity entity)
            {
                VehicleInfo vehicleInfo;
                if (_prefabIdToVehicleInfo.TryGetValue(entity.prefabID, out vehicleInfo))
                    return vehicleInfo;

                foreach (var customVehicleInfo in _customVehicleTypes.Values)
                {
                    if (customVehicleInfo.DetermineLockParent(entity) != null)
                        return customVehicleInfo;
                }

                return null;
            }

            public BaseEntity GetCustomVehicleParent(BaseEntity entity)
            {
                foreach (var vehicleInfo in _customVehicleTypes.Values)
                {
                    var lockParent = vehicleInfo.DetermineLockParent(entity);
                    if (lockParent != null)
                        return lockParent;
                }

                return null;
            }
        }

        #endregion

        #region Lock Info

        private class LockInfo
        {
            public int ItemId;
            public string Prefab;
            public string PermissionAllVehicles;
            public string PermissionFree;
            public string PreHookName;

            public ItemDefinition ItemDefinition =>
                ItemManager.FindItemDefinition(ItemId);

            public ItemBlueprint Blueprint =>
                ItemManager.FindBlueprint(ItemDefinition);
        }

        private readonly LockInfo LockInfo_CodeLock = new LockInfo()
        {
            ItemId = 1159991980,
            Prefab = "assets/prefabs/locks/keypad/lock.code.prefab",
            PermissionAllVehicles = $"{Permission_CodeLock_Prefix}.allvehicles",
            PermissionFree = $"{Permission_CodeLock_Prefix}.free",
            PreHookName = "CanDeployVehicleCodeLock",
        };

        private readonly LockInfo LockInfo_KeyLock = new LockInfo()
        {
            ItemId = -850982208,
            Prefab = "assets/prefabs/locks/keylock/lock.key.prefab",
            PermissionAllVehicles = $"{Permission_KeyLock_Prefix}.allvehicles",
            PermissionFree = $"{Permission_KeyLock_Prefix}.free",
            PreHookName = "CanDeployVehicleKeyLock",
        };

        #endregion

        #region Cooldown Manager

        private class CooldownManager
        {
            private readonly Dictionary<string, float> CooldownMap = new Dictionary<string, float>();
            private readonly float CooldownDuration;

            public CooldownManager(float duration)
            {
                CooldownDuration = duration;
            }

            public void UpdateLastUsedForPlayer(string userID) =>
                CooldownMap[userID] = Time.realtimeSinceStartup;

            public float GetSecondsRemaining(string userID)
            {
                float duration;
                return CooldownMap.TryGetValue(userID, out duration)
                    ? duration + CooldownDuration - Time.realtimeSinceStartup
                    : 0;
            }
        }

        private CooldownManager GetCooldownManager(LockInfo lockInfo) =>
            lockInfo == LockInfo_CodeLock
                ? _craftCodeLockCooldowns
                : _craftKeyLockCooldowns;

        #endregion

        #region Configuration

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty("AllowIfDifferentOwner")]
            public bool AllowIfDifferentOwner = false;

            [JsonProperty("AllowIfNoOwner")]
            public bool AllowIfNoOwner = true;

            [JsonProperty("RequireTCIfNoOwner")]
            public bool RequireTCIfNoOwner = false;

            [JsonProperty("CraftCooldownSeconds")]
            public float CraftCooldownSeconds = 10;

            [JsonProperty("ModularCarSettings")]
            public ModularCarSettings ModularCarSettings = new ModularCarSettings();

            [JsonProperty("DefaultSharingSettings")]
            public SharingSettings SharingSettings = new SharingSettings();
        }

        private class ModularCarSettings
        {
            [JsonProperty("AllowEditingWhileLockedOut")]
            public bool AllowEditingWhileLockedOut = true;
        }

        private class SharingSettings
        {
            [JsonProperty("Clan")]
            public bool Clan = false;

            [JsonProperty("ClanOrAlly")]
            public bool ClanOrAlly = false;

            [JsonProperty("Friends")]
            public bool Friends = false;

            [JsonProperty("Team")]
            public bool Team = false;
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Boilerplate

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_pluginConfig, true);
        }

        #endregion

        #region Localization

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private void ChatMessage(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(string.Format(GetMessage(player.UserIDString, messageName), args));

        private class Lang
        {
            public const string GenericErrorNoPermission = "Generic.Error.NoPermission";
            public const string GenericErrorBuildingBlocked = "Generic.Error.BuildingBlocked";
            public const string GenericErrorCooldown = "Generic.Error.Cooldown";
            public const string GenericErrorVehicleLocked = "Generic.Error.VehicleLocked";
            public const string DeployErrorNoVehicleFound = "Deploy.Error.NoVehicleFound";
            public const string DeployErrorVehicleDead = "Deploy.Error.VehicleDead";
            public const string DeployErrorDifferentOwner = "Deploy.Error.DifferentOwner";
            public const string DeployErrorNoOwner = "Deploy.Error.NoOwner";
            public const string DeployErrorNoOwnerRequiresTC = "Deploy.Error.NoOwner.NoBuildingPrivilege";
            public const string DeployErrorHasLock = "Deploy.Error.HasLock";
            public const string DeployErrorInsufficientResources = "Deploy.Error.InsufficientResources";
            public const string DeployErrorMounted = "Deploy.Error.Mounted";
            public const string DeployErrorModularCarNoCockpit = "Deploy.Error.ModularCar.NoCockpit";
            public const string DeployErrorDistance = "Deploy.Error.Distance";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.GenericErrorNoPermission] = "You don't have permission to do that.",
                [Lang.GenericErrorBuildingBlocked] = "Error: Cannot do that while building blocked.",
                [Lang.GenericErrorCooldown] = "Please wait <color=red>{0}s</color> and try again.",
                [Lang.GenericErrorVehicleLocked] = "That vehicle is locked.",
                [Lang.DeployErrorNoVehicleFound] = "Error: No vehicle found.",
                [Lang.DeployErrorVehicleDead] = "Error: That vehicle is dead.",
                [Lang.DeployErrorDifferentOwner] = "Error: Someone else owns that vehicle.",
                [Lang.DeployErrorNoOwner] = "Error: You do not own that vehicle.",
                [Lang.DeployErrorNoOwnerRequiresTC] = "Error: Locking unowned vehicles requires building privilege.",
                [Lang.DeployErrorHasLock] = "Error: That vehicle already has a lock.",
                [Lang.DeployErrorInsufficientResources] = "Error: Not enough resources to craft a {0}.",
                [Lang.DeployErrorMounted] = "Error: That vehicle is currently occupied.",
                [Lang.DeployErrorModularCarNoCockpit] = "Error: That car needs a cockpit module to receive a lock.",
                [Lang.DeployErrorDistance] = "Error: Too far away."
            }, this, "en");
        }

        #endregion
    }
}
