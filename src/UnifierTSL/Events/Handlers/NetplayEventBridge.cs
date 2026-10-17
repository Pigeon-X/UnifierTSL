using Terraria;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Extensions;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public class NetplayEventBridge
    {
        public readonly ReadonlyEventNoCancelProvider<LeaveEvent> LeaveEvent = new();
        public readonly ReadonlyEventNoCancelProvider<SocketResetEvent> SocketResetEvent = new();
        public readonly ReadonlyEventProvider<Connect> ConnectEvent = new();
        public readonly ReadonlyEventProvider<ReceiveFullClientInfo> ReceiveFullClientInfoEvent = new();
        /// <summary>预认证阶段（name/UUID 认证前）收到的未知/自定义包——观察级放行（15-UTSL IP 显示线 D1）。仅供显式订阅者观察，不进入原版/TShock 处理管线。</summary>
        public readonly ReadonlyEventProvider<PreAuthPacketEvent> PreAuthPacket = new();
        public NetplayEventBridge() {
            On.Terraria.RemoteClient.Reset += RemoteClient_Reset;
        }

        private void RemoteClient_Reset(On.Terraria.RemoteClient.orig_Reset orig, RemoteClient client, RootContext root) {
            if (!root.Netplay.Disconnect) {
                if (client.IsActive) {
                    LeaveEvent leaveData = new(client.Id, root.ToServer());
                    LeaveEvent.Invoke(leaveData);
                }
                SocketResetEvent resetData = new(client.Id, client, root.ToServer());
                SocketResetEvent.Invoke(resetData);
            }
            orig(client, root);
        }
    }
    public readonly struct LeaveEvent(int plr, ServerContext server) : IPlayerEventContent
    {
        public int Who { get; } = plr;
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct SocketResetEvent(int plr, RemoteClient socket, ServerContext server) : IPlayerEventContent
    {
        public int Who { get; } = plr;
        public readonly RemoteClient Socket = socket;
        public readonly ServerContext Server { get; init; } = server;
    }
    public readonly struct Connect(RemoteClient client, string version) : IEventContent
    {
        public readonly RemoteClient Client = client;
        public readonly string Version = version;
    }
    public readonly struct ReceiveFullClientInfo(RemoteClient client, Player player, LocalClientSender sender) : IEventContent
    {
        public readonly RemoteClient Client = client;
        public readonly Player Player = player;
        public readonly LocalClientSender Sender = sender;
    }
    /// <summary>
    /// 预认证阶段未知/自定义包（观察级放行；15-UTSL IP 显示线 D1）。
    /// RawData 指针仅在事件派发期间有效（对应连接读取缓冲的 fixed 区间），订阅者须同步消费、不得外存。
    /// </summary>
    public readonly unsafe struct PreAuthPacketEvent : IEventContent
    {
        public readonly int Who;
        public readonly void* RawBegin;
        public readonly void* RawEnd;
        public PreAuthPacketEvent(int who, void* begin, void* end) { Who = who; RawBegin = begin; RawEnd = end; }
        public ReadOnlySpan<byte> RawData => new(RawBegin, (int)((byte*)RawEnd - (byte*)RawBegin));
    }
}
