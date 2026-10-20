using System;
using System.Collections.Generic;
using HarmonyLib;
using MoreHeroes;
using Photon.Realtime;

namespace MorePlayers
{
    // Co-op hero selection with more than 4 heroes. MoreHeroes adds the boxes (Box_4..) on every client, the same number
    // (the room's hero count), before the game builds its box dictionaries. The co-op-only code still loops over 4 boxes:
    // DrawBoxSelectionNames (players listed in each box), AllBoxWithOwners, CheckButtonEnabled (Begin needs every box owned and
    // every player owning one), SetPlayersReady; those loops are transpiled to follow the boxes (IlPatches, BoxShift).
    // The host gives boxes to players; NetworkManager.PlayerHeroPositionOwner (owner of each hero slot, read when the run starts,
    // by the perks and by the gold/dust split) is created with 4 entries and is grown here when a box past 4 gets an owner.
    // Loading a co-op save: the game re-assigns boxes 0..3 to the saved owners (GameData Owner0..3); the saved team's own Owner
    // fields give boxes 4+ (SavedOwners).
    internal static class HeroSelect
    {
        // Called from transpiled IL: k (a 4: the number of boxes) becomes k + boxes beyond 4 on the current screen.
        public static int BoxShift(int k)
        {
            try
            {
                HeroSelectionManager hsm = HeroSelectionManager.Instance;
                int boxes = hsm?.boxGO?.Length ?? HeroSlots.VanillaCount;
                return k + Math.Max(0, Math.Min(boxes, HeroSlots.MaxCount) - HeroSlots.VanillaCount);
            }
            catch (Exception)
            {
                return k;
            }
        }

        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.AssignHeroPlayerPositionOwner))]
        internal static class OwnerArrayPatch
        {
            private static void Prefix(NetworkManager __instance, int id)
            {
                string[] owners = __instance.PlayerHeroPositionOwner;
                if (owners != null && id >= owners.Length && id < HeroSlots.MaxCount)
                {
                    Array.Resize(ref owners, id + 1);
                    __instance.PlayerHeroPositionOwner = owners;
                }
            }
        }

        // Real player names of heroes 5+ in the co-op save being loaded (index 0 = hero 5). The game keeps Owner0..3 only.
        private static readonly List<string> SavedOwners = new List<string>();

        private static readonly AccessTools.FieldRef<GameData, Dictionary<string, string>> SavedNicks =
            AccessTools.FieldRefAccess<GameData, Dictionary<string, string>>("playerNickRealDict");

        [HarmonyPatch(typeof(GameData), nameof(GameData.LoadData))]
        internal static class LoadDataPatch
        {
            private static void Postfix(GameData __instance)
            {
                SavedOwners.Clear();
                try
                {
                    GameTeam team = AtOManager.Instance?.team;
                    if (team == null || team.Size <= HeroSlots.VanillaCount)
                    {
                        return;
                    }
                    Dictionary<string, string> nicks = SavedNicks(__instance);
                    for (int i = HeroSlots.VanillaCount; i < team.Size; i++)
                    {
                        string owner = team.GetHero(i)?.Owner ?? "";
                        SavedOwners.Add(nicks != null && owner != "" && nicks.TryGetValue(owner, out string real) ? real : owner);
                    }
                    Plugin.Log.LogInfo($"Save loaded with {team.Size} heroes; owners of heroes 5+: {string.Join(", ", SavedOwners)}.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Reading the saved owners of heroes 5+ failed: {e.Message}");
                }
            }
        }

        // The host assigns the saved owners to boxes 0..3 at the end of DrawBoxSelectionNames; do the same for boxes 4+.
        [HarmonyPatch(typeof(HeroSelectionManager), "DrawBoxSelectionNames")]
        internal static class SavedOwnersPatch
        {
            private static void Postfix(HeroSelectionManager __instance)
            {
                if (!GameManager.Instance.IsLoadingGame() || SavedOwners.Count == 0)
                {
                    return;
                }
                try
                {
                    var assigned = new List<string>();
                    foreach (Player p in NetworkManager.Instance.PlayerList)
                    {
                        string real = NetworkManager.Instance.GetPlayerNickReal(p.NickName);
                        for (int k = 0; k < SavedOwners.Count; k++)
                        {
                            int box = HeroSlots.VanillaCount + k;
                            if (box < __instance.boxGO.Length && SavedOwners[k] == real)
                            {
                                __instance.AssignPlayerToBox(p.NickName, box);
                                assigned.Add($"Box_{box}={real}");
                            }
                        }
                    }
                    Plugin.Log.LogInfo($"Loaded co-op game: saved owners of boxes 5+ re-assigned: {(assigned.Count == 0 ? "none (players not in the room)" : string.Join(", ", assigned))}.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Re-assigning the saved owners of boxes 5+ failed: {e.Message}");
                }
            }
        }
    }
}
