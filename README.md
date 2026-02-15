## Features

- Allows players to deploy code locks and key locks to various vehicles, the same way locks are deployed to doors and containers (no commands required).
- Prevents players without lock authorization from accessing seats, fuel, storage, turrets and other vehicle features. Compatible with most plugins that add containers and other attachments to vehicles.
- Lock authorization may be shared with the lock owner's team, friends or clanmates based on the plugin configuration, or via compatible sharing plugins. This allows other players to access the vehicle without requiring a key or code, and prevents them from being targeted by turrets on the vehicle.
- Deploying a lock consumes one from the player inventory, or consumes equivalent resources based on the blueprint price.
- Free locks for players with permission.
- Compatible with taxi modules.

### Notes

- The "Lock" and "Unlock" prompts appear on many vehicles while using them. Sometimes this is useful, but sometimes it just takes up space on your screen. Unfortunately, the plugin can't change this behavior because it's controlled client-side.
- Modular cars must have a cockpit module (i.e., driver seat) to receive a lock. The lock will deploy to the front-most cockpit module if there are multiple. If that cockpit is removed, the lock is moved to another cockpit module if present, else the lock will be destroyed.
- Modular cars may have a built-in lock at the same time as a deployed lock. This is not recommended, but if this happens, players will need to simultaneously satisfy the rules of the built-in lock and the deployed lock in order to access the car.
- Locks do not prevent players from entering transport bays like a modular car flatbed or the back of a scrap transport helicopter.
- Locks on Tugboats cannot be interacted with except within 3 meters from the lock, despite the "Lock" and "Unlock" prompts being visible farther away than 3 meters.
- Doors on Tugboats still needs locks to block opening and closing them. This could possibly be changed on request.

## Commands

Note: In addition to being able to deploy locks directly, you can also use the following commands if you are close to a vehicle and not building blocked. These are useful if you have one of the free permissions but don't have a lock in your inventory.

- `vehiclecodelock` (or `vcodelock`, `vlock`) -- Deploy a code lock to the vehicle you are aiming at.
- `vehiclekeylock` (or `vkeylock`) -- Deploy a key lock to the vehicle you are aiming at.

## Permissions

### All locks

- `vehicledeployedlocks.masterkey` -- Allows the player to open any lock managed by this plugin, useful for allowing admins access to locked vehicles.

### Code Locks

- `vehicledeployedlocks.codelock.free` -- Allows the player to deploy code locks to vehicles without consuming locks or resources from their inventory.

The following permissions allow players to deploy code locks to vehicles.

- `vehicledeployedlocks.codelock.allvehicles` (all in one)
- `vehicledeployedlocks.codelock.attackhelicopter`
- `vehicledeployedlocks.codelock.chinook`
- `vehicledeployedlocks.codelock.duosub`
- `vehicledeployedlocks.codelock.hotairballoon`
- `vehicledeployedlocks.codelock.kayak`
- `vehicledeployedlocks.codelock.locomotive`
- `vehicledeployedlocks.codelock.magnetcrane`
- `vehicledeployedlocks.codelock.minicopter`
- `vehicledeployedlocks.codelock.modularcar`
- `vehicledeployedlocks.codelock.motorbike.sidecar`
- `vehicledeployedlocks.codelock.motorbike`
- `vehicledeployedlocks.codelock.pedalbike`
- `vehicledeployedlocks.codelock.pedaltrike`
- `vehicledeployedlocks.codelock.ptboat`
- `vehicledeployedlocks.codelock.rhib`
- `vehicledeployedlocks.codelock.ridablehorse`
- `vehicledeployedlocks.codelock.rowboat`
- `vehicledeployedlocks.codelock.scraptransport`
- `vehicledeployedlocks.codelock.sedan`
- `vehicledeployedlocks.codelock.sedanrail`
- `vehicledeployedlocks.codelock.snowmobile`
- `vehicledeployedlocks.codelock.solosub`
- `vehicledeployedlocks.codelock.tomaha`
- `vehicledeployedlocks.codelock.tugboat`
- `vehicledeployedlocks.codelock.workcart`
- `vehicledeployedlocks.codelock.workcartaboveground`
- `vehicledeployedlocks.codelock.workcartcovered`

### Key Locks

- `vehicledeployedlocks.keylock.free` -- Allows the player to deploy key locks to vehicles without consuming locks or resources from their inventory.

