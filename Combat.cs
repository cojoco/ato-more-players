using System;
using System.Collections.Generic;
using System.Reflection;
using BattleMatch;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    // Combat in co-op with more than 4 heroes. Every client simulates the fight itself; the host shares the decks at the start and
    // after reshuffles, and repairs a client's state from its sync code when the clients disagree. MoreHeroes already makes the
    // single-player combat code follow the team size (the same code runs on every client), so what is left is the network part:
    // - MatchManager.NET_ShareDecks sends NET_SaveCardDeck("Hero", deck0, deck1, deck2, deck3): heroes 5+ never get the host's deck
    //   order, so their draws differ between clients. The decks of heroes 5+ go in a Net event (DeckPayload), applied in a Postfix
    //   on NET_SaveCardDeck the same way the game applies the first 4 (clear the draw pile, add the host's cards in order).
    // - MatchManager.FixCodeSyncFromMaster (client-side repair) reads the sync code as 4 hero blocks then 4 NPC blocks, like
    //   FixTOTALCo (which MoreHeroes patches); transpiled the same way in IlPatches.
    internal static class Combat
    {
        private const string SaveCardDeck = "NET_SaveCardDeck";

        private static readonly MethodInfo TeamHeroGetter = AccessTools.PropertyGetter(typeof(MatchManager), "TeamHero");

        private static Team TeamHero(MatchManager mm)
        {
            return TeamHeroGetter?.Invoke(mm, null) as Team;
        }

        internal static void Register()
        {
            Net.Outgoing[SaveCardDeck] = DeckPayload;
        }

        // Sender (host): NET_SaveCardDeck("Hero", ...) -> [first hero index, deck of hero 5 (compressed JSON), deck of hero 6, ...].
        private static object[] DeckPayload(object[] rpcArgs)
        {
            if (rpcArgs.Length == 0 || !(rpcArgs[0] is string type) || type != "Hero")
            {
                return null;
            }
            MatchManager mm = MatchManager.Instance;
            Team team = mm != null ? TeamHero(mm) : null;
            if (team == null || team.Count <= HeroSlots.VanillaCount)
            {
                return null;
            }
            var data = new List<object> { HeroSlots.VanillaCount };
            for (int i = HeroSlots.VanillaCount; i < team.Count; i++)
            {
                Character c = team[i];
                data.Add(c == null ? new byte[0] : Functions.CompressToBytes(JsonHelper.ToJson(c.BattleCards.GetPile().ToArray())));
            }
            return data.ToArray();
        }

        [HarmonyPatch(typeof(MatchManager), SaveCardDeck)]
        internal static class SaveCardDeckPatch
        {
            private static void Postfix(MatchManager __instance, string _type)
            {
                if (_type != "Hero")
                {
                    return;
                }
                object[] data = Net.Take(SaveCardDeck);
                if (data == null)
                {
                    if (HeroSlots.Count > HeroSlots.VanillaCount)
                    {
                        Plugin.Log.LogWarning("Combat: the host's decks arrived without heroes 5+ (host without MorePlayers?); their draws may desync.");
                    }
                    return;
                }
                try
                {
                    Team team = TeamHero(__instance);
                    int first = (int)data[0];
                    var applied = new List<int>();
                    for (int k = 1; k < data.Length; k++)
                    {
                        int i = first + k - 1;
                        Character c = team != null && i < team.Count ? team[i] : null;
                        if (c == null || !(data[k] is byte[] bytes))
                        {
                            continue;
                        }
                        var cards = new List<string>();
                        if (bytes.Length != 0)
                        {
                            cards.AddRange(JsonHelper.FromJson<string>(Functions.DecompressFromBytes(bytes)));
                        }
                        c.BattleCards.Clear(DeckType.Deck);
                        c.BattleCards.AddCards(cards);
                        applied.Add(i + 1);
                    }
                    if (Plugin.VerboseNet.Value)
                    {
                        Plugin.Log.LogInfo($"Combat: host's deck order applied to heroes {string.Join(", ", applied)}.");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Combat: applying the host's decks for heroes 5+ failed: {e}");
                }
            }
        }
    }
}
