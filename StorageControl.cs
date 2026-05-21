using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("StorageControl", "mcduffiejohn", "1.0.0")]
    [Description("Resize storage container slot counts via config")]
    public class StorageControl : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            // -- General storage -----------------------------------------------------
            [JsonProperty("Small wooden box slots (game default 12)")]
            public int SmallBox = 12;

            [JsonProperty("Large wooden box slots (game default 30)")]
            public int LargeBox = 30;

            [JsonProperty("Fridge slots (game default 36)")]
            public int Fridge = 36;

            [JsonProperty("Locker slots (game default 30)")]
            public int Locker = 30;

            [JsonProperty("Drop box slots (game default 6)")]
            public int DropBox = 6;

            [JsonProperty("Vending machine slots (game default 9)")]
            public int VendingMachine = 9;

            [JsonProperty("Tool cupboard slots (game default 12)")]
            public int ToolCupboard = 12;

            // -- Crafting / cooking --------------------------------------------------
            // These containers have functional slot roles (fuel, input, output).
            // Only increase these -- shrinking below the functional minimum breaks them.
            [JsonProperty("Furnace slots -- functional slots, increase only (game default 6)")]
            public int Furnace = 6;

            [JsonProperty("Large furnace slots -- functional slots, increase only (game default 18)")]
            public int LargeFurnace = 18;

            [JsonProperty("Campfire slots -- functional slots, increase only (game default 12)")]
            public int Campfire = 12;

            [JsonProperty("BBQ slots -- functional slots, increase only (game default 12)")]
            public int BBQ = 12;

            [JsonProperty("Oil refinery slots -- functional slots, increase only (game default 5)")]
            public int OilRefinery = 5;

            [JsonProperty("Mixing table slots -- functional slots, increase only (game default 6)")]
            public int MixingTable = 6;
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
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

        #region Prefab Mapping

        // Returns configured slot count for the given ShortPrefabName, or -1 if unmanaged.
        private int GetConfiguredSlots(string prefabName)
        {
            switch (prefabName)
            {
                // General storage
                case "woodbox_deployed":        return _config.SmallBox;
                case "large_woodbox_deployed":  return _config.LargeBox;
                case "fridge.deployed":         return _config.Fridge;
                case "locker.deployed":         return _config.Locker;
                case "dropbox.deployed":        return _config.DropBox;
                case "vendingmachine.deployed": return _config.VendingMachine;
                case "cupboard.tool.deployed":  return _config.ToolCupboard;

                // Crafting / cooking
                case "furnace":                 return _config.Furnace;
                case "furnace.large":           return _config.LargeFurnace;
                case "campfire":                return _config.Campfire;
                case "bbq.deployed":            return _config.BBQ;
                case "refinery_small_deployed": return _config.OilRefinery;
                case "mixingtable.deployed":    return _config.MixingTable;

                default:                        return -1;
            }
        }

        // Pure storage containers get panelName = "generic" so the client renders
        // a scrollable grid instead of the fixed-layout native panel.
        // Cooking/crafting containers and containers with special UIs (vending, TC)
        // must keep their native panels -- changing them breaks functional slot roles
        // or the container's own UI (auth list, market screen, etc.).
        private static bool UseGenericPanel(string prefabName)
        {
            switch (prefabName)
            {
                case "woodbox_deployed":
                case "large_woodbox_deployed":
                case "fridge.deployed":
                case "locker.deployed":
                case "dropbox.deployed":
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Apply Helpers

        private void ApplyContainer(StorageContainer container)
        {
            if (container?.inventory == null) return;
            int slots = GetConfiguredSlots(container.ShortPrefabName);
            if (slots <= 0) return;
            container.inventory.capacity = slots;
            if (UseGenericPanel(container.ShortPrefabName))
                container.panelName = "generic";
            container.SendNetworkUpdateImmediate();
        }

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            int containerCount = 0;
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var container = net as StorageContainer;
                if (container == null || container.IsDestroyed) continue;
                if (GetConfiguredSlots(container.ShortPrefabName) > 0)
                {
                    ApplyContainer(container);
                    containerCount++;
                }
            }
            Puts($"Loaded. Resized {containerCount} containers.");
        }

        private void OnEntitySpawned(BaseNetworkable net)
        {
            var container = net as StorageContainer;
            if (container != null)
                NextTick(() => { if (container != null && !container.IsDestroyed) ApplyContainer(container); });
        }

        #endregion

        #region Commands

        [ChatCommand("storagecontrol")]
        private void ChatStorageControl(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { SendReply(player, "Admins only."); return; }
            SendReply(player, BuildStatus());
        }

        [ConsoleCommand("storagecontrol")]
        private void ConsoleStorageControl(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;
            arg.ReplyWith(BuildStatus());
        }

        [ConsoleCommand("storagecontrol.reload")]
        private void ConsoleReload(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;
            LoadConfig();
            OnServerInitialized();
            arg.ReplyWith("StorageControl: config reloaded and all containers updated.");
        }

        private string BuildStatus()
        {
            return
                "=== Storage Control ===\n" +
                $"  Small wooden box      : {_config.SmallBox} slots\n" +
                $"  Large wooden box      : {_config.LargeBox} slots\n" +
                $"  Fridge                : {_config.Fridge} slots\n" +
                $"  Locker                : {_config.Locker} slots\n" +
                $"  Drop box              : {_config.DropBox} slots\n" +
                $"  Vending machine       : {_config.VendingMachine} slots\n" +
                $"  Tool cupboard         : {_config.ToolCupboard} slots\n" +
                $"  Furnace               : {_config.Furnace} slots\n" +
                $"  Large furnace         : {_config.LargeFurnace} slots\n" +
                $"  Campfire              : {_config.Campfire} slots\n" +
                $"  BBQ                   : {_config.BBQ} slots\n" +
                $"  Oil refinery          : {_config.OilRefinery} slots\n" +
                $"  Mixing table          : {_config.MixingTable} slots\n" +
                "Edit oxide/config/StorageControl.json and run 'storagecontrol.reload' to apply.";
        }

        #endregion
    }
}
