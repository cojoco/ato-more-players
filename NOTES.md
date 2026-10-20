# MorePlayers: developer notes

Co-op with more than 4 heroes and rooms for up to 8 players. Depends on MoreHeroes (hero count, every single-player patch, the
HarmonyX fix) and, through it, EnemyScaling. MoreHeroes has no co-op code of its own: MorePlayers switches it on through
`MoreHeroes.MultiplayerApi.RoomHeroCount`.

Line numbers refer to the ilspycmd decompile of the game build current on 2026-10-20 (Unity 2022.3.62f2). No game source is
copied here.

## Scope (decided with the user, 2026-10-20)

- Up to 8 **players**, and heroes split as in vanilla: room size 2-8, hero count up to 8 and at least the number of players,
  the host gives each hero box to a player, a player may own several heroes.
- MorePlayers is a separate mod; MoreHeroes only got a small public hook (see "MoreHeroes 0.2.24" below).
- Testing needs 2+ game instances in one room (one per Steam account: Photon logs in with the Steam name). None was possible
  when 0.1.0 was written; everything is verified offline only.

## How it works

### Room agreement (`Room.cs`)
- Room property `mp_heroes` (int 4..8): set once by the host in a Postfix on `NetworkManager.OnCreatedRoom`:
  loading a save -> the saved team's size; Adventure -> max(MoreHeroes HeroCount, room MaxPlayers); other modes -> 4.
  `Room.HeroCount()` only reads this property, so it cannot change during a run. MoreHeroes' `MultiplayerApi.RoomHeroCount`
  returns it; `HeroSlots.Count` in co-op = team size when the room is expanded, else 4.
- Room and player property `mp_ver`: the MorePlayers version. Each player sets theirs in a Postfix on
  `NetworkManager.OnJoinedRoom` (PUN calls OnJoinedRoom for the creator too).
