using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MoreHeroes;
using TMPro;
using UnityEngine;

namespace MorePlayers
{
    // The conflict screen (co-op only, MapManager.NET_DoConflict, when players pick different map nodes): one hero picks the rule
    // and every hero rolls a card. The prefab (ConflicttResolution, sharedassets10) has 4 hero slots, Characters/char0..char3 at
    // local x -3.5 / -0.8 / 1.95 / 4.7 (y -0.17, Characters at (-0.5, -3.1), root scale 0.9); each slot is the portrait sprite
    // (characterSPR) with children "speed" (cardbacks), "nick" (nicks), "cards" (characterTcards), "num" (characterTnum) and "X"
    // (xSymbol). ConflictManager loops over team.Size and indexes those arrays, so heroes 5+ threw in SetConflict.
    // With more heroes the Awake postfix clones char3 for each extra hero, grows the 6 arrays, and lays all slots out in one row,
    // smaller and closer ([Layout] ConflictSlotScale / ConflictSlotSpacing). RollResult's "every hero has rolled" wait loop is
    // transpiled (i < 4). The order of who picks the rule (AtOManager.GetConflictResolutionOrder, host only) lists heroes 1-4;
    // the others are inserted at random places.
    internal static class Conflict
    {
        private const float VanillaCentreX = 0.6f;   // middle of char0..char3
        private const float VanillaPitch = 2.73f;

        [HarmonyPatch(typeof(ConflictManager), "Awake")]
        internal static class AwakePatch
        {
            private static void Postfix(ConflictManager __instance)
            {
                int n = HeroSlots.Count;
                if (n <= HeroSlots.VanillaCount)
                {
                    return;
                }
                try
                {
                    AddSlots(__instance, n);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Conflict screen: adding slots for heroes 5..{n} failed; the screen may break with more than 4 heroes: {e}");
                }
            }
        }

        private static void AddSlots(ConflictManager cm, int n)
        {
            if (cm.characterSPR == null || cm.characterSPR.Length != HeroSlots.VanillaCount || cm.cardbacks?.Length != HeroSlots.VanillaCount
                || cm.nicks?.Length != HeroSlots.VanillaCount || cm.xSymbol?.Length != HeroSlots.VanillaCount
                || cm.characterTnum?.Length != HeroSlots.VanillaCount || cm.characterTcards?.Length != HeroSlots.VanillaCount)
            {
                throw new InvalidOperationException($"expected 4 entries in each slot array, found characterSPR {cm.characterSPR?.Length}");
            }
            Transform slot3 = cm.characterSPR[3].transform;
            string pCardback = PathIn(slot3, cm.cardbacks[3].transform);
            string pNick = PathIn(slot3, cm.nicks[3].transform);
            string pX = PathIn(slot3, cm.xSymbol[3]);
            string pNum = PathIn(slot3, cm.characterTnum[3].transform);
            string pCards = PathIn(slot3, cm.characterTcards[3]);
            if (pCardback == null || pNick == null || pX == null || pNum == null || pCards == null)
            {
                throw new InvalidOperationException("the 4th slot's parts are not all inside its portrait");
            }

            var spr = new List<SpriteRenderer>(cm.characterSPR);
            var cardbacks = new List<SpriteRenderer>(cm.cardbacks);
            var nicks = new List<TMP_Text>(cm.nicks);
            var xs = new List<Transform>(cm.xSymbol);
            var nums = new List<TMP_Text>(cm.characterTnum);
            var cards = new List<Transform>(cm.characterTcards);
            var created = new List<GameObject>();
            try
            {
                for (int i = HeroSlots.VanillaCount; i < n; i++)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(slot3.gameObject, slot3.parent);
                    created.Add(clone);
                    clone.name = $"char{i} MorePlayers";
                    Transform c = clone.transform;
                    spr.Add(clone.GetComponent<SpriteRenderer>());
                    cardbacks.Add(Find(c, pCardback).GetComponent<SpriteRenderer>());
                    nicks.Add(Find(c, pNick).GetComponent<TMP_Text>());
                    xs.Add(Find(c, pX));
                    nums.Add(Find(c, pNum).GetComponent<TMP_Text>());
                    Transform cardSlot = Find(c, pCards);
                    // A clone of the 4th slot copies any roll card already dealt into it; the prefab's slots start empty.
                    for (int k = cardSlot.childCount - 1; k >= 0; k--)
                    {
                        UnityEngine.Object.Destroy(cardSlot.GetChild(k).gameObject);
                    }
                    cards.Add(cardSlot);
                }
            }
            catch (Exception)
            {
                foreach (GameObject go in created)
                {
                    UnityEngine.Object.Destroy(go);
                }
                throw;
            }
            cm.characterSPR = spr.ToArray();
            cm.cardbacks = cardbacks.ToArray();
            cm.nicks = nicks.ToArray();
            cm.xSymbol = xs.ToArray();
            cm.characterTnum = nums.ToArray();
            cm.characterTcards = cards.ToArray();

            float scale = Plugin.ConflictSlotScale.Value > 0f ? Plugin.ConflictSlotScale.Value : 0.6f;
            float pitch = Plugin.ConflictSlotSpacing.Value > 0f ? Plugin.ConflictSlotSpacing.Value : VanillaPitch * 0.6f;
            var sb = new StringBuilder($"Conflict screen: {n} hero slots in one row, scale {scale:0.00}, {pitch:0.00} apart:");
            for (int i = 0; i < n; i++)
            {
                Transform t = cm.characterSPR[i].transform;
                if (!BaseScale.TryGetValue(t, out Vector3 baseScale))
                {
                    baseScale = t.localScale; // clones were made from the unscaled 4th slot just above
                    BaseScale[t] = baseScale;
                }
                t.localScale = baseScale * scale;
                t.localPosition = new Vector3(VanillaCentreX + (i - (n - 1) / 2f) * pitch, t.localPosition.y, t.localPosition.z);
                sb.Append($" {i + 1}@{t.localPosition.x:0.00}");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static readonly Dictionary<Transform, Vector3> BaseScale = new Dictionary<Transform, Vector3>();

        private static Transform Find(Transform root, string path)
        {
            Transform t = path == "" ? root : root.Find(path);
            if (t == null)
            {
                throw new InvalidOperationException($"clone has no '{path}'");
            }
            return t;
        }

        private static string PathIn(Transform root, Transform part)
        {
            var names = new List<string>();
            for (Transform t = part; t != null; t = t.parent)
            {
                if (t == root)
                {
                    names.Reverse();
                    return string.Join("/", names);
                }
                names.Add(t.name);
            }
            return null;
        }

        // Host only (ConflictManager.SyncroOrder): who picks the rule, rotating after every conflict. Vanilla lists heroes 0..3.
        [HarmonyPatch(typeof(AtOManager), nameof(AtOManager.GetConflictResolutionOrder))]
        internal static class OrderPatch
        {
            private static void Postfix(List<int> __result)
            {
                try
                {
                    int n = HeroSlots.Count;
                    if (__result == null || n <= HeroSlots.VanillaCount)
                    {
                        return;
                    }
                    var added = new List<int>();
                    for (int i = 0; i < n; i++)
                    {
                        if (!__result.Contains(i))
                        {
                            __result.Insert(UnityEngine.Random.Range(0, __result.Count + 1), i);
                            added.Add(i + 1);
                        }
                    }
                    if (added.Count > 0)
                    {
                        Plugin.Log.LogInfo($"Conflict: heroes {string.Join(", ", added)} added to the order of who picks the rule: {string.Join(" ", __result)}.");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Conflict: extending the order failed: {e.Message}");
                }
            }
        }
    }
}
