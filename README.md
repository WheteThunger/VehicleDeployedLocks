## Features

- Allows players to deploy code locks and key locks to various vehicles using commands.
- Prevents players without lock authorization from accessing seats, fuel, storage, turrets and other vehicle features. Compatible with most plugins that add containers to vehicles.
- Lock authorization may be shared with the lock owner's team, friends or clanmates based on the plugin configuration, or via compatible sharing plugins. This allows other players to access the vehicle without requiring a key or code, and prevents them from being targetted by turrets on the vehicle.
- Deploying a lock consumes one from the player inventory, or consumes equivalent resources based on the blueprint price.
- Free locks for players with permission.

### Limitations

- Modular cars must have a cockpit module (i.e., driver seat) to receive a lock. The lock will deploy to the front-most cockpit module if there are multiple. If that cockpit is removed, the lock is moved to another cockpit module if present, else destroyed.
- Modular cars may have a built-in lock at the same time as a deployed lock. This is not recommended, but if this happens, players will need to simultaneously satisfy the rules of the built-in lock and the deployed lock in order to access the car.
- Locks do not prevent players from entering transport bays like a modular car flatbed or the back of a scrap transport helicopter.
- The lock position for ridable horses is a bit awkward and sticks out significantly, but this seems to be required in order for the player to be able to interact with it in most positions.

## Commands

- `vehiclecodelock` (or `vcodelock`, `vlock`) -- Deploy a code lock to the vehicle you are aiming at. You must be within several meters of the vehicle. You must not be building blocked.
- `vehiclekeylock` (or `vkeylock`) -- Deploy a key lock to the vehicle you are aiming at. You must be within several meters of the vehicle. You must not be building blocked.

## Permissions

### Code Locks

- `vehicledeployedlocks.codelock.free` -- Allows the player to deploy code locks to vehicles without consuming locks or resources from their inventory.
- `vehicledeployedlocks.codelock.allvehicles` -- Allows the player to deploy code locks to all supported vehicles.

As an alternative to the `allvehicles` permission, you can grant permissions by vehicle type:
- `vehicledeployedlocks.codelock.chinook`
- `vehicledeployedlocks.codelock.hotairballoon`
- `vehicledeployedlocks.codelock.kayak`
- `vehicledeployedlocks.codelock.minicopter`
- `vehicledeployedlocks.codelock.modularcar`
- `vehicledeployedlocks.codelock.rowboat`
- `vehicledeployedlocks.codelock.rhib`
- `vehicledeployedlocks.codelock.ridablehorse`
- `vehicledeployedlocks.codelock.scraptransport`
- `vehicledeployedlocks.codelock.sedan`

### Key Locks

- `vehicledeployedlocks.keylock.free` -- Allows the player to deploy key locks to vehicles without consuming locks or resources from their inventory.
- `vehicledeployedlocks.keylock.allvehicles` -- Allows the player to deploy key locks to all supported vehicles.

As an alternative to the `allvehicles` permission, you can grant permissions by vehicle type:
- `vehicledeployedlocks.keylock.chinook`
- `vehicledeployedlocks.keylock.hotairballoon`
- `vehicledeployedlocks.keylock.kayak`
- `vehicledeployedlocks.keylock.minicopter`
- `vehicledeployedlocks.keylock.modularcar`
- `vehicledeployedlocks.keylock.rhib`
- `vehicledeployedlocks.keylock.ridablehorse`
- `vehicledeployedlocks.keylock.rowboat`
- `vehicledeployedlocks.keylock.scraptransport`
- `vehicledeployedlocks.keylock.sedan`

## Configuration

```json
{
  "AllowIfDifferentOwner": false,
  "AllowIfNoOwner": true,
  "CraftCooldownSeconds": 10.0,
  "ModularCarSettings": {
    "AllowEditingWhileLockedOut": true
  },
  "DefaultSharingSettings": {
    "Clan": false,
    "ClanOrAlly": false,
    "Friends": false,
    "Team": false
  }
}
```

