using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(MoreHeroesGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.cojoco.moreplayers";
        public const string PluginName = "More Players";
        public const string PluginVersion = "0.1.2";
        public const string MoreHeroesGuid = "com.cojoco.moreheroes";

        internal static ManualLogSource Log;

        internal static ConfigEntry<int> MaxPlayers;
        internal static ConfigEntry<bool> VerboseNet;
        internal static ConfigEntry<float> BoxPlayerScale;
        internal static ConfigEntry<float> BoxPlayerColumnOffset;
        internal static ConfigEntry<bool> MoveGuestWidgets;
        internal static ConfigEntry<float> ConflictSlotScale;
        internal static ConfigEntry<float> ConflictSlotSpacing;

        private void Awake()
        {
            Log = Logger;

            MaxPlayers = Config.Bind("General", "MaxPlayers", 8,
                new ConfigDescription("Largest room size offered when creating a co-op room (the game offers 2-4). Rooms with more than 4 players " +
                                      "or more than 4 heroes need MoreHeroes and MorePlayers on every player's game; the room's hero count is the " +
                                      "host's MoreHeroes HeroCount, at least the number of player slots.", new AcceptableValueRange<int>(4, 8)));
            VerboseNet = Config.Bind("Logging", "VerboseNet", true,
                "Log every extra-hero message sent and received (decks, rewards). Useful while co-op with more than 4 heroes is being tested.");
            BoxPlayerScale = Config.Bind("Layout", "BoxPlayerScale", 0.6f,
                "Hero selection, rooms with more than 4 players: size of the player-name buttons under each hero box (1 = vanilla). They are " +
                "laid out in two columns.");
            BoxPlayerColumnOffset = Config.Bind("Layout", "BoxPlayerColumnOffset", 0.3f,
                "Hero selection, rooms with more than 4 players: distance of the two columns of player-name buttons from the box centre, in the " +
                "box's own units (a box is about 1.2 wide).");
            MoveGuestWidgets = Config.Bind("Layout", "MoveGuestWidgets", true,
                "Hero selection with more than 4 heroes: move the guests' Follow the leader toggle and Ready button (vanilla puts them under " +
                "the boxes, where the second row of boxes now is) to the free space right of the boxes.");

            ConflictSlotScale = Config.Bind("Layout", "ConflictSlotScale", 0f,
                "Conflict screen (co-op players picked different map nodes) with more than 4 heroes: size of each hero's slot (1 = vanilla). 0 = automatic (0.6).");
            ConflictSlotSpacing = Config.Bind("Layout", "ConflictSlotSpacing", 0f,
                "Conflict screen with more than 4 heroes: distance between hero slots, in the screen's own units (vanilla 2.73). 0 = automatic (1.64).");

            // MoreHeroes asks this for the co-op hero count; without MorePlayers it keeps co-op at 4 heroes.
            MultiplayerApi.RoomHeroCount = Room.HeroCount;
            Combat.Register();
            Rewards.Register();

            var harmony = new Harmony(PluginGuid);
            var classes = new List<string>();
            var failures = new List<string>();
            foreach (Type t in new[]
                     {
                         typeof(Room.CreatedRoomPatch), typeof(Room.JoinedRoomPatch), typeof(Room.LaunchPatch), typeof(Net.RpcPatch),
                         typeof(Combat.SaveCardDeckPatch), typeof(Rewards.ShareRewardsPatch), typeof(HeroSelect.OwnerArrayPatch),
                         typeof(HeroSelect.LoadDataPatch), typeof(HeroSelect.SavedOwnersPatch), typeof(HeroSelect.WidgetsPatch), typeof(TeamSync.SetHeroPatch),
                         typeof(TeamSync.ShareGamePatch), typeof(Lobby.AwakePatch), typeof(Lobby.DrawNamesPatch), typeof(Lobby.ColorPatch),
                         typeof(Lobby.BoxAwakePatch), typeof(Lobby.HidePlayersPatch), typeof(Lobby.SelectPlayerPatch), typeof(Lobby.ActivePlayerPatch),
                         typeof(Conflict.AwakePatch), typeof(Conflict.OrderPatch)
                     })
            {
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    classes.Add(t.Name);
                }
                catch (Exception e)
                {
                    failures.Add($"{t.Name} ({e.GetType().Name}: {e.Message})");
                }
            }
            IlPatches.ApplyAll(harmony);
            // Last: the HarmonyX leave fix (MoreHeroes) for every method patched since MoreHeroes loaded.
            int fixedBefore = HarmonyFix.Fixed.Count;
            int failedBefore = HarmonyFix.Failed.Count;
            HarmonyFix.ApplyToAllPatchedMethods(harmony);

            int ilOk = IlPatches.Specs.Count(s => s.Applied);
            var lines = new List<string>
            {
                $"{PluginName} {PluginVersion} loaded. MaxPlayers={MaxPlayers.Value}; co-op hero count comes from the room (host's MoreHeroes HeroCount {MultiplayerApi.ConfiguredHeroCount}).",
                $"Transpilers: {ilOk}/{IlPatches.Specs.Count} applied."
            };
            foreach (MethodSpec s in IlPatches.Specs)
            {
                lines.Add($"  [{(s.Applied ? "ok" : "SKIPPED")}] {s.Name}: {s.Result} ({s.Why})");
            }
            lines.Add($"Prefix/Postfix patches: {classes.Count} applied, {failures.Count} failed.");
            lines.AddRange(classes.Select(c => "  [ok] " + c));
            lines.AddRange(failures.Select(f => "  [FAILED] " + f));
            List<string> newFixed = HarmonyFix.Fixed.Skip(fixedBefore).ToList();
            List<string> newFailed = HarmonyFix.Failed.Skip(failedBefore).ToList();
            lines.Add($"HarmonyX leave fix: {newFixed.Count} more method(s) fixed" + (newFixed.Count > 0 ? ": " + string.Join(", ", newFixed) : "") +
                      (newFailed.Count > 0 ? "; FAILED: " + string.Join(", ", newFailed) : ""));
            bool anyFailed = failures.Count > 0 || ilOk < IlPatches.Specs.Count || newFailed.Count > 0;
            if (anyFailed)
            {
                Log.LogWarning(string.Join("\n", lines));
            }
            else
            {
                Log.LogInfo(string.Join("\n", lines));
            }
        }
    }
}
