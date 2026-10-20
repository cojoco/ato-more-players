using System;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    // Loot screen in co-op. MoreHeroes adds the panels for heroes 5+ and appends them to the turn order in the same order on
    // every client, so shared loot needs nothing more. With "independent loot" the game marks the heroes the local player does
    // not own as already looted, for heroes 1-4 only (IlPatches: LootManager.SetLoot).
    internal static class Loot
    {
        private static readonly AccessTools.FieldRef<LootManager, bool[]> Looted = AccessTools.FieldRefAccess<LootManager, bool[]>("looted");

        // Called from transpiled IL: k (a 4) becomes the number of hero panels (looted[] has one entry per panel).
        public static int PanelShift(int k)
        {
            try
            {
                LootManager lm = LootManager.Instance;
                bool[] looted = lm != null ? Looted(lm) : null;
                return looted == null ? k : k + Math.Max(0, Math.Min(looted.Length, HeroSlots.MaxCount) - HeroSlots.VanillaCount);
            }
            catch (Exception)
            {
                return k;
            }
        }
    }
}
