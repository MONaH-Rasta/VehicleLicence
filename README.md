# VehicleLicence

Oxide plugin for Rust. Allows players to buy, spawn and recall: Boat, RHIB, Sedan, HotAirBalloon, MiniCopter, CH47, etc.

## Permissions

**Permissions for each vehicle can be changed in the configuration**

* `vehiclelicence.use` --  Basic permissions. You must have it to use this plugin
* `vehiclelicence.bypasscost` -- Purchase all vehicles at no cost.
* `vehiclelicence.all` -- Players with this permission can purchase all vehicles.
* `vehiclelicence.admin` -- Bypass looting and mounting detection

## Commands

### Universal Chat Command

**Universal command:  When you type /mini, if you don't own that minicopter the plugin will try to buy it for you before spawning it. If you already have one spawned the plugin will try to recall it to you.**

`"Use Universal Chat Command" = true`  --->  Enable Universal Chat Command

```json
 // If universal chat commands are enabled. /mini and /minicopter will become a universal command
 
"Commands": [ 
        "mini",
        "minicopter"
       ]
```

`"Custom Kill Chat Command Prefix": "no",`   -->  /nomini and /nominicopter will become the command to kill the vehicle

### Chat Commands

* `/license` -- Show help to the player.
* `/buy` -- Show help for buy commands. Buy a license
* `/spawn` -- Show help for spawn commands. Spawn a vehicle
* `/recall` -- Show help for recall commands. Recall vehicles to the vicinity of the player
* `/kill` -- Show help for kill commands. Kill the player's vehicle

**For historical reasons, the Recall command here is to move the vehicle near the player, and the Kill command is to delete the vehicle**

### Console Commands

* `vl.buy` -- Similar to /buy.
* `vl.buy <vehicle type> all` --  Buy the vehicle license for all players
* `vl.buy <vehicle type> <player name or id>` -- Bypass the cost and buy the vehicle license directly. Used for GUIShop and ServerRewards... e.g. 'vl.buy rhib 76561198924840872'
* `vl.remove <vehicle type> all` --  Remove the vehicle license for all players
* `vl.remove <vehicle type> <player name or id>` -- Remove the player's vehicle license
* `vl.spawn` -- Similar to /spawn.
* `vl.recall` -- Similar to /recall.
* `vl.kill` -- Similar to /kill.

```json
Vehicles include normal vehicles and modular vehicles :
 Normal Vehicle Type：
            Rowboat,
            Tugboat,
            RHIB,
            Sedan,
            HotAirBalloon,
            ArmoredHotAirBalloon,
            MiniCopter,
            AttackHelicopter,
            TransportHelicopter,
            Chinook,
            RidableHorse,
            WorkCart,
            MagnetCrane,
            SubmarineSolo,
            SubmarineDuo,
            Snowmobile,
            TomahaSnowmobile,
 Modular Vehicle Type： In your config. Is a custom type
```

```json
Train components are of the following types:
            Engine,
            CoveredEngine,
            Locomotive,
            WagonA,
            WagonB,
            WagonC,
            Unloadable,
            UnloadableLoot,
            UnloadableFuel,
            Caboose
```

## Configuration
  
### Economics and Server Rewards

Use it as a price. e.g.:

```json
      "Purchase Prices": {
        "scrap": { //item short name
          "amount": 1000,
          "displayName": "Scrap"
        },
        "Economics": { //use Economics
          "amount": 1000,
          "displayName": "Money"
        },
        "ServerRewards": { //use ServerRewards
          "amount": 1000,
          "displayName": "RP"
        },
       }
```

### Configuration Details

