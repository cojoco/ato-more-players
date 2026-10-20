using System;
using System.Collections.Generic;
using System.Linq;
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
    // A room with more than 4 players or more than 4 heroes needs MorePlayers (and MoreHeroes) on every player's game; the host's
    // Launch button refuses to start until every player has the same version. A room with at most 4 players and 4 heroes is
    // vanilla, so players without the mods can still play in it.
    internal static class Room
    {
        private const string KeyHeroes = "mp_heroes";
        private const string KeyVersion = "mp_ver";

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

        // Heroes beyond the 4 the game is built for, in the current room.
        internal static int ExtraHeroes => HeroCount() - HeroSlots.VanillaCount;

        internal static bool Expanded => ExtraHeroes > 0;

        // True when the room cannot be played without MorePlayers on every client.
        internal static bool NeedsMod()
        {
            return InRoom && (PhotonNetwork.CurrentRoom.MaxPlayers > HeroSlots.VanillaCount || Expanded);
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
            else if (!GameManager.Instance.IsGameAdventure() || GameManager.Instance.IsWeeklyChallenge())
            {
                // MoreHeroes only adds heroes in Adventure; other modes keep 4 heroes (and so at most 4 players).
                heroes = HeroSlots.VanillaCount;
                why = $"game type {GameManager.Instance.GameType} keeps 4 heroes";
            }
            else
            {
                heroes = Math.Max(MultiplayerApi.ConfiguredHeroCount, maxPlayers);
                why = $"MoreHeroes HeroCount {MultiplayerApi.ConfiguredHeroCount}, room for {maxPlayers} players";
            }
            heroes = Clamp(heroes);
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyHeroes, heroes }, { KeyVersion, Plugin.PluginVersion } });
            Plugin.Log.LogInfo($"Co-op room created: {maxPlayers} players, {heroes} heroes ({why}).");
        }

        // Called (Postfix on NetworkManager.OnJoinedRoom) on every player, host included.
        internal static void AnnounceSelf()
        {
            if (!InRoom)
            {
                return;
            }
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { KeyVersion, Plugin.PluginVersion } });
            if (PhotonNetwork.IsMasterClient)
            {
                // The host's own room properties are not echoed back yet at this point; OnCreatedRoom logged what it published.
                Plugin.Log.LogInfo($"Joined own co-op room {PhotonNetwork.CurrentRoom.Name} as host.");
                return;
            }
            object host = PhotonNetwork.CurrentRoom.CustomProperties?[KeyVersion];
            Plugin.Log.LogInfo($"Joined co-op room {PhotonNetwork.CurrentRoom.Name}: {PhotonNetwork.CurrentRoom.MaxPlayers} players, " +
                               $"{HeroCount()} heroes, host's MorePlayers {(host == null ? "missing (vanilla room)" : host)}.");
        }

        // Players in the room whose MorePlayers version differs from ours (or who have none).
        internal static List<string> PlayersWithoutMod()
        {
            var missing = new List<string>();
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                object v = p.CustomProperties?[KeyVersion];
                if (!(v is string s) || s != Plugin.PluginVersion)
                {
                    string name = NetworkManager.Instance != null ? NetworkManager.Instance.GetPlayerNickReal(p.NickName) : p.NickName;
                    missing.Add(v == null ? $"{name} (no MorePlayers)" : $"{name} (MorePlayers {v})");
                }
            }
            return missing;
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
                                     "More than 4 players only works in Adventure mode.";
                        Plugin.Log.LogWarning("Launch refused: " + few);
                        AlertManager.Instance.AlertConfirm(few);
                        return false;
                    }
                    List<string> missing = PlayersWithoutMod();
                    if (missing.Count == 0)
                    {
                        Plugin.Log.LogInfo($"Launch: every player has MorePlayers {Plugin.PluginVersion}; {PhotonNetwork.PlayerList.Length} players, {HeroCount()} heroes.");
                        return true;
                    }
                    string text = $"This room has {PhotonNetwork.CurrentRoom.MaxPlayers} player slots and {HeroCount()} heroes, so every player needs " +
                                  $"MoreHeroes and MorePlayers {Plugin.PluginVersion}. Missing or different: {string.Join(", ", missing)}.";
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
