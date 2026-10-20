# MorePlayers handoff (2026-10-20, 0.1.0, untested)

For the next agent. Read this, then `NOTES.md` (design, survey, offsets), then `../MoreHeroes/HANDOFF.md` (conventions, tools,
the HarmonyX bug; they all apply here).

## State

- 0.1.0 is written and builds clean; MoreHeroes 0.2.24 has the hook it needs. **Nothing has run in game** (the user's friends
  were not available). Both DLLs are deployed to the user's `BepInEx/plugins`.
- Scope agreed with the user: up to 8 players, heroes split as vanilla (host assigns boxes, players may own several heroes),
  hero count = host's MoreHeroes HeroCount, at least the room size.
- Offline checks: `ilcheck` with `NS=MorePlayers` (11/11 apply), MoreHeroes 53/53 (see "Tools").

## First test (solo, no friends needed)

1. Launch the game, check `Player.log`: "More Players 0.1.0 loaded", "Transpilers: 11/11", no FAILED; MoreHeroes still 53/53.
2. Single-player run start: must behave exactly as before (MorePlayers does nothing outside a room).
3. Open the multiplayer lobby, create-room panel: the players list should offer 2..8 ("Lobby: room sizes offered" log line).
   Creating a room alone shows the rows for MaxPlayers slots ("Lobby: N player rows ..." with the scale). Screenshot it.

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
- Release zips in `../dist` (with an INSTALL.txt like EnemyScaling's) once it works.

## Tools

- Offline transpiler check: `MANAGED=".../AcrossTheObelisk_Data/Managed" NS=MorePlayers MOD=MorePlayers/bin/Release/netstandard2.1/MorePlayers.dll dotnet <scratch>/ilcheck/ilcheck.dll`
  (build `../MoreHeroes/tools/ilcheck` first; it now takes `NS`, default MoreHeroes).
- Build order: MoreHeroes first (MorePlayers references `../MoreHeroes/bin/Release/netstandard2.1/MoreHeroes.dll`), then
  `dotnet build MorePlayers/MorePlayers.csproj -c Release` (deploys to the plugins folder).
- IL constants per method with the next instruction: `python3 ../MoreHeroes/tools/ilm.py <il.txt> <Class> <Method> [consts...]`
  (coroutine bodies are found by `<Method>d__N`; ordinals printed are the ones specs use).
- Conflict prefab and other non-scene objects: `tools/unitypy/findname.py <Data> <text>` (MoreHeroes) finds the asset file,
  then `tree.py <Data>/sharedassetsN.assets <depth> <root name>` dumps it.