- `AllowIfDifferentOwner` (`true` or `false`) -- Whether to allow players to deploy a lock to a vehicle owned by someone else (i.e., a vehicle whose `OwnerID` is a different player's Steam ID). Such vehicles are likely spawned by a plugin, or a plugin allowed the player to claim that vehicle. This is `false` by default to protect owned vehicles from having locks deployed to them by others. Note: If the owner leaves a code lock unlocked, another player can still lock it with a custom code to lock out the owner.
- `AllowIfNoOwner` (`true` or `false`) -- Whether to allow players to deploy a lock to a vehicle that has no owner (i.e., `OwnerID` is `0`), which usually describes vehicles that spawned naturally in the world, though some plugins may spawn vehicles with no owner as well. Note: Vehicles spawned at NPC vendors have no owner by default, unless set by a plugin such as [Vehicle Vendor Options](https://umod.org/plugins/vehicle-vendor-options). You can also use the [Claim Vehicle Ownership](https://umod.org/plugins/claim-vehicle-ownership) plugin to allow players to claim unowned vehicles with an optional cooldown.
- `CraftCooldownSeconds` -- Cooldown for players to craft a lock if they don't have one in their inventory. Since players can pickup vehicle-deployed locks (by design), this cooldown prevents players from effectively making locks faster than they could normally craft them. Configure this based on the crafting speed of locks on your server.
- `ModularCarSettings`
  - `AllowEditingWhileLockedOut` -- Whether to allow players to edit a car at a lift while they are not authorized to the car's lock. This is `true` by default to be consistent with the vanilla car locks which allow players to edit the car (which likely allows removal of the lock). Setting this to `false` will make it impossible for unauthorized players to edit the car.
- `DefaultSharingSettings` (each `true` or `false`) -- Whether to allow players to bypass locks placed by their clanmates, ally clanmates, friends or teammates. More advanced sharing (such as players being in control of these settings) can be achieved via compatible sharing plugins.

## Localization

```json
{
  "Generic.Error.NoPermission": "You don't have permission to do that.",
  "Generic.Error.BuildingBlocked": "Error: Cannot do that while building blocked.",
  "Generic.Error.Cooldown": "Please wait <color=red>{0}s</color> and try again.",
  "Generic.Error.VehicleLocked": "That vehicle is locked.",
  "Deploy.Error.NoVehicleFound": "Error: No vehicle found.",
  "Deploy.Error.VehicleDead": "Error: That vehicle is dead.",
  "Deploy.Error.DifferentOwner": "Error: Someone else owns that vehicle.",
  "Deploy.Error.NoOwner": "Error: You do not own that vehicle.",
  "Deploy.Error.HasLock": "Error: That vehicle already has a lock.",
  "Deploy.Error.InsufficientResources": "Error: Not enough resources to craft a {0}.",
  "Deploy.Error.Mounted": "Error: That vehicle is currently occupied.",
  "Deploy.Error.ModularCar.NoCockpit": "Error: That car needs a cockpit module to receive a lock."
}
```

## Developer API

#### API_DeployCodeLock / API_DeployKeyLock

Plugins can call these APIs to deploy a lock to a supported vehicle. The `BasePlayer` parameter is optional, but providing it is recommended as it allows for potential compatibility with auto-lock plugins, and allows the player to access the key lock without a key.

Note: These will skip several checks, such as permissions, whether the player is building blocked, and whether the vehicle is mounted. This allows your plugin to use discretion to determine whether the player should be allowed to deploy a particular lock to a particular vehicle.

```csharp
CodeLock API_DeployCodeLock(BaseCombatEntity vehicle, BasePlayer player, bool isFree = true)
KeyLock API_DeployKeyLock(BaseCombatEntity vehicle, BasePlayer player, bool isFree = true)
```

The return value will be the newly deployed lock, or `null` if a lock was not deployed for any of the following reasons.
- The vehicle is unsupported
- The vehicle is a modular car and has no cockpit modules
- The vehicle already has a code lock or a key lock
- The vehicle was destroyed or is "dead"
- Another plugin blocked it with the `CanDeployVehicleCodeLock` or `CanDeployVehicleKeyLock` hook
- The `isFree` argument was `false`, and the player didn't have sufficient items or resources to deploy the lock (this also takes into account permission for free locks), or recently purchased one and was on cooldown

#### API_CanPlayerDeployCodeLock / API_CanPlayerDeployKeyLock

Plugins can call these APIs to see if a player is able to deploy a lock to the specified vehicle, following this plugin's configuration and performing the same checks as if the player attempted to deploy a lock via a command, except for permission, cooldown and mounted checks. These methods could be used, for example, by a plugin that adds a UI button to deploy a lock, since it wouldn't have to re-implement various checks that this plugin can already do.

```csharp
bool API_CanPlayerDeployCodeLock(BasePlayer player, BaseCombatEntity vehicle)
bool API_CanPlayerDeployKeyLock(BasePlayer player, BaseCombatEntity vehicle)
```

#### API_CanAccessVehicle

Plugins can call this API to determine whether a player has authorization to a possibly locked vehicle.

```csharp
bool API_CanAccessVehicle(BasePlayer player, BaseCombatEntity vehicle, bool provideFeedback = true)
```

Returns `true` if any of the following criteria are met, else returns `false`.
- The vehicle has no lock
- The lock is unlocked
- The player has direct authorization to the lock
- The lock owner is sharing access with the player
- The `CanUseLockedEntity` hook returned `true`

If `provideFeedback` is true, the lock will play an access granted or denied sound effect, and the player will be sent a chat message if they do not have access.

## Hooks

#### CanDeployVehicleCodeLock / CanDeployVehicleKeyLock

- Called when a player or a plugin tries to deploy a lock to a vehicle.
- Returning `false` will prevent the lock from being deployed. None of the player's items will be consumed.
- Returning `null` will result in the default behavior.

```csharp
object CanDeployVehicleCodeLock(BaseCombatEntity vehicle, BasePlayer player)
object CanDeployVehicleKeyLock(BaseCombatEntity vehicle, BasePlayer player)
```

You can replace the `BaseCombatEntity` type with a more specific one to only run your hook method for specific vehicle types.

#### OnVehicleLockDeployed

Called when a player or a plugin deploys a lock to a vehicle.

```csharp
void OnVehicleLockDeployed(BaseCombatEntity vehicle, BaseLock baseLock)
```

You can replace the `BaseCombatEntity` and `BaseLock` types with more specific ones to only run your hook method for specific vehicle/lock types.

#### OnItemDeployed

This is an Oxide hook that is normally called when deploying a lock or other deployable. To allow for compatibility with other plugins, this plugin calls this hook whenever a code lock is deployed to a car for a player.

Note: This is not called when a lock is deployed via the API without specifying a player.

```csharp
void OnItemDeployed(Deployer deployer, BaseEntity entity)
{
    // Example: Check if a code lock was deployed to a car
    var car = entity as ModularCar;
    if (car == null) return;
    var codeLock = car.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
    if (codeLock != null)
        Puts("A code lock was deployed to a car!");
}
```

#### CanUseLockedEntity

This is an Oxide hook that is normally called when a player attempts to use a locked entity such as a door or box. To allow for compabitility with other plugins, especially sharing plugins, this plugin calls this hook whenever a player tries to access any feature of a locked vehicle, including seats, fuel or storage. This is also called when attempting to edit a locked car at a lift if the plugin is configured with `AllowEditingWhileLockedOut: false`.

- Not called if the lock is currently unlocked. This deviates slightly from the Oxide hook which is called for unlocked doors/boxes/cupboards.
- Returning `true` will allow the player to use the vehicle, regardless of whether they are authorized to the lock. Unless you know what you are doing, you should return `null` instead to avoid potential hook conflicts.
- Returning `false` will prevent the player from using the vehicle, regardless of whether they are authorized to the lock.
- Returning `null` will result in the default behavior.

```csharp
object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
{
    // Example: Only let the lock owner access the car
    if (baseLock == null) return null;
    var seatingModule = baseLock.GetParentEntity() as VehicleModuleSeating;
    if (seatingModule == null) return null;
    var car = seatingModule.Vehicle as ModularCar;
    if (car == null || car.OwnerID == 0 || car.OwnerID == player.userID) return null;
    return false;
}
```