- `LobbyManager.LaunchGameCheckVersion` (host's Launch button, LobbyManager.cs:808) Prefix: in a room that needs the mod
  (MaxPlayers > 4 or hero count > 4) refuse to launch while a player lacks the same `mp_ver`, or there are more players than
  heroes; shows the game's alert with the names.
- A room with MaxPlayers <= 4 and 4 heroes is vanilla: nothing is published that matters and unmodded players can join.

### Extra-hero data over the network (`Net.cs`)
Some RPCs carry exactly 4 heroes (`MatchManager.NET_SaveCardDeck(type, a, b, c, d)`, `RewardsManager.NET_ShareRewards(cards0..3, ...)`).
A mod cannot add `[PunRPC]` methods to the game's PhotonViews, and changing an RPC's arguments on one side breaks the other. So:
- Sender: Prefix on `PhotonView.RPC(string, RpcTarget, object[])`. If the RPC name is in `Net.Outgoing`, it builds the payload
  for heroes 5+ and raises a custom Photon event (code 117, `[kind, ...data]`) **before** the RPC is sent. Both are reliable
  on channel 0, so receivers get the event first (PUN sends RPCs as events on the same channel).
- Receiver: `PhotonNetwork.NetworkingClient.EventReceived` stores the payload by kind; a Postfix on the vanilla RPC handler
  takes it (`Net.Take`) and applies it.
- Kinds so far: `NET_SaveCardDeck` (decks of heroes 5+, `Combat.cs`) and `NET_ShareRewards` (cards of heroes 5+, `Rewards.cs`).

### Combat (`Combat.cs`, `IlPatches`)
- Clients simulate combat themselves; MoreHeroes' patches already make the shared code follow the team size.
- `NET_ShareDecks` (MatchManager.cs:2096) sends the hero decks as 4 arrays: heroes 5+'s draw order would differ between
  clients. Their decks go through Net; `SaveCardDeckPatch` applies them like the game does (clear Deck pile, add in order).
- `FixCodeSyncFromMaster` (MatchManager.cs:15377, client-side repair from the host's sync code): same layout as `FixTOTALCo`
  (hero blocks, 4 NPC blocks, round at [8+extra], enchantments at [9+extra]). Transpiled: 4 #0 (`i < 4 ?`), #1 (`i - 4`),
  #3 (`if (i < 4)` hero part), 8 #1 (`i < 8`), #2 (`array[8]`), 9 #1 (`array[9]`). Not shifted: per-character indices and
  lengths (`aux[3]`, `aux[4]`, `aux[8]`, `aux[10]`, `Length >= 4`, `< 9`, `< 11`) and `array.Length < 10`.
- Already variable in the game: `GenerateSyncCode`, `GenerateSyncCodeForCheckingAction`, `NET_RequestDeckOrderResync`,
  deck order sync (`NET_SetGeneratedDeckOrder`, by hero index), turn save strings (`NET_SetLoadTurn`).
- Emotes: `EmoteManager.SelectNextCharacter` cycled heroActive 0..3 (`> 3`): shifted, so owners of heroes 5+ can emote.

### Run start and team sync (`TeamSync.cs`, `HeroSelect.cs`)
- Only the host runs `HeroSelectionManager.BeginAdventure` (`IsMainPlayer`). It passes N picks; MoreHeroes' `ExtraHeroes`
  creates heroes 5+ (co-op allowed when the room is expanded; no fallback top-up in co-op, perks never from the host's profile).
- `NetworkManager.PlayerHeroPositionOwner` (owner per hero slot) is `new string[4]` (NetworkManager.cs:472, 1561);
  `AssignHeroPlayerPositionOwner` Prefix grows it. Readers already use its length: `AtOManager.InitGameMP` (owners),
  `PerksManager.SetPlayerPerks` co-op branch (perks keyed owner_subclass), `ShareTeam`, `CurrencyManager.DistributeGoldDustBetweenHeroes`.
- `AtOManager.NET_SetTeam` (every client) rebuilds the team with `new GameTeam()` (4 slots) and `SetHero(hero, i)`, which
  does not grow: Prefix on `GameTeam.SetHero` grows the list up to 8. Vanilla never passes an index past 3.
- `ShareGameMP` / `NET_ShareGameMP` (AtOManager.cs:662, 835): crafted cards per hero written and read for k < 4. Both
  transpiled with `TeamSync.RoomShift` (the room property, so both sides agree even before a client has the team); the host
  pads `craftedCards` to the room count first. combatStats, loot/conflict order lists are variable already.

### Hero selection (`HeroSelect.cs`, `Lobby.cs`, `IlPatches`)
- MoreHeroes clones the boxes (co-op: the room's count on every client, not limited by the local profile's unlocked heroes,
  and also when loading a co-op save). Box ids come from the names, `NET_AssignHeroToBox` indexes `boxGO` by id: fine.
- Co-op-only loops over 4 boxes, transpiled with `HeroSelect.BoxShift` (boxGO.Length): `DrawBoxSelectionNames` (4 #0, #1),
  `AllBoxWithOwners` (#0 `num = 4` boxes needing an owner, #1 loop), `CheckButtonEnabled`, `SetPlayersReady`,
  `PerkTree.DoTeamPerks` (#1, the co-op loop; MoreHeroes shifts #0).
- Player-name buttons: each box has 4 `BoxPlayer` buttons (BoxPlayersSelection/BoxPlayer..BoxPlayer (3), local y -0.92 step
  -0.25, scale 1.5 x 1). `DrawBoxSelectionNames` #2 (`k < 4` player slots) follows `Lobby.PlayerSlotShift`. With MaxPlayers > 4,
  a `BoxSelection.Awake` Prefix clones the 4th button (scene boxes; MoreHeroes' clones copy the grown array), lays them out in two
  columns (BoxPlayerScale 0.6, BoxPlayerColumnOffset 0.3 box units; **guessed**, no screenshot yet), and `HidePlayers`,
  `SelectPlayer`, `ActivePlayerNick` (4-loops) are replaced by Prefixes over all buttons.
- Loading a co-op save: the game re-assigns boxes 0..3 from `GameData.Owner0..3` (real names). Heroes 5+'s owners are in the
  saved team (`Character.owner` is serialized); `GameData.LoadData` Postfix maps them through the save's `playerNickRealDict`,
  and a `DrawBoxSelectionNames` Postfix assigns boxes 4+ to the players with those names.

### Lobby (`Lobby.cs`)
- Scene `Lobby` (level2). Create panel: `Settings/number/Dropdown` (TMP_Dropdown `UICreatePlayers`); its option text is parsed
  as MaxPlayers (`LobbyManager.CreateRoom` -> `NetworkManager.CreateRoom`, NetworkManager.cs:465). Options 5..MaxPlayers are
  appended in a `LobbyManager.Awake` Postfix (the scene's options were not decoded; the log lists them).
- Room panel: `Room/Canvas/Settings/player1..player4` (RectTransforms anchored top-left, y -260/-350/-440/-530, 500x80; each a
  `playerNT` text with Border, Image, SlotIcon and from row 2 a `KickN` button). `DrawLobbyNames` (LobbyManager.cs:556) fills
  `roomSlots`, `roomSlotsImage`, `roomSlotsIcon`, `roomSlotsKick` up to MaxPlayers. The Awake Postfix clones player4 into rows
  5..8, finds the same parts by path, grows the 4 arrays, and re-wires the Kick button (the clone's persistent listener is
  `KickPlayer(3)`: turned off, a runtime listener with the row's position added). LaunchButton / SteamFriends sit under the rows
  (centre-anchored y -680); `PlaceRows` shrinks rows and gaps together if row N would reach the Launch button's top.
- `NetworkManager.ColorFromPosition` gives positions 3+ the same colour: Postfix gives 4..7 four more.
- Variable already: `PlayerPositionList` (MaxPlayers), `GetNumPlayers`, ready/status dictionaries, gold/dust per player,
  give-gold target cycling, kick by position.

### Conflict screen (`Conflict.cs`)
- `MapManager.NET_DoConflict` instantiates `conflictPrefab` (`ConflicttResolution` in sharedassets10.assets: root scale 0.9,
  `Characters` at (-0.5, -3.1), `char0..char3` at x -3.5 / -0.8 / 1.95 / 4.7, y -0.17; each with `speed` (cardbacks), `nick`,
  `cards`, `num`, `X`). `SetConflict` loops `team.Size` over those 4-entry arrays: crash with 5+ heroes.
- `ConflictManager.Awake` Postfix: clone char3 per extra hero, map parts by path, grow the 6 arrays, one row centred at the
  vanilla centre (x 0.6), scale 0.6, pitch 1.64 (`[Layout] ConflictSlotScale/Spacing`; **guessed**).
- `RollResult` wait loop `i < 4` transpiled (HeroSlots.Shift). `GetConflictResolutionOrder` (host only) lists heroes 0..3:
  Postfix inserts the missing heroes at random places (host-only, the result is shared by RPC).

### Loot (`Loot.cs`)
- MoreHeroes' panels and its `SetCharacters` Prefix (appends heroes 5+ to `characterOrder` in order) run on every client, so
  shared loot (host shuffles 0..3, `NET_ShareLootIdAndOrder` sends 4 bytes) stays identical everywhere.
- Independent loot: `SetLoot` marks heroes owned by others as looted, `i < 4` (#2) -> `Loot.PanelShift` (looted[] length).

## Survey: co-op 4s left alone (and why)

| Where | Why |
|---|---|
| `NetworkManager.Owner0..3`, `GameData.owner0..3` | save metadata; heroes 5+ handled from the saved team (above) |
| `AlertManager.SetPlayers` (players popup, `playerList` of 4) | cosmetic: players 5+ not listed. Clone like the lobby rows if wanted |
| `CardItem.mpMark[4]` (`ShowMPMark`, shuffle minigame picks) | cosmetic: 5th+ player's pick not marked (loop breaks safely) |
| `CardPlayerManager` loops of 4 | the 4 cards of the shuffle game, not heroes/players |
| `CorruptionManager` 606, `CardCraftManager` 5068/5119 | 4 corruption options |
| Controller lists over `sideCharacters.charArray[j < 4]` (ConflictManager, CorruptionManager, CardCraftManager, ...) | controller only (as MoreHeroes) |
| `RpcTarget`, `Enums.Platform` constants in RPC calls | not counts |
| `MatchManager.turnDataForMP` | 11 strings, per field not per hero |
| `RemoteLogger`, `CheatDrawer` | debug/telemetry |
| `ChallengeSelectionManager*`, weekly, Singularity | not Adventure: 4 heroes, max 4 players (Launch check) |

## MoreHeroes 0.2.24 (the hook)

- `MultiplayerApi` (public): `RoomHeroCount` (Func<int>, set by MorePlayers), `Current()`, `Expanded`, `ConfiguredHeroCount`
  (HeroCount, or 4 when Enabled=false).
- `HeroSlots.Count`: co-op uses the team size when `MultiplayerApi.Expanded`, else 4 (unchanged without MorePlayers).
- `HeroSelectUi`: co-op allowed when expanded; box count = room count (not limited by local unlocks); also when loading a co-op save.
- `ExtraHeroes`: co-op allowed when expanded; target = room count; no fallback top-up and no host-profile perks in co-op;
  `LastExtraHeroes` not overwritten by co-op picks.
- `FinishRunUi.RunHeroes`: co-op allowed when expanded.
- `HarmonyFix` public; `ApplyToAllPatchedMethods` skips methods that already carry the fix, so MorePlayers can call it again.

## Verified offline vs guessed (0.1.0)

- Offline: both mods build with 0 warnings; MorePlayers' 11 transpilers apply to the real game IL (`ilcheck`, NS=MorePlayers),
  MoreHeroes' 53 still apply; no harmful HarmonyX leave drops. Scene layouts read with UnityPy (lobby level2, hero selection
  level4, conflict prefab sharedassets10).
- Not verified at all: anything at runtime. In particular: Prefix/Postfix binding (startup summary), Photon event delivery and
  ordering, `PhotonView.RPC` patch target, every layout (lobby rows, name buttons, conflict slots, MoreHeroes' second box row
  next to the co-op widgets Follow / ready button / MasterDescription), and whether other co-op paths not in the survey break.