The following permissions allow players to deploy key locks to vehicles.

- `vehicledeployedlocks.keylock.allvehicles` (all in one)
- `vehicledeployedlocks.keylock.attackhelicopter`
- `vehicledeployedlocks.keylock.chinook`
- `vehicledeployedlocks.keylock.duosub`
- `vehicledeployedlocks.keylock.hotairballoon`
- `vehicledeployedlocks.keylock.kayak`
- `vehicledeployedlocks.keylock.locomotive`
- `vehicledeployedlocks.keylock.magnetcrane`
- `vehicledeployedlocks.keylock.minicopter`
- `vehicledeployedlocks.keylock.modularcar`
- `vehicledeployedlocks.keylock.motorbike.sidecar`
- `vehicledeployedlocks.keylock.motorbike`
- `vehicledeployedlocks.keylock.pedalbike`
- `vehicledeployedlocks.keylock.pedaltrike`
- `vehicledeployedlocks.keylock.ptboat`
- `vehicledeployedlocks.keylock.rhib`
- `vehicledeployedlocks.keylock.ridablehorse`
- `vehicledeployedlocks.keylock.rowboat`
- `vehicledeployedlocks.keylock.scraptransport`
- `vehicledeployedlocks.keylock.sedan`
- `vehicledeployedlocks.keylock.sedanrail`
- `vehicledeployedlocks.keylock.snowmobile`
- `vehicledeployedlocks.keylock.solosub`
- `vehicledeployedlocks.keylock.tomaha`
- `vehicledeployedlocks.keylock.tugboat`
- `vehicledeployedlocks.keylock.workcart`
- `vehicledeployedlocks.keylock.workcartaboveground`
- `vehicledeployedlocks.keylock.workcartcovered`

## Configuration

```json
{
  "Allow NPCs to bypass locks": false,
  "Allow deploying locks onto unowned vehicles": true,
  "Allow deploying locks onto vehicles owned by teammates": false,
  "Allow deploying locks onto vehicles owned by other players": false,
  "Require cupboard auth to deploy locks onto unowned vehicles": false,
  "Auto claim unowned vehicles when deploying locks": false,
  "Auto replace vehicle ownership when deploying locks": false,
  "Allow pushing vehicles while locked out": true,
  "Cooldown to auto craft locks (seconds)": 10.0,
  "Modular car settings": {
    "Allow editing while locked out": true
  },
  "Lock sharing settings": {
    "Clan": false,
    "Clan or ally": false,
    "Friends": false,
    "Team": false
  },
  "Auto unlock idle vehicles": {
    "Enabled": false,
    "Unlock after idle time (seconds)": 3600.0,
    "Check interval seconds": 300.0,
    "Exempt owned vehicles": true,
    "Exempt vehicles near cupboards": true
  }
}
```

