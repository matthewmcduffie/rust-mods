using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NPCControl", "mcduffiejohn", "1.0.0")]
    [Description("Configure NPC damage multipliers and population caps for bandits and wildlife")]
    public class NPCControl : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty("Bandit outgoing damage multiplier (1.0 = default, 0.5 = half damage, 2.0 = double damage)")]
            public float BanditDamageMultiplier = 1.0f;

            [JsonProperty("Wildlife outgoing damage multiplier (1.0 = default, 0.5 = half damage, 2.0 = double damage)")]
            public float WildlifeDamageMultiplier = 1.0f;

            [JsonProperty("Max bandit NPCs on map at one time (0 = no cap enforced)")]
            public int MaxBandits = 0;

            [JsonProperty("Max wildlife NPCs on map at one time (0 = no cap enforced)")]
            public int MaxWildlife = 0;

            [JsonProperty("Wildlife population convar multiplier -- raises or lowers server spawn density (1.0 = server default, set 2.0 to roughly double, 0.5 to halve)")]
            public float WildlifePopulationMultiplier = 1.0f;
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

        #region NPC Classification

        // ShortPrefabName fragments that identify bandit-class human NPCs
        private static readonly string[] BanditFragments =
        {
            "bandit_guard", "murderer", "scientist", "heavyscientist",
            "tunnel_dweller", "underwater_dweller", "scarecrow"
        };

        // ShortPrefabName fragments that identify wildlife
        private static readonly string[] WildlifeFragments =
        {
            "bear", "polarbear", "boar", "chicken", "stag", "wolf",
            "horse", "shark"
        };

        private static bool MatchesAny(string name, string[] fragments)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var frag in fragments)
                if (name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool IsBandit(BaseEntity entity) =>
            entity != null && MatchesAny(entity.ShortPrefabName, BanditFragments);

        private bool IsWildlife(BaseEntity entity) =>
            entity != null && MatchesAny(entity.ShortPrefabName, WildlifeFragments);

        #endregion

        #region Population Tracking

        private int _banditCount;
        private int _wildlifeCount;
        private Timer _syncTimer;

        private void RebuildCounts()
        {
            _banditCount = 0;
            _wildlifeCount = 0;
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var e = net as BaseEntity;
                if (e == null || e.IsDestroyed) continue;
                if (IsBandit(e)) _banditCount++;
                else if (IsWildlife(e)) _wildlifeCount++;
            }
        }

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            RebuildCounts();
            ApplyWildlifePopulationConVar();
            _syncTimer = timer.Every(300f, RebuildCounts);
            Puts($"Loaded. Bandits: {_banditCount}, Wildlife: {_wildlifeCount}");
        }

        private void Unload()
        {
            _syncTimer?.Destroy();
        }

        private void OnEntitySpawned(BaseNetworkable net)
        {
            var entity = net as BaseEntity;
            if (entity == null) return;

            if (IsBandit(entity))
            {
                _banditCount++;
                if (_config.MaxBandits > 0 && _banditCount > _config.MaxBandits)
                {
                    _banditCount--;
                    NextTick(() => { if (entity != null && !entity.IsDestroyed) entity.Kill(); });
                }
            }
            else if (IsWildlife(entity))
            {
                _wildlifeCount++;
                if (_config.MaxWildlife > 0 && _wildlifeCount > _config.MaxWildlife)
                {
                    _wildlifeCount--;
                    NextTick(() => { if (entity != null && !entity.IsDestroyed) entity.Kill(); });
                }
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            if (IsBandit(entity)) _banditCount = Math.Max(0, _banditCount - 1);
            else if (IsWildlife(entity)) _wildlifeCount = Math.Max(0, _wildlifeCount - 1);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator == null) return null;

            float mult = 1f;
            if (IsBandit(info.Initiator))
                mult = _config.BanditDamageMultiplier;
            else if (IsWildlife(info.Initiator))
                mult = _config.WildlifeDamageMultiplier;

            if (Math.Abs(mult - 1f) > 0.001f)
                info.damageTypes.ScaleAll(mult);

            return null;
        }

        #endregion

        #region ConVar Helper

        private void ApplyWildlifePopulationConVar()
        {
            float v = _config.WildlifePopulationMultiplier;
            if (Math.Abs(v - 1f) < 0.001f) return;
            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "animal.population", v);
                Puts($"animal.population set to {v}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not set animal.population: {ex.Message}");
            }
        }

        #endregion

        #region Commands

        [ChatCommand("npccontrol")]
        private void ChatNpcControl(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You must be an admin to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, BuildStatus());
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, UsageText());
                return;
            }

            string reply = ApplySetting(args[0], args[1]);
            SendReply(player, reply);
        }

        [ConsoleCommand("npccontrol")]
        private void ConsoleNpcControl(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin) return;

            if (!arg.HasArgs())
            {
                arg.ReplyWith(BuildStatus());
                return;
            }

            if (arg.Args.Length < 2)
            {
                arg.ReplyWith(UsageText());
                return;
            }

            arg.ReplyWith(ApplySetting(arg.Args[0], arg.Args[1]));
        }

        private string ApplySetting(string key, string value)
        {
            switch (key.ToLower())
            {
                case "banditdmg":
                    if (!float.TryParse(value, out float bd) || bd < 0f)
                        return "Invalid value. Use a positive number, e.g. 0.5 or 2.0";
                    _config.BanditDamageMultiplier = bd;
                    SaveConfig();
                    return $"Bandit damage multiplier set to {bd}";

                case "wildlifedmg":
                    if (!float.TryParse(value, out float wd) || wd < 0f)
                        return "Invalid value. Use a positive number, e.g. 0.5 or 2.0";
                    _config.WildlifeDamageMultiplier = wd;
                    SaveConfig();
                    return $"Wildlife damage multiplier set to {wd}";

                case "maxbandits":
                    if (!int.TryParse(value, out int mb) || mb < 0)
                        return "Invalid value. Use 0 (unlimited) or a positive whole number.";
                    _config.MaxBandits = mb;
                    SaveConfig();
                    RebuildCounts();
                    return $"Max bandits set to {(mb == 0 ? "unlimited" : mb.ToString())}. Current count: {_banditCount}";

                case "maxwildlife":
                    if (!int.TryParse(value, out int mw) || mw < 0)
                        return "Invalid value. Use 0 (unlimited) or a positive whole number.";
                    _config.MaxWildlife = mw;
                    SaveConfig();
                    RebuildCounts();
                    return $"Max wildlife set to {(mw == 0 ? "unlimited" : mw.ToString())}. Current count: {_wildlifeCount}";

                case "wildlifepop":
                    if (!float.TryParse(value, out float wp) || wp < 0f)
                        return "Invalid value. Use a positive number, e.g. 0.5 or 2.0";
                    _config.WildlifePopulationMultiplier = wp;
                    SaveConfig();
                    ApplyWildlifePopulationConVar();
                    return $"Wildlife population multiplier set to {wp}. Changes take effect as new animals spawn.";

                default:
                    return UsageText();
            }
        }

        private string BuildStatus()
        {
            return
                "=== NPC Control ===\n" +
                $"  banditdmg     : {_config.BanditDamageMultiplier} (bandits: {_banditCount} active)\n" +
                $"  wildlifedmg   : {_config.WildlifeDamageMultiplier} (wildlife: {_wildlifeCount} active)\n" +
                $"  maxbandits    : {(_config.MaxBandits <= 0 ? "no cap" : _config.MaxBandits.ToString())}\n" +
                $"  maxwildlife   : {(_config.MaxWildlife <= 0 ? "no cap" : _config.MaxWildlife.ToString())}\n" +
                $"  wildlifepop   : {_config.WildlifePopulationMultiplier}";
        }

        private static string UsageText() =>
            "Usage: /npccontrol <setting> <value>\n" +
            "  banditdmg   <float>  -- damage bandits deal (0.5 = half, 2.0 = double)\n" +
            "  wildlifedmg <float>  -- damage wildlife deals\n" +
            "  maxbandits  <int>    -- cap total bandits on map (0 = no cap)\n" +
            "  maxwildlife <int>    -- cap total wildlife on map (0 = no cap)\n" +
            "  wildlifepop <float>  -- wildlife spawn density multiplier (needs respawn cycle)\n" +
            "Run /npccontrol with no args to see current values.";

        #endregion
    }
}
