using System;
using System.Collections.Generic;
using HarmonyLib;
using MoreHeroes;

namespace MorePlayers
{
    // Card rewards after a fight. The host rolls the cards for every hero (RewardsManager.SetRewards loops over the whole team)
    // and sends NET_ShareRewards(cards0, cards1, cards2, cards3, ...): heroes 5+ get no cards on the other clients, and their
    // reward rows (added by MoreHeroes) would throw on cardsByOrder[4]. Their cards go in a Net event; a Postfix on
    // NET_ShareRewards adds them to cardsByOrder before ShowRewardsCo reaches row 5 (it waits 0.15 s before each row).
    // Picks already carry the hero index (MASTER_CardSelected / NET_CardSelected, NET_DustSelected), so nothing else changes.
    internal static class Rewards
    {
        private const string ShareRewards = "NET_ShareRewards";

        internal static void Register()
        {
            Net.Outgoing[ShareRewards] = RewardsPayload;
        }

        // Sender (host): [first hero index, string[] cards of hero 5, string[] cards of hero 6, ...].
        private static object[] RewardsPayload(object[] rpcArgs)
        {
            RewardsManager rm = RewardsManager.Instance;
            Dictionary<int, string[]> cards = rm?.cardsByOrder;
            if (cards == null)
            {
                return null;
            }
            var data = new List<object> { HeroSlots.VanillaCount };
            for (int i = HeroSlots.VanillaCount; i < HeroSlots.Count; i++)
            {
                data.Add(cards.TryGetValue(i, out string[] c) && c != null ? c : new string[0]);
            }
            return data.Count > 1 ? data.ToArray() : null;
        }

        [HarmonyPatch(typeof(RewardsManager), ShareRewards)]
        internal static class ShareRewardsPatch
        {
            private static void Postfix(RewardsManager __instance)
            {
                object[] data = Net.Take(ShareRewards);
                if (data == null)
                {
                    if (HeroSlots.Count > HeroSlots.VanillaCount)
                    {
                        Plugin.Log.LogWarning("Rewards: the host's rewards arrived without heroes 5+ (host without MorePlayers?).");
                    }
                    return;
                }
                try
                {
                    int first = (int)data[0];
                    for (int k = 1; k < data.Length; k++)
                    {
                        __instance.cardsByOrder[first + k - 1] = data[k] as string[] ?? new string[0];
                    }
                    if (Plugin.VerboseNet.Value)
                    {
                        Plugin.Log.LogInfo($"Rewards: host's cards for heroes {first + 1}..{first + data.Length - 1} received.");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Rewards: adding the host's cards for heroes 5+ failed: {e}");
                }
            }
        }
    }
}
