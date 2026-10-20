using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MoreHeroes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MorePlayers
{
    // Rooms for up to 8 players.
    // - Lobby (scene "Lobby", Room/Canvas/Settings): the "players" dropdown of the create-room panel offers 2..4 and its text is
    //   parsed as the room's MaxPlayers (NetworkManager.CreateRoom), so 5..[General] MaxPlayers are appended. The room panel has 4
    //   player rows (player1..player4, 90 px apart from y -260, each a name text, a colour image, a platform icon and, from the
    //   second row on, a Kick button). LobbyManager.DrawLobbyNames fills roomSlots/roomSlotsImage/roomSlotsIcon/roomSlotsKick up
    //   to the room's MaxPlayers, so with more than 4 the rows are cloned from player4 and the arrays grown (Awake postfix),
    //   then the rows are packed closer if they would run into the Launch / Steam buttons.
    // - Player colours: NetworkManager.ColorFromPosition gives positions 0..3 their colours and every later one the 4th; players
    //   5..8 get 4 more.
    // - Hero selection: each box lists the players as 4 name buttons (BoxSelection.boxPlayer, "BoxPlayer" .. "BoxPlayer (3)",
    //   0.25 apart under the box) that the host clicks to give the box to a player. With more than 4 player slots the buttons are
    //   cloned (BoxSelection.Awake prefix, before the game hides them) and the BoxSelection loops over 4 buttons are replaced.
    internal static class Lobby
    {
        // Name buttons per hero box on the current hero selection screen (4 unless more were added).
        private static int boxPlayerSlots = HeroSlots.VanillaCount;

        // Called from transpiled IL: k (a 4: the number of player-name buttons in a box) becomes the buttons that exist.
        public static int PlayerSlotShift(int k)
        {
            return k + boxPlayerSlots - HeroSlots.VanillaCount;
        }

        // Player slots the current room has (MaxPlayers), at least 4.
        private static int RoomSlots()
        {
            return Room.InRoom ? Math.Max(HeroSlots.VanillaCount, Math.Min((int)Photon.Pun.PhotonNetwork.CurrentRoom.MaxPlayers, HeroSlots.MaxCount)) : HeroSlots.VanillaCount;
        }

        // ---- Create-room dropdown and room rows ----

        [HarmonyPatch(typeof(LobbyManager), "Awake")]
        internal static class AwakePatch
        {
            private static void Postfix(LobbyManager __instance)
            {
                try
                {
                    AddDropdownOptions(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Lobby: adding 5+ players to the room size list failed: {e}");
                }
                try
                {
                    AddRoomRows(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Lobby: adding rows for players 5+ failed; rooms with more than 4 players will not show them: {e}");
                }
            }
        }

        private static void AddDropdownOptions(LobbyManager lm)
        {
            TMP_Dropdown dd = lm.UICreatePlayers;
            if (dd == null)
            {
                return;
            }
            var existing = new HashSet<string>(dd.options.Select(o => o.text.Trim()));
            var added = new List<string>();
            for (int n = HeroSlots.VanillaCount + 1; n <= Plugin.MaxPlayers.Value; n++)
            {
                string t = n.ToString();
                if (!existing.Contains(t))
                {
                    dd.options.Add(new TMP_Dropdown.OptionData(t));
                    added.Add(t);
                }
            }
            dd.RefreshShownValue();
            Plugin.Log.LogInfo($"Lobby: room sizes offered: {string.Join(", ", dd.options.Select(o => o.text))}" +
                               (added.Count > 0 ? $" ({string.Join(", ", added)} added by MorePlayers)." : "."));
        }

        private static void AddRoomRows(LobbyManager lm)
        {
            int n = HeroSlots.MaxCount;
            if (lm.roomSlots == null || lm.roomSlots.Length != HeroSlots.VanillaCount || lm.roomSlotsImage?.Length != HeroSlots.VanillaCount
                || lm.roomSlotsIcon?.Length != HeroSlots.VanillaCount || lm.roomSlotsKick?.Length != HeroSlots.VanillaCount)
            {
                Plugin.Log.LogWarning($"Lobby: expected 4 room rows, found {lm.roomSlots?.Length}; leaving the room panel alone.");
                return;
            }
            Transform lastText = lm.roomSlots[3].transform;
            Transform row = RowOf(lastText);
            if (row == null)
            {
                Plugin.Log.LogWarning("Lobby: could not find the 4th player row; leaving the room panel alone.");
                return;
            }
            // Paths of the 4th row's parts inside the row, to find the same parts in a clone.
            string pText = PathIn(row, lm.roomSlots[3].transform);
            string pImage = PathIn(row, lm.roomSlotsImage[3].transform);
            string pIcon = PathIn(row, lm.roomSlotsIcon[3].transform);
            string pKick = PathIn(row, lm.roomSlotsKick[3]);
            if (pText == null || pImage == null || pIcon == null || pKick == null)
            {
                Plugin.Log.LogWarning("Lobby: the 4th row's parts are not all inside it; leaving the room panel alone.");
                return;
            }

            var texts = new List<TMP_Text>(lm.roomSlots);
            var images = new List<Image>(lm.roomSlotsImage);
            var icons = new List<Image>(lm.roomSlotsIcon);
            var kicks = new List<Transform>(lm.roomSlotsKick);
            var rows = new List<RectTransform> { RowOf(lm.roomSlots[0].transform) as RectTransform, RowOf(lm.roomSlots[1].transform) as RectTransform,
                RowOf(lm.roomSlots[2].transform) as RectTransform, row as RectTransform };
            var created = new List<GameObject>();
            try
            {
                for (int i = HeroSlots.VanillaCount; i < n; i++)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
                    created.Add(clone);
                    clone.name = $"player{i + 1} MorePlayers";
                    Transform c = clone.transform;
                    texts.Add(c.Find(pText).GetComponent<TMP_Text>());
                    images.Add(c.Find(pImage).GetComponent<Image>());
                    icons.Add(c.Find(pIcon).GetComponent<Image>());
                    Transform kick = c.Find(pKick);
                    kicks.Add(kick);
                    RewireKick(kick, i);
                    rows.Add(c as RectTransform);
                    clone.SetActive(row.gameObject.activeSelf);
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
            lm.roomSlots = texts.ToArray();
            lm.roomSlotsImage = images.ToArray();
            lm.roomSlotsIcon = icons.ToArray();
            lm.roomSlotsKick = kicks.ToArray();
            roomRows = rows;
            launchButton = lm.buttonLaunch as RectTransform;
            placedFor = -1; // new scene: these rows have not been placed yet
            PlaceRows(HeroSlots.VanillaCount);
            Plugin.Log.LogInfo($"Lobby: room panel has {n} player rows.");
        }

        private static List<RectTransform> roomRows = new List<RectTransform>();
        private static RectTransform launchButton;
        private static int placedFor = -1;

        // Rows 1..count, 90 px apart as vanilla; if the last one would reach the Launch / Steam buttons, rows and gaps shrink
        // together (scale s: top + s * (pitch * (count - 1) - height) >= the buttons' top edge), but not below half size.
        private static void PlaceRows(int count)
        {
            if (roomRows.Count < count || roomRows.Count < 2 || roomRows.Any(r => r == null) || count == placedFor)
            {
                return;
            }
            placedFor = count;
            float top = roomRows[0].anchoredPosition.y;
            float pitch = roomRows[1].anchoredPosition.y - top; // -90 in the scene
            float height = roomRows[0].rect.height;
            float scale = 1f;
            float limit = float.NegativeInfinity;
            if (count > HeroSlots.VanillaCount && launchButton != null)
            {
                // The Launch button's top edge in the rows' anchored units (rows share their anchors, so local y and anchored y
                // differ by the same amount for every row).
                var corners = new Vector3[4];
                launchButton.GetWorldCorners(corners);
                float launchTopLocal = roomRows[0].parent.InverseTransformPoint(corners[1]).y;
                float offset = roomRows[0].localPosition.y - top;
                limit = launchTopLocal - offset + 10f;
                float need = height - pitch * (count - 1);
                if (need > 0f && top - limit < need)
                {
                    scale = Mathf.Clamp((top - limit) / need, 0.5f, 1f);
                }
            }
            for (int i = 0; i < roomRows.Count; i++)
            {
                RectTransform r = roomRows[i];
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, top + pitch * scale * i);
                r.localScale = new Vector3(scale, scale, 1f);
            }
            Plugin.Log.LogInfo($"Lobby: {count} player rows, {Math.Abs(pitch * scale):0} px apart, scale {scale:0.00} (first row at y {top:0}, " +
                               $"Launch button top at {(float.IsNegativeInfinity(limit) ? "-" : limit.ToString("0"))}).");
        }

        // Clones keep the 4th row's Kick button, which calls LobbyManager.KickPlayer(3) (a persistent listener with that
        // argument). Turn those off and call KickPlayer with this row's position instead.
        private static void RewireKick(Transform kick, int position)
        {
            Button b = kick != null ? kick.GetComponent<Button>() : null;
            if (b == null)
            {
                return;
            }
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                b.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            }
            b.onClick.AddListener(() => LobbyManager.Instance?.KickPlayer(position));
        }

        // The child of the rows' parent (Settings) that holds this part.
        private static Transform RowOf(Transform part)
        {
            Transform t = part;
            while (t != null && t.parent != null && !(t.name.StartsWith("player", StringComparison.Ordinal) && t.parent.name == "Settings"))
            {
                t = t.parent;
            }
            return t != null && t.parent != null && t.parent.name == "Settings" ? t : null;
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

        // DrawLobbyNames shows MaxPlayers rows; place them for that many.
        [HarmonyPatch(typeof(LobbyManager), "DrawLobbyNames")]
        internal static class DrawNamesPatch
        {
            private static void Prefix()
            {
                try
                {
                    PlaceRows(RoomSlots());
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Lobby: placing the player rows failed: {e.Message}");
                }
            }
        }

        // ---- Player colours ----

        private static readonly string[] ExtraColors = { "#FFC83D", "#12FFEF", "#FF8A3D", "#F78FD6" };

        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.ColorFromPosition))]
        internal static class ColorPatch
        {
            private static void Postfix(int position, ref string __result)
            {
                if (position >= HeroSlots.VanillaCount && position < HeroSlots.VanillaCount + ExtraColors.Length)
                {
                    __result = ExtraColors[position - HeroSlots.VanillaCount];
                }
            }
        }

        // ---- Player-name buttons in the hero selection boxes ----

        [HarmonyPatch(typeof(BoxSelection), "Awake")]
        internal static class BoxAwakePatch
        {
            private static void Prefix(BoxSelection __instance)
            {
                try
                {
                    AddBoxPlayers(__instance, RoomSlots());
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Hero selection: adding name buttons for players 5+ to {__instance.name} failed: {e}");
                }
            }
        }

        private static void AddBoxPlayers(BoxSelection box, int n)
        {
            BoxPlayer[] players = box.boxPlayer;
            if (players == null || players.Length >= n || players.Length != HeroSlots.VanillaCount || players.Any(p => p == null))
            {
                // Boxes cloned by MoreHeroes copy an already grown array; nothing to do.
                boxPlayerSlots = players?.Length ?? HeroSlots.VanillaCount;
                return;
            }
            BoxPlayer last = players[players.Length - 1];
            Vector3 step = last.transform.localPosition - players[players.Length - 2].transform.localPosition;
            var all = new List<BoxPlayer>(players);
            for (int i = players.Length; i < n; i++)
            {
                GameObject clone = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                clone.name = $"BoxPlayer ({i}) MorePlayers";
                clone.transform.localPosition = last.transform.localPosition + step * (i - players.Length + 1);
                all.Add(clone.GetComponent<BoxPlayer>());
            }
            box.boxPlayer = all.ToArray();
            boxPlayerSlots = n;
            LayoutBoxPlayers(box);
        }

        // With more than 4 name buttons, two columns (players 1, 3, 5, 7 left; 2, 4, 6, 8 right) so the list is no taller than 4
        // buttons and does not run into the second row of hero boxes. Buttons are shrunk to [Layout] BoxPlayerScale and placed
        // [Layout] BoxPlayerColumnOffset left/right of the box centre (box-local units; a box is about 1.2 wide).
        internal static void LayoutBoxPlayers(BoxSelection box)
        {
            BoxPlayer[] players = box.boxPlayer;
            if (players == null || players.Length <= HeroSlots.VanillaCount)
            {
                return;
            }
            Vector3 first = players[0].transform.localPosition;
            float pitchY = players[1].transform.localPosition.y - first.y;
            float half = Plugin.BoxPlayerColumnOffset.Value;
            float k = Plugin.BoxPlayerScale.Value;
            for (int i = 0; i < players.Length; i++)
            {
                Transform t = players[i].transform;
                if (!BaseScale.TryGetValue(t, out Vector3 baseScale))
                {
                    baseScale = t.localScale;
                    BaseScale[t] = baseScale;
                }
                t.localScale = new Vector3(baseScale.x * k, baseScale.y * k, baseScale.z);
                int col = i % 2;
                int rowIndex = i / 2;
                t.localPosition = new Vector3(first.x + (col == 0 ? -half : half), first.y + pitchY * k * rowIndex, first.z);
            }
        }

        private static readonly Dictionary<Transform, Vector3> BaseScale = new Dictionary<Transform, Vector3>();

        // BoxSelection.HidePlayers / SelectPlayer / ActivePlayerNick loop over 4 buttons; with more, do the same for all of them.
        [HarmonyPatch(typeof(BoxSelection), "HidePlayers")]
        internal static class HidePlayersPatch
        {
            private static bool Prefix(BoxSelection __instance)
            {
                if (__instance.boxPlayer == null || __instance.boxPlayer.Length <= HeroSlots.VanillaCount)
                {
                    return true;
                }
                foreach (BoxPlayer p in __instance.boxPlayer)
                {
                    p.gameObject.SetActive(false);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(BoxSelection), "SelectPlayer")]
        internal static class SelectPlayerPatch
        {
            private static bool Prefix(BoxSelection __instance, int num)
            {
                if (__instance.boxPlayer == null || __instance.boxPlayer.Length <= HeroSlots.VanillaCount)
                {
                    return true;
                }
                for (int i = 0; i < __instance.boxPlayer.Length; i++)
                {
                    if (i != num)
                    {
                        __instance.boxPlayer[i].HideBorder();
                    }
                    else
                    {
                        __instance.boxPlayer[i].DrawBorder();
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(BoxSelection), "ActivePlayerNick")]
        internal static class ActivePlayerPatch
        {
            private static bool Prefix(BoxSelection __instance, string playerNick)
            {
                if (__instance.boxPlayer == null || __instance.boxPlayer.Length <= HeroSlots.VanillaCount)
                {
                    return true;
                }
                foreach (BoxPlayer p in __instance.boxPlayer)
                {
                    p.Activate(p.playerNick == playerNick && playerNick != "");
                }
                return false;
            }
        }
    }
}
