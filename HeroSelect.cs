using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MoreHeroes;
using Photon.Realtime;
using TMPro;
using UnityEngine;

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
            // The guest-only widgets (Follow the leader toggle, "Press the button when you are ready" + Ready button) sit right under the
        // single row of boxes in vanilla, where MoreHeroes now puts the second row. Move them into the free strip to the right of the
        // boxes (co-op boxes end at about x 7.6, the screen at 9.6): Follow next to row 1, Ready next to row 2, texts wrapped to the
        // strip width. Positions are set once when the screen settles; the game only toggles these widgets on and off afterwards.
        [HarmonyPatch(typeof(HeroSelectionManager), "SetCharacterSelectionTabCount")]
        [HarmonyAfter(Plugin.MoreHeroesGuid)]
        internal static class WidgetsPatch
        {
            private static void Postfix(HeroSelectionManager __instance)
            {
                if (!Plugin.MoveGuestWidgets.Value || !GameManager.Instance.IsMultiplayer() || BoxShift(0) == 0)
                {
                    return;
                }
                try
                {
                    PlaceWidgets(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Hero selection: moving the Follow/Ready widgets failed: {e}");
                }
            }
        }

        private const float StripGap = 0.25f;
        private const float ScreenMargin = 0.15f;

        private static void PlaceWidgets(HeroSelectionManager hsm)
        {
            Bounds? row1 = SpriteBounds(hsm.boxGO, 0, HeroSlots.VanillaCount);
            Bounds? row2 = SpriteBounds(hsm.boxGO, HeroSlots.VanillaCount, hsm.boxGO.Length);
            if (row1 == null || row2 == null)
            {
                Plugin.Log.LogWarning("Hero selection: box bounds not found, Follow/Ready widgets left in place.");
                return;
            }
            float boxesRight = Mathf.Max(row1.Value.max.x, row2.Value.max.x);
            Camera cam = Camera.main;
            float screenRight = cam != null && cam.orthographic ? cam.transform.position.x + cam.orthographicSize * cam.aspect : 9.6f;
            float left = boxesRight + StripGap;
            float right = screenRight - ScreenMargin;
            float width = right - left;
            float cx = (left + right) * 0.5f;
            if (width < 0.8f)
            {
                Plugin.Log.LogWarning($"Hero selection: only {width:0.00} free right of the boxes (x {boxesRight:0.00}..{screenRight:0.00}); Follow/Ready widgets left in place.");
                return;
            }

            var sb = new StringBuilder($"Hero selection: Follow/Ready widgets moved right of the boxes, strip x {left:0.00}..{right:0.00}.");
            // Follow: the X box with its label above it, centred on row 1.
            Transform followBox = hsm.botonFollow != null ? hsm.botonFollow.transform : null;
            TMP_Text followText = followBox?.parent?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => !t.transform.IsChildOf(followBox));
            if (followBox != null)
            {
                float y = row1.Value.center.y;
                followBox.position = new Vector3(cx, y - 0.3f, followBox.position.z);
                if (followText != null)
                {
                    FitText(followText, width, new Vector2(cx, y + 0.35f));
                }
                sb.Append($" Follow at ({cx:0.00}, {y - 0.3f:0.00}){(followText == null ? ", label not found" : "")}.");
            }
            // Ready: the text above the button, centred on row 2.
            if (hsm.readyButton != null)
            {
                float y = row2.Value.center.y;
                hsm.readyButton.position = new Vector3(cx, y - 0.35f, hsm.readyButton.position.z);
                TMP_Text readyText = hsm.readyButtonText != null ? hsm.readyButtonText.GetComponent<TMP_Text>() : null;
                if (readyText != null)
                {
                    FitText(readyText, width, new Vector2(cx, y + 0.4f));
                }
                sb.Append($" Ready button at ({cx:0.00}, {y - 0.35f:0.00}){(readyText == null ? ", text not found" : "")}.");
            }
            if (hsm.masterDescription != null && hsm.masterDescription is RectTransform md)
            {
                var c = new Vector3[4];
                md.GetWorldCorners(c);
                sb.Append($" Master description x {c[0].x:0.00}..{c[2].x:0.00}, y {c[0].y:0.00}..{c[2].y:0.00}; boxes end at y {row2.Value.min.y:0.00}.");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        // Centre a label at the given world point, wrapped to the strip width (centred lines).
        private static void FitText(TMP_Text text, float worldWidth, Vector2 center)
        {
            RectTransform rt = text.rectTransform;
            float sx = Mathf.Abs(rt.lossyScale.x);
            if (sx > 0.0001f)
            {
                rt.sizeDelta = new Vector2(worldWidth / sx, rt.sizeDelta.y);
            }
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.Center;
            rt.position = new Vector3(center.x, center.y, rt.position.z);
        }

        // Sprite bounds of boxes [from, to) (TMP bounds are not settled on the screen's first frame, so sprites only).
        private static Bounds? SpriteBounds(GameObject[] boxes, int from, int to)
        {
            Bounds? all = null;
            for (int i = from; i < Math.Min(to, boxes.Length); i++)
            {
                if (boxes[i] == null || !boxes[i].activeInHierarchy)
                {
                    continue;
                }
                foreach (SpriteRenderer r in boxes[i].GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!r.enabled || r.sprite == null)
                    {
                        continue;
                    }
                    if (all == null)
                    {
                        all = r.bounds;
                    }
                    else
                    {
                        Bounds b = all.Value;
                        b.Encapsulate(r.bounds);
                        all = b;
                    }
                }
            }
            return all;
        }
    }
}