**Note:** Please do not set the cooldown of the vehicle to 0, as this is very bad.  **[Detail](https://umod.org/community/vehicle-license/23761-warning-to-all-people-who-use-this-plugin-major-lag-if-you-have-cooldowns-set-to-0)**

```json
{
  "Settings": {
    "Store Vehicle On Plugin Unloaded / Server Restart": true,
    "Clear Vehicle Data On Map Wipe": false,
    "Interval to check vehicle for wipe (Seconds)": 300.0, //Interval in seconds at which the plugin checks the vehicles to delete those abandoned. 
    "Spawn vehicle in the direction you are looking at": true,//If false, Vehicles will spawn randomly around the player
    
    "Automatically claim vehicles purchased from vehicle vendors": false,//It will only be automatically claimed if you have purchased the vehicle license but the vehicle is not outside
    "Vehicle vendor purchases will unlock the license for the player": false,//If you haven't purchased the license, it will automatically unlock it
    "Limit the number of vehicles at any one time": 0,
    
    "Prevent vehicles from damaging players": true,
    "Prevent vehicles from spawning or recalling in safe zone": true,
    "Prevent vehicles from spawning or recalling when the player are building blocked": true,
    "Prevent vehicles from spawning or recalling when the player is mounted or parented": true,
    
    "Check if any player mounted when recalling a vehicle": true,
    "Check if any player mounted when killing a vehicle": true,
    "Dismount all players when a vehicle is recalled": true,
    
    "Prevent other players from mounting vehicle": true,
    "Prevent mounting on driver's seat only": true,//Required "Prevent other players from mounting vehicle" is true
    "Prevent other players from looting fuel container and inventory": true,
    "Use Teams": false,//Used for "Prevent other players from mounting vehicle" and "Prevent other players from looting fuel container and inventory"
    "Use Clans": true,//Used for "Prevent other players from mounting vehicle" and "Prevent other players from looting fuel container and inventory"
    "Use Friends": true,//Used for "Prevent other players from mounting vehicle" and "Prevent other players from looting fuel container and inventory"
    
    
    "Vehicle No Decay": false,
    "Vehicle No Fire Ball": true,
    "Vehicle No Server Gibs": true,
    "Chinook No Map Marker": true,
    
    "Use Raid Blocker (Need NoEscape Plugin)": false,//Use NoEscape Raid Blocker functionallity.
    "Use Combat Blocker (Need NoEscape Plugin)": false//Use NoEscape Combat Blocker functionallity.
  },
  "Chat Settings": {
    "Use Universal Chat Command": true,
    "Help Chat Command": "license",
    "Buy Chat Command": "buy",
    "Spawn Chat Command": "spawn",
    "Recall Chat Command": "recall",
    "Kill Chat Command": "kill",
    "Custom Kill Chat Command Prefix": "no",//usage: /nomini, /norhib, /nocha47
    "Bypass Cooldown Command": "pay",//usage:: /mini pay or /spawn mini pay
    "Chat Prefix": "<color=#00FFFF>[VehicleLicense]</color>: ",
    "Chat SteamID Icon": 76561198924840872
  },
  "Normal Vehicle Settings": {//This is fixed and no new can be added
    "Rowboat Vehicle": {
      "Purchasable": true,//The vehicle is purchasable.
      "Display Name": "Row Boat",//It will display in chat.
      "Use Permission": true,
      "Permission": "vehiclelicence.rowboat",
      "Distance To Spawn": 5.0,
      "Time Before Vehicle Wipe (Seconds)": 0.0,//0 is disabled
      "Maximum Health": 0.0,//0 is disabled
      "Can Recall Maximum Distance": 0.0,//0 is disabled
      "Can Kill Maximum Distance": 0.0,//0 is disabled
      "Minimum distance from player to recall or spawn": 3.0,//Prevent vehicles from spawning or recalling near another player
      "Remove License Once Crashed": false,//If enabled, it will remove license the vehicle once crashed
      "Purchase Prices": {//You can add more prices
        "scrap": {
          "amount": 500,
          "displayName": "Scrap"
        },
        "economics": {
          "amount": 50,
          "displayName": "Money"
        },
      },
      "Spawn Prices": {},//Similar to "Purchase Prices"
      "Recall Prices": {},//Similar to "Purchase Prices"
      "Recall Cooldown Bypass Prices": {},//Similar to "Purchase Prices"
      "Spawn Cooldown Bypass Prices": {},//Similar to "Purchase Prices"
      "Commands": [//Chat command options. e.g. "/buy row" or "/buy rowboat"
        "row",
        "rowboat"
      ],
      "Spawn Cooldown (Seconds)": 300.0,//Setting it to 0 is not recommended
      "Recall Cooldown (Seconds)": 30.0,//Setting it to 0 is not recommended
      "Cooldown Permissions": {
        "vehiclelicence.vip": {
          "spawnCooldown": 150.0,
          "recallCooldown": 10.0
        },
        "vehiclelicence.vip1": {
          "spawnCooldown": 50.0,
          "recallCooldown": 5.0
        },
      },
      "Amount Of Fuel To Spawn": 0,
      "Refund Fuel On Kill": true,
      "Refund Fuel On Crash": true,
      "Refund Inventory On Kill": true,
      "Refund Inventory On Crash": true,
      "Drop Inventory Items When Vehicle Recall": true
    }
  },
  "Modular Vehicle Settings": {//This is not fixed, new ones can be added
    "SmallCar": {//Modular Vehicle Type. It's up to you
      "Chassis Type (Small, Medium, Large)": "Small", // Three types of chassis
      "Vehicle Module Items": [//The default module for a modular vehicle
        {
          "shortName": "vehicle.1mod.cockpit.with.engine",//Item short name
          "healthPercentage": 50.0 //Module health percentage must not be greater than 100
        },
        {
          "shortName": "vehicle.1mod.storage",
          "healthPercentage": 50.0
        }
      ],
      "Vehicle Engine Items": [//Preset engine items, add them to the engine when you spawn the vehicle
        {
          "shortName": "carburetor1",//Item short name
          "conditionPercentage": 20.0 //Engine item condition percentage
        },
        {
          "shortName": "crankshaft1",
          "conditionPercentage": 20.0
        },
        {
          "shortName": "piston1",
          "conditionPercentage": 20.0
        },
        {
          "shortName": "sparkplug1",
          "conditionPercentage": 20.0
        },
        {
          "shortName": "valve1",
          "conditionPercentage": 20.0
        }
      ]
    }
  },  
  "Train Vehicle Settings": {
    "CompleteTrain": {
      "Train Components": [ // Custom components for trains
        {
          "type": "Engine" // When you recall, only the first component can be recalled.
        },
        {
          "type": "WagonA"
        },
        {
          "type": "WagonB"
        },
        {
          "type": "WagonC"
        },
        {
          "type": "Unloadable"
        },
        {
          "type": "UnloadableLoot"
        }
      ]
    }
  },
}
```

## Localization

```json
{
  "Help": "These are the available commands:",
  "HelpLicence1": "<color=#4DFF4D>/{0}</color> -- To buy a vehicle",
  "HelpLicence2": "<color=#4DFF4D>/{0}</color> -- To spawn a vehicle",
  "HelpLicence3": "<color=#4DFF4D>/{0}</color> -- To recall a vehicle",
  "HelpLicence4": "<color=#4DFF4D>/{0}</color> -- To kill a vehicle",
  "HelpLicence5": "<color=#4DFF4D>/{0}</color> -- To buy, spawn or recall a <color=#009EFF>{1}</color>",
  "HelpBuy": "<color=#4DFF4D>/{0} {1}</color> -- To buy a <color=#009EFF>{2}</color>",
  "HelpBuyPrice": "<color=#4DFF4D>/{0} {1}</color> -- To buy a <color=#009EFF>{2}</color>. Price: {3}",
  "HelpSpawn": "<color=#4DFF4D>/{0} {1}</color> -- To spawn a <color=#009EFF>{2}</color>",
  "HelpSpawnPrice": "<color=#4DFF4D>/{0} {1}</color> -- To spawn a <color=#009EFF>{2}</color>. Price: {3}",
  "HelpRecall": "<color=#4DFF4D>/{0} {1}</color> -- To recall a <color=#009EFF>{2}</color>",
  "HelpRecallPrice": "<color=#4DFF4D>/{0} {1}</color> -- To recall a <color=#009EFF>{2}</color>. Price: {3}",
  "HelpKill": "<color=#4DFF4D>/{0} {1}</color> -- To kill a <color=#009EFF>{2}</color>",
  "NotAllowed": "You do not have permission to use this command.",
  "RaidBlocked": "<color=#FF1919>You may not do that while raid blocked</color>.",
  "CombatBlocked": "<color=#FF1919>You may not do that while combat blocked</color>.",
  "OptionNotFound": "This '<color=#009EFF>{0}</color>' option doesn't exist.",
  "VehiclePurchased": "You have purchased a <color=#009EFF>{0}</color>, type <color=#4DFF4D>/{1}</color> for more information.",
  "VehicleAlreadyPurchased": "You have already purchased <color=#009EFF>{0}</color>.",
  "VehicleCannotBeBought": "<color=#009EFF>{0}</color> is unpurchasable",
  "VehicleNotOut": "<color=#009EFF>{0}</color> is not out, type <color=#4DFF4D>/{1}</color> for more information.",
  "AlreadyVehicleOut": "You already have a <color=#009EFF>{0}</color> outside, type <color=#4DFF4D>/{1}</color> for more information.",
  "VehicleNotYetPurchased": "You have not yet purchased a <color=#009EFF>{0}</color>, type <color=#4DFF4D>/{1}</color> for more information.",
  "VehicleSpawned": "You spawned your <color=#009EFF>{0}</color>.",
  "VehicleRecalled": "You recalled your <color=#009EFF>{0}</color>.",
  "VehicleKilled": "You killed your <color=#009EFF>{0}</color>.",
  "VehicleOnSpawnCooldown": "You must wait <color=#FF1919>{0}</color> seconds before you can spawn your <color=#009EFF>{1}</color>.",
  "VehicleOnRecallCooldown": "You must wait <color=#FF1919>{0}</color> seconds before you can recall your <color=#009EFF>{1}</color>.",
  "NotLookingAtWater": "You must be looking at water to spawn or recall a <color=#009EFF>{0}</color>.",
  "BuildingBlocked": "You can't spawn a <color=#009EFF>{0}</color> appear if you don't have the building privileges.",
  "RefundedVehicleItems": "Your <color=#009EFF>{0}</color> vehicle items was refunded to your inventory.",
  "PlayerMountedOnVehicle": "It cannot be recalled when players mounted on your <color=#009EFF>{0}</color>.",
  "PlayerInSafeZone": "You cannot spawn or recall your <color=#009EFF>{0}</color> in the safe zone.",
  "VehicleInventoryDropped": "Your <color=#009EFF>{0}</color> vehicle inventory cannot be recalled, it have dropped to the ground.",
  "NoResourcesToPurchaseVehicle": "You don't have enough resources to buy a <color=#009EFF>{0}</color>. You are missing: {1}",
  "NoResourcesToSpawnVehicle": "You don't have enough resources to spawn a <color=#009EFF>{0}</color>. You are missing: {1}",
  "NoResourcesToRecallVehicle": "You don't have enough resources to recall a <color=#009EFF>{0}</color>. You are missing: {1}",
  "MountedOrParented": "You cannot spawn or recall a <color=#009EFF>{0}</color> when mounted or parented.",
  "RecallTooFar": "You must be within <color=#FF1919>{0}</color> meters of <color=#009EFF>{1}</color> to recall."
}
```

 If you created another language file, please send it to me and I will add it to the plugin for others to use.

## Hooks

```csharp
        private object CanLicensedVehicleSpawn(BasePlayer player, string vehicleType, Vector3 position, Quaternion rotation)
        private void OnLicensedVehicleSpawned(BaseEntity entity, BasePlayer player, string vehicleType)
        private object CanLicensedVehicleRecall(BaseEntity entity, BasePlayer player, string vehicleType, Vector3 position, Quaternion rotation)
        private void OnLicensedVehicleRecalled(BaseEntity entity, BasePlayer player, string vehicleType)
```

## Credits

* **[Hetachi](https://umod.org/user/Hetachi)**, for the original idea
* **Sorrow**, the original author of this plugin
* **Arainrr**, previous maintainer
