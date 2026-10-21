# MorePlayers handoff (2026-10-20, 0.1.0 released; lobby checked solo, co-op untested)

For the next agent. Read this, then `NOTES.md` (design, survey, offsets), then `../MoreHeroes/HANDOFF.md` (conventions, tools,
the HarmonyX bug; they all apply here).

## State

- 0.1.0 is released (GitHub pre-release, see "Repos and releases") and deployed to the user's `BepInEx/plugins`, with
  MoreHeroes 0.2.24 (has the hook MorePlayers needs).
- Scope agreed with the user: up to 8 players, heroes split as vanilla (host assigns boxes, players may own several heroes),
  hero count = host's MoreHeroes HeroCount, at least the room size.
- Offline checks: `ilcheck` with `NS=MorePlayers` (11/11 apply), MoreHeroes 53/53 (see "Tools").

## Solo test (done 2026-10-20, user's screenshot + Player.log)

- Startup: MoreHeroes 0.2.24 53/53 transpilers, 55 patches; MorePlayers 0.1.0 11/11 transpilers, 20 patches, 0 failed; no exceptions.
- Lobby: room sizes 2..8 offered; an 8-player room showed 8 rows, packed to scale 0.51 (46 px apart) to stay above the
  Invite Steam / Launch buttons. User: "that at least looks good so far". Optional polish offered, not done: two columns of 4
  rows at full size (there is room to the right), and the game's "Play with up to 3 other friends!" text.
- The host's "Joined co-op room ... 4 heroes, host's MorePlayers missing" log line was wrong (the host's own room properties
  are not echoed back yet in OnJoinedRoom); fixed: the host now logs "Joined own co-op room ... as host" (in 0.1.0 as released).
- **Not yet run with a second player.** Nothing past the lobby has been seen. The friend test is next.

## First co-op test (2026-10-20, 3 players, 8 heroes)

- Got through hero selection, map, town and into a fight (host's Player.log). Screenshot: the guests' Follow toggle and
  "Press the button when you are ready" + Ready button overlapped the second row of boxes.
- 0.1.1 (built, deployed, untested): `HeroSelect.WidgetsPatch` moves those widgets into the strip right of the boxes (co-op
  boxes end at about x 7.6): Follow beside row 1, Ready beside row 2, labels wrapped to the strip width (`[Layout] MoveGuestWidgets`).
  The log line "Follow/Ready widgets moved ..." also prints MasterDescription's rect; check it against the boxes' bottom.
  Every player needs 0.1.1 (Launch refuses mismatched versions).

## 0.1.2 (built, deployed, untested)

- Version bump only over 0.1.1 (the co-op speed briefly added here moved to the separate GameSpeed mod). Every player needs 0.1.2.

## Test plan with friends (every player sends Player.log)

Start small, each step only after the previous works:
1. 2 players, host HeroCount 5, room size 2: hero selection (5 boxes on both; host assigns box 5; Begin), first fight (hero 5's
   deck: "[Net] Sent/Received extra data for NET_SaveCardDeck", "Combat: host's deck order applied to heroes 5"), rewards
   (`NET_ShareRewards` extra data, row 5 has cards on the guest), loot, an event, the map. Watch for desync messages.
2. Same with 8 heroes.
3. Conflict: both players pick different map nodes (screenshot of the conflict screen with 5+ heroes).
4. Save and reload the co-op run (boxes 5+ re-assigned: "Loaded co-op game: saved owners of boxes 5+ re-assigned").
5. 5+ players (needs 5 accounts): lobby rows, name buttons under the boxes (screenshot), colours, every player owning a box.

## Likely trouble spots

- Layouts are guesses: lobby rows (`PlaceRows`), name buttons (`[Layout] BoxPlayerScale/ColumnOffset`), conflict slots, and
  MoreHeroes' second row of boxes next to the co-op widgets (Follow toggle, ready button, master description) in hero selection.
- `PhotonView.RPC` patch: check the startup summary binds `RpcPatch`; if not, the overload signature changed.
- If a guest's heroes 5+ act differently from the host's: desync. Compare logs; check that the extra-data messages arrived
  before the RPC ("Combat: the host's decks arrived without heroes 5+" means the event was missing).
- Co-op screens not in the survey (NOTES "Survey" lists what was checked).

## Next work (after testing)

- Fix what testing finds.
- Cosmetic, players 5+: players popup (`AlertManager.playerList`), shuffle-minigame marks (`CardItem.mpMark`).
- When co-op works: mark the GitHub release as a full release (it is a pre-release), update README status.

## Tools

- Offline transpiler check: `MANAGED=".../AcrossTheObelisk_Data/Managed" NS=MorePlayers MOD=MorePlayers/bin/Release/netstandard2.1/MorePlayers.dll dotnet <scratch>/ilcheck/ilcheck.dll`
  (build `../MoreHeroes/tools/ilcheck` first; it now takes `NS`, default MoreHeroes).
- Build order: MoreHeroes first (MorePlayers references `../MoreHeroes/bin/Release/netstandard2.1/MoreHeroes.dll`), then
  `dotnet build MorePlayers/MorePlayers.csproj -c Release` (deploys to the plugins folder).
- IL constants per method with the next instruction: `python3 ../MoreHeroes/tools/ilm.py <il.txt> <Class> <Method> [consts...]`
  (coroutine bodies are found by `<Method>d__N`; ordinals printed are the ones specs use).
- Conflict prefab and other non-scene objects: `tools/unitypy/findname.py <Data> <text>` (MoreHeroes) finds the asset file,
  then `tree.py <Data>/sharedassetsN.assets <depth> <root name>` dumps it.

## Repos and releases (all three mods)

- Each mod folder is its own git repo; the parent folder is not. Remotes use ssh (https failed: the user's `gh` login is ssh):
  `git@github.com:cojoco/ato-enemy-scaling.git`, `ato-more-heroes.git`, `ato-more-players.git` (branch `main`).
- Releases (2026-10-20): ato-enemy-scaling v0.1.0, ato-more-heroes v0.2.24, ato-more-players v0.1.0 (pre-release). Each asset is
  `<Mod>-<version>.zip` with `<Mod>.dll` + `INSTALL.txt` (the repo's INSTALL.txt), same as EncounterPicker. Copies in `../dist`.
- Release recipe: bump `PluginVersion`, build Release with 0 warnings, check `strings -e l <dll> | grep -x <version>`, zip with
  Python `zipfile` (the shell is zsh and has no `zip`; zsh does not word-split `$var`, so loop with Python), commit/push, then
  `gh release create v<version> dist/<Mod>-<version>.zip -R cojoco/<repo> --target main --title "<Mod name> <version>" -F notes.md`.
  Release notes link the dependency repos. Ask the user before committing, pushing or releasing.
- Commit messages end with the attribution lines from the session's system reminder.
