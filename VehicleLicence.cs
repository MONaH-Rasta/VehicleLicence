using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vehicle Licence", "Sorrow/TheDoc/Arainrr", "1.5.0")]
    [Description("Allows players to buy vehicles and then spawn or store it")]
    public class VehicleLicence : RustPlugin
    {
        #region Fields

        [PluginReference] private readonly Plugin Economics, ServerRewards, Friends, Clans, NoEscape;
        private const string PERMISSION_USE = "vehiclelicence.use";
        private const string PERMISSION_ALL = "vehiclelicence.all";
        private const string PERMISSION_BYPASS_COST = "vehiclelicence.bypasscost";

        private const string PREFAB_ROWBOAT = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        private const string PREFAB_RHIB = "assets/content/vehicles/boats/rhib/rhib.prefab";
        private const string PREFAB_SEDAN = "assets/content/vehicles/sedan_a/sedantest.entity.prefab";
        private const string PREFAB_HOTAIRBALLOON = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab";
        private const string PREFAB_MINICOPTER = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private const string PREFAB_TRANSPORTCOPTER = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string PREFAB_CHINOOK = "assets/prefabs/npc/ch47/ch47.entity.prefab";
        private const string PREFAB_RIDABLEHORSE = "assets/rust.ai/nextai/testridablehorse.prefab";
        private const string PREFAB_ITEM_DROP = "assets/prefabs/misc/item drop/item_drop.prefab";

        private readonly Dictionary<BaseEntity, Vehicle> vehiclesCache = new Dictionary<BaseEntity, Vehicle>();
        private readonly static int LAYER_GROUND = Rust.Layers.Solid | Rust.Layers.Mask.Water;

        private enum VehicleType
        {
            Rowboat,
            RHIB,
            Sedan,
            HotAirBalloon,
            MiniCopter,
            TransportHelicopter,
            Chinook,
            RidableHorse
        }

        #endregion Fields

        #region Oxide Hooks

        private void Init()
        {
            LoadData();
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_ALL, this);
            permission.RegisterPermission(PERMISSION_BYPASS_COST, this);
            foreach (var vehicleS in configData.vehicleS.Values)
            {
                if (string.IsNullOrEmpty(vehicleS.permission)) continue;
                permission.RegisterPermission(vehicleS.permission, this);
            }
            foreach (var perm in configData.permCooldown.Keys)
                permission.RegisterPermission(perm, this);
            if (configData.chatS.useUniversalCommand)
            {
                foreach (var command in configData.vehicleS.Values.SelectMany(x => x.commands))
                {
                    if (string.IsNullOrEmpty(command)) continue;
                    cmd.AddChatCommand(command, this, nameof(CmdUniversal));
                }
            }
            cmd.AddChatCommand(configData.chatS.helpCommand, this, nameof(CmdLicenceHelp));
            cmd.AddChatCommand(configData.chatS.buyCommand, this, nameof(CmdBuyVehicle));
            cmd.AddChatCommand(configData.chatS.spawnCommand, this, nameof(CmdSpawnVehicle));
            cmd.AddChatCommand(configData.chatS.recallCommand, this, nameof(CmdRecallVehicle));
            cmd.AddChatCommand(configData.chatS.killCommand, this, nameof(CmdKillVehicle));
        }

        private void OnServerInitialized()
        {
            if (!configData.globalS.preventMounting) Unsubscribe(nameof(CanMountEntity));
            if (configData.globalS.checkVehiclesTime > 0) CheckVehicles();
            else Unsubscribe(nameof(OnEntityDismounted));
            if (!configData.globalS.noDecay) Unsubscribe(nameof(OnEntityTakeDamage));
        }

        private void Unload()
        {
            foreach (var entry in vehiclesCache.ToList())
            {
                if (entry.Key != null && !entry.Key.IsDestroyed)
                {
                    RefundFuel(entry.Key, entry.Value);
                    entry.Key.Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }
            SaveData();
        }

        private void OnServerSave() => timer.Once(UnityEngine.Random.Range(0f, 60f), SaveData);

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;
            if (permission.UserHasPermission(player.UserIDString, PERMISSION_BYPASS_COST))
                PurchaseAllVehicles(player.userID);
        }

        private void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            var vehicleParent = entity?.VehicleParent();
            if (vehicleParent == null || vehicleParent.IsDestroyed) return;
            if (!vehiclesCache.ContainsKey(vehicleParent)) return;
            vehiclesCache[vehicleParent].OnDismount();
        }

        private object CanMountEntity(BasePlayer friend, BaseMountable entity)
        {
            var vehicleParent = entity?.VehicleParent();
            if (vehicleParent == null || vehicleParent.IsDestroyed) return null;
            if (!vehiclesCache.ContainsKey(vehicleParent)) return null;
            var ownerID = vehiclesCache[vehicleParent].playerID;
            if (AreFriends(ownerID, friend.userID)) return null;
            if (configData.globalS.blockDriverSeat && vehicleParent.HasMountPoints() && entity != vehicleParent.mountPoints[0].mountable) return null;
            return false;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null) return;
            if (!vehiclesCache.ContainsKey(entity)) return;
            if (hitInfo?.damageTypes?.Get(Rust.DamageType.Decay) > 0)
                hitInfo.damageTypes.Scale(Rust.DamageType.Decay, 0);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info) => CheckEntity(entity, true);

        private void OnEntityKill(BaseEntity entity) => CheckEntity(entity);

        #endregion Oxide Hooks

        #region Update Old Data

        private void UpdataOldData()
        {
            Dictionary<ulong, LicencedPlayer> licencedPlayer;
            try { licencedPlayer = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, LicencedPlayer>>(Name); }
            catch { return; }
            foreach (var entry in licencedPlayer)
            {
                if (entry.Value.Vehicles.Count > 0)
                {
                    var vehicleTypes = new Dictionary<VehicleType, Vehicle>();
                    foreach (var vehicle in entry.Value.Vehicles)
                        vehicleTypes.Add(GetVehiclePrefab(vehicle.Value.Prefab), new Vehicle());
                    storedData.playerData.Add(entry.Key, vehicleTypes);
                }
            }
            SaveData();
        }

        private class LicencedPlayer
        {
            public readonly ulong Userid;
            public Dictionary<string, Vehicle1> Vehicles;
        }

        private class Vehicle1
        {
            public ulong Userid;
            public string Prefab;
            public uint Id;
            public TimeSpan Spawned;
            public DateTime LastDismount;
        }

        private VehicleType GetVehiclePrefab(string prefab)
        {
            switch (prefab)
            {
                case PREFAB_ROWBOAT: return VehicleType.Rowboat;
                case PREFAB_RHIB: return VehicleType.RHIB;
                case PREFAB_SEDAN: return VehicleType.Sedan;
                case PREFAB_HOTAIRBALLOON: return VehicleType.HotAirBalloon;
                case PREFAB_MINICOPTER: return VehicleType.MiniCopter;
                case PREFAB_TRANSPORTCOPTER: return VehicleType.TransportHelicopter;
                case PREFAB_CHINOOK: return VehicleType.Chinook;
                case PREFAB_RIDABLEHORSE: return VehicleType.RidableHorse;
            }
            return default(VehicleType);
        }

        #endregion Update Old Data

        #region Helpers

        private void CheckEntity(BaseEntity entity, bool onCrash = false)
        {
            if (entity == null) return;
            Vehicle vehicle;
            if (!vehiclesCache.TryGetValue(entity, out vehicle)) return;
            vehiclesCache.Remove(entity);
            if (!onCrash || !configData.globalS.notRefundFuelOnCrash)
                RefundFuel(entity, vehicle);
            Dictionary<VehicleType, Vehicle> vehicles;
            if (storedData.playerData.TryGetValue(vehicle.playerID, out vehicles) && vehicles.ContainsKey(vehicle.vehicleType))
            {
                if (onCrash && configData.globalS.removeVehicleOnCrash)
                    vehicles.Remove(vehicle.vehicleType);
                else vehicles[vehicle.vehicleType].OnDeath();
            }
        }

        private void PurchaseAllVehicles(ulong playerID)
        {
            Dictionary<VehicleType, Vehicle> vehicles;
            var array = Enum.GetValues(typeof(VehicleType));
            if (!storedData.playerData.TryGetValue(playerID, out vehicles))
            {
                vehicles = new Dictionary<VehicleType, Vehicle>();
                foreach (VehicleType vehicleType in array)
                    vehicles.Add(vehicleType, new Vehicle());
                storedData.playerData.Add(playerID, vehicles);
            }
            else
            {
                if (vehicles.Count == array.Length) return;
                foreach (VehicleType vehicleType in array)
                {
                    if (!vehicles.ContainsKey(vehicleType))
                        vehicles.Add(vehicleType, new Vehicle());
                }
            }
            SaveData();
        }

        private void CheckVehicles()
        {
            foreach (var entry in vehiclesCache.ToList())
            {
                if (entry.Key == null || entry.Key.IsDestroyed) continue;
                if (VehicleAnyMounted(entry.Key)) continue;
                if (VehicleIsActive(entry.Value)) continue;
                RefundFuel(entry.Key, entry.Value);
                entry.Key.Kill(BaseNetworkable.DestroyMode.Gib);
            }
            timer.Once(configData.globalS.checkVehiclesTime, CheckVehicles);
        }

        private bool VehicleIsActive(Vehicle vehicle)
        {
            var vehicleS = configData.vehicleS[vehicle.vehicleType];
            if (vehicleS.wipeTime <= 0) return true;
            return TimeEx.currentTimestamp - vehicle.lastDismount < vehicleS.wipeTime;
        }

        private void RefundFuel(BaseEntity entity, Vehicle vehicle)
        {
            ItemContainer itemContainer = null;
            switch (vehicle.vehicleType)
            {
                case VehicleType.Sedan:
                case VehicleType.Chinook:
                    return;

                case VehicleType.MiniCopter:
                case VehicleType.TransportHelicopter:
                    itemContainer = (entity as MiniCopter)?.fuelStorageInstance.Get(true)?.GetComponent<StorageContainer>()?.inventory;
                    break;

                case VehicleType.HotAirBalloon:
                    itemContainer = (entity as HotAirBalloon)?.fuelStorageInstance.Get(true)?.GetComponent<StorageContainer>()?.inventory;
                    break;

                case VehicleType.RHIB:
                case VehicleType.Rowboat:
                    itemContainer = (entity as MotorRowboat)?.fuelStorageInstance.Get(true)?.GetComponent<StorageContainer>()?.inventory;
                    break;

                case VehicleType.RidableHorse:
                    itemContainer = (entity as RidableHorse)?.inventory;
                    break;
            }
            if (itemContainer == null) return;
            var player = RustCore.FindPlayerById(vehicle.playerID);
            if (player == null) itemContainer.Drop(PREFAB_ITEM_DROP, entity.GetDropPosition(), entity.transform.rotation);
            else if (itemContainer.itemList?.Count > 0)
            {
                foreach (var item in itemContainer.itemList.ToList())
                    player.GiveItem(item);
                Print(player, Lang("RefundedVehicleFuel", player.UserIDString, configData.vehicleS[vehicle.vehicleType].displayName));
            }
        }

        private bool IsBlocked(BasePlayer player)
        {
            if (configData.globalS.useRaidBlocker && IsRaidBlocked(player.UserIDString))
            {
                Print(player, Lang("RaidBlocked", player.UserIDString));
                return true;
            }
            if (configData.globalS.useCombatBlocker && IsCombatBlocked(player.UserIDString))
            {
                Print(player, Lang("CombatBlocked", player.UserIDString));
                return true;
            }
            return false;
        }

        private bool IsRaidBlocked(string playerID) => (bool)(NoEscape?.Call("IsRaidBlocked", playerID) ?? false);

        private bool IsCombatBlocked(string playerID) => (bool)(NoEscape?.Call("IsCombatBlocked", playerID) ?? false);

        #region Methods

        private bool HasPermission(BasePlayer player, VehicleType vehicleType)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            if (!vehicleS.usePermission || string.IsNullOrEmpty(vehicleS.permission)) return true;
            if (permission.UserHasPermission(player.UserIDString, PERMISSION_ALL)) return true;
            return permission.UserHasPermission(player.UserIDString, vehicleS.permission);
        }

        private string GetVehiclePrefab(VehicleType vehicleType)
        {
            switch (vehicleType)
            {
                case VehicleType.Rowboat: return PREFAB_ROWBOAT;
                case VehicleType.RHIB: return PREFAB_RHIB;
                case VehicleType.Sedan: return PREFAB_SEDAN;
                case VehicleType.HotAirBalloon: return PREFAB_HOTAIRBALLOON;
                case VehicleType.MiniCopter: return PREFAB_MINICOPTER;
                case VehicleType.TransportHelicopter: return PREFAB_TRANSPORTCOPTER;
                case VehicleType.Chinook: return PREFAB_CHINOOK;
                case VehicleType.RidableHorse: return PREFAB_RIDABLEHORSE;
            }
            return string.Empty;
        }

        private bool AreFriends(ulong playerID, ulong friendID)
        {
            if (playerID == friendID) return true;
            if (configData.globalS.useTeams && SameTeam(playerID, friendID)) return true;
            if (configData.globalS.useFriends && HasFriend(playerID, friendID)) return true;
            if (configData.globalS.useClans && SameClan(playerID, friendID)) return true;
            return false;
        }

        private bool SameTeam(ulong playerID, ulong friendID)
        {
            if (!RelationshipManager.TeamsEnabled()) return false;
            var playerTeam = RelationshipManager.Instance.FindPlayersTeam(playerID);
            if (playerTeam == null) return false;
            var friendTeam = RelationshipManager.Instance.FindPlayersTeam(friendID);
            if (friendTeam == null) return false;
            return playerTeam == friendTeam;
        }

        private bool HasFriend(ulong playerID, ulong friendID)
        {
            if (Friends == null) return false;
            return (bool)Friends.Call("HasFriend", playerID, friendID);
        }

        private bool SameClan(ulong playerID, ulong friendID)
        {
            if (Clans == null) return false;
            //Clans
            var isMember = Clans.Call("IsClanMember", playerID.ToString(), friendID.ToString());
            if (isMember != null) return (bool)isMember;
            //Rust:IO Clans
            var playerClan = Clans.Call("GetClanOf", playerID);
            if (playerClan == null) return false;
            var friendClan = Clans.Call("GetClanOf", friendID);
            if (friendClan == null) return false;
            return (string)playerClan == (string)friendClan;
        }

        private bool VehicleAnyMounted(BaseEntity entity)
        {
            if (entity is BaseVehicle) return (entity as BaseVehicle).AnyMounted();
            List<BasePlayer> players = Facepunch.Pool.GetList<BasePlayer>();
            Vis.Entities(entity.transform.position, 2.5f, players, Rust.Layers.Server.Players);
            bool flag = players.Count > 0;
            Facepunch.Pool.FreeList(ref players);
            return flag;
        }

        private double GetPermCooldown(BasePlayer player, VehicleType vehicleType, double defaultCooldown)
        {
            float cooldown;
            foreach (var entry in configData.permCooldown)
            {
                if (entry.Value.TryGetValue(vehicleType, out cooldown) && defaultCooldown > cooldown && permission.UserHasPermission(player.UserIDString, entry.Key))
                    defaultCooldown = cooldown;
            }
            return defaultCooldown;
        }

        private Vector3 GetLookingAtGroundPos(BasePlayer player, float distance)
        {
            RaycastHit hit;
            Ray ray = player.eyes.HeadRay();
            if (Physics.Raycast(ray, out hit, distance, LAYER_GROUND))
                return hit.point;
            var position = ray.origin + ray.direction * distance;
            if (Physics.Raycast(position + Vector3.up * 100, Vector3.down, out hit, 200, LAYER_GROUND))
                return hit.point;
            position.y = TerrainMeta.HeightMap.GetHeight(position);
            return position;
        }

        private bool IsLookingAtWater(BasePlayer player, float distance)
        {
            Vector3 lookingAt = GetLookingAtGroundPos(player, distance);
            return WaterLevel.Test(lookingAt);
        }

        private Vector3 GetGroundPosition(Vector3 position)
        {
            RaycastHit hitInfo;
            if (Physics.Raycast(position + Vector3.up * 200, Vector3.down, out hitInfo, 250f, LAYER_GROUND)) position.y = hitInfo.point.y;
            else position.y = TerrainMeta.HeightMap.GetHeight(position);
            return position;
        }

        #endregion Methods

        #region API

        private bool HasVehicle(ulong playerID, string type)
        {
            VehicleType vehicleType;
            if (!Enum.TryParse(type, true, out vehicleType)) return false;
            return storedData.playerData.ContainsKey(playerID) && storedData.playerData[playerID].ContainsKey(vehicleType);
        }

        private List<string> GetPlayerVehicles(ulong playerID) => storedData.playerData.ContainsKey(playerID) ? storedData.playerData[playerID].Keys.Select(x => x.ToString()).ToList() : new List<string>();

        #endregion API

        #endregion Helpers

        #region Commands

        private bool IsValidOption(BasePlayer player, string option, out VehicleType vehicleType)
        {
            foreach (var entry in configData.vehicleS)
            {
                if (entry.Value.commands.Any(x => x.ToLower() == option))
                {
                    if (!HasPermission(player, entry.Key))
                    {
                        Print(player, Lang("NotAllowed", player.UserIDString));
                        vehicleType = default(VehicleType);
                        return false;
                    }
                    vehicleType = entry.Key;
                    return true;
                }
            }
            Print(player, Lang("OptionNotFound", player.UserIDString, option));
            vehicleType = default(VehicleType);
            return false;
        }

        #region Universal Command

        private void CmdUniversal(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            command = command.ToLower();
            foreach (var entry in configData.vehicleS)
            {
                if (entry.Value.commands.Any(x => x.ToLower() == command))
                {
                    UniversalVehicleCommand(player, entry.Key);
                    return;
                }
            }
        }

        private void UniversalVehicleCommand(BasePlayer player, VehicleType vehicleType)
        {
            if (!HasPermission(player, vehicleType))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (IsBlocked(player)) return;
            string reason;
            Dictionary<VehicleType, Vehicle> vehicleData;
            if (!storedData.playerData.TryGetValue(player.userID, out vehicleData))
                vehicleData = new Dictionary<VehicleType, Vehicle>();
            Vehicle vehicle;
            if (vehicleData.TryGetValue(vehicleType, out vehicle))
            {
                bool checkWater = vehicleType == VehicleType.Rowboat || vehicleType == VehicleType.RHIB;
                if (vehicle.entityID != 0)//recall
                {
                    foreach (var entry in vehiclesCache.ToList())
                    {
                        if (entry.Value.playerID == player.userID && entry.Value.vehicleType == vehicleType)
                        {
                            if (CanRecall(player, entry.Key, vehicleType, out reason, checkWater))
                                RecallVehicle(player, entry.Key, vehicleType, checkWater);
                            else Print(player, reason);
                            return;
                        }
                    }
                }
                else//spawn
                {
                    if (CanSpawn(player, vehicleType, out reason, checkWater))
                        SpawnVehicle(player, vehicleType, checkWater);
                    else Print(player, reason);
                }
                return;
            }
            if (!BuyVehicle(player, vehicleType, out reason))//buy
                Print(player, reason);
        }

        #endregion Universal Command

        #region Help Command

        private void CmdLicenceHelp(BasePlayer player, string command, string[] args)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Lang("Help", player.UserIDString));
            stringBuilder.AppendLine(Lang("HelpLicence1", player.UserIDString, configData.chatS.buyCommand));
            stringBuilder.AppendLine(Lang("HelpLicence2", player.UserIDString, configData.chatS.spawnCommand));
            stringBuilder.AppendLine(Lang("HelpLicence3", player.UserIDString, configData.chatS.recallCommand));
            stringBuilder.AppendLine(Lang("HelpLicence4", player.UserIDString, configData.chatS.killCommand));

            foreach (var entry in configData.vehicleS)
            {
                if (entry.Value.purchasable && entry.Value.commands.Count > 0)
                    stringBuilder.AppendLine(Lang("HelpLicence5", player.UserIDString, entry.Value.commands[0]));
            }
            Print(player, stringBuilder.ToString());
        }

        #endregion Help Command

        #region Buy Command

        [ConsoleCommand("vl.buy")]
        private void CCmdBuyVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (arg.IsAdmin && arg.Args != null && arg.Args.Length == 2)
            {
                player = RustCore.FindPlayer(arg.Args[1]);
                if (player == null)
                {
                    Print(arg, $"Player '{arg.Args[1]}' not found");
                    return;
                }
                IsBuyOption(player, arg.Args[0].ToLower(), false);
                return;
            }
            if (player != null) CmdBuyVehicle(player, string.Empty, arg.Args);
            else Print(arg, $"The server console cannot use '{arg.cmd.FullName}'");
        }

        private void CmdBuyVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in configData.vehicleS)
                {
                    if (entry.Value.purchasable && entry.Value.commands.Count > 0)
                    {
                        var prices = string.Join(", ", from p in entry.Value.price select $"{p.Value.displayName} x{p.Value.amount}");
                        stringBuilder.AppendLine(Lang("HelpBuy", player.UserIDString, configData.chatS.buyCommand, entry.Value.commands[0], entry.Value.displayName, prices));
                    }
                }
                Print(player, stringBuilder.ToString());
                return;
            }
            if (IsBlocked(player)) return;
            IsBuyOption(player, args[0].ToLower());
        }

        private bool IsBuyOption(BasePlayer player, string option, bool pay = true)
        {
            VehicleType vehicleType;
            if (IsValidOption(player, option, out vehicleType))
            {
                string reason;
                if (!BuyVehicle(player, vehicleType, out reason, pay))
                    Print(player, reason);
                return true;
            }
            return false;
        }

        private bool BuyVehicle(BasePlayer player, VehicleType vehicleType, out string reason, bool pay = true)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            if (!vehicleS.purchasable)
            {
                reason = Lang("VehicleCannotBeBuyed", player.UserIDString, vehicleS.displayName);
                return false;
            }
            Dictionary<VehicleType, Vehicle> vehicleData;
            if (!storedData.playerData.TryGetValue(player.userID, out vehicleData))
            {
                vehicleData = new Dictionary<VehicleType, Vehicle>();
                storedData.playerData.Add(player.userID, vehicleData);
            }
            if (vehicleData.ContainsKey(vehicleType))
            {
                reason = Lang("VehicleAlreadyPurchased", player.UserIDString, vehicleS.displayName);
                return false;
            }
            string missing;
            if (pay && !TryPay(player, vehicleS, out missing))
            {
                reason = Lang("NotEnoughCost", player.UserIDString, vehicleS.displayName, missing);
                return false;
            }
            vehicleData.Add(vehicleType, new Vehicle());
            Print(player, Lang("VehiclePurchased", player.UserIDString, vehicleS.displayName, configData.chatS.spawnCommand));
            reason = null;
            SaveData();
            return true;
        }

        private bool TryPay(BasePlayer player, ConfigData.VehicleS vehicleS, out string missing)
        {
            if (permission.UserHasPermission(player.UserIDString, PERMISSION_BYPASS_COST))
            {
                missing = null;
                return true;
            }
            if (!CanPay(player, vehicleS, out missing)) return false;
            List<Item> collect = new List<Item>();
            foreach (var entry in vehicleS.price)
            {
                if (entry.Value.amount <= 0) continue;
                var item = ItemManager.FindItemDefinition(entry.Key);
                if (item != null)
                {
                    player.inventory.Take(collect, item.itemid, entry.Value.amount);
                    player.Command("note.inv", item.itemid, -entry.Value.amount);
                    continue;
                }
                switch (entry.Key.ToLower())
                {
                    case "economics":
                        Economics.Call("Withdraw", player.userID, (double)entry.Value.amount);
                        continue;

                    case "serverrewards":
                        ServerRewards.Call("TakePoints", player.userID, entry.Value.amount);
                        continue;
                }
            }
            foreach (Item item in collect) item.Remove();
            missing = null;
            return true;
        }

        private bool CanPay(BasePlayer player, ConfigData.VehicleS vehicleS, out string missing)
        {
            Dictionary<string, int> missingResources = new Dictionary<string, int>();
            foreach (var entry in vehicleS.price)
            {
                if (entry.Value.amount <= 0) continue;
                int missingAmount = 0;
                var item = ItemManager.FindItemDefinition(entry.Key);
                if (item != null) missingAmount = entry.Value.amount - player.inventory.GetAmount(item.itemid);
                else missingAmount = MissingMoney(entry.Key, entry.Value.amount, player.userID);
                if (missingAmount > 0)
                {
                    if (!missingResources.ContainsKey(entry.Value.displayName)) missingResources.Add(entry.Value.displayName, 0);
                    missingResources[entry.Value.displayName] += missingAmount;
                }
            }
            if (missingResources.Count > 0)
            {
                missing = string.Empty;
                foreach (var entry in missingResources)
                    missing += $"\n* {entry.Key} x{entry.Value}";
                return false;
            }
            missing = null;
            return true;
        }

        private int MissingMoney(string key, int price, ulong playerID)
        {
            switch (key.ToLower())
            {
                case "economics":
                    var balance = Economics?.Call("Balance", playerID);
                    if (balance != null && balance is double)
                    {
                        var n = price - (double)balance;
                        if (n <= 0) return 0;
                        return (int)Math.Ceiling(n);
                    }
                    return price;

                case "serverrewards":
                    var points = ServerRewards?.Call("CheckPoints", playerID);
                    if (points != null && points is int)
                    {
                        var n = price - (int)points;
                        if (n <= 0) return 0;
                        return n;
                    }
                    return price;

                default:
                    PrintError($"Unknown currency type '{key}'");
                    return price;
            }
        }

        #endregion Buy Command

        #region Spawn Command

        [ConsoleCommand("vl.spawn")]
        private void CCmdSpawnVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) Print(arg, $"The server console cannot use '{arg.cmd.FullName}'");
            else CmdSpawnVehicle(player, string.Empty, arg.Args);
        }

        private void CmdSpawnVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in configData.vehicleS)
                    if (entry.Value.purchasable && entry.Value.commands.Count > 0)
                        stringBuilder.AppendLine(Lang("HelpSpawn", player.UserIDString, configData.chatS.spawnCommand, entry.Value.commands[0], entry.Value.displayName));
                Print(player, stringBuilder.ToString());
                return;
            }
            if (IsBlocked(player)) return;
            IsSpawnOption(player, args[0].ToLower());
        }

        private bool IsSpawnOption(BasePlayer player, string option)
        {
            VehicleType vehicleType;
            if (IsValidOption(player, option, out vehicleType))
            {
                string reason;
                bool checkWater = vehicleType == VehicleType.Rowboat || vehicleType == VehicleType.RHIB;
                if (CanSpawn(player, vehicleType, out reason, checkWater))
                    SpawnVehicle(player, vehicleType, checkWater);
                else Print(player, reason);
                return true;
            }
            return false;
        }

        private bool CanSpawn(BasePlayer player, VehicleType vehicleType, out string reason, bool checkWater = false)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            if (player.IsBuildingBlocked())
            {
                reason = Lang("BuildindBlocked", player.UserIDString, vehicleS.displayName);
                return false;
            }
            if (player.isMounted || player.HasParent())
            {
                reason = Lang("MountedOrParented", player.UserIDString, vehicleS.displayName);
                return false;
            }
            Dictionary<VehicleType, Vehicle> vehicleData;
            if (!storedData.playerData.TryGetValue(player.userID, out vehicleData))
                vehicleData = new Dictionary<VehicleType, Vehicle>();
            Vehicle vehicle;
            if (!vehicleData.TryGetValue(vehicleType, out vehicle))
            {
                reason = Lang("VehicleNotYetPurchased", player.UserIDString, vehicleS.displayName);
                return false;
            }
            if (vehicle.entityID != 0)
            {
                reason = Lang("AlreadyVehicleOut", player.UserIDString, vehicleS.displayName, configData.chatS.recallCommand);
                return false;
            }
            if (checkWater && !IsLookingAtWater(player, vehicleS.distance))
            {
                reason = Lang("NotLookingAtWater", player.UserIDString, vehicleS.displayName);
                return false;
            }
            var cooldown = GetPermCooldown(player, vehicleType, vehicleS.cooldown);
            if (cooldown > 0)
            {
                var timeleft = Math.Ceiling(cooldown - (TimeEx.currentTimestamp - vehicle.lastDeath));
                if (timeleft > 0)
                {
                    reason = Lang("VehicleOnCooldown", player.UserIDString, timeleft, vehicleS.displayName);
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private void SpawnVehicle(BasePlayer player, VehicleType vehicleType, bool checkWater = false)
        {
            var prefab = GetVehiclePrefab(vehicleType);
            if (string.IsNullOrEmpty(prefab)) return;
            var vehicleS = configData.vehicleS[vehicleType];
            Vector3 position; Quaternion rotation;
            GetVehicleSpawnPos(player, vehicleS.distance, checkWater, out position, out rotation);
            var entity = GameManager.server.CreateEntity(prefab, position, rotation);
            if (entity == null) return;
            entity.enableSaving = false;
            entity.OwnerID = player.userID;
            entity.Spawn();

            if (configData.globalS.noServerGibs && entity is BaseVehicle)
                (entity as BaseVehicle).serverGibs.guid = string.Empty;
            if (configData.globalS.noFireBall && entity is BaseHelicopterVehicle)
                (entity as BaseHelicopterVehicle).fireBall.guid = string.Empty;
            if (configData.globalS.noMapMarker && entity is CH47Helicopter)
            {
                var helicopter = entity as CH47Helicopter;
                helicopter.mapMarkerInstance?.Kill();
                helicopter.mapMarkerEntityPrefab.guid = string.Empty;
            }
            var vehicle = new Vehicle { playerID = player.userID, vehicleType = vehicleType, entityID = entity.net.ID, lastDismount = TimeEx.currentTimestamp };
            vehiclesCache.Add(entity, vehicle);
            storedData.playerData[player.userID][vehicleType] = vehicle;
            Print(player, Lang("VehicleSpawned", player.UserIDString, vehicleS.displayName));
        }

        #endregion Spawn Command

        #region Recall Command

        [ConsoleCommand("vl.recall")]
        private void CCmdRecallVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) Print(arg, $"The server console cannot use '{arg.cmd.FullName}'");
            else CmdRecallVehicle(player, string.Empty, arg.Args);
        }

        private void CmdRecallVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in configData.vehicleS)
                    if (entry.Value.purchasable && entry.Value.commands.Count > 0)
                        stringBuilder.AppendLine(Lang("HelpRecall", player.UserIDString, configData.chatS.recallCommand, entry.Value.commands[0], entry.Value.displayName));
                Print(player, stringBuilder.ToString());
                return;
            }
            if (IsBlocked(player)) return;
            IsReallOption(player, args[0].ToLower());
        }

        private bool IsReallOption(BasePlayer player, string option)
        {
            VehicleType vehicleType;
            if (IsValidOption(player, option, out vehicleType))
            {
                RecallVehicle(player, vehicleType);
                return true;
            }
            return false;
        }

        private bool RecallVehicle(BasePlayer player, VehicleType vehicleType)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            Dictionary<VehicleType, Vehicle> vehicleData;
            if (!storedData.playerData.TryGetValue(player.userID, out vehicleData))
                vehicleData = new Dictionary<VehicleType, Vehicle>();
            Vehicle vehicle;
            if (!vehicleData.TryGetValue(vehicleType, out vehicle))
            {
                Print(player, Lang("VehicleNotYetPurchased", player.UserIDString, vehicleS.displayName));
                return false;
            }
            if (vehicle.entityID != 0)
            {
                foreach (var entry in vehiclesCache.ToList())
                {
                    if (entry.Value.playerID == player.userID && entry.Value.vehicleType == vehicleType)
                    {
                        if (entry.Key != null && !entry.Key.IsDestroyed)
                        {
                            string reason;
                            bool checkWater = vehicleType == VehicleType.Rowboat || vehicleType == VehicleType.RHIB;
                            if (CanRecall(player, entry.Key, vehicleType, out reason, checkWater))
                            {
                                RecallVehicle(player, entry.Key, vehicleType, checkWater);
                                return true;
                            }
                            Print(player, reason);
                            return false;
                        }
                    }
                }
            }
            Print(player, Lang("VehicleNotOut", player.UserIDString, vehicleS.displayName));
            return false;
        }

        private bool CanRecall(BasePlayer player, BaseEntity vehicle, VehicleType vehicleType, out string reason, bool checkWater = false)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            if (configData.globalS.anyMountedRecall && VehicleAnyMounted(vehicle))
            {
                reason = Lang("PlayerMountedOnVehicle", player.UserIDString, vehicleS.displayName);
                return false;
            }
            if (vehicleS.recallMinDis > 0 && Vector3.Distance(player.transform.position, vehicle.transform.position) < vehicleS.recallMinDis)
            {
                reason = Lang("RecallTooFar", player.UserIDString, vehicleS.recallMinDis, vehicleS.displayName);
                return false;
            }
            if (player.IsBuildingBlocked())
            {
                reason = Lang("BuildindBlocked", player.UserIDString, vehicleS.displayName);
                return false;
            }
            if (player.isMounted || player.HasParent())
            {
                reason = Lang("MountedOrParented", player.UserIDString, vehicleS.displayName);
                return false;
            }
            if (checkWater && !IsLookingAtWater(player, vehicleS.distance))
            {
                reason = Lang("NotLookingAtWater", player.UserIDString, vehicleS.displayName);
                return false;
            }
            reason = null;
            return true;
        }

        private void RecallVehicle(BasePlayer player, BaseEntity vehicle, VehicleType vehicleType, bool checkWater = false)
        {
            if (vehicle is BaseVehicle) (vehicle as BaseVehicle).DismountAllPlayers();
            else
            {
                List<BasePlayer> players = Facepunch.Pool.GetList<BasePlayer>();
                Vis.Entities(vehicle.transform.position, 2.5f, players, Rust.Layers.Server.Players);
                foreach (var p in players) if (p.HasParent()) p.SetParent(null);
                Facepunch.Pool.FreeList(ref players);
            }
            var vehicleS = configData.vehicleS[vehicleType];
            Vector3 position; Quaternion rotation;
            GetVehicleSpawnPos(player, vehicleS.distance, checkWater, out position, out rotation);
            vehicle.transform.position = position;
            vehicle.transform.rotation = rotation;
            vehicle.transform.hasChanged = true;
            Print(player, Lang("VehicleRecalled", player.UserIDString, vehicleS.displayName));
        }

        #endregion Recall Command

        #region Kill Command

        [ConsoleCommand("vl.kill")]
        private void CCmdKillVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) Print(arg, $"The server console cannot use '{arg.cmd.FullName}'");
            else CmdKillVehicle(player, string.Empty, arg.Args);
        }

        private void CmdKillVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in configData.vehicleS)
                    if (entry.Value.purchasable && entry.Value.commands.Count > 0)
                        stringBuilder.AppendLine(Lang("HelpKill", player.UserIDString, configData.chatS.killCommand, entry.Value.commands[0], entry.Value.displayName));
                Print(player, stringBuilder.ToString());
                return;
            }
            if (IsBlocked(player)) return;
            IsKillOption(player, args[0].ToLower());
        }

        private bool IsKillOption(BasePlayer player, string option)
        {
            VehicleType vehicleType;
            if (IsValidOption(player, option, out vehicleType))
            {
                KillVehicle(player, vehicleType);
                return true;
            }
            return false;
        }

        private bool KillVehicle(BasePlayer player, VehicleType vehicleType)
        {
            var vehicleS = configData.vehicleS[vehicleType];
            Dictionary<VehicleType, Vehicle> vehicleData;
            if (!storedData.playerData.TryGetValue(player.userID, out vehicleData))
                vehicleData = new Dictionary<VehicleType, Vehicle>();
            Vehicle vehicle;
            if (!vehicleData.TryGetValue(vehicleType, out vehicle))
            {
                Print(player, Lang("VehicleNotYetPurchased", player.UserIDString, vehicleS.displayName));
                return false;
            }
            if (vehicle.entityID != 0)
            {
                foreach (var entry in vehiclesCache.ToList())
                {
                    if (entry.Value.playerID == player.userID && entry.Value.vehicleType == vehicleType)
                    {
                        if (entry.Key != null && !entry.Key.IsDestroyed)
                        {
                            if (configData.globalS.anyMountedKill && VehicleAnyMounted(entry.Key))
                            {
                                Print(player, Lang("PlayerMountedOnVehicle", player.UserIDString, vehicleS.displayName));
                                return false;
                            }
                            RefundFuel(entry.Key, entry.Value);
                            entry.Key.Kill(BaseNetworkable.DestroyMode.Gib);
                        }
                        Print(player, Lang("VehicleKilled", player.UserIDString, vehicleS.displayName));
                        return true;
                    }
                }
            }
            Print(player, Lang("VehicleNotOut", player.UserIDString, vehicleS.displayName));
            return false;
        }

        #endregion Kill Command

        private void GetVehicleSpawnPos(BasePlayer player, float distance, bool checkWater, out Vector3 spawnPos, out Quaternion spawnRot)
        {
            if (configData.globalS.spawnLookingAt) spawnPos = GetLookingAtGroundPos(player, distance);
            else
            {
                if (checkWater)
                {
                    spawnPos = player.transform.position;
                    for (int i = 0; i < 10; i++)
                    {
                        var originPos = GetLookingAtGroundPos(player, distance);
                        var circle = UnityEngine.Random.insideUnitCircle * distance;
                        spawnPos = originPos + new Vector3(circle.x, 0, circle.y);
                        if (Vector3.Distance(spawnPos, player.transform.position) > 2f) break;
                    }
                }
                else
                {
                    var direction = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
                    spawnPos = player.transform.position + direction * distance;
                }
            }
            spawnPos = GetGroundPosition(spawnPos);
            var normalized = (spawnPos - player.transform.position).normalized;
            var angle = normalized != Vector3.zero ? Quaternion.LookRotation(normalized).eulerAngles.y : UnityEngine.Random.Range(0f, 360f);
            spawnRot = Quaternion.Euler(new Vector3(0f, angle + 90f, 0f));
            spawnPos += Vector3.up * 1.5f;
        }

        #endregion Commands

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Settings")]
            public Settings globalS = new Settings();

            public class Settings
            {
                [JsonProperty(PropertyName = "Interval to check vehicle for wipe (Seconds)")] public float checkVehiclesTime = 300;
                [JsonProperty(PropertyName = "Prevent other players from mounting vehicle")] public bool preventMounting = true;
                [JsonProperty(PropertyName = "Prevent mounting on driver's seat only")] public bool blockDriverSeat = true;
                [JsonProperty(PropertyName = "Check if any player mounted when recalling a vehicle")] public bool anyMountedRecall = true;
                [JsonProperty(PropertyName = "Check if any player mounted when killing a vehicle")] public bool anyMountedKill = true;
                [JsonProperty(PropertyName = "Spawn vehicle in the direction you are looking at")] public bool spawnLookingAt = true;
                [JsonProperty(PropertyName = "Use Teams")] public bool useTeams = false;
                [JsonProperty(PropertyName = "Use Clans")] public bool useClans = true;
                [JsonProperty(PropertyName = "Use Friends")] public bool useFriends = true;
                [JsonProperty(PropertyName = "Vehicle No Decay")] public bool noDecay = false;
                [JsonProperty(PropertyName = "Vehicle No Fire Ball")] public bool noFireBall = true;
                [JsonProperty(PropertyName = "Chinook No Map Marker")] public bool noMapMarker = true;
                [JsonProperty(PropertyName = "Vehicle No Server Gibs")] public bool noServerGibs = true;
                [JsonProperty(PropertyName = "Remove Vehicles On Crash")] public bool removeVehicleOnCrash = false;
                [JsonProperty(PropertyName = "Not Refund Fuel On Crash")] public bool notRefundFuelOnCrash = false;
                [JsonProperty(PropertyName = "Clear Vehicle Data On Map Wipe")] public bool clearVehicleOnWipe = false;
                [JsonProperty(PropertyName = "Use Raid Blocker (Need NoEscape Plugin)")] public bool useRaidBlocker = false;
                [JsonProperty(PropertyName = "Use Combat Blocker (Need NoEscape Plugin)")] public bool useCombatBlocker = false;
            }

            [JsonProperty(PropertyName = "Chat Settings")]
            public ChatSettings chatS = new ChatSettings();

            public class ChatSettings
            {
                [JsonProperty(PropertyName = "Use Universal Chat Command")] public bool useUniversalCommand = true;
                [JsonProperty(PropertyName = "Help Chat Command")] public string helpCommand = "license";
                [JsonProperty(PropertyName = "Buy Chat Command")] public string buyCommand = "buy";
                [JsonProperty(PropertyName = "Spawn Chat Command")] public string spawnCommand = "spawn";
                [JsonProperty(PropertyName = "Recall Chat Command")] public string recallCommand = "recall";
                [JsonProperty(PropertyName = "Kill Chat Command")] public string killCommand = "kill";
                [JsonProperty(PropertyName = "Chat Prefix")] public string prefix = "[VehicleLicence]: ";
                [JsonProperty(PropertyName = "Chat Prefix Color")] public string prefixColor = "#B366FF";
                [JsonProperty(PropertyName = "Chat SteamID Icon")] public ulong steamIDIcon = 76561198924840872;
            }

            [JsonProperty(PropertyName = "Cooldown Permission Settings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, Dictionary<VehicleType, float>> permCooldown = new Dictionary<string, Dictionary<VehicleType, float>>()
            {
                ["vehiclelicence.vip"] = new Dictionary<VehicleType, float>
                {
                    [VehicleType.Rowboat] = 90f,
                    [VehicleType.RHIB] = 150f,
                    [VehicleType.Sedan] = 450f,
                    [VehicleType.HotAirBalloon] = 450f,
                    [VehicleType.MiniCopter] = 900f,
                    [VehicleType.TransportHelicopter] = 1200f,
                    [VehicleType.Chinook] = 1500f,
                    [VehicleType.RidableHorse] = 1500f,
                }
            };

            [JsonProperty(PropertyName = "Vehicle Settings")]
            public Dictionary<VehicleType, VehicleS> vehicleS = new Dictionary<VehicleType, VehicleS>()
            {
                [VehicleType.Rowboat] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Row Boat",
                    cooldown = 180,
                    distance = 5,
                    permission = "vehiclelicence.rowboat",
                    commands = new List<string> { "row", "rowboat" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 500, displayName = "Scrap" } }
                },
                [VehicleType.RHIB] = new VehicleS
                {
                    purchasable = true,
                    displayName = "RHIB",
                    cooldown = 300,
                    distance = 10,
                    permission = "vehiclelicence.rhib",
                    commands = new List<string> { "rhib" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 1000, displayName = "Scrap" } }
                },
                [VehicleType.Sedan] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Sedan",
                    cooldown = 180,
                    distance = 5,
                    permission = "vehiclelicence.sedan",
                    commands = new List<string> { "car", "sedan" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 300, displayName = "Scrap" } }
                },
                [VehicleType.HotAirBalloon] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Hot Air Balloon",
                    cooldown = 900,
                    distance = 20,
                    permission = "vehiclelicence.hotairballoon",
                    commands = new List<string> { "hab", "hotairballoon" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 5000, displayName = "Scrap" } }
                },
                [VehicleType.MiniCopter] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Mini Copter",
                    cooldown = 1800,
                    distance = 8,
                    permission = "vehiclelicence.minicopter",
                    commands = new List<string> { "mini", "minicopter" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 10000, displayName = "Scrap" } }
                },
                [VehicleType.TransportHelicopter] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Transport Copter",
                    cooldown = 2400,
                    distance = 10,
                    permission = "vehiclelicence.transportcopter",
                    commands = new List<string> { "tcop", "transportcopter" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 20000, displayName = "Scrap" } }
                },
                [VehicleType.Chinook] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Chinook",
                    cooldown = 3000,
                    distance = 20,
                    permission = "vehiclelicence.chinook",
                    commands = new List<string> { "ch47", "chinook" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 30000, displayName = "Scrap" } }
                },
                [VehicleType.RidableHorse] = new VehicleS
                {
                    purchasable = true,
                    displayName = "Ridable Horse",
                    cooldown = 3000,
                    distance = 5,
                    permission = "vehiclelicence.ridablehorse",
                    commands = new List<string> { "horse", "ridablehorse" },
                    price = new Dictionary<string, VehicleS.PriceInfo> { ["scrap"] = new VehicleS.PriceInfo { amount = 700, displayName = "Scrap" } }
                },
            };

            public class VehicleS
            {
                [JsonProperty(PropertyName = "Purchasable")] public bool purchasable;
                [JsonProperty(PropertyName = "Use Permission")] public bool usePermission = true;
                [JsonProperty(PropertyName = "Permission")] public string permission;
                [JsonProperty(PropertyName = "Vehicle Display Name")] public string displayName;
                [JsonProperty(PropertyName = "Distance To Spawn")] public float distance;
                [JsonProperty(PropertyName = "Can Recall Min Distance")] public float recallMinDis;
                [JsonProperty(PropertyName = "Cooldown (Seconds)")] public double cooldown;
                [JsonProperty(PropertyName = "Time Before Vehicle Wipe (Seconds)")] public double wipeTime;
                [JsonProperty(PropertyName = "Commands")] public List<string> commands;
                [JsonProperty(PropertyName = "Price")] public Dictionary<string, PriceInfo> price;

                public class PriceInfo
                {
                    public int amount;
                    public string displayName;
                }
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion ConfigurationFile

        #region DataFile

        private StoredData storedData;

        private class StoredData
        {
            public Dictionary<ulong, Dictionary<VehicleType, Vehicle>> playerData = new Dictionary<ulong, Dictionary<VehicleType, Vehicle>>();
        }

        private class Vehicle
        {
            public double lastDeath;
            [JsonIgnore] public uint entityID;
            [JsonIgnore] public ulong playerID;
            [JsonIgnore] public double lastDismount;
            [JsonIgnore] public VehicleType vehicleType;

            public void OnDismount() => lastDismount = TimeEx.currentTimestamp;

            public void OnDeath()
            {
                entityID = 0;
                lastDeath = TimeEx.currentTimestamp;
            }
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                storedData = new StoredData();
                UpdataOldData();
                SaveData();
            }
        }

        private void ClearData()
        {
            storedData = new StoredData();
            SaveData();
        }

        private void OnNewSave(string filename)
        {
            if (configData.globalS.clearVehicleOnWipe)
                ClearData();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion DataFile

        #region LanguageFile

        private void Print(BasePlayer player, string message) => Player.Message(player, message, $"<color={configData.chatS.prefixColor}>{configData.chatS.prefix}</color>", configData.chatS.steamIDIcon);

        private void Print(ConsoleSystem.Arg arg, string message)
        {
            var player = arg.Player();
            if (player == null) Puts(message);
            else PrintToConsole(player, message);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Help"] = "These are the available commands:",
                ["HelpLicence1"] = "<color=#4DFF4D>/{0}</color> -- To buy a vehicle",
                ["HelpLicence2"] = "<color=#4DFF4D>/{0}</color> -- To spawn a vehicle",
                ["HelpLicence3"] = "<color=#4DFF4D>/{0}</color> -- To recall a vehicle",
                ["HelpLicence4"] = "<color=#4DFF4D>/{0}</color> -- To kill a vehicle",
                ["HelpLicence5"] = "<color=#4DFF4D>/{0}</color> -- To buy, spawn or recall a vehicle",

                ["HelpBuy"] = "<color=#4DFF4D>/{0} {1}</color> -- To buy a {2}. Price: <color=#FF1919>{3}</color>",
                ["HelpSpawn"] = "<color=#4DFF4D>/{0} {1}</color> -- To spawn a {2}",
                ["HelpRecall"] = "<color=#4DFF4D>/{0} {1}</color> -- To recall a {2}",
                ["HelpKill"] = "<color=#4DFF4D>/{0} {1}</color> -- To kill a {2}",

                ["NotAllowed"] = "You do not have permission to use this command.",
                ["NotEnoughCost"] = "You don't have enough resources to buy a {0}. You are missing:{1}",
                ["RaidBlocked"] = "<color=#FF1919>You may not do that while raid blocked</color>.",
                ["CombatBlocked"] = "<color=#FF1919>You may not do that while combat blocked</color>.",
                ["OptionNotFound"] = "This '{0}' option doesn't exist.",
                ["VehiclePurchased"] = "You have purchased a {0}, type <color=#4DFF4D>/{1}</color> for more information.",
                ["VehicleAlreadyPurchased"] = "You have already purchased {0}.",
                ["VehicleCannotBeBuyed"] = "{0} is unpurchasable",
                ["VehicleNotOut"] = "{0} is not out.",
                ["AlreadyVehicleOut"] = "You already have a {0} outside, type <color=#4DFF4D>/{1}</color> for more information.",
                ["VehicleNotYetPurchased"] = "You have not yet purchased a {0}.",
                ["VehicleSpawned"] = "You spawned your {0}.",
                ["VehicleRecalled"] = "You recalled your {0}.",
                ["VehicleKilled"] = "You killed your {0}.",
                ["VehicleOnCooldown"] = "You must wait {0} seconds before you can spawn your {1}.",
                ["NotLookingAtWater"] = "You must be looking at water to spawn or recall a {0}.",
                ["BuildindBlocked"] = "You can't spawn a {0} appear if you don't have the building privileges.",
                ["RefundedVehicleFuel"] = "Your {0} fuel was refunded to your inventory.",
                ["PlayerMountedOnVehicle"] = "It cannot be recalled when players mounted on your {0}.",

                ["MountedOrParented"] = "You cannot spawn a vehicle when mounted or parented.",
                ["RecallTooFar"] = "You must be within {0} meters of {1} to recall.",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Help"] = "可用命令列表:",
                ["HelpLicence1"] = "<color=#4DFF4D>/{0}</color> -- 购买一辆载具",
                ["HelpLicence2"] = "<color=#4DFF4D>/{0}</color> -- 生成一辆载具",
                ["HelpLicence3"] = "<color=#4DFF4D>/{0}</color> -- 召回一辆载具",
                ["HelpLicence4"] = "<color=#4DFF4D>/{0}</color> -- 删除一辆载具",
                ["HelpLicence5"] = "<color=#4DFF4D>/{0}</color> -- 购买，生成，召回一辆载具",

                ["HelpBuy"] = "<color=#4DFF4D>/{0} {1}</color> -- 购买一辆 {2}. 价格: <color=#FF1919>{3}</color>",
                ["HelpSpawn"] = "<color=#4DFF4D>/{0} {1}</color> -- 生成一辆 {2}",
                ["HelpRecall"] = "<color=#4DFF4D>/{0} {1}</color> -- 召回一辆 {2}",
                ["HelpKill"] = "<color=#4DFF4D>/{0} {1}</color> -- 删除一辆 {2}",

                ["NotAllowed"] = "您没有权限使用该命令",
                ["NotEnoughCost"] = "您没有足够的资源购买 {0}，还需要:{1}",
                ["RaidBlocked"] = "<color=#FF1919>您被突袭阻止了，不能使用该命令</color>.",
                ["CombatBlocked"] = "<color=#FF1919>您被战斗阻止了，不能使用该命令</color>.",
                ["OptionNotFound"] = "该 '{0}' 选项不存在",
                ["VehiclePurchased"] = "您购买了 {0}, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                ["VehicleAlreadyPurchased"] = "您已经购买了 {0}",
                ["VehicleCannotBeBuyed"] = "{0} 是不可购买的",
                ["VehicleNotOut"] = "您还没有生成您的 {0}",
                ["AlreadyVehicleOut"] = "您已经生成了您的 {0}, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                ["VehicleNotYetPurchased"] = "您还没有购买 {0}.",
                ["VehicleSpawned"] = "您生成了您的 {0}.",
                ["VehicleRecalled"] = "您召回了您的 {0}.",
                ["VehicleKilled"] = "您删除了您的 {0}.",
                ["VehicleOnCooldown"] = "您必须等待 {0} 秒才能生成您的 {1}",
                ["NotLookingAtWater"] = "您必须看着水面才能生成您的 {0}",
                ["BuildindBlocked"] = "您没有领地柜权限，无法生成您的 {0}",
                ["RefundedVehicleFuel"] = "您的 {0} 燃料已经归还回您的库存",
                ["PlayerMountedOnVehicle"] = "您的 {0} 上坐着玩家，无法被召回",

                ["MountedOrParented"] = "当您坐着或者在附着在实体上时无法生成载具",
                ["RecallTooFar"] = "您必须在 {0} 米内才能召回您的 {1}",
            }, this, "zh-CN");
        }

        #endregion LanguageFile
    }
}