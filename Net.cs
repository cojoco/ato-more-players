using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;

namespace MorePlayers
{
    // A side channel for data the game's RPCs have no room for (they carry exactly 4 heroes, e.g. NET_SaveCardDeck(type, a, b, c, d)).
    // A mod cannot add [PunRPC] methods to the game's PhotonViews, and changing an RPC's arguments on one side breaks the other,
    // so the vanilla RPC is sent unchanged and the extra heroes' data goes in a custom Photon event sent just before it:
    // a Prefix on PhotonView.RPC looks the RPC name up in Outgoing and raises the event first. Both go through the server
    // reliably on channel 0, so every client receives the event before the RPC it belongs to. The receiving side keeps the data
    // in Pending (by kind), and a patch on the vanilla RPC handler uses it.
    internal static class Net
    {
        // Custom event code (PUN uses 200 and up; the game raises no custom events).
        private const byte EventCode = 117;

        private static bool registered;

        // RPC name -> builds the extra payload (or null for none) from the RPC's arguments, on the sender.
        internal static readonly Dictionary<string, Func<object[], object[]>> Outgoing = new Dictionary<string, Func<object[], object[]>>();

        // Payload kind (the RPC name) -> payload, on receivers. Read with Take.
        private static readonly Dictionary<string, object[]> Pending = new Dictionary<string, object[]>();

        internal static void Register()
        {
            if (registered)
            {
                return;
            }
            PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
            registered = true;
        }

        // The payload that arrived for this RPC (and forgets it), or null.
        internal static object[] Take(string kind)
        {
            if (Pending.TryGetValue(kind, out object[] data))
            {
                Pending.Remove(kind);
                return data;
            }
            return null;
        }

        private static void Send(string kind, object[] data, RpcTarget target)
        {
            var content = new object[data.Length + 1];
            content[0] = kind;
            Array.Copy(data, 0, content, 1, data.Length);
            var options = new RaiseEventOptions
            {
                Receivers = target == RpcTarget.MasterClient ? ReceiverGroup.MasterClient
                    : target == RpcTarget.All || target == RpcTarget.AllBuffered || target == RpcTarget.AllViaServer || target == RpcTarget.AllBufferedViaServer
                        ? ReceiverGroup.All : ReceiverGroup.Others
            };
            if (!PhotonNetwork.RaiseEvent(EventCode, content, options, SendOptions.SendReliable))
            {
                Plugin.Log.LogError($"[Net] Could not send the extra heroes' data for {kind}.");
            }
        }

        // ExitGames...EventData: the game has its own EventData (event definitions).
        private static void OnEvent(ExitGames.Client.Photon.EventData ev)
        {
            if (ev.Code != EventCode)
            {
                return;
            }
            try
            {
                if (!(ev.CustomData is object[] content) || content.Length == 0 || !(content[0] is string kind))
                {
                    Plugin.Log.LogWarning("[Net] Ignoring a malformed MorePlayers event.");
                    return;
                }
                var data = new object[content.Length - 1];
                Array.Copy(content, 1, data, 0, data.Length);
                Pending[kind] = data;
                if (Plugin.VerboseNet.Value)
                {
                    Plugin.Log.LogInfo($"[Net] Received extra data for {kind} ({data.Length} item(s)).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Reading a MorePlayers event failed: {e}");
            }
        }

        [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RPC), typeof(string), typeof(RpcTarget), typeof(object[]))]
        internal static class RpcPatch
        {
            private static void Prefix(string methodName, RpcTarget target, object[] parameters)
            {
                if (methodName == null || !Outgoing.TryGetValue(methodName, out Func<object[], object[]> build) || !Room.Expanded)
                {
                    return;
                }
                try
                {
                    object[] data = build(parameters ?? new object[0]);
                    if (data != null)
                    {
                        Send(methodName, data, target);
                        if (Plugin.VerboseNet.Value)
                        {
                            Plugin.Log.LogInfo($"[Net] Sent extra data for {methodName} ({data.Length} item(s)).");
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[Net] Building the extra heroes' data for {methodName} failed; heroes 5+ may desync: {e}");
                }
            }
        }
    }
}
