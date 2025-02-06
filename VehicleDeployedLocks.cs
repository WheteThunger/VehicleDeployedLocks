using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vehicle Deployed Locks", "WhiteThunder", "1.13.3")]
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

        private Configuration _config;

        private CooldownManager _craftCodeLockCooldowns;
        private CooldownManager _craftKeyLockCooldowns;

        private readonly VehicleInfoManager _vehicleInfoManager;
        private readonly LockedVehicleTracker _lockedVehicleTracker;
        private readonly AutoUnlockManager _autoUnlockManager;
        private readonly ReskinManager _reskinManager;

        private readonly object False = false;

        private enum PayType { Item, Resources, Free }

        public VehicleDeployedLocks()
        {
            _vehicleInfoManager = new VehicleInfoManager(this);
            _lockedVehicleTracker = new LockedVehicleTracker(_vehicleInfoManager);
            _autoUnlockManager = new AutoUnlockManager(this, _lockedVehicleTracker);
            _reskinManager = new ReskinManager(_vehicleInfoManager, _lockedVehicleTracker);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(Permission_MasterKey, this);
            permission.RegisterPermission(LockInfo_CodeLock.PermissionFree, this);
            permission.RegisterPermission(LockInfo_CodeLock.PermissionAllVehicles, this);
            permission.RegisterPermission(LockInfo_KeyLock.PermissionFree, this);
            permission.RegisterPermission(LockInfo_KeyLock.PermissionAllVehicles, this);

            _craftKeyLockCooldowns = new CooldownManager(_config.CraftCooldownSeconds);
            _craftCodeLockCooldowns = new CooldownManager(_config.CraftCooldownSeconds);

            if (_config.AllowPushWhileLockedOut)
            {
                Unsubscribe(nameof(OnVehiclePush));
            }

            Unsubscribe(nameof(OnEntityKill));
        }

        private void OnServerInitialized()
        {
            _vehicleInfoManager.OnServerInitialized();
            _lockedVehicleTracker.OnServerInitialized();
            _autoUnlockManager.OnServerInitialized(_config.AutoUnlockSettings);

            if (_config.UpdateLockPositions)
            {
                foreach (var networkable in BaseNetworkable.serverEntities)
                {
                    var entity = networkable as BaseEntity;
                    if ((object)entity == null)
                        continue;

                    var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(entity);
                    if (vehicleInfo == null)
                        continue;

                    var lockEntity = GetVehicleLock(entity);
                    if (lockEntity == null)
                        continue;

                    var transform = lockEntity.transform;
                    transform.localPosition = vehicleInfo.LockPosition;
                    transform.localRotation = vehicleInfo.LockRotation;
                    lockEntity.SendNetworkUpdate_Position();
                }
            }

            Subscribe(nameof(OnEntityKill));
        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            // Don't lock taxi modules
            if (entity is ModularCarSeat carSeat && IsTaxiSeat(carSeat))
                return null;

            return CanPlayerInteractWithParentVehicle(player, entity);
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            // Don't lock taxi module shopfronts
            if (container is ModularVehicleShopFront)
                return null;

            return CanPlayerInteractWithParentVehicle(player, container);
        }

        private object CanLootEntity(BasePlayer player, ContainerIOEntity container)
        {
            return CanPlayerInteractWithParentVehicle(player, container);
        }

        private object CanLootEntity(BasePlayer player, RidableHorse horse)
        {
            return CanPlayerInteractWithVehicle(player, horse);
        }

        private object CanLootEntity(BasePlayer player, ModularCarGarage carLift)
        {
            if (carLift == null
                || _config.ModularCarSettings.AllowEditingWhileLockedOut
                || !carLift.PlatformIsOccupied)
                return null;

            return CanPlayerInteractWithVehicle(player, carLift.carOccupant);
        }

        private object OnHorseLead(RidableHorse horse, BasePlayer player)
        {
            return CanPlayerInteractWithVehicle(player, horse);
        }

        private object OnHotAirBalloonToggle(HotAirBalloon hab, BasePlayer player)
        {
            return CanPlayerInteractWithVehicle(player, hab);
        }

        private object OnSwitchToggle(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (electricSwitch == null)
                return null;

            var autoTurret = electricSwitch.GetParentEntity() as AutoTurret;
            if (autoTurret != null)
                return CanPlayerInteractWithParentVehicle(player, autoTurret);

            return null;
        }

        private object OnTurretAuthorize(AutoTurret entity, BasePlayer player)
        {
            return CanPlayerInteractWithParentVehicle(player, entity);
        }

        private object OnTurretTarget(AutoTurret autoTurret, BasePlayer player)
        {
            if (autoTurret == null || player == null || player.UserIDString == null)
                return null;

            var turretParent = autoTurret.GetParentEntity();
            var vehicle = turretParent as BaseVehicle ?? (turretParent as BaseVehicleModule)?.Vehicle;
            if (vehicle == null)
                return null;

            var baseLock = GetVehicleLock(vehicle);
            if (baseLock == null)
                return null;

            if (CanPlayerBypassLock(player, baseLock, provideFeedback: false))
                return False;

            return null;
        }

        private object CanSwapToSeat(BasePlayer player, ModularCarSeat carSeat)
        {
            // Don't lock taxi modules
            if (IsTaxiSeat(carSeat))
                return null;

            return CanPlayerInteractWithParentVehicle(player, carSeat, provideFeedback: false);
        }

        private object OnVehiclePush(BaseVehicle vehicle, BasePlayer player)
        {
            return CanPlayerInteractWithVehicle(player, vehicle);
        }

        private void OnEntityKill(BaseLock baseLock)
        {
            var vehicle = GetParentVehicle(baseLock);
            if (vehicle == null)
                return;

            _lockedVehicleTracker.OnLockRemoved(vehicle);
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

            var car2 = car;
            var baseLock2 = baseLock;

            NextTick(() =>
            {
                if (car2 == null)
                {
                    _lockedVehicleTracker.OnLockRemoved(car2);
                    baseLock2.Kill();
                }
                else
                {
                    var driverModule = FindFirstDriverModule(car2);
                    if (driverModule == null)
                    {
                        _lockedVehicleTracker.OnLockRemoved(car2);
                        baseLock2.Kill();
                    }
                    else
                    {
                        baseLock2.SetParent(driverModule);
                    }
                }
            });
        }

        // Allow players to deploy locks directly without any commands.
        private object CanDeployItem(BasePlayer basePlayer, Deployer deployer, NetworkableId entityId)
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
            {
                lockInfo = LockInfo_CodeLock;
            }
            else if (itemid == LockInfo_KeyLock.ItemId)
            {
                lockInfo = LockInfo_KeyLock;
            }
            else
            {
                return null;
            }

            var vehicleInfo = GetVehicleAndInfo(BaseNetworkable.serverEntities.Find(entityId) as BaseEntity, basePlayer, out var vehicle, fromDeployHook: true);
            if (vehicleInfo == null)
                return null;

            var player = basePlayer.IPlayer;

            // Trick to make sure the replies are in chat instead of console.
            player.LastCommand = CommandType.Chat;

            if (!VerifyCanDeploy(player, vehicle, vehicleInfo, lockInfo, out var payType)
                || !VerifyDeployDistance(player, vehicle))
                return False;

            DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, basePlayer, payType);
            return False;
        }

        private object OnEntityReskin(Snowmobile snowmobile, ItemSkinDirectory.Skin skin, BasePlayer player)
        {
            var baseLock = GetVehicleLock(snowmobile);
            if (baseLock == null)
                return null;

            if (_vehicleInfoManager.GetVehicleInfo(snowmobile) == null)
                return null;

            if (baseLock.IsLocked() && !CanPlayerBypassLock(player, baseLock, provideFeedback: true))
                return False;

            _reskinManager.HandleReskinPre(snowmobile, baseLock);

            // In case another plugin blocks the pre-hook, add back or destroy the lock.
            NextTick(_reskinManager.CleanupAction);

            return null;
        }

        private void OnEntityReskinned(Snowmobile snowmobile, ItemSkinDirectory.Skin skin, BasePlayer player)
        {
            _reskinManager.HandleReskinPost(snowmobile);
        }

        #endregion

        #region API

        [HookMethod(nameof(API_DeployCodeLock))]
        public CodeLock API_DeployCodeLock(BaseEntity vehicle, BasePlayer player, bool isFree = true)
        {
            return DeployLockForAPI(vehicle, player, LockInfo_CodeLock, isFree) as CodeLock;
        }

        [HookMethod(nameof(API_DeployKeyLock))]
        public KeyLock API_DeployKeyLock(BaseEntity vehicle, BasePlayer player, bool isFree = true)
        {
            return DeployLockForAPI(vehicle, player, LockInfo_KeyLock, isFree) as KeyLock;
        }

        [HookMethod(nameof(API_CanPlayerDeployCodeLock))]
        public bool API_CanPlayerDeployCodeLock(BasePlayer player, BaseEntity vehicle)
        {
            return CanPlayerDeployLockForAPI(player, vehicle, LockInfo_CodeLock);
        }

        [HookMethod(nameof(API_CanPlayerDeployKeyLock))]
        public bool API_CanPlayerDeployKeyLock(BasePlayer player, BaseEntity vehicle)
        {
            return CanPlayerDeployLockForAPI(player, vehicle, LockInfo_KeyLock);
        }

        [HookMethod(nameof(API_CanAccessVehicle))]
        public bool API_CanAccessVehicle(BasePlayer player, BaseEntity vehicle, bool provideFeedback = true)
        {
            return CanPlayerInteractWithVehicle(player, vehicle, provideFeedback) == null;
        }

        [HookMethod(nameof(API_RegisterCustomVehicleType))]
        public void API_RegisterCustomVehicleType(string vehicleType, Vector3 lockPosition, Quaternion lockRotation, string parentBone, Func<BaseEntity, BaseEntity> determineLockParent)
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
        private void CodeLockCommand(IPlayer player, string cmd, string[] args)
        {
            LockCommand(player, LockInfo_CodeLock);
        }

        [Command("vehiclekeylock", "vkeylock")]
        private void KeyLockCommand(IPlayer player, string cmd, string[] args)
        {
            LockCommand(player, LockInfo_KeyLock);
        }

        private void LockCommand(IPlayer player, LockInfo lockInfo)
        {
            if (player.IsServer)
                return;

            var basePlayer = player.Object as BasePlayer;
            var vehicleInfo = GetVehicleAndInfo(GetLookEntity(basePlayer, MaxDeployDistance), basePlayer, out var vehicle);
            if (vehicleInfo == null)
            {
                ReplyToPlayer(player, Lang.DeployErrorNoVehicleFound);
                return;
            }

            if (!VerifyCanDeploy(player, vehicle, vehicleInfo, lockInfo, out var payType))
                return;

            DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, basePlayer, payType);
        }

        #endregion

        #region Helper Methods - General

        private static bool HasPermissionAny(IPlayer player, params string[] permissionNames)
        {
            foreach (var perm in permissionNames)
            {
                if (player.HasPermission(perm))
                    return true;
            }

            return false;
        }

        private static BaseLock GetVehicleLock(BaseEntity vehicle)
        {
            return vehicle.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
        }

        private static bool IsLockableEntity(BaseEntity entity)
        {
            if (entity.IsBusy())
                return false;

            return entity.HasSlot(BaseEntity.Slot.Lock);
        }

        private static string[] FindPrefabsOfType<T>() where T : BaseEntity
        {
            var prefabList = new List<string>();

            foreach (var assetPath in GameManifest.Current.entities)
            {
                var entity = GameManager.server.FindPrefab(assetPath)?.GetComponent<T>();
                if (entity == null)
                    continue;

                prefabList.Add(entity.PrefabName);
            }

            return prefabList.ToArray();
        }

        private static bool IsTaxiSeat(ModularCarSeat carSeat)
        {
            return carSeat is { associatedSeatingModule.DoorsAreLockable: false };
        }

        #endregion

        #region Helper Methods - Lock Authorization

        private bool IsPlayerAuthorizedToCodeLock(ulong userID, CodeLock codeLock)
        {
            return codeLock.whitelistPlayers.Contains(userID)
                || codeLock.guestPlayers.Contains(userID);
        }

        private bool IsPlayerAuthorizedToLock(BasePlayer player, BaseLock baseLock)
        {
            return (baseLock as KeyLock)?.HasLockPermission(player)
                ?? IsPlayerAuthorizedToCodeLock(player.userID, baseLock as CodeLock);
        }

        private bool PlayerHasMasterKeyForLock(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString, Permission_MasterKey);
        }

        private bool IsLockSharedWithPlayer(BasePlayer player, BaseLock baseLock)
        {
            var ownerId = baseLock.OwnerID;
            if (ownerId == 0 || ownerId == player.userID)
                return false;

            // In case the owner was locked out for some reason
            var codeLock = baseLock as CodeLock;
            if (codeLock != null && !IsPlayerAuthorizedToCodeLock(ownerId, codeLock))
                return false;

            var sharingSettings = _config.SharingSettings;
            if (sharingSettings.Team && player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(ownerId))
                    return true;
            }

            if (sharingSettings.Friends && Friends != null)
            {
                var friendsResult = Friends.Call("HasFriend", baseLock.OwnerID, player.userID);
                if (friendsResult is true)
                    return true;
            }

            if ((sharingSettings.Clan || sharingSettings.ClanOrAlly) && Clans != null)
            {
                var clanMethodName = sharingSettings.ClanOrAlly ? "IsMemberOrAlly" : "IsClanMember";
                var clanResult = Clans.Call(clanMethodName, ownerId.ToString(), player.UserIDString);
                if (clanResult is true)
                    return true;
            }

            return false;
        }

        private bool CanPlayerBypassLock(BasePlayer player, BaseLock baseLock, bool provideFeedback)
        {
            var hookResult = Interface.CallHook("CanUseLockedEntity", player, baseLock);
            if (hookResult is bool result)
                return result;

            if (_config.AllowNPCsToBypassLocks && (player.IsNpc || !player.userID.IsSteamId()))
                return true;

            var canAccessLock = IsPlayerAuthorizedToLock(player, baseLock)
                || IsLockSharedWithPlayer(player, baseLock)
                || PlayerHasMasterKeyForLock(player);

            if (canAccessLock)
            {
                if (provideFeedback && !(baseLock is KeyLock))
                {
                    Effect.server.Run(Prefab_CodeLock_UnlockEffect, baseLock, 0, Vector3.zero, Vector3.forward);
                }

                return true;
            }

            if (provideFeedback)
            {
                Effect.server.Run(Prefab_CodeLock_DeniedEffect, baseLock, 0, Vector3.zero, Vector3.forward);
                ChatMessage(player, Lang.GenericErrorVehicleLocked);
            }

            return false;
        }

        private object CanPlayerInteractWithVehicle(BasePlayer player, BaseEntity vehicle, bool provideFeedback = true)
        {
            if (player == null || vehicle == null)
                return null;

            var baseLock = GetVehicleLock(vehicle);
            if (baseLock == null || !baseLock.IsLocked())
                return null;

            if (CanPlayerBypassLock(player, baseLock, provideFeedback))
                return null;

            return False;
        }

        private BaseEntity GetParentVehicle(BaseEntity entity)
        {
            var parent = entity.GetParentEntity();
            if (parent == null)
                return null;

            // Check for a vehicle module first since they are considered vehicles.
            var parentModule = parent as BaseVehicleModule;
            if (parentModule != null)
                return parentModule.Vehicle;

            if (parent is HotAirBalloon or BaseVehicle)
                return parent;

            return _vehicleInfoManager.GetCustomVehicleParent(entity);
        }

        private object CanPlayerInteractWithParentVehicle(BasePlayer player, BaseEntity entity, bool provideFeedback = true)
        {
            return CanPlayerInteractWithVehicle(player, GetParentVehicle(entity), provideFeedback);
        }

        #endregion

        #region Helper Methods - Deploying Locks

        private static bool DeployWasBlocked(BaseEntity vehicle, BasePlayer player, LockInfo lockInfo)
        {
            return Interface.CallHook(lockInfo.PreHookName, vehicle, player) is false;
        }

        private static BaseEntity GetLookEntity(BasePlayer player, float maxDistance)
        {
            if (!Physics.Raycast(player.eyes.HeadRay(), out var hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return null;

            return hit.GetEntity();
        }

        private static bool IsDead(BaseEntity entity)
        {
            return (entity as BaseCombatEntity)?.IsDead() ?? false;
        }

        private static VehicleModuleSeating FindFirstDriverModule(ModularCar car)
        {
            for (var socketIndex = 0; socketIndex < car.TotalSockets; socketIndex++)
            {
                if (car.TryGetModuleAt(socketIndex, out var module))
                {
                    var seatingModule = module as VehicleModuleSeating;
                    if (seatingModule != null && seatingModule.HasADriverSeat())
                        return seatingModule;
                }
            }

            return null;
        }

        private static bool CanCarHaveLock(ModularCar car)
        {
            return FindFirstDriverModule(car) != null;
        }

        private static bool CanVehicleHaveALock(BaseEntity vehicle)
        {
            // Only modular cars have restrictions
            var car = vehicle as ModularCar;
            return car == null || CanCarHaveLock(car);
        }

        private static Item GetPlayerLockItem(BasePlayer player, LockInfo lockInfo)
        {
            return player.inventory.FindItemByItemID(lockInfo.ItemId);
        }

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

            return player.inventory.crafting.CanCraft(lockInfo.ItemDefinition);
        }

        private static RidableHorse GetClosestHorse(HitchTrough hitchTrough, BasePlayer player)
        {
            var closestDistance = float.MaxValue;
            RidableHorse closestHorse = null;

            foreach (var hitchSpot in hitchTrough.hitchSpots)
            {
                if (!hitchSpot.IsOccupied())
                    continue;

                var distance = Vector3.Distance(player.transform.position, hitchSpot.tr.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    if (hitchSpot.hitchableEntRef.Get(serverside: true) is RidableHorse ridableHorse)
                    {
                        closestHorse = ridableHorse;
                    }
                }
            }

            return closestHorse;
        }

        private static void ClaimVehicle(BaseEntity vehicle, ulong ownerId)
        {
            vehicle.OwnerID = ownerId;
            Interface.CallHook("OnVehicleOwnershipChanged", vehicle);
        }

        private bool IsVehicleConnectedEntity(BasePlayer basePlayer, BaseEntity entity, out BaseEntity vehicle)
        {
            vehicle = null;

            if (entity is BaseVehicleModule module)
            {
                vehicle = module.Vehicle ?? module.GetParentEntity();
                return true;
            }

            if (entity is ModularCarGarage carLift)
            {
                vehicle = carLift.carOccupant;
                return true;
            }

            if (entity is HitchTrough hitchTrough)
            {
                vehicle = GetClosestHorse(hitchTrough, basePlayer);
                return true;
            }

            return false;
        }

        private VehicleInfo GetVehicleAndInfo(BaseEntity entity, BasePlayer basePlayer, out BaseEntity vehicle, bool fromDeployHook = false)
        {
            if (entity == null)
            {
                vehicle = null;
                return null;
            }

            var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(entity);
            if (vehicleInfo != null)
            {
                vehicle = entity;
                return vehicleInfo;
            }

            if (IsVehicleConnectedEntity(basePlayer, entity, out vehicle) && vehicle != null)
                return _vehicleInfoManager.GetVehicleInfo(vehicle);

            if (fromDeployHook && IsLockableEntity(entity))
            {
                // Let the game decide whether to lock the entity, instead of resolving the parent vehicle.
                vehicle = null;
                return null;
            }

            vehicle = entity.GetParentEntity();
            if (vehicle == null)
                return null;

            return _vehicleInfoManager.GetVehicleInfo(vehicle);
        }

        private bool AllowNoOwner(BaseEntity vehicle)
        {
            return _config.AllowIfNoOwner
                || vehicle.OwnerID != 0;
        }

        private bool AllowDifferentOwner(IPlayer player, BaseEntity vehicle)
        {
            return _config.AllowIfDifferentOwner
                || vehicle.OwnerID == 0
                || vehicle.OwnerID.ToString() == player.Id;
        }

        private void MaybeChargePlayerForLock(BasePlayer player, LockInfo lockInfo, PayType payType)
        {
            if (payType == PayType.Free)
                return;

            if (payType == PayType.Item)
            {
                // Prefer taking the item they are holding in case they are deploying directly.
                var heldItem = player.GetActiveItem();
                if (heldItem != null && heldItem.info.itemid == lockInfo.ItemId)
                {
                    heldItem.UseItem(1);
                }
                else
                {
                    player.inventory.Take(null, lockInfo.ItemId, 1);
                }

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

        private BaseLock DeployLock(BaseEntity vehicle, VehicleInfo vehicleInfo, LockInfo lockInfo, ulong ownerId = 0)
        {
            var parentToEntity = vehicleInfo.DetermineLockParent(vehicle);
            if (parentToEntity == null)
                return null;

            var baseLock = GameManager.server.CreateEntity(lockInfo.Prefab, vehicleInfo.LockPosition, vehicleInfo.LockRotation) as BaseLock;
            if (baseLock == null)
                return null;

            var keyLock = baseLock as KeyLock;
            if (keyLock != null)
            {
                keyLock.keyCode = UnityEngine.Random.Range(1, 100000);
            }

            // Assign lock ownership when the lock is being deployed by/for a player.
            if (ownerId != 0)
            {
                baseLock.OwnerID = ownerId;
            }

            baseLock.SetParent(parentToEntity, vehicleInfo.ParentBone);
            baseLock.Spawn();
            vehicle.SetSlot(BaseEntity.Slot.Lock, baseLock);

            // Auto lock key locks to be consistent with vanilla.
            if (ownerId != 0 && keyLock != null)
            {
                keyLock.SetFlag(BaseEntity.Flags.Locked, true);
            }

            Effect.server.Run(Prefab_CodeLock_DeployedEffect, baseLock.transform.position);
            Interface.CallHook("OnVehicleLockDeployed", vehicle, baseLock);
            _lockedVehicleTracker.OnLockAdded(vehicle);

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

            // Potentially assign vehicle ownership when the lock is being deployed by/for a player.
            if (vehicle.OwnerID == 0)
            {
                if (_config.AutoClaimUnownedVehicles)
                {
                    ClaimVehicle(vehicle, player.userID);
                }
            }
            else if (vehicle.OwnerID != player.userID && _config.AutoReplaceVehicleOwnership)
            {
                ClaimVehicle(vehicle, player.userID);
            }

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
            {
                payType = PayType.Free;
            }
            else if (!VerifyPlayerCanDeployLock(player.IPlayer, lockInfo, out payType))
            {
                return null;
            }

            if (DeployWasBlocked(vehicle, player, lockInfo))
                return null;

            return player != null
                ? DeployLockForPlayer(vehicle, vehicleInfo, lockInfo, player, payType)
                : DeployLock(vehicle, vehicleInfo, lockInfo);
        }

        private bool CanPlayerDeployLockForAPI(BasePlayer player, BaseEntity vehicle, LockInfo lockInfo)
        {
            return vehicle != null
                && !IsDead(vehicle)
                && _vehicleInfoManager.GetVehicleInfo(vehicle) != null
                && AllowNoOwner(vehicle)
                && AllowDifferentOwner(player.IPlayer, vehicle)
                && player.CanBuild()
                && GetVehicleLock(vehicle) == null
                && CanVehicleHaveALock(vehicle)
                && CanPlayerAffordLock(player, lockInfo, out _)
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

        private bool VerifyNotForSale(IPlayer player, BaseEntity vehicle)
        {
            var rideableAnimal = vehicle as BaseRidableAnimal;
            if (rideableAnimal == null || !rideableAnimal.IsForSale())
                return true;

            ReplyToPlayer(player, Lang.DeployErrorOther);
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
            if (basePlayer == null)
                return false;

            if (vehicle.OwnerID == 0 && _config.RequireTCIfNoOwner)
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

        private bool VerifyPlayerCanDeployLock(IPlayer player, LockInfo lockInfo, out PayType payType)
        {
            return VerifyPlayerCanAffordLock(player.Object as BasePlayer, lockInfo, out payType)
                && VerifyOffCooldown(player, lockInfo, payType);
        }

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
                && VerifyNotForSale(player, vehicle)
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

            public Func<BaseEntity, BaseEntity> DetermineLockParent = entity => entity;
            public Func<BaseEntity, float> TimeSinceLastUsed = _ => 0;

            public void OnServerInitialized(VehicleDeployedLocks plugin)
            {
                CodeLockPermission = $"{Permission_CodeLock_Prefix}.{VehicleType}";
                KeyLockPermission = $"{Permission_KeyLock_Prefix}.{VehicleType}";

                if (!plugin.permission.PermissionExists(CodeLockPermission, plugin))
                {
                    plugin.permission.RegisterPermission(CodeLockPermission, plugin);
                }

                if (!plugin.permission.PermissionExists(KeyLockPermission, plugin))
                {
                    plugin.permission.RegisterPermission(KeyLockPermission, plugin);
                }

                // Custom vehicles aren't currently allowed to specify prefabs since they reuse existing prefabs.
                if (PrefabPaths != null)
                {
                    var prefabIds = new List<uint>();
                    foreach (var prefabName in PrefabPaths)
                    {
                        var prefabId = StringPool.Get(prefabName);
                        if (prefabId != 0)
                        {
                            prefabIds.Add(prefabId);
                        }
                        else
                        {
                            plugin.LogError($"Invalid prefab. Please alert the plugin maintainer -- {prefabName}");
                        }
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
                    return mountable.AnyMounted();

                return false;
            }
        }

        private class VehicleInfoManager
        {
            private static readonly FieldInfo BikeTimeSinceLastUsedField = typeof(Bike).GetField("timeSinceLastUsed",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            private readonly VehicleDeployedLocks _plugin;
            private readonly Dictionary<uint, VehicleInfo> _prefabIdToVehicleInfo = new();
            private readonly Dictionary<string, VehicleInfo> _customVehicleTypes = new();

            public VehicleInfoManager(VehicleDeployedLocks plugin)
            {
                _plugin = plugin;
            }

            public void OnServerInitialized()
            {
                var allVehicles = new[]
                {
                    new VehicleInfo
                    {
                        VehicleType = "attackhelicopter",
                        PrefabPaths = new[] { "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab" },
                        LockPosition = new Vector3(-0.6f, 1.08f, 1.01f),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as AttackHelicopter)?.lastEngineOnTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "chinook",
                        PrefabPaths = new[] { "assets/prefabs/npc/ch47/ch47.entity.prefab" },
                        LockPosition = new Vector3(-1.175f, 2, 6.5f),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as CH47Helicopter)?.lastPlayerInputTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "duosub",
                        PrefabPaths = new[] { "assets/content/vehicles/submarine/submarineduo.entity.prefab" },
                        LockPosition = new Vector3(-0.455f, 1.29f, 0.75f),
                        LockRotation = Quaternion.Euler(0, 180, 10),
                        TimeSinceLastUsed = vehicle => (vehicle as SubmarineDuo)?.timeSinceLastUsed ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "hotairballoon",
                        PrefabPaths = new[] { "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab" },
                        LockPosition = new Vector3(1.45f, 0.9f, 0),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as HotAirBalloon)?.sinceLastBlast ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "kayak",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/kayak/kayak.prefab" },
                        LockPosition = new Vector3(-0.43f, 0.2f, 0.2f),
                        LockRotation = Quaternion.Euler(0, 90, 90),
                        TimeSinceLastUsed = vehicle => (vehicle as Kayak)?.timeSinceLastUsed ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "locomotive",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab" },
                        LockPosition = new Vector3(-0.11f, 2.89f, 4.95f),
                        TimeSinceLastUsed = vehicle => (vehicle as TrainEngine)?.decayingFor ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "magnetcrane",
                        PrefabPaths = new[] { "assets/content/vehicles/crane_magnet/magnetcrane.entity.prefab" },
                        LockPosition = new Vector3(-1.735f, -1.445f, 0.79f),
                        LockRotation = Quaternion.Euler(0, 0, 90),
                        ParentBone = "Top",
                        TimeSinceLastUsed = vehicle => Time.realtimeSinceStartup - (vehicle as MagnetCrane)?.lastDrivenTime ?? Time.realtimeSinceStartup,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "minicopter",
                        PrefabPaths = new[] { "assets/content/vehicles/minicopter/minicopter.entity.prefab" },
                        LockPosition = new Vector3(-0.15f, 0.7f, -0.1f),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as Minicopter)?.lastEngineOnTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "modularcar",
                        // There are at least 37 valid Modular Car prefabs.
                        PrefabPaths = FindPrefabsOfType<ModularCar>(),
                        LockPosition = new Vector3(-0.9f, 0.35f, -0.5f),
                        DetermineLockParent = vehicle => FindFirstDriverModule((ModularCar)vehicle),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as ModularCar)?.lastEngineOnTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "motorbike.sidecar",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/motorbike_sidecar.prefab" },
                        LockPosition = new Vector3(-0.09f, 0.65f, 0.03f),
                        TimeSinceLastUsed = vehicle => (TimeSince)BikeTimeSinceLastUsedField.GetValue(vehicle),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "motorbike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/motorbike.prefab" },
                        LockPosition = new Vector3(-0.09f, 0.65f, 0.03f),
                        TimeSinceLastUsed = vehicle => (TimeSince)BikeTimeSinceLastUsedField.GetValue(vehicle),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "pedalbike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/pedalbike.prefab" },
                        LockPosition = new Vector3(0, 0.6f, 0.1f),
                        TimeSinceLastUsed = vehicle => (TimeSince)BikeTimeSinceLastUsedField.GetValue(vehicle),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "pedaltrike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/pedaltrike.prefab" },
                        LockPosition = new Vector3(0, 0.6f, 0.1f),
                        TimeSinceLastUsed = vehicle => (TimeSince)BikeTimeSinceLastUsedField.GetValue(vehicle),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "rhib",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/rhib/rhib.prefab" },
                        LockPosition = new Vector3(-0.68f, 2.00f, 0.7f),
                        TimeSinceLastUsed = vehicle => (vehicle as RHIB)?.timeSinceLastUsedFuel ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "ridablehorse",
                        PrefabPaths = new[] { "assets/rust.ai/nextai/testridablehorse.prefab" },
                        LockPosition = new Vector3(-0.6f, 0.25f, -0.1f),
                        LockRotation = Quaternion.Euler(0, 95, 90),
                        ParentBone = "Horse_RootBone",
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as RidableHorse)?.lastInputTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "rowboat",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/rowboat/rowboat.prefab" },
                        LockPosition = new Vector3(-0.83f, 0.51f, -0.57f),
                        TimeSinceLastUsed = vehicle => (vehicle as MotorRowboat)?.timeSinceLastUsedFuel ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "scraptransport",
                        PrefabPaths = new[] { "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab" },
                        LockPosition = new Vector3(-1.25f, 1.22f, 1.99f),
                        TimeSinceLastUsed = vehicle => Time.time - (vehicle as ScrapTransportHelicopter)?.lastEngineOnTime ?? Time.time,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "sedan",
                        PrefabPaths = new[] { "assets/content/vehicles/sedan_a/sedantest.entity.prefab" },
                        LockPosition = new Vector3(-1.09f, 0.79f, 0.5f),
                    },
                    new VehicleInfo
                    {
                        VehicleType = "sedanrail",
                        PrefabPaths = new[] { "assets/content/vehicles/sedan_a/sedanrail.entity.prefab" },
                        LockPosition = new Vector3(-1.09f, 1.025f, -0.26f),
                        TimeSinceLastUsed = vehicle => (vehicle as TrainEngine)?.decayingFor ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "snowmobile",
                        PrefabPaths = new[] { "assets/content/vehicles/snowmobiles/snowmobile.prefab" },
                        LockPosition = new Vector3(-0.205f, 0.59f, 0.4f),
                        TimeSinceLastUsed = vehicle => (vehicle as Snowmobile)?.timeSinceLastUsed ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "solosub",
                        PrefabPaths = new[] { "assets/content/vehicles/submarine/submarinesolo.entity.prefab" },
                        LockPosition = new Vector3(0f, 1.85f, 0f),
                        LockRotation = Quaternion.Euler(0, 90, 90),
                        TimeSinceLastUsed = vehicle => (vehicle as BaseSubmarine)?.timeSinceLastUsed ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "tomaha",
                        PrefabPaths = new[] { "assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab" },
                        LockPosition = new Vector3(-0.37f, 0.4f, 0.125f),
                        TimeSinceLastUsed = vehicle => (vehicle as Snowmobile)?.timeSinceLastUsed ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "tugboat",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/tugboat/tugboat.prefab" },
                        LockPosition = new Vector3(0.065f, 6.8f, 4.12f),
                        LockRotation = Quaternion.Euler(0, 90, 60),
                        TimeSinceLastUsed = vehicle => (vehicle as Tugboat)?.timeSinceLastUsedFuel ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "workcart",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart.entity.prefab" },
                        LockPosition = new Vector3(-0.2f, 2.35f, 2.7f),
                        TimeSinceLastUsed = vehicle => (vehicle as TrainEngine)?.decayingFor ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "workcartaboveground",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab" },
                        LockPosition = new Vector3(-0.2f, 2.35f, 2.7f),
                        TimeSinceLastUsed = vehicle => (vehicle as TrainEngine)?.decayingFor ?? 0,
                    },
                    new VehicleInfo
                    {
                        VehicleType = "workcartcovered",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab" },
                        LockPosition = new Vector3(-0.2f, 2.35f, 2.7f),
                        TimeSinceLastUsed = vehicle => (vehicle as TrainEngine)?.decayingFor ?? 0,
                    },
                };

                foreach (var vehicleInfo in allVehicles)
                {
                    vehicleInfo.OnServerInitialized(_plugin);
                    foreach (var prefabId in vehicleInfo.PrefabIds)
                    {
                        _prefabIdToVehicleInfo[prefabId] = vehicleInfo;
                    }
                }
            }

            public void RegisterCustomVehicleType(VehicleDeployedLocks plugin, VehicleInfo vehicleInfo)
            {
                vehicleInfo.OnServerInitialized(plugin);
                _customVehicleTypes[vehicleInfo.VehicleType] = vehicleInfo;
            }

            public VehicleInfo GetVehicleInfo(BaseEntity entity)
            {
                if (_prefabIdToVehicleInfo.TryGetValue(entity.prefabID, out var vehicleInfo))
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

            public ItemDefinition ItemDefinition => ItemManager.FindItemDefinition(ItemId);

            public ItemBlueprint Blueprint => ItemManager.FindBlueprint(ItemDefinition);
        }

        private readonly LockInfo LockInfo_CodeLock = new()
        {
            ItemId = 1159991980,
            Prefab = "assets/prefabs/locks/keypad/lock.code.prefab",
            PermissionAllVehicles = $"{Permission_CodeLock_Prefix}.allvehicles",
            PermissionFree = $"{Permission_CodeLock_Prefix}.free",
            PreHookName = "CanDeployVehicleCodeLock",
        };

        private readonly LockInfo LockInfo_KeyLock = new()
        {
            ItemId = -850982208,
            Prefab = "assets/prefabs/locks/keylock/lock.key.prefab",
            PermissionAllVehicles = $"{Permission_KeyLock_Prefix}.allvehicles",
            PermissionFree = $"{Permission_KeyLock_Prefix}.free",
            PreHookName = "CanDeployVehicleKeyLock",
        };

        #endregion

        #region Locked Vehicle Tracker

        private class LockedVehicleTracker
        {
            public Dictionary<VehicleInfo, HashSet<BaseEntity>> VehiclesWithLocksByType { get; } = new();

            private readonly VehicleInfoManager _vehicleInfoManager;

            public LockedVehicleTracker(VehicleInfoManager vehicleInfoManager)
            {
                _vehicleInfoManager = vehicleInfoManager;
            }

            public void OnServerInitialized()
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    var baseEntity = entity as BaseEntity;
                    if (baseEntity == null)
                        continue;

                    var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(baseEntity);
                    if (vehicleInfo == null || GetVehicleLock(baseEntity) == null)
                        continue;

                    OnLockAdded(baseEntity);
                }
            }

            public void OnLockAdded(BaseEntity vehicle)
            {
                GetEntityListForVehicle(vehicle)?.Add(vehicle);
            }

            public void OnLockRemoved(BaseEntity vehicle)
            {
                GetEntityListForVehicle(vehicle)?.Remove(vehicle);
            }

            private HashSet<BaseEntity> EnsureEntityList(VehicleInfo vehicleInfo)
            {
                if (!VehiclesWithLocksByType.TryGetValue(vehicleInfo, out var vehicleList))
                {
                    vehicleList = new HashSet<BaseEntity>();
                    VehiclesWithLocksByType[vehicleInfo] = vehicleList;
                }
                return vehicleList;
            }

            private HashSet<BaseEntity> GetEntityListForVehicle(BaseEntity entity)
            {
                var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(entity);
                if (vehicleInfo == null)
                    return null;

                return EnsureEntityList(vehicleInfo);
            }
        }

        #endregion

        #region Auto Unlock Manager

        private class AutoUnlockManager
        {
            private VehicleDeployedLocks _plugin;
            private LockedVehicleTracker _lockedVehicleTracker;
            private AutoUnlockSettings _autoUnlockSettings;

            public AutoUnlockManager(VehicleDeployedLocks plugin, LockedVehicleTracker lockedVehicleTracker)
            {
                _plugin = plugin;
                _lockedVehicleTracker = lockedVehicleTracker;
            }

            public void OnServerInitialized(AutoUnlockSettings settings)
            {
                _autoUnlockSettings = settings;

                if (!settings.Enabled)
                    return;

                _plugin.timer.Every(settings.CheckIntervalSeconds, CheckVehicles);
            }

            private void CheckVehicles()
            {
                List<BaseEntity> remainingVehicles = null;

                foreach (var (vehicleInfo, vehicleList) in _lockedVehicleTracker.VehiclesWithLocksByType)
                {
                    foreach (var vehicle in vehicleList)
                    {
                        if (vehicle == null || vehicle.IsDestroyed)
                            continue;

                        if (_autoUnlockSettings.ExemptOwnedVehicles && vehicle.OwnerID != 0)
                            continue;

                        var baseLock = GetVehicleLock(vehicle);
                        if (baseLock == null || baseLock.IsDestroyed || !baseLock.IsLocked())
                            continue;

                        var timeSinceUsed = vehicleInfo.TimeSinceLastUsed(vehicle);
                        if (timeSinceUsed < _autoUnlockSettings.IdleSeconds)
                            continue;

                        if (_autoUnlockSettings.ExemptNearTC)
                        {
                            remainingVehicles ??= new List<BaseEntity>();
                            remainingVehicles.Add(vehicle);
                            continue;
                        }

                        Unlock(baseLock);
                    }
                }

                if (remainingVehicles == null)
                    return;

                var vehicleIndex = 0;

                // The remaining vehicles need more expensive checks, so spread them out across multiple frames.
                _plugin.timer.Repeat(0, remainingVehicles.Count, () =>
                {
                    var vehicle = remainingVehicles[vehicleIndex++];
                    if (vehicle == null || vehicle.IsDestroyed)
                        return;

                    var baseLock = GetVehicleLock(vehicle);
                    if (baseLock == null || baseLock.IsDestroyed || !baseLock.IsLocked())
                        return;

                    if (vehicle.GetBuildingPrivilege() != null)
                        return;

                    Unlock(baseLock);
                });
            }

            private void Unlock(BaseLock baseLock)
            {
                baseLock.SetFlag(BaseEntity.Flags.Locked, false);
                Effect.server.Run(Prefab_CodeLock_UnlockEffect, baseLock, 0, Vector3.zero, Vector3.forward);
            }
        }

        #endregion

        #region Reskin Management

        private class ReskinEvent
        {
            public BaseEntity Entity;
            public BaseLock BaseLock;
            public Vector3 Position;

            public void Assign(BaseEntity entity, BaseLock baseLock)
            {
                Entity = entity;
                BaseLock = baseLock;
                Position = entity?.transform.position ?? Vector3.zero;
            }

            public void Reset()
            {
                Assign(null, null);
            }
        }

        private class ReskinManager
        {
            private VehicleInfoManager _vehicleInfoManager;
            private LockedVehicleTracker _lockedVehicleTracker;

            // Pool only a single reskin event since usually there will be at most a single event per frame.
            private ReskinEvent _pooledReskinEvent;

            // Keep track of all reskin events happening in a frame, in case there are multiple.
            private List<ReskinEvent> _reskinEvents = new();

            public readonly Action CleanupAction;

            public ReskinManager(VehicleInfoManager vehicleInfoManager, LockedVehicleTracker lockedVehicleTracker)
            {
                _vehicleInfoManager = vehicleInfoManager;
                _lockedVehicleTracker = lockedVehicleTracker;
                CleanupAction = CleanupEvents;
            }

            public void HandleReskinPre(BaseEntity entity, BaseLock baseLock)
            {
                _pooledReskinEvent ??= new ReskinEvent();

                var reskinEvent = _pooledReskinEvent.Entity == null
                    ? _pooledReskinEvent
                    : new ReskinEvent();

                // Unparent the lock to prevent it from being destroyed.
                // It will later be parented to the newly spawned entity.
                baseLock.SetParent(null);

                reskinEvent.Assign(entity, baseLock);
                _reskinEvents.Add(reskinEvent);
            }

            public void HandleReskinPost(BaseEntity entity)
            {
                var reskinEvent = FindReskinEventForPosition(entity.transform.position);
                if (reskinEvent == null)
                    return;

                var baseLock = reskinEvent.BaseLock;
                if (baseLock == null || baseLock.IsDestroyed)
                    return;

                var vehicleInfo = _vehicleInfoManager.GetVehicleInfo(entity);
                if (vehicleInfo == null)
                    return;

                _reskinEvents.Remove(reskinEvent);

                baseLock.SetParent(entity, vehicleInfo.ParentBone);
                entity.SetSlot(BaseEntity.Slot.Lock, baseLock);
                _lockedVehicleTracker.OnLockAdded(entity);

                var lockTransform = baseLock.transform;
                lockTransform.localPosition = vehicleInfo.LockPosition;
                lockTransform.localRotation = vehicleInfo.LockRotation;
                baseLock.SendNetworkUpdateImmediate();

                if (reskinEvent == _pooledReskinEvent)
                {
                    reskinEvent.Reset();
                }
            }

            private ReskinEvent FindReskinEventForPosition(Vector3 position)
            {
                foreach (var reskinEvent in _reskinEvents)
                {
                    if (reskinEvent.Position == position)
                        return reskinEvent;
                }

                return null;
            }

            private void CleanupEvents()
            {
                if (_reskinEvents.Count == 0)
                    return;

                foreach (var reskinEvent in _reskinEvents)
                {
                    var baseLock = reskinEvent.BaseLock;
                    if (baseLock == null || baseLock.IsDestroyed || baseLock.HasParent())
                        continue;

                    var entity = reskinEvent.Entity;
                    if (entity != null && !entity.IsDestroyed)
                    {
                        // The reskin event must have been blocked, so reparent the lock to it.
                        baseLock.SetParent(reskinEvent.Entity);
                        continue;
                    }

                    // The post event wasn't called, and the original entity is gone, so destroy the lock.
                    baseLock.Kill();
                }

                _pooledReskinEvent.Reset();
                _reskinEvents.Clear();
            }
        }

        #endregion

        #region Cooldown Manager

        private class CooldownManager
        {
            private readonly Dictionary<string, float> _cooldownMap = new();
            private readonly float _cooldownDuration;

            public CooldownManager(float duration)
            {
                _cooldownDuration = duration;
            }

            public void UpdateLastUsedForPlayer(string userID)
            {
                _cooldownMap[userID] = Time.realtimeSinceStartup;
            }

            public float GetSecondsRemaining(string userID)
            {
                return _cooldownMap.TryGetValue(userID, out var duration)
                    ? duration + _cooldownDuration - Time.realtimeSinceStartup
                    : 0;
            }
        }

        private CooldownManager GetCooldownManager(LockInfo lockInfo)
        {
            return lockInfo == LockInfo_CodeLock
                ? _craftCodeLockCooldowns
                : _craftKeyLockCooldowns;
        }

        #endregion

        #region Configuration

        private class AutoUnlockSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled;

            [JsonProperty("Unlock after idle time (seconds)")]
            public float IdleSeconds = 3600;

            [JsonProperty("IdleSeconds")]
            private float DeprecatedIdleSeconds { set => IdleSeconds = value; }

            [JsonProperty("Check interval seconds")]
            public float CheckIntervalSeconds = 300;

            [JsonProperty("CheckIntervalSeconds")]
            private float DeprecatedCheckIntervalSeconds { set => CheckIntervalSeconds = value; }

            [JsonProperty("Exempt owned vehicles")]
            public bool ExemptOwnedVehicles = true;

            [JsonProperty("ExemptOwnedVehicles")]
            private bool DeprecatedExemptOwnedVehicles { set => ExemptOwnedVehicles = value; }

            [JsonProperty("Exempt vehicles near cupboards")]
            public bool ExemptNearTC = true;

            [JsonProperty("ExemptNearTC")]
            private bool DeprecatedExemptNearTC { set => ExemptNearTC = value; }
        }

        private class ModularCarSettings
        {
            [JsonProperty("Allow editing while locked out")]
            public bool AllowEditingWhileLockedOut = true;

            [JsonProperty("AllowEditingWhileLockedOut")]
            private bool DeprecatedAllowEditingWhileLockedOut { set => AllowEditingWhileLockedOut = value; }
        }

        private class SharingSettings
        {
            [JsonProperty("Clan")]
            public bool Clan;

            [JsonProperty("Clan or ally")]
            public bool ClanOrAlly;

            [JsonProperty("ClanOrAlly")]
            private bool DeprecatedClanOrAlly { set => ClanOrAlly = value; }

            [JsonProperty("Friends")]
            public bool Friends;

            [JsonProperty("Team")]
            public bool Team;
        }

        private class Configuration : BaseConfiguration
        {
            [JsonProperty("Update lock positions", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool UpdateLockPositions;

            [JsonProperty("Allow NPCs to bypass locks")]
            public bool AllowNPCsToBypassLocks;

            [JsonProperty("Allow deploying locks onto vehicles owned by other players")]
            public bool AllowIfDifferentOwner;

            [JsonProperty("AllowIfDifferentOwner")]
            private bool DeprecatedAllowIfDifferentOwner { set => AllowIfDifferentOwner = value; }

            [JsonProperty("Allow deploying locks onto unowned vehicles")]
            public bool AllowIfNoOwner = true;

            [JsonProperty("AllowIfNoOwner")]
            private bool DeprecatedAllowIfNoOwner { set => AllowIfNoOwner = value; }

            [JsonProperty("Require cupboard auth to deploy locks onto unowned vehicles")]
            public bool RequireTCIfNoOwner;

            [JsonProperty("RequireTCIfNoOwner")]
            private bool DeprecatedRequireTCIfNoOwner { set => RequireTCIfNoOwner = value; }

            [JsonProperty("Auto claim unowned vehicles when deploying locks")]
            public bool AutoClaimUnownedVehicles;

            [JsonProperty("Auto replace vehicle ownership when deploying locks")]
            public bool AutoReplaceVehicleOwnership;

            [JsonProperty("Allow pushing vehicles while locked out")]
            public bool AllowPushWhileLockedOut = true;

            [JsonProperty("AllowPushWhileLockedOut")]
            private bool DeprecatedAllowPushWhileLockedOut { set => AllowPushWhileLockedOut = value; }

            [JsonProperty("Cooldown to auto craft locks (seconds)")]
            public float CraftCooldownSeconds = 10;

            [JsonProperty("CraftCooldownSeconds")]
            private float DeprecatedCraftCooldownSeconds { set => CraftCooldownSeconds = value; }

            [JsonProperty("Modular car settings")]
            public ModularCarSettings ModularCarSettings = new();

            [JsonProperty("ModularCarSettings")]
            private ModularCarSettings DeprecatedModularCarSettings { set => ModularCarSettings = value; }

            [JsonProperty("Lock sharing settings")]
            public SharingSettings SharingSettings = new();

            [JsonProperty("DefaultSharingSettings")]
            private SharingSettings DeprecatedSharingSettings { set => SharingSettings = value; }

            [JsonProperty("Auto unlock idle vehicles")]
            public AutoUnlockSettings AutoUnlockSettings = new();

            [JsonProperty("AutoUnlockIdleVehicles")]
            private AutoUnlockSettings DeprecatedAutoUnlockSettings { set => AutoUnlockSettings = value; }
        }

        private Configuration GetDefaultConfig() => new();

        #endregion

        #region Configuration Helpers

        private class BaseConfiguration
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

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;
                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
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

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
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
            Config.WriteObject(_config, true);
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

        private static class Lang
        {
            public const string GenericErrorNoPermission = "Generic.Error.NoPermission";
            public const string GenericErrorBuildingBlocked = "Generic.Error.BuildingBlocked";
            public const string GenericErrorCooldown = "Generic.Error.Cooldown";
            public const string GenericErrorVehicleLocked = "Generic.Error.VehicleLocked";
            public const string DeployErrorNoVehicleFound = "Deploy.Error.NoVehicleFound";
            public const string DeployErrorVehicleDead = "Deploy.Error.VehicleDead";
            public const string DeployErrorOther = "Deploy.Error.Other";
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
                [Lang.DeployErrorOther] = "Error: You cannot do that.",
                [Lang.DeployErrorDifferentOwner] = "Error: Someone else owns that vehicle.",
                [Lang.DeployErrorNoOwner] = "Error: You do not own that vehicle.",
                [Lang.DeployErrorNoOwnerRequiresTC] = "Error: Locking unowned vehicles requires building privilege.",
                [Lang.DeployErrorHasLock] = "Error: That vehicle already has a lock.",
                [Lang.DeployErrorInsufficientResources] = "Error: Not enough resources to craft a {0}.",
                [Lang.DeployErrorMounted] = "Error: That vehicle is currently occupied.",
                [Lang.DeployErrorModularCarNoCockpit] = "Error: That car needs a cockpit module to receive a lock.",
                [Lang.DeployErrorDistance] = "Error: Too far away."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.GenericErrorNoPermission] = "Você não tem permissão para fazer isso.",
                [Lang.GenericErrorBuildingBlocked] = "Erro: Não é possível fazer isso enquanto o edifício está bloqueado.",
                [Lang.GenericErrorCooldown] = "Aguarde <color=red>{0} seg</color> e tente novamente.",
                [Lang.GenericErrorVehicleLocked] = "Esse veículo está trancado.",
                [Lang.DeployErrorNoVehicleFound] = "Erro: Nenhum veículo encontrado.",
                [Lang.DeployErrorVehicleDead] = "Erro: esse veículo está destruido.",
                [Lang.DeployErrorOther] = "Erro: Você não pode fazer isso.",
                [Lang.DeployErrorDifferentOwner] = "Erro: outra pessoa é proprietária desse veículo.",
                [Lang.DeployErrorNoOwner] = "Erro: você não possui esse veículo.",
                [Lang.DeployErrorNoOwnerRequiresTC] = "Erro: o bloqueio de veículos sem proprietário requer privilégio de construção.",
                [Lang.DeployErrorHasLock] = "Erro: esse veículo já tem fechadura.",
                [Lang.DeployErrorInsufficientResources] = "Erro: recursos insuficientes para criar um {0}.",
                [Lang.DeployErrorMounted] = "Erro: esse veículo está ocupado no momento.",
                [Lang.DeployErrorModularCarNoCockpit] = "Erro: esse carro precisa de um módulo de cabine para receber um bloqueio.",
                [Lang.DeployErrorDistance] = "Erro: muito longe."
            }, this, "pt-BR");
        }

        #endregion
    }
}
