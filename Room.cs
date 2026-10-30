using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using ExitGames.Client.Photon;
using HarmonyLib;
using MoreHeroes;
using Photon.Pun;
using Photon.Realtime;

namespace MorePlayers
{
    // What every player in a co-op room agrees on, stored in Photon room/player properties (Photon keeps them and hands them to
    // players who join later):
    // - room "mp_heroes": the run's hero count (4..8), set by the host when the room is created: the host's MoreHeroes HeroCount,
    //   at least the room size (every player must own a hero), or the saved team's size when loading a co-op save.
    // - room "mp_ver" and player "mp_ver": the MorePlayers version of the host and of each player.
    // - player "mp_mods": each player's MoreHeroes and EnemyScaling versions ("MoreHeroes=0.2.27;EnemyScaling=0.3.0"). They change
    //   the run and enemy stats, and every client simulates combat itself, so a different version desyncs even when MorePlayers
    //   matches (EnemyScaling 0.3.0's per-hero values are unknown to 0.2.x).
    // A room with more than 4 players or more than 4 heroes needs MorePlayers (and MoreHeroes) on every player's game; the host's
    // Launch button refuses to start until every player has the same version. A room with at most 4 players and 4 heroes is
    // vanilla, so players without the mods can still play in it.
    internal static class Room
    {
        private const string KeyHeroes = "mp_heroes";
        private const string KeyVersion = "mp_ver";
        private const string KeyMods = "mp_mods";
        private const string KeyScaleRolls = "mp_rolls";
        private const string KeyScaleCosts = "mp_costs";

        // Mods whose versions must match the host's in a room that needs MorePlayers. Read from BepInEx, so a missing mod is
        // reported instead of failing to load.
        private static readonly (string Guid, string Name)[] CheckedMods =
        {
            (Plugin.MoreHeroesGuid, "MoreHeroes"),
            ("com.cojoco.enemyscaling", "EnemyScaling"),
        };

        internal static bool InRoom => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null;

        // The run's hero count in the current room (4 outside a room or if the host has no MorePlayers). This is what
        // MoreHeroes.MultiplayerApi.RoomHeroCount returns, so it must not change during a run: it only reads the room property,
        // which the host sets once when the room is created.
        internal static int HeroCount()
        {
            if (!InRoom)
            {
                return HeroSlots.VanillaCount;
            }
            object v = PhotonNetwork.CurrentRoom.CustomProperties?[KeyHeroes];
            return v is int n ? Clamp(n) : HeroSlots.VanillaCount;
        }

        // The host's MoreHeroes [Balance] values (MoreHeroes.MultiplayerApi.RoomScaleGroupRolls / RoomScaleGroupEventCosts); null
        // outside a room or if the room has no value.
        internal static bool? ScaleGroupRolls() => RoomBool(KeyScaleRolls);
        internal static bool? ScaleGroupEventCosts() => RoomBool(KeyScaleCosts);

        private static bool? RoomBool(string key)
        {
            if (!InRoom)
            {
                return null;
            }
            object v = PhotonNetwork.CurrentRoom.CustomProperties?[key];
            return v is bool b ? b : (bool?)null;
        }

        // Heroes beyond the 4 the game is built for, in the current room.
        internal static int ExtraHeroes => HeroCount() - HeroSlots.VanillaCount;

        internal static bool Expanded => ExtraHeroes > 0;

        // True when the room cannot be played without MorePlayers on every client.
        internal static bool NeedsMod()
        {
            return InRoom && (PhotonNetwork.CurrentRoom.MaxPlayers > HeroSlots.VanillaCount || Expanded);
        }

        // Same rule as MoreHeroes (HeroSlots.ModeAllowsExtraHeroes). The game's IsObeliskChallenge() is also true for weekly runs.
        private static bool ModeAllowsExtraHeroes()
        {
            GameManager gm = GameManager.Instance;
            return !gm.IsWeeklyChallenge() && (gm.IsGameAdventure() || gm.IsObeliskChallenge() || gm.IsSingularity());
        }