- `Allow NPCs to bypass locks` (`true` or `false`) -- Whether to allow NPCs to bypass vehicle locks.
- `Allow deploying locks onto unowned vehicles` (`true` or `false`) -- Whether to allow players to deploy a lock onto a vehicle that has no owner (i.e., `OwnerID` is `0`), which usually describes vehicles that spawned naturally in the world, though some plugins may spawn vehicles with no owner as well. Note: Vehicles spawned at NPC vendors have no owner by default, unless set by a plugin such as [Vehicle Vendor Options](https://umod.org/plugins/vehicle-vendor-options). You can also use the [Claim Vehicle](https://umod.org/plugins/claim-vehicle) plugin to allow players to claim unowned vehicles with an optional cooldown.
- `Allow deploying locks onto vehicles owned by teammates` (`true` or `false`) -- Whether to allow players to deploy a lock onto a vehicle owned by a teammate. Note: This setting is effectively `true` while `Allow deploying locks onto vehicles owned by other players` is `true`.
- `Allow deploying locks onto vehicles owned by other players` (`true` or `false`) -- Whether to allow players to deploy a lock onto a vehicle owned by someone else (i.e., a vehicle whose `OwnerID` is a different player's Steam ID). Such vehicles are likely spawned by a plugin, or a plugin allowed the player to claim that vehicle. This is `false` by default to protect owned vehicles from having locks deployed onto them by others. Note: Regardless of this setting, if the owner leaves a code lock unlocked, another player can still lock it with a custom code to lock out the owner.
- `Require cupboard auth to deploy locks onto unowned vehicles` (`true` or `false`) -- Whether to require players to be within TC radius to deploy a lock onto an **unowned** vehicle.
- `Auto claim unowned vehicles when deploying locks` (`true` or `false` -- Whether to automatically assign vehicle ownership to the player deploying the lock, if the vehicle is not already owned. 
- `Auto replace vehicle ownership when deploying locks` (`true` or `false`) -- Whether to automatically assign vehicle ownership to the player deploying the lock, if the vehicle is already owned.
- `Allow pushing vehicles while locked out` (`true` or `false`) -- Whether to allow players to push a vehicle while they are not authorized to the vehicle's lock. This is `true` by default to be consistent with vanilla behavior.
- `Cooldown to auto craft locks (seconds)` -- Cooldown for players to craft a lock if they don't have one in their inventory. Since players can pickup vehicle-deployed locks (by design), this cooldown prevents players from effectively making locks faster than they could normally craft them. Configure this based on the crafting speed of locks on your server.
- `Modular car settings`
  - `AllowEditingWhileLockedOut` -- Whether to allow players to edit a car at a lift while they are not authorized to the car's lock. This is `true` by default to be consistent with the vanilla car locks which allow players to edit the car (which likely allows removal of the lock). Setting this to `false` will make it impossible for unauthorized players to edit the car.
- `Lock sharing settings` (each `true` or `false`) -- Whether to allow players to bypass locks placed by their clanmates, ally clanmates, friends or teammates.
- `Auto unlock idle vehicles` -- Settings to automatically detect idle vehicles and unlock them.
  - `Enabled` (`true` or `false`) -- While `true`, vehicles will periodically be checked for idleness and potentially unlocked.
  - `Unlock after idle time (seconds)` -- Determines how long after a vehicle has been used that it will be considered idle. Supports all vehicles **except** Sedan which does not track activity information in vanilla.
  - `Check interval seconds` -- How often to check vehicles for idleness.
  - `Exempt owned vehicles` -- While `true`, owned vehicles are exempt from idleness checks.
  - `Exempt vehicles near cupboards` -- While `true`, vehicles near TCs are exempt from idleness checks.

## Localization

```json
{
  "Generic.Error.NoPermission": "You don't have permission to do that.",
  "Generic.Error.BuildingBlocked": "Error: Cannot do that while building blocked.",
  "Generic.Error.Cooldown": "Please wait <color=red>{0}s</color> and try again.",
  "Generic.Error.VehicleLocked": "That vehicle is locked.",
  "Deploy.Error.NoVehicleFound": "Error: No vehicle found.",
  "Deploy.Error.VehicleDead": "Error: That vehicle is dead.",
  "Deploy.Error.Other": "Error: You cannot do that.",
  "Deploy.Error.DifferentOwner": "Error: Someone else owns that vehicle.",
  "Deploy.Error.NoOwner": "Error: You do not own that vehicle.",
  "Deploy.Error.NoOwner.NoBuildingPrivilege": "Error: Locking unowned vehicles requires building privilege.",
  "Deploy.Error.HasLock": "Error: That vehicle already has a lock.",
  "Deploy.Error.InsufficientResources": "Error: Not enough resources to craft a {0}.",
  "Deploy.Error.Mounted": "Error: That vehicle is currently occupied.",
  "Deploy.Error.ModularCar.NoCockpit": "Error: That car needs a cockpit module to receive a lock.",
  "Deploy.Error.Distance": "Error: Too far away."
}
```

## Developer API

#### API_DeployCodeLock / API_DeployKeyLock

Plugins can call these APIs to deploy a lock onto a supported vehicle. The `BasePlayer` parameter is optional, but providing it is recommended as it allows for potential compatibility with auto-lock plugins, and allows the player to access the key lock without a key.

Note: These will skip several checks, such as permissions, whether the player is building blocked, and whether the vehicle is mounted. This allows your plugin to use discretion to determine whether the player should be allowed to deploy a particular lock onto a particular vehicle.

```csharp
CodeLock API_DeployCodeLock(BaseEntity vehicle, BasePlayer player, bool isFree = true)
KeyLock API_DeployKeyLock(BaseEntity vehicle, BasePlayer player, bool isFree = true)
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
bool API_CanPlayerDeployCodeLock(BasePlayer player, BaseEntity vehicle)
bool API_CanPlayerDeployKeyLock(BasePlayer player, BaseEntity vehicle)
```

#### API_CanAccessVehicle

Plugins can call this API to determine whether a player has authorization to a possibly locked vehicle.

```csharp
bool API_CanAccessVehicle(BasePlayer player, BaseEntity vehicle, bool provideFeedback = true)
```

Returns `true` if any of the following criteria are met, else returns `false`.
- The vehicle has no lock
- The lock is unlocked
- The player has direct authorization to the lock
- The lock owner is sharing access with the player
- The `CanUseLockedEntity` hook returned `true`

If `provideFeedback` is true, the lock will play an access granted or denied sound effect, and the player will be sent a chat message if they do not have access.

#### API_RegisterCustomVehicleType (Experimental)

Plugins can call this API to register custom vehicle types.

```csharp
void API_RegisterCustomVehicleType(string vehicleType, Vector3 lockPosition, Quaternion lockRotation, string parentBone, Func<BaseEntity, BaseEntity> determineLockParent)
```

How it works:
- Your plugin should call this API once to register itself with Vehicle Deployed Locks
  - It's safe to call more than once, but only once is necessary
- Vehicle Deployed Locks will automatically register the following permissions
  - `vehicledeployedlocks.codelock.<vehicleType>`
  - `vehicledeployedlocks.keylock.<vehicleType>`
- The `determineLockParent` function will be called at two possible times, listed below
  - When a player attempts to deploy a lock onto an unrecognized entity
    - If the function returns an entity (as opposed to `null`), this indicates to Vehicle Deployed Locks that the entity can receive a lock
  - When a player attempts to mount or use an object with an unrecognized parent
    - If the function returns an entity (as opposed to `null`), this indicates to Vehicle Deployed Locks that it can find the lock on that entity, for authorization purposes
- The `lockPosition` and `lockRotation` should be relative to the entity that is returned by `determineLockParent`
- The `parentBone` can be a string referring to the bone of the parent entity that the lock should be positioned relative to
  - For example, this plugin uses specific bones for attaching locks to Horses and Magnet Cranes
  - Set to `null` if you don't need to specify a bone (most common)

Example:

```csharp
[PluginReference]
Plugin VehicleDeployedLocks;

void OnServerInitialized()
{
    RegisterWithVehicleDeployedLocks();
}

void OnPluginLoaded(Plugin plugin)
{
    if (plugin == VehicleDeployedLocks)
        RegisterWithVehicleDeployedLocks();
}

void RegisterWithVehicleDeployedLocks()
{
    if (VehicleDeployedLocks == null)
        return;

    Func<BaseEntity, BaseEntity> determineLockParent = (entity) =>
    {
        var computerStation = entity as ComputerStation;
        if (computerStation == null)
            return null;

        // Only return non-null when this is a custom vehicle.
        return computerStation.GetParentEntity() as Drone;
    };

    VehicleDeployedLocks.Call("API_RegisterCustomVehicleType", "megadrone", LockPosition, LockRotation, null, determineLockParent);
}
```

## Hooks

#### CanDeployVehicleCodeLock / CanDeployVehicleKeyLock

- Called when a player or a plugin tries to deploy a lock onto a vehicle.
- Returning `false` will prevent the lock from being deployed. None of the player's items will be consumed.
- Returning `null` will result in the default behavior.

```csharp
bool? CanDeployVehicleCodeLock(BaseEntity vehicle, BasePlayer player)
bool? CanDeployVehicleKeyLock(BaseEntity vehicle, BasePlayer player)
```

You can replace the `BaseEntity` type with a more specific one to only run your hook method for specific vehicle types.

#### OnVehicleLockDeployed

Called when a player or a plugin deploys a lock onto a vehicle.

```csharp
void OnVehicleLockDeployed(BaseEntity vehicle, BaseLock baseLock)
```

You can replace the `BaseEntity` and `BaseLock` types with more specific ones to only run your hook method for specific vehicle/lock types.

#### OnItemDeployed

This is an Oxide hook that is normally called when deploying a lock or other deployable. To allow for compatibility with other plugins, this plugin calls this hook whenever a code lock is deployed onto a vehicle for a player.

```csharp
void OnItemDeployed(Deployer deployer, BaseEntity entity, BaseLock baseLock)
```

Note: This is not called when a lock is deployed via the API without specifying a player. For that, use `OnVehicleLockDeployed`.

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
