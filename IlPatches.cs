using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    // The same transpiler engine as MoreHeroes' IlPatches (copied so MoreHeroes keeps no co-op code): a spec lists each constant's
    // exact count in the method and which occurrences to shift; a call to a shift method is inserted after each of those
    // constants, and on any mismatch the method stays vanilla and an error is logged.
    // What the instruction right after a replaced constant must look like. A cheap sanity check on top of the exact
    // occurrence count, so a game update that reshuffles a method is caught instead of patching the wrong constant.
    internal enum Shape
    {
        Branch, // loop bound or comparison: next is a conditional branch or clt/cgt/ceq
        NewArr, // array size: next is newarr
        Sub,    // index arithmetic (i - 4): next is sub
        Elem,   // array index: next is ldelem
        Store,  // initial loop value: next is stloc
        Field,  // value stored in a field: next is stfld
        Rem,    // modulo (x % 4): next is rem
        Any     // e.g. a call argument
    }

    internal class SiteSpec
    {
        public int Constant;
        public int ExpectedCount;
        public (int Ordinal, Shape Shape)[] Replace;
        public MethodInfo ShiftWith; // null = the method's shift (MethodSpec.ShiftWith)
    }

    internal class MethodSpec
    {
        public string Name;
        public Func<MethodBase> Resolve;
        public SiteSpec[] Sites;
        public string Why;
        public MethodInfo ShiftWith; // null = HeroSlots.Shift (MoreHeroes: combat team size)
        public Func<List<CodeInstruction>, string> Edit; // custom edit instead of constant sites: returns null or the reason it does not match

        // Filled in when the transpiler runs.
        public string Result;
        public bool Applied;
        public int Replaced;
    }

    internal static class IlPatches
    {
        private static readonly MethodInfo ShiftMethod = AccessTools.Method(typeof(HeroSlots), nameof(HeroSlots.Shift));
        private static readonly Dictionary<MethodBase, MethodSpec> ByMethod = new Dictionary<MethodBase, MethodSpec>();

        internal static readonly List<MethodSpec> Specs = new List<MethodSpec>();

        private static SiteSpec S(int constant, int expected, params (int, Shape)[] replace)
        {
            return new SiteSpec { Constant = constant, ExpectedCount = expected, Replace = replace };
        }

        // Same as S, but these occurrences follow a different count than the rest of the method (e.g. team loops in a method
        // whose other constants follow the number of minigame turn slots).
        private static SiteSpec S(MethodInfo shiftWith, int constant, int expected, params (int, Shape)[] replace)
        {
            return new SiteSpec { Constant = constant, ExpectedCount = expected, Replace = replace, ShiftWith = shiftWith };
        }

        private static MethodSpec M(string name, Func<MethodBase> resolve, string why, params SiteSpec[] sites)
        {
            return new MethodSpec { Name = name, Resolve = resolve, Why = why, Sites = sites };
        }

        // Same as M, but the constants follow a different count (e.g. the number of reward rows or hero selection boxes).
        private static MethodSpec M(string name, Func<MethodBase> resolve, MethodInfo shiftWith, string why, params SiteSpec[] sites)
        {
            return new MethodSpec { Name = name, Resolve = resolve, Why = why, Sites = sites, ShiftWith = shiftWith };
        }

        // A custom IL edit (not a constant shift). The edit verifies the IL itself and returns an error if it does not match.
        private static MethodSpec E(string name, Func<MethodBase> resolve, string why, Func<List<CodeInstruction>, string> edit)
        {
            return new MethodSpec { Name = name, Resolve = resolve, Why = why, Sites = new SiteSpec[0], Edit = edit };
        }

        private static MethodBase Mm(string name)
        {
            return AccessTools.Method(typeof(MatchManager), name);
        }

        private static MethodBase Co(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            return m == null ? null : AccessTools.EnumeratorMoveNext(m);
        }

        // Expected counts come from the IL of the game build this was written against (see NOTES.md for the survey).
        // Ordinals count occurrences of that constant in the method, in IL order, starting at 0.
        private static void Define()
        {
            Specs.Clear();

            // Combat: client-side repair from the host's sync code. Same layout as FixTOTALCo (MoreHeroes): hero blocks, then 4 NPC
            // blocks, then round (8) and enchantments (9). i < 4 ? hero : npc[i - 4] (#0, #1), i < 8 (8 #1), hero-only part
            // i < 4 (#3), array[8] (8 #2), array[9] (9 #1). Not shifted: per-character field indices and lengths (aux[3], aux[4],
            // aux[8], aux[10], Length >= 4 / < 9 / < 11) and the minimum section count (Length < 10).
            Specs.Add(M("MatchManager.FixCodeSyncFromMaster", () => Mm("FixCodeSyncFromMaster"),
                "co-op desync repair from the host's sync code: hero/NPC combined loop and the indices after the hero blocks",
                S(4, 5, (0, Shape.Branch), (1, Shape.Sub), (3, Shape.Branch)),
                S(8, 3, (1, Shape.Branch), (2, Shape.Elem)),
                S(9, 2, (1, Shape.Elem))));

            // Hero selection, co-op only code: loops over the boxes (HeroSelect.BoxShift follows boxGO).
            MethodInfo boxes = AccessTools.Method(typeof(HeroSelect), nameof(HeroSelect.BoxShift));
            Func<string, MethodBase> hs = n => AccessTools.Method(typeof(HeroSelectionManager), n);
            Specs.Add(M("HeroSelectionManager.DrawBoxSelectionNames", () => hs("DrawBoxSelectionNames"), boxes,
                "list the players in every box: box loops j < 4 (#0) and l < 4 (#1); #2 is the 4 player-name slots of a box (Lobby)",
                S(4, 3, (0, Shape.Branch), (1, Shape.Branch)),
                S(AccessTools.Method(typeof(Lobby), nameof(Lobby.PlayerSlotShift)), 4, 3, (2, Shape.Branch))));
            Specs.Add(M("HeroSelectionManager.AllBoxWithOwners", () => hs("AllBoxWithOwners"), boxes,
                "Begin check: boxes that need an owner (num = 4) and the box loop (i < 4)",
                S(4, 2, (0, Shape.Store), (1, Shape.Branch))));
            Specs.Add(M("HeroSelectionManager.CheckButtonEnabled", () => hs("CheckButtonEnabled"), boxes,
                "Begin check: every box owned and every player owns a box (i < 4)",
                S(4, 1, (0, Shape.Branch))));
            Specs.Add(M("HeroSelectionManager.SetPlayersReady", () => hs("SetPlayersReady"), boxes,
                "ready marks on the boxes (i < 4)",
                S(4, 1, (0, Shape.Branch))));

            Specs.Add(M("PerkTree.DoTeamPerks", () => AccessTools.Method(typeof(PerkTree), "DoTeamPerks"), boxes,
                "perk tree in hero selection: teammates' perks, co-op box loop (#1; MoreHeroes shifts the single-player loop #0)",
                S(4, 2, (1, Shape.Branch))));

            // Game state share: per-hero crafted card counts, written (k < 4) and read (m < 4) for the room's hero count.
            MethodInfo room = AccessTools.Method(typeof(TeamSync), nameof(TeamSync.RoomShift));
            Specs.Add(M("AtOManager.ShareGameMP", () => Co(typeof(AtOManager), "ShareGameMP"), room,
                "game state share (host): crafted cards per hero k < 4 (#1; #0 and #2 are an array index and a coroutine value)",
                S(4, 3, (1, Shape.Branch))));
            Specs.Add(M("AtOManager.NET_ShareGameMP", () => AccessTools.Method(typeof(AtOManager), "NET_ShareGameMP"), room,
                "game state share (other players): crafted cards per hero m < 4",
                S(4, 1, (0, Shape.Branch))));

            // Combat emotes: the hero whose emote you send cycles 0..3 (heroActive > 3 -> 0), so a player who owns only heroes 5+
            // could never pick one of theirs. (MoreHeroes already shifts the matching heroActive < 4 check in SetCharactersPing.)
            Specs.Add(M("EmoteManager.SelectNextCharacter", () => AccessTools.Method(typeof(EmoteManager), "SelectNextCharacter"),
                "emotes: cycle through every hero (heroActive > 3)",
                S(3, 1, (0, Shape.Branch))));

            // Conflict screen: RollResult waits until every rolling hero has a result (i < 4). Arrays are sized team.Size.
            Specs.Add(M("ConflictManager.RollResult", () => Co(typeof(ConflictManager), "RollResult"),
                "conflict: wait until every hero's roll is in (i < 4; the 3 is the rule buttons)",
                S(4, 1, (0, Shape.Branch))));

            // Loot with "independent loot" (each player picks for their own heroes at once): heroes the player does not own are
            // marked as looted (i < 4, #2; #0 and #1 are coroutine states). Follows the loot panels MoreHeroes added (looted[]).
            Specs.Add(M("LootManager.SetLoot", () => Co(typeof(LootManager), "SetLoot"), AccessTools.Method(typeof(Loot), nameof(Loot.PanelShift)),
                "independent loot: mark the heroes of other players as looted (i < 4)",
                S(4, 3, (2, Shape.Branch))));
        }

        internal static void ApplyAll(Harmony harmony)
        {
            Define();
            var transpiler = new HarmonyMethod(AccessTools.Method(typeof(IlPatches), nameof(Transpiler)));
            foreach (MethodSpec spec in Specs)
            {
                try
                {
                    MethodBase target = spec.Resolve();
                    if (target == null)
                    {
                        spec.Result = "method not found";
                        continue;
                    }
                    ByMethod[target] = spec;
                    harmony.Patch(target, transpiler: transpiler);
                    if (spec.Result == null)
                    {
                        spec.Result = "transpiler did not run";
                    }
                }
                catch (Exception e)
                {
                    spec.Applied = false;
                    spec.Result = "patch threw " + e.GetType().Name + ": " + e.Message;
                }
            }
        }

        private static int? IntConstant(CodeInstruction ci)
        {
            OpCode op = ci.opcode;
            if (op == OpCodes.Ldc_I4_M1) return -1;
            if (op == OpCodes.Ldc_I4_0) return 0;
            if (op == OpCodes.Ldc_I4_1) return 1;
            if (op == OpCodes.Ldc_I4_2) return 2;
            if (op == OpCodes.Ldc_I4_3) return 3;
            if (op == OpCodes.Ldc_I4_4) return 4;
            if (op == OpCodes.Ldc_I4_5) return 5;
            if (op == OpCodes.Ldc_I4_6) return 6;
            if (op == OpCodes.Ldc_I4_7) return 7;
            if (op == OpCodes.Ldc_I4_8) return 8;
            if ((op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4) && ci.operand != null)
            {
                try
                {
                    return Convert.ToInt32(ci.operand);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        private static bool ShapeMatches(Shape shape, CodeInstruction next)
        {
            if (next == null)
            {
                return false;
            }
            OpCode op = next.opcode;
            switch (shape)
            {
                case Shape.Branch:
                    return op.FlowControl == FlowControl.Cond_Branch || op == OpCodes.Clt || op == OpCodes.Clt_Un || op == OpCodes.Cgt
                           || op == OpCodes.Cgt_Un || op == OpCodes.Ceq;
                case Shape.NewArr:
                    return op == OpCodes.Newarr;
                case Shape.Sub:
                    return op == OpCodes.Sub;
                case Shape.Elem:
                    return op == OpCodes.Ldelem_Ref || op == OpCodes.Ldelem_I4 || op == OpCodes.Ldelem;
                case Shape.Store:
                    return op == OpCodes.Stloc || op == OpCodes.Stloc_S || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1
                           || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;
                case Shape.Field:
                    return op == OpCodes.Stfld;
                case Shape.Rem:
                    return op == OpCodes.Rem;
                default:
                    return true;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            List<CodeInstruction> list = instructions.ToList();
            if (original == null || !ByMethod.TryGetValue(original, out MethodSpec spec))
            {
                Plugin.Log.LogError($"Transpiler called for an unknown method {original?.DeclaringType?.Name}.{original?.Name}; leaving it unchanged.");
                return list;
            }

            if (spec.Edit != null)
            {
                var copy = new List<CodeInstruction>(list);
                string error;
                try
                {
                    error = spec.Edit(copy);
                }
                catch (Exception e)
                {
                    error = "edit threw " + e.GetType().Name + ": " + e.Message;
                }
                if (error != null)
                {
                    return Fail(spec, list, error);
                }
                spec.Applied = true;
                spec.Replaced = 1;
                spec.Result = "ok, custom edit applied";
                return copy;
            }

            // Pass 1: find every site and verify counts and shapes. Nothing is changed unless everything matches.
            var toShift = new List<int>();
            var shiftAt = new Dictionary<int, MethodInfo>();
            foreach (SiteSpec site in spec.Sites)
            {
                var positions = new List<int>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (IntConstant(list[i]) == site.Constant)
                    {
                        positions.Add(i);
                    }
                }
                if (positions.Count != site.ExpectedCount)
                {
                    return Fail(spec, list, $"expected {site.ExpectedCount} x constant {site.Constant}, found {positions.Count}");
                }
                foreach ((int ordinal, Shape shape) in site.Replace)
                {
                    int idx = positions[ordinal];
                    CodeInstruction next = idx + 1 < list.Count ? list[idx + 1] : null;
                    if (!ShapeMatches(shape, next))
                    {
                        return Fail(spec, list, $"constant {site.Constant} #{ordinal}: expected {shape} after it, found {next?.opcode.Name ?? "end"}");
                    }
                    toShift.Add(idx);
                    shiftAt[idx] = site.ShiftWith ?? spec.ShiftWith ?? ShiftMethod;
                }
            }

            // Pass 2: insert "call HeroSlots.Shift" (or the site's / method's own shift) after each chosen constant. Inserting after (not replacing) keeps labels and
            // exception blocks on the original instruction. Insert from the end so earlier indices stay valid.
            foreach (int idx in toShift.Distinct().OrderByDescending(x => x))
            {
                list.Insert(idx + 1, new CodeInstruction(OpCodes.Call, shiftAt[idx]));
            }
            spec.Applied = true;
            spec.Replaced = toShift.Count;
            spec.Result = $"ok, {toShift.Count} constant(s) now follow the hero count";
            return list;
        }

        private static List<CodeInstruction> Fail(MethodSpec spec, List<CodeInstruction> list, string reason)
        {
            spec.Applied = false;
            spec.Replaced = 0;
            spec.Result = "SKIPPED, IL does not match (game updated?): " + reason;
            Plugin.Log.LogError($"[IL] {spec.Name}: {spec.Result}. The method is left as vanilla.");
            return list;
        }
    }
}
