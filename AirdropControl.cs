using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AirdropControl", "mcduffiejohn", "1.0.0")]
    [Description("Adjust airdrop frequency, loot table, and show a map marker that clears after looting")]
    public class AirdropControl : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            // -- Frequency ------------------------------------------------------------
            [JsonProperty("Custom drop frequency in minutes (0 = do not override server timing)")]
            public float FrequencyMinutes = 0f;

            [JsonProperty("Max simultaneous active drops on the map (0 = no cap)")]
            public int MaxActiveDrops = 0;

            // -- Map marker -----------------------------------------------------------
            [JsonProperty("Show map marker when drop lands")]
            public bool ShowMapMarker = true;

            [JsonProperty("Map marker fill color hex (e.g. #FF6600 for orange)")]
            public string MarkerColor = "#FF6600";

            [JsonProperty("Map marker radius on map (0.1 small - 0.5 large)")]
            public float MarkerRadius = 0.3f;

            // -- Announcements --------------------------------------------------------
            [JsonProperty("Broadcast to all players when a drop lands")]
            public bool AnnounceOnLand = true;

            [JsonProperty("Broadcast to all players when a drop is first opened")]
            public bool AnnounceOnFirstLoot = true;

            // -- Loot -----------------------------------------------------------------
            [JsonProperty("Override drop loot with table below (false = game default loot)")]
            public bool OverrideLoot = false;

            [JsonProperty("Items to put in each drop when override is on (0 = fill all slots)")]
            public int ItemsPerDrop = 6;

            [JsonProperty("Loot table -- only used when Override is true")]
            public List<LootEntry> LootTable = BuildDefaultLootTable();

            public static List<LootEntry> BuildDefaultLootTable() => new List<LootEntry>
            {
                new LootEntry("rifle.ak",           "AK-47",               1,   1,   0.15f),
                new LootEntry("rifle.lr300",         "LR-300",              1,   1,   0.15f),
                new LootEntry("rifle.m39",           "M39 Rifle",           1,   1,   0.10f),
                new LootEntry("lmg.m249",            "M249",                1,   1,   0.05f),
                new LootEntry("ammo.rifle",          "5.56 Rifle Ammo",     64,  256, 0.85f),
                new LootEntry("ammo.pistol",         "Pistol Bullet",       64,  128, 0.60f),
                new LootEntry("explosive.timed",     "C4",                  1,   3,   0.30f),
                new LootEntry("grenade.beancan",     "Beancan Grenade",     2,   6,   0.40f),
                new LootEntry("medical.syringe",     "Medical Syringe",     2,   5,   0.75f),
                new LootEntry("largemedkit",         "Large Medkit",        1,   2,   0.50f),
                new LootEntry("coffeecan.helmet",    "Coffee Can Helmet",   1,   1,   0.25f),
                new LootEntry("heavy.plate.jacket",  "Heavy Plate Jacket",  1,   1,   0.20f),
                new LootEntry("fuel.lowgrade",       "Low Grade Fuel",      50,  200, 0.60f),
                new LootEntry("gunpowder",           "Gun Powder",          100, 500, 0.55f),
                new LootEntry("metal.refined",       "High Quality Metal",  5,   25,  0.45f),
                new LootEntry("cloth",               "Cloth",               50,  200, 0.50f),
            };
        }

        private class LootEntry
        {
            [JsonProperty("shortname")]   public string Shortname   = "";
            [JsonProperty("displayName")] public string DisplayName = "";
            [JsonProperty("minAmount")]   public int    MinAmount   = 1;
            [JsonProperty("maxAmount")]   public int    MaxAmount   = 1;
            [JsonProperty("probability")] public float  Probability = 1.0f;
            [JsonProperty("skinId")]      public ulong  SkinId      = 0UL;

            public LootEntry() { }
            public LootEntry(string sn, string dn, int min, int max, float prob)
            {
                Shortname = sn; DisplayName = dn; MinAmount = min; MaxAmount = max; Probability = prob;
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
                if (_config.LootTable == null) _config.LootTable = Configuration.BuildDefaultLootTable();
            }
            catch
            {
                PrintError("Config is corrupt -- resetting to defaults.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region State

        // SupplyDrop net.ID -> map marker (ulong in this Rust build)
        private readonly Dictionary<ulong, MapMarkerGenericRadius> _markers =
            new Dictionary<ulong, MapMarkerGenericRadius>();

        // SupplyDrop net.IDs that are currently active (spawned, not yet killed)
        private readonly HashSet<ulong> _activeDropIds = new HashSet<ulong>();

        // SupplyDrop net.IDs that have already had a first-loot announcement
        private readonly HashSet<ulong> _announcedLoot = new HashSet<ulong>();

        private Timer _frequencyTimer;

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            if (_config == null)
            {
                LoadDefaultConfig();
                SaveConfig();
            }
            StartFrequencyTimer();
            Puts("Loaded.");
        }

        private void Unload()
        {
            _frequencyTimer?.Destroy();
            foreach (var marker in _markers.Values)
                if (marker != null && !marker.IsDestroyed)
                    marker.Kill();
            _markers.Clear();
        }

        // Track supply drops as they spawn (includes the parachute phase)
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var drop = entity as SupplyDrop;
            if (drop?.net == null) return;

            _activeDropIds.Add(drop.net.ID.Value);

            if (_config.OverrideLoot)
            {
                // Brief delay so the game has time to run its own PopulateLoot first, then we replace
                ulong id = drop.net.ID.Value;
                timer.Once(0.3f, () =>
                {
                    if (drop == null || drop.IsDestroyed) return;
                    ReplaceDropLoot(drop);
                });
            }
        }

        // Drop has hit the ground -- create marker and announce
        private void OnSupplyDropLanded(SupplyDrop drop)
        {
            if (drop?.net == null) return;

            if (_config.ShowMapMarker)
                CreateMarker(drop);

            if (_config.AnnounceOnLand)
            {
                string grid = ToGridRef(drop.transform.position);
                Server.Broadcast(
                    $"<color=#FF6600>[ Airdrop ]</color> Supply drop has landed at <color=#FFD700>{grid}</color>."
                );
            }
        }

        // Player opens the supply drop
        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!_config.AnnounceOnFirstLoot || player == null) return;
            var drop = entity as SupplyDrop;
            if (drop?.net == null) return;

            ulong id = drop.net.ID.Value;
            if (_announcedLoot.Contains(id)) return;
            _announcedLoot.Add(id);

            string grid = ToGridRef(drop.transform.position);
            Server.Broadcast(
                $"<color=#FF6600>[ Airdrop ]</color> <color=#FFD700>{player.displayName}</color> is looting the drop at {grid}."
            );
        }

        // Player closes the supply drop -- remove marker if all items are gone
        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            var drop = entity as SupplyDrop;
            if (drop?.net == null) return;

            if (drop.inventory == null || drop.inventory.itemList.Count == 0)
                RemoveMarker(drop.net.ID.Value);
        }

        // Drop entity destroyed (natural despawn or looted out) -- always clean up
        private void OnEntityKill(BaseNetworkable entity)
        {
            var drop = entity as SupplyDrop;
            if (drop?.net == null) return;

            ulong id = drop.net.ID.Value;
            _activeDropIds.Remove(id);
            _announcedLoot.Remove(id);
            RemoveMarker(id);
        }

        #endregion

        #region Frequency Timer

        private void StartFrequencyTimer()
        {
            _frequencyTimer?.Destroy();
            if (_config.FrequencyMinutes <= 0f) return;

            float seconds = _config.FrequencyMinutes * 60f;
            _frequencyTimer = timer.Every(seconds, TriggerAirdrop);
            Puts($"Custom airdrop timer started -- every {_config.FrequencyMinutes} min.");
        }

        private void TriggerAirdrop()
        {
            if (_config.MaxActiveDrops > 0 && _activeDropIds.Count >= _config.MaxActiveDrops)
            {
                Puts($"Airdrop skipped -- {_activeDropIds.Count}/{_config.MaxActiveDrops} active.");
                return;
            }
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "event.airdrop");
        }

        #endregion

        #region Map Marker

        private const string MarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";

        private void CreateMarker(SupplyDrop drop)
        {
            var marker = GameManager.server.CreateEntity(MarkerPrefab, drop.transform.position)
                as MapMarkerGenericRadius;

            if (marker == null) return;

            Color fill = new Color(1f, 0.4f, 0f); // fallback orange
            ColorUtility.TryParseHtmlString(_config.MarkerColor, out fill);

            marker.alpha  = 0.85f;
            marker.color1 = fill;
            marker.color2 = Color.white;
            marker.radius = Mathf.Clamp(_config.MarkerRadius, 0.05f, 1f);
            marker.Spawn();
            marker.SendUpdate();

            _markers[drop.net.ID.Value] = marker;
        }

        private void RemoveMarker(ulong dropId)
        {
            if (!_markers.TryGetValue(dropId, out var marker)) return;
            _markers.Remove(dropId);
            if (marker != null && !marker.IsDestroyed)
                marker.Kill();
        }

        #endregion

        #region Custom Loot

        private void ReplaceDropLoot(SupplyDrop drop)
        {
            if (_config.LootTable == null || _config.LootTable.Count == 0) return;

            drop.inventory.Clear();

            int target = _config.ItemsPerDrop > 0
                ? Math.Min(_config.ItemsPerDrop, drop.inventory.capacity)
                : drop.inventory.capacity;

            // Fisher-Yates shuffle for variety each drop
            var pool = new List<LootEntry>(_config.LootTable);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }

            int added = 0;
            foreach (var entry in pool)
            {
                if (added >= target) break;
                if (string.IsNullOrEmpty(entry.Shortname)) continue;
                if (UnityEngine.Random.value > entry.Probability) continue;

                int amount = UnityEngine.Random.Range(entry.MinAmount, entry.MaxAmount + 1);
                var item = ItemManager.CreateByName(entry.Shortname, amount, entry.SkinId);
                if (item == null)
                {
                    PrintWarning($"Unknown shortname in loot table: '{entry.Shortname}'");
                    continue;
                }

                if (!item.MoveToContainer(drop.inventory))
                    item.Remove();
                else
                    added++;
            }
        }

        #endregion

        #region Grid Reference

        private static string ToGridRef(Vector3 pos)
        {
            float mapSize = ConVar.Server.worldsize;
            const float cellSize = 150f;

            int col = Mathf.Clamp(Mathf.FloorToInt((pos.x + mapSize * 0.5f) / cellSize), 0, 999);
            int row = Mathf.Clamp(Mathf.FloorToInt((mapSize * 0.5f - pos.z) / cellSize), 0, 999);

            // Build column letter(s) -- handles AA, AB, etc. on large maps
            string letter = string.Empty;
            int c = col;
            do
            {
                letter = (char)('A' + c % 26) + letter;
                c = c / 26 - 1;
            }
            while (c >= 0);

            return $"{letter}{row + 1}";
        }

        #endregion

        #region Commands

        [ChatCommand("airdropcontrol")]
        private void ChatCmd(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { SendReply(player, "Admins only."); return; }
            SendReply(player, BuildStatus());
        }

        [ConsoleCommand("airdropcontrol")]
        private void ConsoleCmd(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;
            arg.ReplyWith(BuildStatus());
        }

        [ConsoleCommand("airdropcontrol.trigger")]
        private void ConsoleTrigger(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "event.airdrop");
            arg.ReplyWith("Airdrop triggered.");
        }

        [ConsoleCommand("airdropcontrol.reload")]
        private void ConsoleReload(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;
            LoadConfig();
            StartFrequencyTimer();
            arg.ReplyWith("AirdropControl: config reloaded.");
        }

        private string BuildStatus()
        {
            return
                "=== Airdrop Control ===\n" +
                $"  Frequency       : {(_config.FrequencyMinutes <= 0 ? "server default" : $"{_config.FrequencyMinutes} min")}\n" +
                $"  Max active drops: {(_config.MaxActiveDrops <= 0 ? "no cap" : _config.MaxActiveDrops.ToString())}\n" +
                $"  Active drops    : {_activeDropIds.Count}\n" +
                $"  Map marker      : {_config.ShowMapMarker} (color {_config.MarkerColor}, radius {_config.MarkerRadius})\n" +
                $"  Announce land   : {_config.AnnounceOnLand}\n" +
                $"  Announce loot   : {_config.AnnounceOnFirstLoot}\n" +
                $"  Override loot   : {_config.OverrideLoot} ({_config.LootTable?.Count ?? 0} entries, {_config.ItemsPerDrop} items/drop)\n" +
                "Commands: airdropcontrol.trigger | airdropcontrol.reload";
        }

        #endregion
    }
}
