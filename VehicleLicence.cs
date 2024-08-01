using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Vehicle License", "Sorrow|TheDoc", "1.3.2")]
    [Description("Allows players to buy vehicles and then spawn or store it")]
    class VehicleLicence : RustPlugin
    {
        #region Fields
        [PluginReference]
        Plugin Economics, ServerRewards, NoEscape;

        private ConfigData _configData;

        private Dictionary<ulong, LicencedPlayer> _licencedPlayer = new Dictionary<ulong, LicencedPlayer>();
        private Dictionary<uint, Vehicle> _vehiclesCache = new Dictionary<uint, Vehicle>();

        private int _intervalToCheckVehicle;
        private int _timeBeforeVehicleWipe;

        private bool _useEconomics;
        private bool _useServerRewards;
		private bool _useRaidBlocker;
		private bool _useCombatBlocker;
        private bool _usePermissions;
        private string _itemsNeededToBuyVehicles;
		private bool _removeVehicleOnCrash;

        private const string Prefix = "<color=#B366FF>[Vehicle License]</color> ";
        private const string RowBoatPrefab = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        private const string RhibPrefab = "assets/content/vehicles/boats/rhib/rhib.prefab";
        private const string SedanPrefab = "assets/content/vehicles/sedan_a/sedantest.entity.prefab";
        private const string HotAirBalloonPrefab = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab";
        private const string MiniCopterPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
		private const string TransportCopterPrefab = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string ChinookPrefab = "assets/prefabs/npc/ch47/ch47.entity.prefab";
        #endregion

        #region uMod Hooks
        private void OnServerInitialized()
        {
            _intervalToCheckVehicle = _configData.Settings.IntervalToCheckVehicles;
            _timeBeforeVehicleWipe = _configData.Settings.TimeBeforeVehicleWipe;
            _useEconomics = _configData.Settings.UseEconomics;
            _useServerRewards = _configData.Settings.UseServerRewards;
            _itemsNeededToBuyVehicles = _configData.Settings.ItemsNeededToBuyVehicles;
            _usePermissions = _configData.Settings.UsePermissions;
			_useRaidBlocker = _configData.Settings.UseRaidBlocker;
			_useCombatBlocker = _configData.Settings.UseCombatBlocker;
			_removeVehicleOnCrash = _configData.Settings.RemoveVehicleOnCrash;

            if (Economics == null && _useEconomics)
            {
                PrintWarning("Economics is not loaded, get it at https://umod.org");
            }
            else if (Economics != null && _useEconomics)
            {
                _itemsNeededToBuyVehicles = "Money";
            }
            if (ServerRewards == null && _useServerRewards)
            {
                PrintWarning("ServerRewards is not loaded, get it at https://umod.org");
            }
            else if (ServerRewards != null && _useServerRewards)
            {
                _itemsNeededToBuyVehicles = "RP";
            }

            LoadData();
            CheckVehicles();
            BroadcastHelp(); // TODO remove for relase
        }

        private void Loaded()
        {
            permission.RegisterPermission("vehiclelicence.use", this);
            permission.RegisterPermission("vehiclelicence.rowboat", this);
            permission.RegisterPermission("vehiclelicence.rhib", this);
            permission.RegisterPermission("vehiclelicence.sedan", this);
            permission.RegisterPermission("vehiclelicence.hotairballoon", this);
            permission.RegisterPermission("vehiclelicence.minicopter", this);
			permission.RegisterPermission("vehiclelicence.transportcopter", this);
            permission.RegisterPermission("vehiclelicence.chinook", this);
        }

        private void Unload()
        {
            foreach (var vehicle in _vehiclesCache.ToList())
            {
                var licencedPlayer = GetLicencedPlayer(vehicle.Value);
                if (licencedPlayer == null) continue;

                RemoveVehicle(licencedPlayer, GetVehicleSettings(vehicle.Value.Prefab));
            }

            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnServerShutdown()
        {
            SaveData();
        }

        private void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            var vehicleParent = entity?.VehicleParent();
            if (vehicleParent == null) return;
            Vehicle vehicle;
            if (!_vehiclesCache.TryGetValue(vehicleParent.net.ID, out vehicle)) return;
            vehicle.LastDismount = DateTime.UtcNow;
        }

		private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
			// TODO check for boats (row or rhib) and check their Health.

			//PrintWarning("entity.name = " + entity.name);
			//PrintWarning("hitInfo = " + hitInfo.damageTypes.Total());

			// if ((entity.name.Contains("foundation")) & (!entity.name.Contains("triangle")) & (!entity.name.Contains("steps")))
			// {
				// if ((ProtectFoundation == true) & (entity is BuildingBlock))
				// {
						// return false;
				// }
			// }
		}

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null || entity.net?.ID == null) return;
            Vehicle vehicle;

            if (!_vehiclesCache.TryGetValue(entity.net.ID, out vehicle)) return;
            _vehiclesCache.Remove(entity.net.ID);

			//PrintWarning("vehicle =" + vehicle.Prefab);

			LicencedPlayer licencedPlayer = GetLicencedPlayer(vehicle);
            if (licencedPlayer == null) return;

            vehicle.Id = 0;

            var player = licencedPlayer.Player;
            if (player == null) return;

			//PrintWarning("player =" + player.userID);
			//PrintWarning("removing vehicle");
			//PrintWarning("_removeVehicleOnCrash = " + _removeVehicleOnCrash);

			if (licencedPlayer != null && _removeVehicleOnCrash) {
				licencedPlayer.Vehicles.Remove(vehicle.Prefab);
				//PrintWarning("removal Complete");
			}
        }
        #endregion

        #region Commands
        /// <summary>
        /// Commands the license help.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The arguments.</param>
        [ChatCommand("license")]
        void CmdLicenceHelp(BasePlayer player, string command, string[] args)
        {
			if (IsRaidBlocked(player.UserIDString)) {
				Msg("raidblocked", player);
				return;
			}
			if (IsCombatBlocked(player.UserIDString)) {
				Msg("comatblocked", player);
				return;
			}

            Msg("helpLicence", player);
            LicencedPlayer licencedPlayer;
            if (_licencedPlayer.TryGetValue(player.userID, out licencedPlayer)) return;
            licencedPlayer = new LicencedPlayer(player.userID, null);
            _licencedPlayer.Add(player.userID, licencedPlayer);
        }

        /// <summary>
        /// Commands the buy vehicle.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The arguments.</param>
        [ChatCommand("buy")]
        void CmdBuyVehicle(BasePlayer player, string command, IReadOnlyList<string> args)
        {
			if (IsRaidBlocked(player.UserIDString)) {
				Msg("raidblocked", player);
				return;
			}
			if (IsCombatBlocked(player.UserIDString)) {
				Msg("comatblocked", player);
				return;
			}

            if (args.Count < 1) Msg("helpBuy", player, new object[] {
                _itemsNeededToBuyVehicles,
				GetVehicleSettings(RowBoatPrefab).price.ToString(),
				GetVehicleSettings(RhibPrefab).price.ToString(),
                GetVehicleSettings(SedanPrefab).price.ToString(),
				GetVehicleSettings(HotAirBalloonPrefab).price.ToString(),
				GetVehicleSettings(MiniCopterPrefab).price.ToString(),
				GetVehicleSettings(TransportCopterPrefab).price.ToString(),
                GetVehicleSettings(ChinookPrefab).price.ToString()
            });
            else
            {
                LicencedPlayer licencedPlayer;
                if (!_licencedPlayer.TryGetValue(player.userID, out licencedPlayer))
                {
                    licencedPlayer = new LicencedPlayer(player.userID, null);
                    _licencedPlayer.Add(player.userID, licencedPlayer);
                }

                var arg = args[0].ToLower();
                if (!PlayerHasPermission(player, arg))
                {
                    Msg("noPermission", player);
                    return;
                }
                if (IsCase(arg, RowBoatPrefab))
                {
                    BuyVehicle(player, licencedPlayer, RowBoatPrefab);
                }
                else if (IsCase(arg, RhibPrefab))
                {
                    BuyVehicle(player, licencedPlayer, RhibPrefab);
                }
                else if (IsCase(arg, SedanPrefab))
                {
                    BuyVehicle(player, licencedPlayer, SedanPrefab);
                }
                else if (IsCase(arg, HotAirBalloonPrefab))
                {
                    BuyVehicle(player, licencedPlayer, HotAirBalloonPrefab);
                }
                else if (IsCase(arg, MiniCopterPrefab))
                {
                    BuyVehicle(player, licencedPlayer, MiniCopterPrefab);
                }
				else if (IsCase(arg, TransportCopterPrefab))
                {
                    BuyVehicle(player, licencedPlayer, TransportCopterPrefab);
                }
                else if (IsCase(arg, ChinookPrefab))
                {
                    BuyVehicle(player, licencedPlayer, ChinookPrefab);
                }
                else
                {
                    Msg("helpOptionNotFound", player);
                }
            }
        }


        /// <summary>
        /// Commands the spawn vehicle.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The arguments.</param>
        [ChatCommand("spawn")]
        void CmdSpawnVehicle(BasePlayer player, string command, string[] args)
        {
			if (IsRaidBlocked(player.UserIDString)) {
				Msg("raidblocked", player);
				return;
			}
			if (IsCombatBlocked(player.UserIDString)) {
				Msg("comatblocked", player);
				return;
			}

            if (args.Length == 0) Msg("helpSpawn", player);
            if (args.Length >= 1)
            {
                if (player.IsBuildingBlocked())
                {
                    Msg("buildindBlocked", player);
                    return;
                }

                LicencedPlayer licencedPlayer;
                if (_licencedPlayer.TryGetValue(player.userID, out licencedPlayer))
                {
                    var arg = args[0].ToLower();
                    if (!PlayerHasPermission(player, arg))
                    {
                        Msg("noPermission", player);
                        return;
                    }

                    string prefab;
                    if (IsCase(arg, RowBoatPrefab))
                    {
                        prefab = RowBoatPrefab;
                        if (IsSpawning(licencedPlayer, prefab, true)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else if (IsCase(arg, RhibPrefab))
                    {
                        prefab = RhibPrefab;
                        if (IsSpawning(licencedPlayer, prefab, true)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else if (IsCase(arg, SedanPrefab))
                    {
                        prefab = SedanPrefab;
                        if (IsSpawning(licencedPlayer, prefab)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else if (IsCase(arg, HotAirBalloonPrefab))
                    {
                        prefab = HotAirBalloonPrefab;
                        if (IsSpawning(licencedPlayer, prefab)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else if (IsCase(arg, MiniCopterPrefab))
                    {
                        prefab = MiniCopterPrefab;
                        if (IsSpawning(licencedPlayer, prefab)) SpawnVehicle(licencedPlayer, prefab);
                    }
					else if (IsCase(arg, TransportCopterPrefab))
                    {
                        prefab = TransportCopterPrefab;
                        if (IsSpawning(licencedPlayer, prefab)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else if (IsCase(arg, ChinookPrefab))
                    {
                        prefab = ChinookPrefab;
                        if (IsSpawning(licencedPlayer, prefab)) SpawnVehicle(licencedPlayer, prefab);
                    }
                    else
                    {
                        Msg("helpOptionNotFound", player);
                    }
                }
                else
                {
                    Msg("didntBuyVehicle", player);
                }
            }
        }

        /// <summary>
        /// Commands the recall vehicle.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The arguments.</param>
        [ChatCommand("recall")]
        void CmdRecallVehicle(BasePlayer player, string command, string[] args)
        {
			if (IsRaidBlocked(player.UserIDString)) {
				Msg("raidblocked", player);
				return;
			}
			if (IsCombatBlocked(player.UserIDString)) {
				Msg("comatblocked", player);
				return;
			}

            LicencedPlayer licencedPlayer;

            if (args.Length < 1)
            {
                Msg("helpRecall", player);
            }
            else
            {
                if (!_licencedPlayer.TryGetValue(player.userID, out licencedPlayer)) return;
                var arg = args[0].ToLower();
                if (!PlayerHasPermission(player, arg))
                {
                    Msg("noPermission", player);
                    return;
                }
                if (IsCase(arg, RowBoatPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(RowBoatPrefab));
                }
                else if (IsCase(arg, RhibPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(RhibPrefab));
                }
                else if (IsCase(arg, SedanPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(SedanPrefab));
                }
                else if (IsCase(arg, HotAirBalloonPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(HotAirBalloonPrefab));
                }
                else if (IsCase(arg, MiniCopterPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(MiniCopterPrefab));
                }
				else if (IsCase(arg, TransportCopterPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(TransportCopterPrefab));
                }
                else if (IsCase(arg, ChinookPrefab))
                {
                    RemoveVehicle(licencedPlayer, GetVehicleSettings(ChinookPrefab));
                }
                else
                {
                    Msg("helpOptionNotFound", player);
                }
            }
        }

        #endregion

        #region Functions
        /// <summary>
        /// Buys the vehicle.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="licencedPlayer">The licenced player.</param>
        /// <param name="prefab">The prefab.</param>
        private void BuyVehicle(BasePlayer player, LicencedPlayer licencedPlayer, string prefab)
        {
            Vehicle vehicle;
            var vehicleSettings = GetVehicleSettings(prefab);

			if (!vehicleSettings.purchasable) {
                Msg("vehicleCannotBeBuyed", player, new[] { vehicleSettings.name });
				return;
			} else {
				//PrintWarning("vehicleSettings.purchasable = " + vehicleSettings.purchasable);
			}

            if (licencedPlayer.Vehicles.TryGetValue(prefab, out vehicle))
            {
                Msg("vehicleAlreadyPurchased", player, new[] { vehicleSettings.name });
            }
            else if (vehicleSettings.name != "null" && vehicleSettings.purchasable)
            {
                if (!Withdraw(player, vehicleSettings)) return;
                vehicle = new Vehicle(prefab, player.userID);
                licencedPlayer.SetVehicle(vehicle);
            }
            else
            {
                Msg("vehicleCannotBeBuyed", player, new[] { vehicleSettings.name });
            }
        }

        /// <summary>
        /// Spawns the vehicle.
        /// </summary>
        /// <param name="licencedPlayer">The licenced player.</param>
        /// <param name="prefab">The prefab.</param>
        /// <returns></returns>
        private BaseEntity SpawnVehicle(LicencedPlayer licencedPlayer, string prefab)
        {
            var player = licencedPlayer.Player;
            if (player == null) return null;
            var vehicleSettings = GetVehicleSettings(prefab);
            var vehicle = licencedPlayer.GetVehicle(prefab);
            if (vehicle == null) return null;
            var position = player.transform.position + new Vector3(0f, 1.6f, 0f);
            var rotation = player.transform.rotation;
            var entity = GameManager.server.CreateEntity(vehicle.Prefab, position + (Vector3.forward * vehicleSettings.distanceToSpawn), rotation);
            if (entity == null) return null;
            entity.enableSaving = true;
            entity.Spawn();
            if (entity.net == null) return null;
            vehicle.Spawned = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
            vehicle.Id = entity.net.ID;
            vehicle.LastDismount = DateTime.UtcNow;
            _vehiclesCache.Add(vehicle.Id, vehicle);
            licencedPlayer.SetVehicle(vehicle);
            Msg("vehicleSpawned", player, new[] { vehicleSettings.name });

            return entity;
        }

        /// <summary>
        /// Removes the vehicle.
        /// </summary>
        /// <param name="licencedPlayer">The licenced player.</param>
        /// <param name="vehicleSettings">The vehicle settings.</param>
        private void RemoveVehicle(LicencedPlayer licencedPlayer, VehicleSettings vehicleSettings)
        {
            var player = licencedPlayer.Player;
            var vehicle = licencedPlayer.GetVehicle(vehicleSettings.prefab);
            if (player != null && vehicle == null)
            {
                Msg("vehicleNotYetPurchased", player, new[] { vehicleSettings.name });
            }
            else
            {
                var vehicleId = vehicle.Id;
                _vehiclesCache.Remove(vehicle.Id);
                BaseNetworkable.serverEntities.Find(vehicle.Id)?.Kill();
                vehicle.Id = 0;
                licencedPlayer.SetVehicle(vehicle);
                if (player != null && vehicleId != 0)
                {
                    Msg("vehicleRecalled", player, new[] { vehicleSettings.name });
                }
                else if (player != null && vehicleId == 0)
                {
                    Msg("vehicleNotOut", player, new[] { vehicleSettings.name });
                }
            }
        }

        /// <summary>
        /// Checks the vehicles.
        /// </summary>
        private void CheckVehicles()
        {
            foreach (var v in _vehiclesCache.ToList())
            {
                var vehicle = v.Value;
                var vehicleNetworkable = BaseNetworkable.serverEntities.Find(vehicle.Id);
                if (vehicleNetworkable == null) continue;
                var vehicleEntity = vehicleNetworkable.GetComponent<BaseVehicle>();
                if (vehicleEntity == null) continue;
                if (vehicleEntity.IsMounted()) continue;
                if (VehicleIsActive(vehicle)) continue;
                var licencedPlayer = GetLicencedPlayer(vehicle);
                if (licencedPlayer == null) continue;
                RemoveVehicle(licencedPlayer, GetVehicleSettings(vehicle.Prefab));
            }

            timer.Once(_intervalToCheckVehicle * 60f, CheckVehicles);
        }

        /// <summary>
        /// Broadcasts the help.
        /// </summary>
        private void BroadcastHelp()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                Msg("announcement", player);
            }

            timer.Once(60 * Random.Range(15, 45), CheckVehicles);
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Determines whether the specified argument is case.
        /// </summary>
        /// <param name="arg">The argument.</param>
        /// <param name="prefab">The prefab.</param>
        /// <returns>
        ///   <c>true</c> if the specified argument is case; otherwise, <c>false</c>.
        /// </returns>
        private bool IsCase(string arg, string prefab)
        {
            return GetVehicleSettings(prefab).commands.IndexOf(arg) >= 0 && GetVehicleSettings(prefab).commands.IndexOf(arg) < GetVehicleSettings(prefab).commands.Count;
        }

        /// <summary>
        /// Withdraws the specified player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="vehicleSettings">The vehicle settings.</param>
        /// <returns></returns>
        private bool Withdraw(BasePlayer player, VehicleSettings vehicleSettings)
        {
            var result = false;
            var item = ItemManager.FindItemDefinition(_itemsNeededToBuyVehicles);

            if (Economics != null && _useEconomics)
            {
				var playerCoins = (double)Economics.CallHook("Balance", player.UserIDString);
//				PrintWarning("playerCoins = " + playerCoins);
//				PrintWarning("vehicleSettings.price = " + vehicleSettings.price);

				if (playerCoins < vehicleSettings.price) {
					Msg("noMoney", player);
					return false;
				}

                result = Economics.Call<bool>("Withdraw", player.userID, Convert.ToDouble(vehicleSettings.price));
//				PrintWarning("result = " + result);
            }
            else if (ServerRewards != null && _useServerRewards)
            {
				int RPs = (int)ServerRewards?.Call("CheckPoints", player.userID);
				//PrintWarning("RPs = " + RPs);

				if (RPs < vehicleSettings.price) {
					Msg("noMoney", player);
					return false;
				}

				result = (bool)ServerRewards?.Call("TakePoints", player.userID, vehicleSettings.price);
				//PrintWarning("ServerRewards result = " + result);
            }
            else if (item != null && player.inventory.GetAmount(item.itemid) >= vehicleSettings.price)
            {
                player.inventory.Take(null, item.itemid, vehicleSettings.price);
                result = true;
            }

            if (result)
            {
                Msg("vehiclePurchased", player, new[] { vehicleSettings.name });
                return true;
            }

            Msg("noMoney", player);
            return false;
        }

        /// <summary>
        /// Determines whether the specified licenced player is spawning.
        /// </summary>
        /// <param name="licencedPlayer">The licenced player.</param>
        /// <param name="prefab">The prefab.</param>
        /// <param name="water">if set to <c>true</c> [water].</param>
        /// <returns>
        ///   <c>true</c> if the specified licenced player is spawning; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSpawning(LicencedPlayer licencedPlayer, string prefab, bool water = false)
        {
            var player = licencedPlayer.Player;
            if (player == null) return false;
            var vehicleSettings = GetVehicleSettings(prefab);
            var vehicle = licencedPlayer.GetVehicle(prefab);
            if (vehicle == null)
            {
                Msg("vehicleNotYetPurchased", player, new[] { vehicleSettings.name });
                return false;
            }

            if (vehicle.Id != 0)
            {
                Msg("alreadyVehicleOut", player, new[] { vehicleSettings.name });
                return false;
            }

            if (water && !IsInWater(player))
            {
                Msg("notInWater", player);
                return false;
            }

            if (vehicleSettings.cooldownToSpawn > 0 && vehicle.Spawned > (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).Subtract(TimeSpan.FromSeconds(vehicleSettings.cooldownToSpawn)))
            {
                Msg("vehicleOnCooldown", player, new[] { Convert.ToInt32((vehicle.Spawned - (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).Subtract(TimeSpan.FromSeconds(vehicleSettings.cooldownToSpawn))).TotalSeconds).ToString(), vehicleSettings.name });
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether [is in water] [the specified player].
        /// </summary>
        /// <param name="player">The player.</param>
        /// <returns>
        ///   <c>true</c> if [is in water] [the specified player]; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInWater(BasePlayer player)
        {
            var modelState = player.modelState;
            return modelState != null && modelState.waterLevel > 0f;
        }

        /// <summary>
        /// Player has permission.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="arg">The argument.</param>
        /// <returns></returns>
        private bool PlayerHasPermission(BasePlayer player, string arg)
        {
            if (!_usePermissions) return true;
            if (permission.UserHasPermission(player.UserIDString, "vehiclelicence.use")) return true;
            if (IsCase(arg, RowBoatPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.rowboat");
            }

            if (IsCase(arg, RhibPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.rhib");
            }

            if (IsCase(arg, SedanPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.sedan");
            }

            if (IsCase(arg, HotAirBalloonPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.hotairballoon");
            }

            if (IsCase(arg, MiniCopterPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.minicopter");
            }

			if (IsCase(arg, TransportCopterPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.transportcopter");
            }

            if (IsCase(arg, ChinookPrefab))
            {
                return permission.UserHasPermission(player.UserIDString, "vehiclelicence.chinook");
            }
            return false;
        }

        /// <summary>
        /// Gets the vehicle settings.
        /// </summary>
        /// <param name="prefab">The prefab.</param>
        /// <returns></returns>
        private VehicleSettings GetVehicleSettings(string prefab)
        {
            switch (prefab)
            {
                case RowBoatPrefab:
                    return _configData.Vehicles.RowBoat;
                case RhibPrefab:
                    return _configData.Vehicles.RHIB;
                case SedanPrefab:
                    return _configData.Vehicles.Sedan;
                case HotAirBalloonPrefab:
                    return _configData.Vehicles.HotAirBalloon;
                case MiniCopterPrefab:
                    return _configData.Vehicles.MiniCopter;
				case TransportCopterPrefab:
                    return _configData.Vehicles.TransportCopter;
                case ChinookPrefab:
                    return _configData.Vehicles.Chinook;
                default:
                    return new VehicleSettings("null", "null", false, 999999, -1, 0, new List<string>());
            }
        }

        /// <summary>
        /// Gets the licenced player.
        /// </summary>
        /// <param name="vehicle">The vehicle.</param>
        /// <returns></returns>
        private LicencedPlayer GetLicencedPlayer(Vehicle vehicle)
        {
            LicencedPlayer licencedPlayer;
            if (_licencedPlayer.TryGetValue(vehicle.Player.userID, out licencedPlayer)) return licencedPlayer;
            return null;
        }

        /// <summary>
        /// Vehicles the is active.
        /// </summary>
        /// <param name="vehicle">The vehicle.</param>
        /// <returns></returns>
        private bool VehicleIsActive(Vehicle vehicle)
        {
            return vehicle.LastDismount.Ticks >= DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(_timeBeforeVehicleWipe)).Ticks;
        }
        #endregion

        #region Localization
        /// <summary>
        /// MSGs the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="player">The player.</param>
        /// <param name="args">The arguments.</param>
        private void Msg(string key, BasePlayer player, object[] args = null)
        {
            var message = lang.GetMessage(key, this, player.UserIDString);
            if (args != null)
            {
                message = string.Format(message, args);
            }
            Player.Message(player, message, Prefix, 76561198924840872);
        }

        /// <summary>
        /// Loads the default messages.
        /// </summary>
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["announcement"] = "Type <color=#4DFF4D>/license</color> to get help.",
				["raidblocked"] = "<color=#FF1919>You may not do that while raid blocked</color>.",
				["comatblocked"] = "<color=#FF1919>You may not do that while combat blocked</color>.",
                ["helpLicence"] = "These are the available commands: \n" +
                    "<color=#4DFF4D>/buy</color> -- To buy a vehicle \n" +
                    "<color=#4DFF4D>/spawn</color> -- To spawn a vehicle \n" +
                    "<color=#4DFF4D>/recall</color> -- To recall a vehicle",
                ["helpBuy"] = "These are the available commands: \n" +
                    "Item needed: <color=#FF1919>{0}</color> \n" +
                    "<color=#4DFF4D>/buy row</color> -- <color=#FF1919>{1}</color> to buy a rowing boat \n" +
                    "<color=#4DFF4D>/buy rhib</color> -- <color=#FF1919>{2}</color> to buy a RHIB \n" +
                    "<color=#4DFF4D>/buy sedan</color> -- <color=#FF1919>{3}</color> to buy a sedan \n" +
                    "<color=#4DFF4D>/buy hab</color> -- <color=#FF1919>{4}</color> to buy an hot air balloon \n" +
                    "<color=#4DFF4D>/buy copter</color> -- <color=#FF1919>{5}</color> to buy a mini copter \n" +
					"<color=#4DFF4D>/buy transport</color> -- <color=#FF1919>{6}</color> to buy a transport copter \n" +
                    "<color=#4DFF4D>/buy ch47</color> -- <color=#FF1919>{7}</color> to buy a chinook \n",
                ["helpSpawn"] = "These are the available commands: \n" +
                    "<color=#4DFF4D>/spawn row</color> -- To spawn a rowing boat \n" +
                    "<color=#4DFF4D>/spawn rhib</color> -- To spawn a RHIB \n" +
                    "<color=#4DFF4D>/spawn sedan</color> -- To spawn a sedan \n" +
                    "<color=#4DFF4D>/spawn hab</color> -- To spawn an hot air balloon \n" +
                    "<color=#4DFF4D>/spawn copter</color> -- To spawn a mini copter \n" +
					"<color=#4DFF4D>/spawn transport</color> -- To spawn a transport copter \n" +
                    "<color=#4DFF4D>/spawn ch47</color> -- To spawn a chinook \n",
                ["helpRecall"] = "These are the available commands: \n" +
                    "<color=#4DFF4D>/recall row</color> -- To recall a rowing boat \n" +
                    "<color=#4DFF4D>/recall rhib</color> -- To recall a RHIB \n" +
                    "<color=#4DFF4D>/recall sedan</color> -- To recall a sedan \n" +
                    "<color=#4DFF4D>/recall hab</color> -- To recall an hot air balloon \n" +
                    "<color=#4DFF4D>/recall copter</color> -- To recall a mini copter \n" +
					"<color=#4DFF4D>/recall transport</color> -- To recall a transport copter \n" +
                    "<color=#4DFF4D>/recall ch47</color> -- To recall a Chinook \n",
                ["helpOptionNotFound"] = "This option doesn't exist.",
                ["vehiclePurchased"] = "You have purchased a {0}, type <color=#4DFF4D>/spawn</color> for more information.",
                ["vehicleAlreadyPurchased"] = "You have already purchased {0}.",
                ["vehicleCannotBeBuyed"] = "You can't buy a {0}.",
                ["vehicleNotOut"] = "{0} is not out.",
                ["noMoney"] = "You don't have enough money.",
                ["didntBuyVehicle"] = "You didn't purchase a vehicle.",
                ["alreadyVehicleOut"] = "You already have a {0} outside, type <color=#4DFF4D>/spawn</color> for more information.",
                ["vehicleNotYetPurchased"] = "You have not yet purchased a {0}.",
                ["vehicleSpawned"] = "You spawned your {0}.",
                ["vehicleRecalled"] = "You recalled your {0}.",
                ["vehicleOnCooldown"] = "You must wait {0} seconds before you can spawn your {1}.",
                ["notInWater"] = "You must be in the water to use this command.",
                ["buildindBlocked"] = " You can't spawn a boat appear if you don't have the building privileges.",
                ["noPermission"] = "You do not have permission to do this."
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["announcement"] = "Tapez <color=#4DFF4D>/license</color> pour obtenir de l'aide.",
				["raidblocked"] = "<color=#FF1919>You may not do that while raid blocked</color>.",
				["comatblocked"] = "<color=#FF1919>You may not do that while combat blocked</color>.",
                ["helpLicence"] = "Voici les commandes disponibles : \n" +
                    "<color=#4DFF4D>/buy</color> -- Pour acheter un véhicule \n" +
                    "<color=#4DFF4D>/spawn</color> -- Pour faire apparaître un véhicule \n" +
                    "<color=#4DFF4D>/recall</color> -- Pour ranger un véhicule",
                ["helpBuy"] = "Voici les commandes disponibles : \n" +
                    "Objet requis : <color=#FF1919>{0}</color> \n" +
                    "<color=#4DFF4D>/buy row</color> -- <color=#FF1919>{1}</color> pour acheter un bateau à rames \n" +
                    "<color=#4DFF4D>/buy rhib</color> -- <color=#FF1919>{2}</color> pour acheter un RHIB \n" +
                    "<color=#4DFF4D>/buy sedan</color> -- <color=#FF1919>{3}</color> pour acheter une voiture \n" +
                    "<color=#4DFF4D>/buy hab</color> -- <color=#FF1919>{4}</color> pour acheter une montgolfière \n" +
                    "<color=#4DFF4D>/buy copter</color> -- <color=#FF1919>{5}</color> pour acheter un mini hélicoptère \n" +
					"<color=#4DFF4D>/buy transport</color> -- <color=#FF1919>{6}</color> pour acheter un hélicoptère de transport \n" +
                    "<color=#4DFF4D>/buy ch47</color> -- <color=#FF1919>{7}</color> pour acheter un Chinook \n",
                ["helpSpawn"] = "Voici les commandes disponibles : \n" +
                    "<color=#4DFF4D>/spawn row</color> -- Pour faire apparaître un bateau à rames \n" +
                    "<color=#4DFF4D>/spawn rhib</color> -- Pour faire apparaître un RHIB \n" +
                    "<color=#4DFF4D>/spawn sedan</color> -- Pour faire apparaître une voiture \n" +
                    "<color=#4DFF4D>/spawn hab</color> -- Pour faire apparaître une montgolfière \n" +
                    "<color=#4DFF4D>/spawn copter</color> -- Pour faire apparaître un mini hélicoptère \n" +
					"<color=#4DFF4D>/spawn transport</color> -- Pour faire apparaître un hélicoptère de transport \n" +
                    "<color=#4DFF4D>/spawn ch47</color> -- Pour faire apparaître un Chinook \n",
                ["helpRecall"] = "Voici les commandes disponibles : \n" +
                    "<color=#4DFF4D>/recall row</color> -- Pour ranger un bateau à rames \n" +
                    "<color=#4DFF4D>/recall rhib</color> -- Pour ranger un RHIB \n" +
                    "<color=#4DFF4D>/recall sedan</color> -- Pour ranger une voiture \n" +
                    "<color=#4DFF4D>/recall hab</color> -- Pour ranger une montgolfière \n" +
                    "<color=#4DFF4D>/recall copter</color> -- Pour ranger un mini hélicoptère \n" +
					"<color=#4DFF4D>/recall transport</color> -- Pour ranger un hélicoptère de transport \n" +
                    "<color=#4DFF4D>/recall ch47</color> -- Pour ranger un Chinook \n",
                ["helpOptionNotFound"] = "Cette option n'existe pas.",
                ["vehiclePurchased"] = "Vous avez acheté un {0}, tapez <color=#4DFF4D>/spawn</color> pour plus d'informations.",
                ["vehicleAlreadyPurchased"] = "Vous avez déjà acheté ce {0}.",
                ["vehicleCannotBeBuyed"] = "Vous ne pouvez pas acheter un {0}.",
                ["vehicleNotOut"] = "{0} n'est pas dehors.",
                ["noMoney"] = "Vous n'avez pas assez d'argent.",
                ["didntBuyVehicle"] = "Vous n'avez pas acheté de vehicule.",
                ["alreadyVehicleOut"] = "Vous avez déjà un {0} à l'extérieur, tapez <color=#4DFF4D>/recall</color> pour plus d'informations.",
                ["vehicleNotYetPurchased"] = "Vous n'avez pas encore acheté de {0}.",
                ["vehicleSpawned"] = "Vous avez fait apparaître votre {0}.",
                ["vehicleRecalled"] = "Vous avez rangé votre {0}.",
                ["vehicleOnCooldown"] = "Vous devez attendre {0} secondes avant de pouvoir faire apparaître votre {1}.",
                ["notInWater"] = "Vous devez être dans l'eau pour utiliser cette commande.",
                ["buildindBlocked"] = "Vous ne pouvez pas faire apparaître un {0} si vous n'avez pas les privilèges de construction.",
                ["noPermission"] = "Vous n'avez pas la permission de faire ceci."
            }, this, "fr");

			lang.RegisterMessages(new Dictionary<string, string>
            {
                ["announcement"] = "Tippe <color=#4DFF4D>/license</color> um Hilfe zu bekommen.",
				["raidblocked"] = "<color=#FF1919>Dies geht nicht während eines Raids</color>.",
				["comatblocked"] = "<color=#FF1919>Dies geht nicht während eines Kampfes</color>.",
                ["helpLicence"] = "Verfügbare Kommandos: \n" +
                    "<color=#4DFF4D>/buy</color> -- Fahrzeug kaufen \n" +
                    "<color=#4DFF4D>/spawn</color> -- Fahrzeug ausparken \n" +
                    "<color=#4DFF4D>/recall</color> -- Fahrzeug einparken",
                ["helpBuy"] = "Verfügbare Kommandos: \n" +
                    "Item needed: <color=#FF1919>{0}</color> \n" +
                    "<color=#4DFF4D>/buy row</color> -- <color=#FF1919>{1}</color> Boot kaufen \n" +
                    "<color=#4DFF4D>/buy rhib</color> -- <color=#FF1919>{2}</color> Motorboot kaufen \n" +
                    "<color=#4DFF4D>/buy sedan</color> -- <color=#FF1919>{3}</color> Auto kaufen \n" +
                    "<color=#4DFF4D>/buy hab</color> -- <color=#FF1919>{4}</color> Heißluftballon kaufen \n" +
                    "<color=#4DFF4D>/buy copter</color> -- <color=#FF1919>{5}</color> Helikopter kaufen \n" +
					"<color=#4DFF4D>/buy transport</color> -- <color=#FF1919>{6}</color> Transporthelikopter kaufen \n" +
                    "<color=#4DFF4D>/buy ch47</color> -- <color=#FF1919>{7}</color> Tandemhubschrauber kaufen \n",
                ["helpSpawn"] = "Verfügbare Kommandos: \n" +
                    "<color=#4DFF4D>/spawn row</color> -- Boot ausparken \n" +
                    "<color=#4DFF4D>/spawn rhib</color> -- Motorboot ausparken \n" +
                    "<color=#4DFF4D>/spawn sedan</color> -- Auto ausparken \n" +
                    "<color=#4DFF4D>/spawn hab</color> -- Heißluftballon ausparken \n" +
                    "<color=#4DFF4D>/spawn copter</color> -- Helikopter ausparken \n" +
					"<color=#4DFF4D>/spawn transport</color> -- Transporthelikopter ausparken \n" +
                    "<color=#4DFF4D>/spawn ch47</color> -- Tandemhubschrauber ausparken \n",
                ["helpRecall"] = "Verfügbare Kommandos: \n" +
                    "<color=#4DFF4D>/recall row</color> -- Boot einparken \n" +
                    "<color=#4DFF4D>/recall rhib</color> -- Motorboot einparken \n" +
                    "<color=#4DFF4D>/recall sedan</color> -- Auto einparken \n" +
                    "<color=#4DFF4D>/recall hab</color> -- Heißluftballon einparken \n" +
                    "<color=#4DFF4D>/recall copter</color> -- Helikopter einparken \n" +
					"<color=#4DFF4D>/recall transport</color> -- Transporthelikopter einparken \n" +
                    "<color=#4DFF4D>/recall ch47</color> -- Tandemhubschrauber einparken \n",
                ["helpOptionNotFound"] = "Diese Option existiert nicht.",
                ["vehiclePurchased"] = "Du hast ein {0} gekauft, tippe <color=#4DFF4D>/spawn</color> für mehr Informationen.",
                ["vehicleAlreadyPurchased"] = "Du hast bereits ein {0} gekauft.",
                ["vehicleCannotBeBuyed"] = "Du kannst kein {0} kaufen.",
                ["vehicleNotOut"] = "{0} ist nicht ausgeparkt.",
                ["noMoney"] = "Du hast nicht genügend Geld.",
                ["didntBuyVehicle"] = "Du hast kein Fahrzeug gekauft.",
                ["alreadyVehicleOut"] = "Du hast bereits ein {0} ausgeparkt, tippe <color=#4DFF4D>/spawn</color> für mehr Informationen.",
                ["vehicleNotYetPurchased"] = "Du hast noch kein {0} gekauft.",
                ["vehicleSpawned"] = "Du hast dein {0} ausgeparkt.",
                ["vehicleRecalled"] = "Du hast dein {0} eingeparkt.",
                ["vehicleOnCooldown"] = "Du musst {0} Sekunden warten bevor du dein {1} ausparken kannst.",
                ["notInWater"] = "Du musst dich hierfür im Wasser befinden.",
                ["buildindBlocked"] = "Du kannst hier nichts ausparken, da dir hier die Bauberechtigung fehlt.",
                ["noPermission"] = "Dir fehlt hierfür die Berechtigung."
            }, this, "de");
        }
        #endregion

        #region Config
        /// <summary>
        /// Loads the configuration.
        /// </summary>
        protected override void LoadConfig()
        {
            base.LoadConfig();
            _configData = Config.ReadObject<ConfigData>();

            Config.WriteObject(_configData, true);
        }

        /// <summary>
        /// Loads the default configuration.
        /// </summary>
        protected override void LoadDefaultConfig() => _configData = GetBaseConfig();

        /// <summary>
        /// Saves the configuration.
        /// </summary>
        protected override void SaveConfig() => Config.WriteObject(_configData, true);

		bool IsRaidBlocked(string targetId) => _useRaidBlocker && (bool)(NoEscape?.Call("IsRaidBlocked", targetId) ?? false);
		bool IsCombatBlocked(string targetId) => _useCombatBlocker && (bool)(NoEscape?.Call("IsCombatBlocked", targetId) ?? false);

        /// <summary>
        /// Loads the data.
        /// </summary>
        private void LoadData()
        {
            _licencedPlayer = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, LicencedPlayer>>("VehicleLicence");
        }

        /// <summary>
        /// Saves the data.
        /// </summary>
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("VehicleLicence", _licencedPlayer);

        /// <summary>
        /// Gets the base configuration.
        /// </summary>
        /// <returns>
        /// Config data
        /// </returns>
        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Settings = new ConfigData.SettingsOption
                {
                    IntervalToCheckVehicles = 5,
                    TimeBeforeVehicleWipe = 15,
                    UseEconomics = false,
                    UseServerRewards = false,
                    ItemsNeededToBuyVehicles = "scrap",
                    UsePermissions = false
                },

                Vehicles = new ConfigData.VehiclesOption
                {
                    RowBoat = new VehicleSettings("Row Boat", RowBoatPrefab, true, 500, 180, 3, new List<string> { "row", "rowboat" }),
                    RHIB = new VehicleSettings("RHIB", RhibPrefab, true, 1000, 300, 10, new List<string> { "rhib" }),
                    Sedan = new VehicleSettings("Sedan", SedanPrefab, true, 300, 180, 5, new List<string> { "sedan", "car" }),
                    HotAirBalloon = new VehicleSettings("Hot Air Balloon", HotAirBalloonPrefab, true, 5000, 900, 20, new List<string> { "hotairballoon", "hab" }),
                    MiniCopter = new VehicleSettings("MiniCopter", MiniCopterPrefab, true, 10000, 1800, 8, new List<string> { "minicopter", "copter" }),
					TransportCopter = new VehicleSettings("TransportCopter", TransportCopterPrefab, true, 20000, 2400, 10, new List<string> { "transportcopter", "transport" }),
                    Chinook = new VehicleSettings("Chinook", ChinookPrefab, true, 30000, 3000, 25, new List<string> { "chinook", "ch47" })
                }
            };
        }
        #endregion

        #region Class
        class ConfigData
        {
            [JsonProperty(PropertyName = "Define your licence settings")]
            public SettingsOption Settings { get; set; }

            public class SettingsOption
            {
                [JsonProperty(PropertyName = "Interval in minutes to check vehicle for wipe")]
                public int IntervalToCheckVehicles { get; set; }
                [JsonProperty(PropertyName = "Time before vehicle wipe in minutes")]
                public int TimeBeforeVehicleWipe { get; set; }
                [JsonProperty(PropertyName = "Use Economics to buy vehicles")]
                public bool UseEconomics { get; set; }
                [JsonProperty(PropertyName = "Use ServerRewards to buy vehicles")]
                public bool UseServerRewards { get; set; }
                [JsonProperty(PropertyName = "Shortname of item needed to buy vehicles")]
                public string ItemsNeededToBuyVehicles { get; set; }
                [JsonProperty(PropertyName = "Use permissions for chat commands")]
                public bool UsePermissions { get; set; }
                [JsonProperty(PropertyName = "Use Raid Blocker")]
                public bool UseRaidBlocker { get; set; }
                [JsonProperty(PropertyName = "Use Combat Blocker")]
                public bool UseCombatBlocker { get; set; }
                [JsonProperty(PropertyName = "Remove Vehicles On Crash")]
                public bool RemoveVehicleOnCrash { get; set; }
            }

            [JsonProperty(PropertyName = "Define your vehicles options")]
            public VehiclesOption Vehicles { get; set; }

            public class VehiclesOption
            {
                [JsonProperty(PropertyName = "RowBoat")]
                public VehicleSettings RowBoat { get; set; }
                [JsonProperty(PropertyName = "RHIB")]
                public VehicleSettings RHIB { get; set; }
                [JsonProperty(PropertyName = "Sedan")]
                public VehicleSettings Sedan { get; set; }
                [JsonProperty(PropertyName = "HotAirBalloon")]
                public VehicleSettings HotAirBalloon { get; set; }
                [JsonProperty(PropertyName = "MiniCopter")]
                public VehicleSettings MiniCopter { get; set; }
				[JsonProperty(PropertyName = "TransportCopter")]
                public VehicleSettings TransportCopter { get; set; }
                [JsonProperty(PropertyName = "Chinook")]
                public VehicleSettings Chinook { get; set; }
            }
        }

        class LicencedPlayer
        {
            public readonly ulong Userid;
            public Dictionary<string, Vehicle> Vehicles;

            [JsonConstructor]
            public LicencedPlayer(ulong userid, Dictionary<string, Vehicle> vehicles)
            {
                Userid = userid;
				Vehicles = vehicles ?? new Dictionary<string, Vehicle>();
            }

            [JsonIgnore]
            public BasePlayer Player => BasePlayer.FindByID(Userid);

            [JsonIgnore]
            public ulong userid
            {
                get
                {
                    return Userid;
                }
            }

            public void SetVehicle(Vehicle vehicle)
            {
                if (Vehicles.ContainsKey(vehicle.Prefab))
                {
                    Vehicles[vehicle.Prefab] = vehicle;
                }
                else
                {
                    Vehicles.Add(vehicle.Prefab, vehicle);
                }
            }

            public Vehicle GetVehicle(string prefab)
            {
                Vehicle result = null;

                if (Vehicles.ContainsKey(prefab))
                {
                    result = Vehicles[prefab];
                }

                return result;
            }

            public Vehicle GetVehicle(Vehicle vehicle)
            {
                Vehicle result = null;

                if (Vehicles.ContainsKey(vehicle.Prefab))
                {
                    result = Vehicles[vehicle.Prefab];
                }

                return result;
            }
        }

        class Vehicle
        {
            public ulong Userid { get; }
            public string Prefab { get; }
            public uint Id { get; set; }
            public TimeSpan Spawned { get; set; }
            public DateTime LastDismount { get; set; }

            [JsonConstructor]
            public Vehicle(string prefab, ulong userid)
            {
                Userid = userid;
                Prefab = prefab;
                LastDismount = DateTime.MinValue;
            }

            [JsonIgnore]
            public BasePlayer Player => BasePlayer.FindByID(Userid);
        }

        class VehicleSettings
        {
            private string Name;
            private string Prefab;
            private bool Purchasable;
            private int Price;
            private int CooldownToSpawn;
            private int DistanceToSpawn;
            private List<string> Commands;

            [JsonConstructor]
            public VehicleSettings(string name, string prefab, bool purchasable, int price, int cooldownToSpawn, int distanceToSpawn, List<string> commands)
            {
                Name = name;
                Prefab = prefab;
                Purchasable = purchasable;
                Price = price;
                CooldownToSpawn = cooldownToSpawn;
                DistanceToSpawn = distanceToSpawn;
                Commands = commands;
            }

            public string name
            {
                get
                {
                    return Name;
                }
            }

            public string prefab
            {
                get
                {
                    return Prefab;
                }
            }

            public bool purchasable
            {
                get
                {
                    return Purchasable;
                }
            }

            public int price
            {
                get
                {
                    return Price;
                }
            }

            public int cooldownToSpawn
            {
                get
                {
                    return CooldownToSpawn;
                }
            }

            public int distanceToSpawn
            {
                get
                {
                    return DistanceToSpawn;
                }
            }

            public List<string> commands
            {
                get
                {
                    return Commands;
                }
            }
        }
        #endregion
    }
}
