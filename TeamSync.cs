using System;
using System.Collections.Generic;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    // Run state the host shares with the other players.
    // - AtOManager.NET_SetTeam (every client, after the run starts and after many changes): rebuilds the team with
    //   "new GameTeam()" (4 empty slots) and GameTeam.SetHero(hero, i), which does not grow the list, so heroes 5+ threw and
    //   were lost. SetHero now grows the list up to 8 slots. In single player MoreHeroes appends extras itself, and vanilla never
    //   passes an index past 3, so this changes nothing there.
    // - AtOManager.ShareGameMP / NET_ShareGameMP (game state when a scene loads): the per-hero crafted card counts are written
    //   and read for 4 heroes (k < 4 on both sides, IlPatches with RoomShift). The host pads its list to the room's hero count
    //   first, so it has an entry for every hero it writes.
    internal static class TeamSync
    {
        // Called from transpiled IL: k (a 4: the number of heroes in a co-op message) becomes the room's hero count. Both sides
        // of a message use the room property, so they agree even if one side's team is not set up yet.
        public static int RoomShift(int k)
        {
            return k + Math.Max(0, Room.ExtraHeroes);
        }

        [HarmonyPatch(typeof(GameTeam), nameof(GameTeam.SetHero))]
        internal static class SetHeroPatch
        {
            private static void Prefix(GameTeam __instance, int index)
            {
                if (index >= __instance.heroes.Count && index < HeroSlots.MaxCount)
                {
                    while (__instance.heroes.Count <= index)
                    {
                        __instance.heroes.Add(null);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(AtOManager), "ShareGameMP")]
        internal static class ShareGamePatch
        {
            private static void Prefix(AtOManager __instance)
            {
                try
                {
                    int n = Room.HeroCount();
                    List<Dictionary<string, int>> crafted = __instance.craftedCards;
                    if (n <= HeroSlots.VanillaCount || crafted == null)
                    {
                        return;
                    }
                    while (crafted.Count < n)
                    {
                        crafted.Add(new Dictionary<string, int>());
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Padding the crafted card list before sharing the game failed: {e.Message}");
                }
            }
        }
    }
}
