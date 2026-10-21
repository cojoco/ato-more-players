# More Players (experimental)

A BepInEx 5 mod for Across the Obelisk: co-op Adventure with more than 4 heroes, and rooms for up to 8 players.
It builds on [More Heroes](https://github.com/cojoco/ato-more-heroes) (which puts up to 8 heroes in a run) and needs it installed.

**Status: 0.1.0, built but not yet tested in game.** Everything here was written against the game's code and checked offline;
nothing has been played with two or more players yet.

## Related mods

- [More Heroes](https://github.com/cojoco/ato-more-heroes): up to 8 heroes in a run. Required by More Players.
- [Enemy Scaling](https://github.com/cojoco/ato-enemy-scaling): stronger enemies. Required by More Heroes, so also needed here.

## What it does

- **More heroes in co-op.** The room's hero count is the host's More Heroes `HeroCount` (up to 8), and at least the room's
  number of player slots. Every player sees that many hero boxes; as in vanilla, the host gives each box to a player and a
  player can own several heroes. Combat, rewards, loot, events, the map conflict screen and the end-of-run screen work for
  every hero, and each player controls their own.
- **Up to 8 players.** The create-room list offers 5 to 8 players (`[General] MaxPlayers`). The lobby shows a row per player,
  each hero box lists every player, and players 5 to 8 get their own colours.
- **Faster co-op:** see the separate [Game Speed](https://github.com/cojoco/ato-game-speed) mod; the host's speed applies to every player in the room.
- A room with at most 4 players and 4 heroes (More Heroes `HeroCount = 4`) is plain vanilla, so friends without the mods can join.

## Requirements

- BepInEx 5.4 (win_x64 on Windows, linux_x64 on Linux), [More Heroes](https://github.com/cojoco/ato-more-heroes) and [Enemy Scaling](https://github.com/cojoco/ato-enemy-scaling) (More Heroes needs it).
- **Every player** needs More Heroes and More Players, the same versions. When a room has more than 4 player slots or more than
  4 heroes, the host's Launch button refuses to start until every player has the same More Players version, and says who is
  missing it.
- Only the host's `HeroCount` matters; the other players' copies read the room's value (their `HeroCount` and `Enabled` are ignored while in someone else's room, from More Heroes 0.2.25).

## How to play

1. Host: set `HeroCount` in `BepInEx/config/com.cojoco.moreheroes.cfg` (e.g. 8), create a co-op Adventure room and pick the
   number of players.
2. Everyone joins. The host presses Launch.
3. Hero selection: the host gives each box to a player (click a name under the box); each player fills their boxes.

## Limits

- Adventure mode only. Other modes keep 4 heroes, so they cannot have more than 4 players.
- **Saves:** a co-op save with more than 4 heroes needs both mods on every player's game to load.
- Not done yet (cosmetic, nothing breaks): the players list behind the chat/players button shows 4 players, and in the card
  shuffle minigame only 4 players' picks are marked on the cards.
- Controller navigation does not reach heroes 5 to 8 (same as More Heroes).

## Config

`BepInEx/config/com.cojoco.moreplayers.cfg` (created on first launch):

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | MaxPlayers | 8 | Largest room size offered when creating a room (4-8). |
| Layout | BoxPlayerScale | 0.6 | Hero selection with more than 4 players: size of the player-name buttons under each box. |
| Layout | BoxPlayerColumnOffset | 0.3 | Hero selection with more than 4 players: distance of the two columns of name buttons from the box centre. |
| Layout | ConflictSlotScale | 0 | Conflict screen with more than 4 heroes: size of each hero's slot. 0 = automatic (0.6). |
| Layout | ConflictSlotSpacing | 0 | Conflict screen with more than 4 heroes: distance between hero slots. 0 = automatic (1.64). |
| Logging | VerboseNet | true | Log every extra-hero message (decks, rewards) sent and received. |

## Reporting problems

Send `Player.log` from **every** player (host and guests), right after the problem:
- Linux: `~/.config/unity3d/Dreamsite Games/AcrossTheObelisk/Player.log`
- Windows: `%USERPROFILE%\AppData\LocalLow\Dreamsite Games\AcrossTheObelisk\Player.log`