        // Called (Postfix on NetworkManager.OnCreatedRoom) on the host only.
        internal static void Publish()
        {
            if (!InRoom || !PhotonNetwork.IsMasterClient)
            {
                return;
            }
            int maxPlayers = PhotonNetwork.CurrentRoom.MaxPlayers;
            int heroes;
            string why;
            AtOManager ato = AtOManager.Instance;
            if (GameManager.Instance.IsLoadingGame() && ato?.team != null)
            {
                heroes = ato.team.Size;
                why = "the saved team";
            }
            else if (!ModeAllowsExtraHeroes())
            {
                // MoreHeroes adds heroes in Adventure, Obelisk Challenge and Singularity; the weekly challenge (and anything else)
                // keeps 4 heroes, and so at most 4 players.
                heroes = HeroSlots.VanillaCount;
                why = $"game type {GameManager.Instance.GameType} keeps 4 heroes";
            }
            else
            {
                heroes = Math.Max(MultiplayerApi.ConfiguredHeroCount, maxPlayers);
                why = $"MoreHeroes HeroCount {MultiplayerApi.ConfiguredHeroCount}, room for {maxPlayers} players";
            }
            heroes = Clamp(heroes);
            bool rolls = MultiplayerApi.ConfiguredScaleGroupRolls;
            bool costs = MultiplayerApi.ConfiguredScaleGroupEventCosts;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { KeyHeroes, heroes }, { KeyVersion, Plugin.PluginVersion }, { KeyScaleRolls, rolls }, { KeyScaleCosts, costs }
            });
            Plugin.Log.LogInfo($"Co-op room created: {maxPlayers} players, {heroes} heroes ({why}); host's ScaleGroupRolls={rolls}, ScaleGroupEventCosts={costs}.");
        }

        // Called (Postfix on NetworkManager.OnJoinedRoom) on every player, host included.
        internal static void AnnounceSelf()
        {
            if (!InRoom)
            {
                return;
            }
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { KeyVersion, Plugin.PluginVersion }, { KeyMods, ModVersions() } });
            if (PhotonNetwork.IsMasterClient)
            {
                // The host's own room properties are not echoed back yet at this point; OnCreatedRoom logged what it published.
                Plugin.Log.LogInfo($"Joined own co-op room {PhotonNetwork.CurrentRoom.Name} as host ({DescribeMods(ModVersions())}).");
                return;
            }
            object host = PhotonNetwork.CurrentRoom.CustomProperties?[KeyVersion];
            Plugin.Log.LogInfo($"Joined co-op room {PhotonNetwork.CurrentRoom.Name}: {PhotonNetwork.CurrentRoom.MaxPlayers} players, " +
                               $"{HeroCount()} heroes, host's MorePlayers {(host == null ? "missing (vanilla room)" : host)}; mine: {DescribeMods(ModVersions())}.");
        }

        // Players in the room whose MorePlayers, MoreHeroes or EnemyScaling version differs from ours (or who have none).
        internal static List<string> PlayersWithoutMod()
        {
            var missing = new List<string>();
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                string name = NetworkManager.Instance != null ? NetworkManager.Instance.GetPlayerNickReal(p.NickName) : p.NickName;
                object v = p.CustomProperties?[KeyVersion];
                if (!(v is string s) || s != Plugin.PluginVersion)
                {
                    missing.Add(v == null ? $"{name} (no MorePlayers)" : $"{name} (MorePlayers {v})");
                    continue;
                }
                string diff = ModDifferences(p.CustomProperties?[KeyMods] as string);
                if (diff != null)
                {
                    missing.Add($"{name} ({diff})");
                }
            }
            return missing;
        }

        // This game's checked mod versions, e.g. "MoreHeroes=0.2.27;EnemyScaling=0.3.0" ("none" for a mod that is not loaded).
        internal static string ModVersions()
        {
            return string.Join(";", CheckedMods.Select(m =>
                m.Name + "=" + (Chainloader.PluginInfos.TryGetValue(m.Guid, out PluginInfo info) ? info.Metadata.Version.ToString() : "none")));
        }

        private static Dictionary<string, string> ParseMods(string mods)
        {
            var d = new Dictionary<string, string>();
            foreach (string part in (mods ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    d[part.Substring(0, eq)] = part.Substring(eq + 1);
                }
            }
            return d;
        }

        // "MoreHeroes 0.2.27, EnemyScaling 0.3.0"
        internal static string DescribeMods(string mods)
        {
            return string.Join(", ", ParseMods(mods).Select(kv => kv.Value == "none" ? $"no {kv.Key}" : $"{kv.Key} {kv.Value}"));
        }

        // What differs between another player's mods and ours, e.g. "EnemyScaling 0.2.1, host has 0.3.0"; null when they match.
        private static string ModDifferences(string theirs)
        {
            Dictionary<string, string> mine = ParseMods(ModVersions());
            Dictionary<string, string> other = ParseMods(theirs);
            var diffs = new List<string>();
            foreach (KeyValuePair<string, string> kv in mine)
            {
                if (!other.TryGetValue(kv.Key, out string v))
                {
                    diffs.Add($"{kv.Key} version unknown");
                }
                else if (v != kv.Value)
                {
                    diffs.Add($"{(v == "none" ? $"no {kv.Key}" : $"{kv.Key} {v}")}, host has {kv.Value}");
                }
            }
            return diffs.Count == 0 ? null : string.Join("; ", diffs);
        }

        private static int Clamp(int n)
        {
            return n < HeroSlots.VanillaCount ? HeroSlots.VanillaCount : n > HeroSlots.MaxCount ? HeroSlots.MaxCount : n;
        }

        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnCreatedRoom))]
        internal static class CreatedRoomPatch
        {
            private static void Postfix()
            {
                try
                {
                    Publish();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Could not publish the room's hero count: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnJoinedRoom))]
        internal static class JoinedRoomPatch
        {
            private static void Postfix()
            {
                try
                {
                    Net.Register();
                    AnnounceSelf();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Could not announce MorePlayers to the room: {e}");
                }
            }
        }

        // The host's Launch button: refuse to start a room that needs the mods while a player lacks them (or has another version),
        // otherwise the game would desync or crash later in ways that are hard to tell apart from bugs.
        [HarmonyPatch(typeof(LobbyManager), "LaunchGameCheckVersion")]
        internal static class LaunchPatch
        {
            private static bool Prefix()
            {
                try
                {
                    if (!NeedsMod())
                    {
                        return true;
                    }
                    if (PhotonNetwork.PlayerList.Length > HeroCount())
                    {
                        string few = $"{PhotonNetwork.PlayerList.Length} players but only {HeroCount()} heroes: every player needs a hero. " +
                                     "More than 4 players does not work in the weekly challenge.";
                        Plugin.Log.LogWarning("Launch refused: " + few);
                        AlertManager.Instance.AlertConfirm(few);
                        return false;
                    }
                    List<string> missing = PlayersWithoutMod();
                    if (missing.Count == 0)
                    {
                        Plugin.Log.LogInfo($"Launch: every player has MorePlayers {Plugin.PluginVersion}, {DescribeMods(ModVersions())}; {PhotonNetwork.PlayerList.Length} players, {HeroCount()} heroes.");
                        return true;
                    }
                    string text = $"This room has {PhotonNetwork.CurrentRoom.MaxPlayers} player slots and {HeroCount()} heroes, so every player needs " +
                                  $"the host's versions: MorePlayers {Plugin.PluginVersion}, {DescribeMods(ModVersions())}. Missing or different: {string.Join(", ", missing)}.";
                    Plugin.Log.LogWarning("Launch refused: " + text);
                    AlertManager.Instance.AlertConfirm(text);
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Launch check failed, launching anyway: {e}");
                    return true;
                }
            }
        }
    }
}
