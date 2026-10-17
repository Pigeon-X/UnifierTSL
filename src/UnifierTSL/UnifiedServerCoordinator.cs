using NuGet.Protocol.Plugins;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Terraria;
using Terraria.Localization;
using Terraria.Net.Sockets;
using TrProtocol;
using TrProtocol.NetPackets;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;
using UnifierTSL.Network;
using UnifierTSL.Performance;
using UnifierTSL.Servers;
using static UnifierTSL.Utilities;

namespace UnifierTSL
{
    public class UnifiedServerCoordinator
    {
        private unsafe class PendingConnection(int index)
        {
            public readonly int Index = index;
            public Player player = new() { whoAmI = index };
            public byte[] readBuffer = new byte[1024 * 8];
            public int totalData = 0;
            public string? ReceivedUUID = "";
            public void Reset(ISocket socket) {
                player.active = false;
                preAuthDeferredFrames[Index]?.Clear();   // F1：换会话时清空预认证暂存帧
                players[Index] = player;

                RemoteClient client = globalClients[Index];
                client.ClientUUID = ReceivedUUID = null;
                client.Name = player.name = string.Empty;

                totalData = 0;
                client.Data.Clear();
                client.TimeOutTimer = 0;
                client.State = 0;
                client._isReading = false;
                client.PendingTermination = false;
                client.PendingTerminationApproved = false;
                client.SpamClear();
                client.IsActive = false;

                client.Socket?.Close();
                client.Socket = socket;
            }
            public void Update(RemoteClient client) {
                if (!client.IsActive) {
                    client.State = 0;
                    client.IsActive = true;
                }

                if (client._isReading) {
                    return;
                }

                try {
                    if (client.Socket.IsDataAvailable()) {
                        client._isReading = true;
                        client.Socket.AsyncReceive(client.ReadBuffer, 0, client.ReadBuffer.Length, delegate (object state, int length) {
                            if (length == 0) {
                                client.PendingTermination = true;
                            }
                            else {
                                try {
                                    Buffer.BlockCopy(client.ReadBuffer, 0, readBuffer, totalData, length);
                                    totalData += length;
                                    ProcessBytes(client);
                                }
                                catch {
                                    client.PendingTermination = true;
                                }
                            }

                            client._isReading = false;
                        });
                    }
                }
                catch {
                    client.PendingTermination = true;
                }
            }
            private void ProcessBytes(RemoteClient client) {

                if (client.PendingTermination) {
                    client.PendingTerminationApproved = true;
                    return;
                }
                int readPosition = 0;
                int unreadLength = totalData;
                try {
                    while (unreadLength >= 2) {
                        int packetLength = BitConverter.ToUInt16(readBuffer, readPosition);

                        if (packetLength < 3 || packetLength > 1000) {
                            client.PendingTermination = true;
                            return;
                        }

                        if (unreadLength >= packetLength && packetLength != 0) {

                            CountReceivedData(client.Id, (uint)packetLength, null);

                            ProcessPacket(readPosition + 2, packetLength - 2);

                            if (client.PendingTermination) {
                                client.PendingTerminationApproved = true;
                                break;
                            }

                            unreadLength -= packetLength;
                            readPosition += packetLength;

                            ServerContext? currentServer = GetClientCurrentlyServer(Index);
                            if (currentServer != null) {
                                MessageBuffer buffer = globalMsgBuffers[Index];
                                int f1Write = 0;
                                // 15-UTSL 2026-10-16（F1）：路由完成时，先把预认证期暂存的握手生命周期帧（6/8/12）注入服务器管道（等效"晚到"，解注入帧序竞态）
                                List<byte[]>? f1Stash = preAuthDeferredFrames[Index];
                                if (f1Stash is not null && f1Stash.Count > 0) {
                                    int injected = 0; int injectedBytes = 0;
                                    foreach (byte[] frame in f1Stash) {
                                        if (f1Write + frame.Length + unreadLength > buffer.readBuffer.Length) { break; }
                                        Array.Copy(frame, 0, buffer.readBuffer, f1Write, frame.Length);
                                        f1Write += frame.Length; injected++; injectedBytes += frame.Length;
                                    }
                                    Logger.Info(category: "PendingConnection", message: $"[D1-DBG] F1 暂存帧注入（路由后）：{injected}/{f1Stash.Count} 枚 {injectedBytes} B" + (injected < f1Stash.Count ? "（容量不足，其余弃投）" : ""));
                                    f1Stash.Clear();
                                }
                                // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                                // Then update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                                for (int i = 0; i < unreadLength; i++) {
                                    buffer.readBuffer[f1Write + i] = readBuffer[readPosition + i];
                                }
                                buffer.totalData = f1Write + unreadLength;
                                // Make sure remaining data will be processed in the next round on the next server
                                buffer.checkBytes = true;
                                return;
                            }

                            continue;
                        }
                        break;
                    }
                }
                catch (Exception exception) {
                    if (readPosition < readBuffer.Length - 100) {
                        Console.WriteLine(Language.GetTextValue("Error.NetMessageError", readBuffer[readPosition + 2]));
                    }
                    unreadLength = 0;
                    readPosition = 0;

                    Logger.LogHandledException(
                        category: "PendingConnection",
                        message: Language.GetTextValue("Error.NetMessageError", readBuffer[readPosition + 2]),
                        ex: exception);
                }
                if (unreadLength != totalData) {
                    for (int i = 0; i < unreadLength; i++) {
                        readBuffer[i] = readBuffer[i + readPosition];
                    }
                    totalData = unreadLength;
                }
            }

            private void ProcessPacket(int packetStart, int contentLength) {
                LocalClientSender sender = clientSenders[Index];
                RemoteClient client = globalClients[Index];
                MessageID type = (MessageID)readBuffer[packetStart];
                fixed (byte* buffer = readBuffer) {
                    void* readPtr = buffer + packetStart + 1;
                    void* endPtr = buffer + packetStart + contentLength;
                    switch (type) {
                        case MessageID.ClientHello: {
                                ClientHello msg = new(ref readPtr, endPtr);
                                UnifierApi.EventHub.Netplay.ConnectEvent.Invoke(new Connect(client, msg.Version), out bool handled);
                                if (handled) {
                                    return;
                                }
                                string expectedVersion = "Terraria" + Main.curRelease;
                                var checkCanJoin = new CheckVersion(client, msg.Version, msg.Version == expectedVersion);
                                UnifierApi.EventHub.Coordinator.CheckVersion.Invoke(ref checkCanJoin);
                                if (msg.Version != expectedVersion) {
                                    string remoteAddress = client.Socket?.GetRemoteAddress().ToString() ?? "<unknown>";
                                    if (checkCanJoin.CanJoin) {
                                        Logger.Info(
                                            category: "PendingConnection",
                                            message: GetParticularString("{0} is player IP address, {1} is client version string, {2} is server version string",
                                                $"Version mismatch from {remoteAddress}: client='{msg.Version}', server='{expectedVersion}'. Cross-version join allowed by policy."));
                                    }
                                    else {
                                        Logger.Warning(
                                            category: "PendingConnection",
                                            message: GetParticularString("{0} is player IP address, {1} is client version string, {2} is server version string",
                                                $"Version mismatch from {remoteAddress}: client='{msg.Version}', server='{expectedVersion}'. Connection rejected."));
                                    }
                                }
                                UnifierApi.EventHub.Coordinator.CheckVersion.Invoke(ref checkCanJoin);
                                if (!checkCanJoin.CanJoin) {
                                    sender.Kick(Lang.mp[4].ToNetworkText());
                                    break;
                                }
                                if (!string.IsNullOrEmpty(ServerPassword)) {
                                    client.State = -1;
                                    sender.SendFixedPacket(new RequestPassword());
                                    break;
                                }
                                client.State = 1;
                                sender.SendFixedPacket(new LoadPlayer((byte)Index, false));
                                break;
                            }
                        case MessageID.SendPassword: {
                                SendPassword msg = new(ref readPtr, endPtr);
                                if (msg.Password == ServerPassword) {
                                    client.State = 1;
                                    sender.SendFixedPacket(new LoadPlayer((byte)Index, false));
                                }
                                else {
                                    sender.Kick(Lang.mp[1].ToNetworkText());
                                }
                                break;
                            }
                        case MessageID.SyncPlayer: {
                                SyncPlayer msg = new(ref readPtr, endPtr);
                                player.ApplySyncPlayerPacket(msg);
                                client.Name = msg.Name;
                                if (!string.IsNullOrEmpty(msg.Name) && !string.IsNullOrEmpty(client.ClientUUID)) {
                                    uuidToNameCache[client.ClientUUID] = msg.Name;
                                }
                                break;
                            }
                        case MessageID.ClientUUID: {
                                ClientUUID msg = new(ref readPtr, endPtr);
                                var uuid = msg.UUID.Trim();

                                if (!Guid.TryParse(uuid, out _)) {
                                    sender.Kick(NetworkText.FromLiteral(GetString("Invalid client UUID.")));
                                    Logger.Warning(
                                        category: "PendingConnection",
                                        message: GetParticularString("{0} is player name, {1} is player IP address, {2} is invalid raw UUID string", $"'{client.Name}' ({client.Socket.GetRemoteAddress()}) sent invalid UUID '{uuid}' before authentication. Kicked."));
                                    return;
                                }

                                client.ClientUUID = ReceivedUUID = Crypto.Sha512Hex(uuid, Encoding.ASCII);
                                if (!string.IsNullOrEmpty(client.Name)) {
                                    uuidToNameCache[client.ClientUUID] = client.Name;
                                }
                                else if (uuidToNameCache.TryGetValue(client.ClientUUID, out string? cachedName)) {
                                    if (string.IsNullOrEmpty(player.name)) { player.name = cachedName; }
                                    client.Name = cachedName;
                                    Logger.Info(category: "PendingConnection", message: $"[PendingConnection] 回程会话按 UUID 补全名字：{cachedName}（{client.ClientUUID.Substring(0, 8)}…）");
                                }

                                UnifierApi.EventHub.Netplay.ReceiveFullClientInfoEvent.Invoke(new(client, player, sender), out bool h);
                                if (h) {
                                    if (!client.PendingTermination || !client.PendingTerminationApproved) {
                                        sender.Kick(NetworkText.FromLiteral(GetString("You are not allowed to join this server.")));
                                    }
                                    return;
                                }

                                SwitchJoinServerEvent e = new(player, client, [.. Servers.Where(s => s.IsRunning && s.CheckPlayerCanJoinIn(player, client, out _))]);
                                UnifierApi.EventHub.Coordinator.SwitchJoinServer.Invoke(ref e);
                                ServerContext? joinServer = e.JoinServer;

                                if (joinServer is null) {
                                    sender.Kick(NetworkText.FromLiteral(GetString($"No available server found for you.")));

                                    Logger.Warning(
                                        category: "PendingConnection",
                                        message: GetParticularString("{0} is player name, {1} is player UUID", $"No available server found for player '{player.name}' ({client.ClientUUID}); connection aborted."));
                                }
                                else {
                                    SyncPlayer playerData = player.CreateSyncPacket(Index);
                                    Player serverPlayer = players[Index] = joinServer.Main.player[Index];
                                    serverPlayer.ApplySyncPlayerPacket(in playerData, false);
                                    globalClients[Index].ResetSections(joinServer);

                                    SetClientCurrentlyServer(Index, joinServer);
                                    // Player.Spawn activates the slot after WorldData and tile-data synchronization.

                                    UnifierApi.EventHub.Coordinator.JoinServer.Invoke(new(joinServer, player.whoAmI));

                                    UnifierApi.UpdateTitle();

                                    Logger.Info(
                                        category: "PendingConnection",
                                        message: GetParticularString("{0} is player name, {1} is player UUID, {2} is server name", $"Player '{player.name}' ({client.ClientUUID}) routed to server '{joinServer.Name}'."));
                                }

                                break;
                            }
                        default: {
                                // 预认证阶段收到未知/意外包。
                                //   严格模式（旧行为）：直接踢。问题是【经维度代理进入】的客户端会在地图流程里
                                //   插入自己的握手包（实测为 Placeholder），导致玩家"第一次进服必被踢一次、要重试"。
                                //   默认模式（新行为）：忽略该包并记录（按频率限流），连接继续等待真正的登录包。
                                // 恢复旧行为：环境变量 UTSL_STRICT_PREAUTH_PACKETS=1
                                if (StrictPreAuthPackets) {
                                    sender.Kick(Lang.mp[2].ToNetworkText());

                                    Logger.Warning(
                                        category: "PendingConnection",
                                        message: GetParticularString("{0} is player name, {1} is player IP address, {2} is packet type number", $"'{client.Name}' ({client.Socket.GetRemoteAddress()}) sent invalid packet {type} before name/UUID authentication. Kicked."));
                                }
                                else {
                                    // 15-UTSL 2026-10-16（F2 明细）：忽略日志补 type#/len/head；6/8/12=生命周期包→永不限流+★LIFECYCLE
                                    NoteIgnoredPreAuthPacket(client, type, contentLength + 2, readPtr);
                                    // 15-UTSL 2026-10-16（F1）：早到握手生命周期包缓存，路由完成后注入（等效"晚到"，解注入帧序竞态）
                                    if (type is MessageID.RequestWorldInfo or MessageID.RequestTileData or MessageID.SpawnPlayer) {
                                        StashPreAuthFrame(Index, contentLength + 2, readBuffer, packetStart - 2);
                                    }
                                    // 15-UTSL 2026-10-15（IP 显示线 D1）：观察级放行——把预认证自定义包（经维度推送的 67 号真实地址等）
                                    // 派发给显式订阅 PreAuthPacket 的插件；不改动「不进入原版/TShock 处理」的语义。开关：UTSL_PREAUTH_OBSERVE_PASS=0 关闭。
                                    if (PreAuthObservePass) {
                                        try {
                                            PreAuthPacketEvent preAuthEvt = new(Index, buffer + packetStart, buffer + packetStart + contentLength);
                                            Logger.Info(category: "PendingConnection", message: $"[D1-DBG] PreAuthPacket 派发 type={type} len={contentLength}");
                                            UnifierApi.EventHub.Netplay.PreAuthPacket.Invoke(in preAuthEvt, out _);
                                        }
                                        catch (Exception exD1Dbg) { try { Logger.Warning(category: "PendingConnection", message: $"[D1-DBG] PreAuthPacket 派发异常: " + exD1Dbg.GetType().Name + " / " + exD1Dbg.Message); } catch { } }
                                    }
                                }
                                break;
                            }
                    }
                }
            }
        }
        private class LoggerHost : ILoggerHost
        {
            public string Name => "UnifierTSL";
            public string? CurrentLogCategory { get; set; }
        }
        private static readonly ILoggerHost LogHost = new LoggerHost();
        private static readonly RoleLogger logger;
        public static RoleLogger Logger => logger;

        /// <summary>
        /// 预认证阶段收到未知包时是否"严格踢人"。
        /// 默认 false = 忽略并记录 —— 经维度代理进入的客户端会在地图流程里插包（实测 Placeholder），
        /// 严格模式会让玩家每轮首次进服都被踢一次、必须重试。
        /// 环境变量 UTSL_STRICT_PREAUTH_PACKETS=1 / true 可恢复旧的严格行为。
        /// </summary>
        public static bool StrictPreAuthPackets { get; set; } = IsStrictPreAuthRequested();

        private static bool IsStrictPreAuthRequested() {
            string? raw = Environment.GetEnvironmentVariable("UTSL_STRICT_PREAUTH_PACKETS");
            return !string.IsNullOrWhiteSpace(raw)
                && (raw.Trim() == "1" || raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 预认证阶段收到未知/自定义包时，是否做「观察级放行」——派发给显式订阅 PreAuthPacket 的插件（15-UTSL IP 显示线 D1）。
        /// 默认 true（本机为维度代理链路服务）；UTSL_PREAUTH_OBSERVE_PASS=0 / false 可关闭（回到纯丢弃）。
        /// </summary>
        public static bool PreAuthObservePass { get; set; } = IsPreAuthObservePassRequested();

        private static bool IsPreAuthObservePassRequested() {
            string? raw = Environment.GetEnvironmentVariable("UTSL_PREAUTH_OBSERVE_PASS");
            return !(raw is not null && (raw.Trim() == "0" || raw.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)));
        }

        private static int ignoredPreAuthPacketCount;
        private static long lastIgnoredPreAuthLogTick;

        /// <summary>忽略并记录（首次 + 之后每 60 秒最多一条，避免被刷屏；F2：6/8/12 生命周期包永不限流并带明细）。</summary>
        private static unsafe void NoteIgnoredPreAuthPacket(RemoteClient client, MessageID type, int len, void* payload) {
            int count = Interlocked.Increment(ref ignoredPreAuthPacketCount);
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref lastIgnoredPreAuthLogTick);
            bool lifecycle = type is MessageID.RequestWorldInfo or MessageID.RequestTileData or MessageID.SpawnPlayer;
            if (count != 1 && now - last < 60_000 && !lifecycle) {
                return;
            }

            Interlocked.Exchange(ref lastIgnoredPreAuthLogTick, now);
            string remoteAddress;
            try { remoteAddress = client.Socket?.GetRemoteAddress().ToString() ?? "<unknown>"; }
            catch { remoteAddress = "<unknown>"; }

            Logger.Warning(
                category: "PendingConnection",
                message: GetParticularString("{0} is player IP address, {1} is packet type, {2} is ignored packet count",
                    $"'{client.Name}' ({remoteAddress}) sent packet {type}(#{(int)type}) len={len}{HeadHex(payload, 8)} before name/UUID authentication. Ignored and logged (total ignored: {count}){(lifecycle ? " ★LIFECYCLE" : "")}; set UTSL_STRICT_PREAUTH_PACKETS=1 to restore the old kick behavior."));
        }

        /// <summary>F2：指针头十六进制（≤8B），仅日志用。</summary>
        private static unsafe string HeadHex(void* p, int n) {
            try {
                if (p == null || n <= 0) return "";
                var sb = new StringBuilder(" head=");
                byte* bp = (byte*)p;
                for (int i = 0; i < n; i++) { if (i > 0) sb.Append('-'); sb.Append(bp[i].ToString("X2")); }
                return sb.ToString();
            } catch { return ""; }
        }

        /// <summary>F1：暂存预认证期早到的握手生命周期帧（6=RequestWorldInfo/8=RequestTileData/12=SpawnPlayer），待路由完成后经 globalMsgBuffers 注入（等效"晚到"）。</summary>
        private static void StashPreAuthFrame(int index, int frameLen, byte[] source, int frameStart) {
            try {
                if (index < 0 || index >= Netplay.MaxConnections) return;
                if (frameLen < 3 || frameLen > 1024 || frameStart < 0 || frameStart + frameLen > source.Length) return;
                List<byte[]> list = preAuthDeferredFrames[index] ??= new List<byte[]>();
                int total = 0; foreach (byte[] f in list) total += f.Length;
                if (list.Count >= 8 || total + frameLen > 512) {
                    Logger.Warning(category: "PendingConnection", message: $"[D1-DBG] F1 暂存溢出（{list.Count} 枚/{total} B），本帧弃投 len={frameLen}");
                    return;
                }
                byte[] copy = new byte[frameLen];
                Array.Copy(source, frameStart, copy, 0, frameLen);
                list.Add(copy);
                Logger.Info(category: "PendingConnection", message: $"[D1-DBG] F1 暂存早到包（待路由后注入）：type={(MessageID)source[frameStart + 2]} len={frameLen} 累计={list.Count}");
            } catch { }
        }
        private static readonly ListenerController listenerController = new(7777);
        public static int ListenPort => listenerController.ListenPort;
        public static string ServerPassword { get; set; } = "";

        public static readonly Player[] players = new Player[Netplay.MaxConnections];
        public static readonly RemoteClient[] globalClients = new RemoteClient[Netplay.MaxConnections];
        public static readonly LocalClientSender[] clientSenders = new LocalClientSender[Netplay.MaxConnections];
        public static readonly MessageBuffer[] globalMsgBuffers = new MessageBuffer[Netplay.MaxConnections + 1];
        // 15-UTSL 2026-10-16（F1）：预认证期早到握手包暂存（6/8/12），路由完成后注入服务器管道
        private static readonly List<byte[]>?[] preAuthDeferredFrames = new List<byte[]>[Netplay.MaxConnections];
        private static readonly ServerContext?[] clientCurrentlyServers = new ServerContext?[Netplay.MaxConnections];
        private static readonly PendingConnection[] pendingConnects = new PendingConnection[Netplay.MaxConnections];
        // 15-UTSL：UUID→名字缓存（回程/快速转移会话在名字帧到达前路由时，用缓存补全名字；修复空名→guest 与 WSR 信号失败）
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> uuidToNameCache = new(StringComparer.Ordinal);

        private static ImmutableArray<ServerContext> servers = [];
        public static ImmutableArray<ServerContext> Servers => servers;
        public static void AddServer(ServerContext server) {
            if (ImmutableInterlocked.Update(ref servers, arr => arr.Contains(server) ? arr : arr.Add(server))) {
                UnifierApi.EventHub.Server.AddServer.Invoke(new(server));
                UnifierApi.EventHub.Server.ServerListChanged.Invoke(new());
            }
        }
        public static void RemoveServer(ServerContext server) {
            // 先把仍绑定在这个世界上的连接解绑。否则它们的 socket 还连着、GetClientCurrentlyServer 仍返回
            // 这个已被移除的世界，但该世界的线程已死、Dispatcher 已释放 —— 连接会永久"假死"
            //（命令与聊天全部失效），并且由于 from.Dispatcher.CheckAccess() 恒为 false 而无法再被转移出去。
            for (int i = 0; i < globalClients.Length; i++) {
                if (!ReferenceEquals(GetClientCurrentlyServer(i), server)) {
                    continue;
                }
                if (globalClients[i] is RemoteClient client) {
                    // 与 UpdateConnectedClients 里的 client.Update(server) 使用同一把锁，避免并发改状态
                    lock (client) {
                        SetClientCurrentlyServer(i, null);
                        client.PendingTermination = true;
                        client.PendingTerminationApproved = true;
                    }
                } else {
                    SetClientCurrentlyServer(i, null);
                }
                players[i] = new();
            }
            if (ImmutableInterlocked.Update(ref servers, arr => arr.Remove(server))) {
                UnifierApi.EventHub.Server.RemoveServer.Invoke(new(server));
                UnifierApi.EventHub.Server.ServerListChanged.Invoke(new());
            }
        }
        public static ServerContext? GetClientCurrentlyServer(int clientIndex)
            => Volatile.Read(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(clientCurrentlyServers), clientIndex));
        private static void SetClientCurrentlyServer(int clientIndex, ServerContext? server)
            => Volatile.Write(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(clientCurrentlyServers), clientIndex), server);
        public static Player GetPlayer(int clientIndex) => players[clientIndex];
        //{
        //    var server = GetClientCurrentlyServer(clientIndex);
        //    return server is null ? pendingConnects[clientIndex].player : players[clientIndex];
        //}

        private static readonly UdpClient broadcastClient;

        public static event Action? Started;

        public static IPEndPoint ListeningEndpoint => listenerController.ListeningEndpoint;

        public static void Load() { }
        static UnifiedServerCoordinator() {
            logger = UnifierApi.CreateLogger(LogHost);
            On.Terraria.NetMessageSystemContext.CheckBytes += ProcessBytes;
            On.Terraria.NetplaySystemContext.UpdateServerInMainThread += UpdateServerInMainThread;
            On.Terraria.NetMessageSystemContext.CheckCanSend += NetMessageSystemContext_CheckCanSend;

            for (int i = 0; i < Netplay.MaxConnections; i++) {
                globalClients[i] = new RemoteClient() {
                    Id = i,
                    ReadBuffer = new byte[1024]
                };
                pendingConnects[i] = new(i);
                clientSenders[i] = new(i);
            }
            for (int i = 0; i < globalMsgBuffers.Length; i++) {
                globalMsgBuffers[i] = new MessageBuffer() {
                    whoAmI = i
                };
            }
            broadcastClient = new UdpClient();
        }
        private static Thread? ServerLoopThread;
        private static int serverLoopStartToken;

        public static void Launch(int listenPort, string password = "") {
            ServerPassword = password;
            broadcastClient.EnableBroadcast = true;
            if (!listenerController.TryRebindPort(listenPort, hasListeningWork: false, out ListenerChange change)) {
                Logger.Warning(
                    category: "Listener",
                    message: GetParticularString("{0} is requested listen port", $"Cannot initialize listener to invalid port '{listenPort}'."));
            }
            else {
                ApplySessionChange(change);
            }

            EnsureServerLoopStarted();
            UnifierApi.MarkRunning();
        }

        public static bool RebindListener(int listenPort) {
            if (!LauncherPortRules.IsValidListenPort(listenPort)) {
                Logger.Warning(
                    category: "Listener",
                    message: GetParticularString("{0} is requested listen port", $"Cannot rebind listener to invalid port '{listenPort}'."));
                return false;
            }

            if (listenPort == ListenPort) {
                return true;
            }

            if (!listenerController.TryRebindPort(listenPort, HasListeningWork(), out ListenerChange change)) {
                Logger.Warning(
                    category: "Listener",
                    message: GetParticularString("{0} is requested listen port", $"Failed to bind listener to port '{listenPort}'. Keeping the current listener."));
                return false;
            }

            ApplySessionChange(change);

            Logger.Info(
                category: "Listener",
                message: GetParticularString("{0} is requested listen port", $"Listener rebound to port '{listenPort}'."));
            return true;
        }

        private static void EnsureServerLoopStarted() {
            if (Interlocked.CompareExchange(ref serverLoopStartToken, 1, 0) != 0) {
                return;
            }

            try {
                Thread thread = new(ServerLoop) {
                    IsBackground = true,
                    Name = "Server Thread"
                };
                ServerLoopThread = thread;
                thread.Start();
            }
            catch (Exception ex) {
                Volatile.Write(ref serverLoopStartToken, 0);
                Logger.Error(
                    category: "Coordinator",
                    message: GetString("Failed to start server loop thread."),
                    ex: ex);
                throw;
            }
        }

        private static void ApplySessionChange(ListenerChange change) {
            if (!change.Success) {
                return;
            }

            if (change.Started is not null) {
                ListenerSession session = change.Started;
                Task.Run(() => ListenLoop(session));
                Task.Run(() => LaunchBroadcast(session));
            }

            if (change.Stopped is not null) {
                ReleaseSession(change.Stopped);
            }

            if (change.PortChanged) {
                UnifierApi.UpdateTitle(empty: ActiveConnections == 0);
            }
        }

        private static void ReleaseSession(ListenerSession session) {
            try {
                session.Cts.Cancel();
            }
            catch (Exception ex) {
                Logger.LogHandledException(
                    category: "Listener",
                    message: GetString("Failed to cancel listener session token."),
                    ex: ex);
            }
            try {
                session.Listener.Stop();
            }
            catch (Exception ex) {
                Logger.LogHandledException(
                    category: "Listener",
                    message: GetString("Failed to stop listener session."),
                    ex: ex);
            }
            try {
                session.Listener.Dispose();
            }
            catch (Exception ex) {
                Logger.LogHandledException(
                    category: "Listener",
                    message: GetString("Failed to dispose listener session."),
                    ex: ex);
            }
            try {
                session.Cts.Dispose();
            }
            catch (Exception ex) {
                Logger.LogHandledException(
                    category: "Listener",
                    message: GetString("Failed to dispose listener session token source."),
                    ex: ex);
            }
        }

        private static bool NetMessageSystemContext_CheckCanSend(On.Terraria.NetMessageSystemContext.orig_CheckCanSend orig, NetMessageSystemContext self, int clientIndex) {
            LocalClientSender? sender = clientSenders[clientIndex];
            if (sender is null) {
                return false;
            }

            if (!Monitor.TryEnter(sender.RoutePublicationGate)) {
                // 闸门被别的线程占着 ⇒ 这个出站包会被【静默丢弃】（返回 false）。
                // 15-UTSL 20261015：正是靠这个判据抓到"在 lock(RoutePublicationGate) 里 Post 到别的线程再 Wait"那个坑
                //（目标线程发送 → TryEnter 必败 → 7/10/12 全丢）。留计数+限流日志，便于以后一眼看见。
                int gateFails = Interlocked.Increment(ref checkCanSendGateFailCount);
                long nowTicks = Environment.TickCount64;
                if (gateFails == 1 || nowTicks - Interlocked.Read(ref lastGateFailLogTick) >= 30_000) {
                    Interlocked.Exchange(ref lastGateFailLogTick, nowTicks);
                    Logger.Warning(
                        category: "PlayerTransfer",
                        message: $"[CheckCanSend] RoutePublicationGate 被占用，已静默丢弃一个出站包（累计 {gateFails}）。若出现在转移窗口内，说明有人在持锁期间等待其它线程完成发送。");
                }
                return false;
            }
            try {
                return GetClientCurrentlyServer(clientIndex) == self.root && globalClients[clientIndex].IsConnected();
            }
            finally {
                Monitor.Exit(sender.RoutePublicationGate);
            }
        }

        private static void UpdateServerInMainThread(On.Terraria.NetplaySystemContext.orig_UpdateServerInMainThread orig, NetplaySystemContext self) {
            UnifiedServerProcess.RootContext server = self.root;
            for (int i = 0; i < Netplay.MaxConnections; i++) {
                if (server != GetClientCurrentlyServer(i)) {
                    continue;
                }
                server.NetMessage.CheckBytes(i);
            }
        }

        static void CountReceivedData(int who, uint size, ServerContext? server) {
            ServerPerformance.Network.ReceivedPacket();
            ServerPerformance.Network.ReceivedBytes(size);
            if (server?.Performance is { } perf) {
                perf.CurrentFrameData.ReceivedPacketCount += 1;
                perf.CurrentFrameData.ReceivedBytesCount += size;
            }
            var client = clientSenders[who];
            client.ReceivedBytesCount += size;
            client.ReceivedPacketCount += 1;
        }

        private static void ProcessBytes(On.Terraria.NetMessageSystemContext.orig_CheckBytes orig, NetMessageSystemContext netMsg, int clientIndex) {
            ServerContext server = netMsg.root.ToServer();
            MessageBuffer buffer = globalMsgBuffers[clientIndex];
            lock (buffer) {
                if (GetClientCurrentlyServer(clientIndex) != server) {
                    return;
                }
                if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                    server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                    buffer.checkBytes = false;
                    return;
                }
                int readPosition = 0;
                int unreadLength = buffer.totalData;
                try {
                    while (unreadLength >= 2) {
                        int packetLength = BitConverter.ToUInt16(buffer.readBuffer, readPosition);

                        if (packetLength < 3 || packetLength > 1000) {
                            server.Netplay.Clients[clientIndex].PendingTermination = true;
                            return;
                        }

                        if (unreadLength >= packetLength && packetLength != 0) {
                            CountReceivedData(clientIndex, (uint)packetLength, server);

                            try {
                                NetPacketHandler.ProcessBytes(server, buffer, readPosition + 2, packetLength - 2);
                            }
                            catch (Exception exception) {
                                netMsg.LogMessageError(clientIndex, buffer.readBuffer[readPosition + 2].ToString(), exception);
                            }

                            if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                                server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                                buffer.checkBytes = false;
                                break;
                            }

                            unreadLength -= packetLength;
                            readPosition += packetLength;

                            if (GetClientCurrentlyServer(clientIndex) != server) {
                                // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                                // Then update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                                for (int i = 0; i < unreadLength; i++) {
                                    buffer.readBuffer[i] = buffer.readBuffer[readPosition + i];
                                }
                                buffer.totalData = unreadLength;
                                // Make sure remaining data will be processed in the next round on the next server
                                buffer.checkBytes = true;
                                return;
                            }

                            continue;
                        }
                        break;
                    }
                }
                catch (Exception exception) {
                    netMsg.LogMessageError(clientIndex, "?", exception);
                    unreadLength = 0;
                    readPosition = 0;
                }
                if (unreadLength != buffer.totalData) {
                    for (int i = 0; i < unreadLength; i++) {
                        buffer.readBuffer[i] = buffer.readBuffer[i + readPosition];
                    }
                    buffer.totalData = unreadLength;
                }
                buffer.checkBytes = false;
            }
        }


        private static void ServerLoop() {
            int sleepStep = 0;
            Started?.Invoke();
            while (true) {
                try {
                    StartListeningIfNeeded();
                    UpdateConnectedClients();
                    sleepStep = (sleepStep + 1) % 10;
                    Thread.Sleep(sleepStep == 0 ? 1 : 0);
                }
                finally {

                }
            }
        }
        public static bool AnyClientsConnected => ActiveConnections > 0;
        public static int ActiveConnections { get; private set; }
        private static void UpdateConnectedClients() {
            try {
                int activeConnections = 0;

                foreach (ServerContext server in servers) {
                    if (server.IsRunning) {
                        server.routedConnectionCountAccumulator = 0;
                        server.playerCountAccumulator = 0;
                    }
                }

                for (int i = 0; i < Netplay.MaxConnections; i++) {
                    RemoteClient client = globalClients[i];
                    ServerContext? server = GetClientCurrentlyServer(i);
                    if (server is not null) {
                        if (client.PendingTermination) {
                            if (client.PendingTerminationApproved) {
                                // 先取出真正的玩家对象与其 active：client.Reset 会把 Main.player[i] 换成 new Player()，
                                // 之后读到的永远是 false —— 原对象永不下线、PlayerDisconnect 也永不触发。
                                Player boundPlayer = server.Main.player[i];
                                bool wasActive = boundPlayer is not null && boundPlayer.active;

                                client.Reset(server);
                                server.NetMessage.SyncDisconnectedPlayer(i);

                                if (wasActive) {
                                    server.Player.Hooks.PlayerDisconnect(i);
                                }
                                if (boundPlayer is not null) {
                                    boundPlayer.active = false;
                                }
                                server.Main.player[i].active = false;

                                client.Socket = null;
                                // 解绑路由：否则该索引被新连接复用时，会指向这个已空掉的世界。
                                SetClientCurrentlyServer(i, null);
                            }
                            continue;
                        }
                        if (client.IsConnected()) {
                            activeConnections += 1;
                            lock (client) {
                                client.Update(server);
                                server.routedConnectionCountAccumulator += 1;
                                if (client.State == 10 && server.Main.player[i].active) {
                                    server.playerCountAccumulator += 1;
                                }
                            }
                            continue;
                        }
                    }
                    else {
                        if (client.PendingTermination) {
                            if (client.PendingTerminationApproved) {
                                ResetUnboundClientState(client);
                            }
                        }
                        if (client.IsConnected()) {
                            activeConnections += 1;
                            lock (client) {
                                pendingConnects[i].Update(client);
                            }
                            continue;
                        }
                    }
                    if (client.IsActive) {
                        client.PendingTermination = true;
                        client.PendingTerminationApproved = true;
                        continue;
                    }
                }

                foreach (ServerContext server in servers) {
                    if (server.IsRunning) {
                        server.hasRoutedConnections = server.routedConnectionCountAccumulator > 0;
                        server.Netplay.HasFullyConnectedClients = server.playerCountAccumulator > 0;
                        server.ActivePlayerCount = server.playerCountAccumulator;
                    }
                }

                if (AnyClientsConnected && activeConnections == 0) {
                    UnifierApi.EventHub.Coordinator.LastPlayerLeftEvent.Invoke(default);
                }
                if (ActiveConnections != activeConnections) {
                    ActiveConnections = activeConnections;
                    UnifierApi.UpdateTitle(empty: activeConnections == 0);
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        // Used only for pre-routing cleanup when the client is not bound to any server context.
        // Routed clients must use the standard server-bound reset path instead.
        static void ResetUnboundClientState(RemoteClient client) {
            client.Data.Clear();

            client.ClientUUID = null;

            client.TimeOutTimer = 0;
            client.State = 0;
            client._isReading = false;
            client.PendingTermination = false;
            client.PendingTerminationApproved = false;
            client.SpamClear();
            client.IsActive = false;

            var plr = players[client.Id] = new();
            plr.active = false;

            globalMsgBuffers[client.Id].Reset();
            client.Socket?.Close();
        }

        public static int GetClientSpace() {
            int space = Main.maxPlayers;
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (globalClients[i].IsActive) {
                    space -= 1;
                }
            }
            return space;
        }
        public static int GetActiveClientCount() {
            int count = 0;
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (globalClients[i].IsActive) {
                    count += 1;
                }
            }
            return count;
        }

        /// <summary>
        /// 转移时是否把"目标世界上线段"投递到目标世界线程执行（B-1 修复）。
        /// 默认 true；环境变量 UTSL_TRANSFER_ONLINE_ON_TARGET_THREAD=0 回退旧行为（源线程内联）。
        /// </summary>
        public static bool TransferOnlineOnTargetThread { get; set; } =
            !string.Equals(Environment.GetEnvironmentVariable("UTSL_TRANSFER_ONLINE_ON_TARGET_THREAD"), "0", StringComparison.Ordinal);
        private static string TryGetCtxClientActive(ServerContext to, int plr) {
            try {
                RemoteClient? c = to.Netplay.Clients[plr];
                return c is null ? "null" : (c.IsActive ? "true" : "false");
            }
            catch { return "err"; }
        }
        private static int checkCanSendGateFailCount;
        private static long lastGateFailLogTick;
        private static void StartListeningIfNeeded() {
            if (!listenerController.TryEnsureState(HasListeningWork(), out ListenerChange change)) {
                return;
            }

            ApplySessionChange(change);
        }
        private static void ListenLoop(ListenerSession session) {
            CancellationToken token = session.Cts.Token;
            while (!token.IsCancellationRequested && listenerController.IsCurrentSession(session.Generation)) {
                TcpClient? client = null;
                try {
                    client = session.Listener.AcceptTcpClient();
                }
                catch {
                    if (token.IsCancellationRequested || !listenerController.IsCurrentSession(session.Generation)) {
                        break;
                    }
                }
                if (client is null) {
                    continue;
                }
                try {
                    CreateSocketEvent e = new(client);
                    UnifierApi.EventHub.Coordinator.CreateSocket.Invoke(ref e);
                    OnConnectionAccepted(e.Socket ?? new TcpSocket(client), session);
                }
                catch {

                }
            }
        }
        private static void LaunchBroadcast(ListenerSession session) {
            try {
                CancellationToken token = session.Cts.Token;
                int playerCountPosInStream = 0;
                byte[] data;
                using (MemoryStream memoryStream = new()) {
                    using (BinaryWriter bw = new(memoryStream)) {
                        int value = 1010;
                        bw.Write(value);
                        bw.Write(session.Port);
                        bw.Write("UnifierTSL-Servers");
                        string text = Dns.GetHostName();
                        if (text == "localhost") {
                            text = Environment.MachineName;
                        }
                        bw.Write(text);
                        // maxTilesX
                        bw.Write(ushort.MaxValue);
                        // HasCrimson
                        bw.Write(true);
                        // GameMode
                        bw.Write(2);
                        bw.Write((byte)255);
                        playerCountPosInStream = (int)memoryStream.Position;
                        bw.Write((byte)0);
                        // hardMode
                        bw.Write(true);
                        bw.Flush();
                        data = memoryStream.ToArray();
                    }
                }
                do {
                    data[playerCountPosInStream] = (byte)GetActiveClientCount();
                    try {
                        broadcastClient.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, 8888));
                    }
                    catch {
                    }
                    Thread.Sleep(1000);
                }
                while (!token.IsCancellationRequested && listenerController.IsCurrentSession(session.Generation));
            }
            catch {
            }
        }

        private static void OnConnectionAccepted(ISocket client, ListenerSession session) {
            int id = FindNextEmptyClientSlot();
            if (id != -1) {
                clientSenders[id].ResetDataForNewClient();
                SetClientCurrentlyServer(id, null);
                pendingConnects[id].Reset(client);

                Logger.Info(
                    category: "ConnectionAccept",
                    message: GetParticularString("{0} is client IP address", $"Accepted connection: {client.GetRemoteAddress()}"));
            }
            else {
                TmpSocketSender sender = new(client);
                sender.Kick(NetworkText.FromKey("CLI.ServerIsFull"), true);

                Logger.Info(
                    category: "ConnectionAccept",
                    message: GetString("Server is full"));
            }
            if (FindNextEmptyClientSlot() == -1 && listenerController.IsCurrentSession(session.Generation)) {
                if (listenerController.TryEnsureState(HasListeningWork(), out ListenerChange change)) {
                    ApplySessionChange(change);
                }

                Logger.Info(
                    category: "ConnectionAccept",
                    message: GetString("No more slots available, stopping listener"));
            }
        }
        private static int FindNextEmptyClientSlot() {
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (globalClients[i].Socket is null) {
                    return i;
                }
            }
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (!globalClients[i].IsConnected()) {
                    return i;
                }
            }
            return -1;
        }

        private static bool HasListeningWork() {
            return servers.Any(s => s.IsRunning) && GetClientSpace() > 0;
        }

        /// <summary>
        /// 切换收尾组（broadcast→SyncConnectedPlayer→139→129→greet）——fx=0 内联分支与 fx=1 目标线程分支共用。
        /// 双分支化（B-1 收尾缺陷结构修复）：原实现只在 fx=0 分支内，置 1 会整组丢失；
        /// 抽为共用后两分支均可执行（仍由调用点按 UTSL_SWITCH_FINISH_FRAMES=1 门控）。
        /// </summary>
        private static void RunSwitchFinishGroup(ServerContext to, byte plr) {
            System.Diagnostics.Stopwatch swFin = System.Diagnostics.Stopwatch.StartNew();
            try {
                var nm = to.NetMessage;
                nm.buffer[plr].broadcast = true;
                Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 收尾组 ①broadcast=true +{swFin.ElapsedMilliseconds}ms");
                nm.SyncConnectedPlayer(plr);
                Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 收尾组 ②SyncConnectedPlayer 返回 +{swFin.ElapsedMilliseconds}ms");
                bool isHost = nm.DoesPlayerSlotCountAsAHost(plr);
                to.Main.countsAsHostForGameplay[plr] = isHost;
                if (isHost) {
                    bool h139 = nm.TrySendData((int)Terraria.ID.MessageID.SetCountsAsHostForGameplay, plr, -1, null, plr, 1f);
                    Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 收尾组 ③139(host={isHost}) TrySendData={h139} +{swFin.ElapsedMilliseconds}ms");
                }
                bool f129 = nm.TrySendData((int)Terraria.ID.MessageID.FinishedConnectingToServer, plr);
                Logger.Info(category: "PlayerTransfer", message: $"[Trace] 收尾组 ④129 FinishedConnectingToServer TrySendData={f129} +{swFin.ElapsedMilliseconds}ms");
                nm.greetPlayer(plr);
                Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 收尾组 ⑤greetPlayer 返回 +{swFin.ElapsedMilliseconds}ms");
            }
            catch (Exception exFin) { try { Logger.Error(category: "PlayerTransfer", message: "[TransferTrace] 收尾组异常", ex: exFin); } catch { } }
        }

        /// <summary>
        /// 15-UTSL 卡切兜底（staged 2026-10-16；默认关=键控）：重放上线段子集
        /// 「7 号 WorldData＋出生区分块（SyncServerOnlineToPlayer）＋12 号 PlayerSpawn」到目标世界线程；
        /// 不含 ④JoinToOthers/收尾组（避免他人侧重复提示）。仅由看门狗在"额度已尽且客户端仍连接且路由未变"时调用；
        /// 键：Watchdog.json「硬重驱」（0-2=上限，默认 0=关）。
        /// </summary>
        internal static bool HardReDriveSwitch(ServerContext to, byte plr, string reason) {
            try {
                ServerContext? cur = GetClientCurrentlyServer(plr);
                RemoteClient? g = globalClients[plr];
                if (cur is null || !ReferenceEquals(cur, to) || g is null || !g.IsConnected()) {
                    return false;
                }
                to.Dispatcher.Post(() => {
                    System.Diagnostics.Stopwatch swRD = System.Diagnostics.Stopwatch.StartNew();
                    try {
                        Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 硬重驱(7+出生区+12) 进入 plr={plr} 目标={to.Name} 依据={reason}");
                        to.SyncServerOnlineToPlayer(plr);
                        Player p = to.Main.player[plr];
                        p.Spawn(to, PlayerSpawnContext.SpawningIntoWorld);
                        to.NetMessage.SendData(Terraria.ID.MessageID.PlayerSpawn, plr, -1, null, plr, (byte)PlayerSpawnContext.SpawningIntoWorld);
                        Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 硬重驱(7+出生区+12) 完成 +{swRD.ElapsedMilliseconds}ms plr={plr} 目标={to.Name}");
                    }
                    catch (Exception exRD) {
                        try { Logger.Error(category: "PlayerTransfer", message: "[卡切] 硬重驱异常", ex: exRD); } catch { }
                    }
                });
                return true;
            }
            catch (Exception exHard) {
                try { Logger.Warning(category: "PlayerTransfer", message: "[卡切] 硬重驱投递失败: " + exHard.Message); } catch { }
                return false;
            }
        }

        public static void TransferPlayerToServer(byte plr, ServerContext to, bool ignoreChecks = false) {
            ServerContext? from = GetClientCurrentlyServer(plr);
            if (from is null || from == to || (!to.IsRunning && !ignoreChecks)) {
                return;
            }
            if (!from.Dispatcher.CheckAccess()) {
                throw new InvalidOperationException("Player transfer must be initiated on the source server update thread.");
            }
            // 必须同尺寸。跨尺寸转移【不会越界】（客户端 tile 数组按最大世界分配），但会造成
            // 客户端边界处读到新世界之外的旧图格 —— 表现为空气墙与显示错乱；
            // 1.4.5.8 加入这道检查正是为了它。想要跨尺寸，只能先做客户端侧的图格重建。
            if (from.Main.maxTilesX != to.Main.maxTilesX || from.Main.maxTilesY != to.Main.maxTilesY) {
                Logger.Warning(
                    category: "PlayerTransfer",
                    message: GetParticularString("{0} is player name, {1} is source server name, {2} is destination server name", $"Cannot transfer player '{from.Main.player[plr].name}' from {from.Name} to {to.Name}: seamless transfer requires equal world dimensions."));
                return;
            }

            UnifierApi.EventHub.Coordinator.PreServerTransfer.Invoke(new(from, to, plr), out bool handled);
            if (handled) {
                return;
            }

            MessageBuffer msgBuffer = globalMsgBuffers[plr];
            lock (msgBuffer) {
                RemoteClient client = globalClients[plr];
                if (GetClientCurrentlyServer(plr) != from || !client.IsConnected() || client.PendingTermination ||
                    client.State != 10 || !from.Main.player[plr].active ||
                    (!to.IsRunning && !ignoreChecks)) {
                    return;
                }

                Player player;
                LocalClientSender sender = clientSenders[plr];
                lock (sender.RoutePublicationGate) {
                    try {
                        from.SyncPlayerLeaveToOthers(plr);
                        from.SyncServerOfflineToPlayer(plr);

                        Player inactivePlayer = to.Main.player[plr];
                        player = players[plr] = from.Main.player[plr];
                        from.Main.player[plr] = inactivePlayer;
                        inactivePlayer.active = false;
                        to.Main.player[plr] = player;
                        player.active = false;

                        client.ResetSections(to);
                        SetClientCurrentlyServer(plr, to);

                    }
                    catch (Exception ex) {
                        Logger.Error(
                            category: "PlayerTransfer",
                            message: GetParticularString("{0} is player name, {1} is source server name, {2} is destination server name", $"Failed to transfer player '{client.Name}' from {from.Name} to {to.Name}."),
                            ex: ex);
                        // 回滚路由绑定：让后续断线清理作用在源世界（与目标世界对象已交换的事实相比，
                        // 指向一个仍然活着的世界总是更安全；kick 后客户端重连会走完整的干净路径）。
                        try { SetClientCurrentlyServer(plr, from); } catch { }
                        sender.Kick(NetworkText.FromLiteral("Server transfer failed."));
                        return;
                    }
                }
                        // ============================================================
                        // 【上线段】必须在 lock 外执行（15-UTSL 20261015 二次修复）
                        // 原因：CheckCanSend（:552-563）发送前要 Monitor.TryEnter(RoutePublicationGate)，
                        //   拿不到就 return false → 包被【静默丢弃】。若在 lock 内 Post 到目标线程再 Wait，
                        //   目标线程发送时必然 TryEnter 失败 ⇒ 7/10/12 全丢（与 14-维度 的假设一致）。
                        // 因此：lock 只包住"离场段 + 槽位交换 + 路由切换"（原子性所需），上线段在锁外执行。
                        // ============================================================
                        // ===== B-1 修复（15-UTSL）：目标世界"上线段"必须在【目标世界的线程/上下文】执行 =====
                        // 旧行为：在源世界 dispatcher 线程上内联调用 to.SyncServerOnlineToPlayer(plr) 等，
                        //   这些包（7 号 WorldData / 10 号 TileSection / 12 号 PlayerSpawn）在【源世界上下文】里
                        //   被铸造，而客户端路由指针此时已指向目标世界 → 被按世界的出站闸门丢弃。
                        //   实测（2026-10-15 14:30 抓包）：离线段 139/34×16 全到；上线段 7/10/12 全缺；
                        //   路由切换后由目标世界 tick 自然发出的 57/23/151 能到 ⇒ 客户端收不到 7 号（WorldData）
                        //   ⇒ 不清分块、不重发 8 号 ⇒ 停在旧世界 ⇒ 约 30 秒超时断线。
                        // 修法：把上线段投递到目标世界线程执行，并等它完成（保持 RoutePublicationGate 语义）。
                        // 回退开关：环境变量 UTSL_TRANSFER_ONLINE_ON_TARGET_THREAD=0
                        // ===== 15-UTSL 诊断探针（转移时打几行，定位"7/10/12 为何不上线到客户端"）=====
                        try {
                            RemoteClient g = globalClients[plr];
                            bool sameClient;
                            try { sameClient = ReferenceEquals(to.Netplay.Clients[plr], g); } catch { sameClient = false; }
                            Logger.Info(
                                category: "PlayerTransfer",
                                message: $"[TransferTrace] who={plr} to={to.Name} sameClient={sameClient} globalState={(g is null ? -99 : g.State)} globalConnected={(g is not null && g.IsConnected())} globalSocket={(g?.Socket is null ? "null" : "ok")} toPlayerActive={to.Main.player[plr]?.active} toCtxClientActive={TryGetCtxClientActive(to, plr)}");
                        }
                        catch (Exception exTrace) { try { Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 探针异常: " + exTrace.Message); } catch { } }

                        if (TransferOnlineOnTargetThread) {
                            using ManualResetEventSlim onlineDone = new(false);
                            try {
                                to.Dispatcher.Post(() => {
                                    try {
                                        to.SyncServerOnlineToPlayer(plr);
                                        player.Spawn(to, PlayerSpawnContext.SpawningIntoWorld);
                                        to.NetMessage.SendData(Terraria.ID.MessageID.PlayerSpawn, plr, -1, null, plr, (byte)PlayerSpawnContext.SpawningIntoWorld);
                                        to.SyncPlayerJoinToOthers(plr);
                                    }
                                    catch (Exception exInner) {
                                        Logger.Error(
                                            category: "PlayerTransfer",
                                            message: GetParticularString("{0} is destination server name", $"Online-sync on target thread failed for '{to.Name}'."),
                                            ex: exInner);
                                    }
                                    finally { onlineDone.Set(); }
                                });
                            }
                            catch (Exception exPost) {
                                Logger.Warning(
                                    category: "PlayerTransfer",
                                    message: GetParticularString("{0} is destination server name", $"Failed to post online-sync to target server '{to.Name}': {exPost.Message}"));
                                onlineDone.Set();
                            }
                            if (onlineDone.Wait(TimeSpan.FromSeconds(15))) {
                                // 显式探针：再补发一次 7 号 WorldData，记录返回来判断"发送路径是否可用"
                                try {
                                    bool probe = to.NetMessage.TrySendData(Terraria.ID.MessageID.WorldData, plr);
                                    Logger.Info(
                                        category: "PlayerTransfer",
                                        message: $"[TransferTrace] 显式补发 7 号(WorldData) → TrySendData={probe}（true=已投递，false=被发送层拒绝）");
                                }
                                catch (Exception exP) { try { Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 补发 7 号异常: " + exP.Message); } catch { } }

                                // 控制包：向【同一个客户端】发一个无害的 2 号 Status（定向，非广播）。
                                // 判读：控制包到、7 号不到 ⇒ 7 号被特判/发送侧按消息类型差别对待；
                                //       控制包也到不了 ⇒ 定向发送路径对该客户端整体不可用（查 clientSenders/写 socket）。
                                try {
                                    bool ctrl = to.NetMessage.TrySendData(
                                        msgType: 9,   // ← 14-维度 更正：本栈 MessageID.Kick=2、StatusText=9（原来写 2 = 会把玩家踢回主菜单）
                                        remoteClient: plr,
                                        ignoreClient: -1,
                                        text: NetworkText.FromLiteral("[TransferTrace] 诊断控制包（可忽略）"));
                                    Logger.Info(
                                        category: "PlayerTransfer",
                                        message: $"[TransferTrace] 控制包 9 号(StatusText,定向) → TrySendData={ctrl}（true=未抛异常；是否到达看代理抓包）");
                                }
                                catch (Exception exC) { try { Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 控制包异常: " + exC.Message); } catch { } }

                                // ===== 收尾组双分支化（B-1 收尾缺陷结构修复）：fx=1 目标线程分支同样执行收尾组 =====
                                if (string.Equals(Environment.GetEnvironmentVariable("UTSL_SWITCH_FINISH_FRAMES"), "1", StringComparison.Ordinal)) {
                                    RunSwitchFinishGroup(to, plr);
                                }
                            }
                            else {
                                Logger.Warning(
                                    category: "PlayerTransfer",
                                    message: GetParticularString("{0} is destination server name", $"Online-sync on target server '{to.Name}' did not finish within 15s."));
                            }
                        }
                        else {
                            // v4 分点打点：内联上线段（锁外、不 Post）逐步留痕，异常/卡死一眼定位
                            System.Diagnostics.Stopwatch swOnline = System.Diagnostics.Stopwatch.StartNew();
                            Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段(内联) 进入 t={DateTime.Now:HH:mm:ss.fff}");
                            try { to.SyncServerOnlineToPlayer(plr); Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ①SyncServerOnlineToPlayer 返回 +{swOnline.ElapsedMilliseconds}ms"); }
                            catch (Exception exS1) { Logger.Error(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ①异常 +{swOnline.ElapsedMilliseconds}ms", ex: exS1); }
                            try { player.Spawn(to, PlayerSpawnContext.SpawningIntoWorld); Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ②Spawn 返回 +{swOnline.ElapsedMilliseconds}ms"); }
                            catch (Exception exS2) { Logger.Error(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ②异常 +{swOnline.ElapsedMilliseconds}ms", ex: exS2); }
                            try { to.NetMessage.SendData(Terraria.ID.MessageID.PlayerSpawn, plr, -1, null, plr, (byte)PlayerSpawnContext.SpawningIntoWorld); Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ③SendData(PlayerSpawn) 返回 +{swOnline.ElapsedMilliseconds}ms"); }
                            catch (Exception exS3) { Logger.Error(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ③异常 +{swOnline.ElapsedMilliseconds}ms", ex: exS3); }
                            try { to.SyncPlayerJoinToOthers(plr); Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ④JoinToOthers 返回 +{swOnline.ElapsedMilliseconds}ms"); }
                            catch (Exception exS4) { Logger.Error(category: "PlayerTransfer", message: $"[TransferTrace] 上线段 ④异常 +{swOnline.ElapsedMilliseconds}ms", ex: exS4); }
                            Logger.Info(category: "PlayerTransfer", message: $"[TransferTrace] 上线段(内联) 退出 t={DateTime.Now:HH:mm:ss.fff} 总耗时={swOnline.ElapsedMilliseconds}ms");
                            // ===== 15-UTSL 切换收尾组（实验；UTSL_SWITCH_FINISH_FRAMES=1 开启，默认关）=====
                            // 双分支化：本体抽到 RunSwitchFinishGroup，fx=0/fx=1 共用。
                            if (string.Equals(Environment.GetEnvironmentVariable("UTSL_SWITCH_FINISH_FRAMES"), "1", StringComparison.Ordinal)) {
                                RunSwitchFinishGroup(to, plr);
                            }

                        }

                        // ===== 15-UTSL 就绪看门狗（B-1 收尾缺陷修复；UTSL_SWITCH_WATCHDOG=1 开启，默认关）=====
                        if (SwitchSpawnWatchdog.Enabled) {
                            SwitchSpawnWatchdog.Arm(to, plr, player.name, from.Name);
                        }

                from.Log.Success(
                    category: "PlayerTransfer",
                    message: GetParticularString("{0} is player name, {1} is destination server name, {2} is number of players in destination server", $"Player '{player.name}' transferred to {to.Name}, current players: {to.NPC.GetActivePlayerCount()}"));
                UnifierApi.EventHub.Coordinator.PostServerTransfer.Invoke(new(from, to, plr));
                to.Log.Success(
                    category: "PlayerTransfer",
                    message: GetParticularString("{0} is player name, {1} is source server name, {2} is number of players in current server", $"Player '{player.name}' joined from {from.Name}, current players: {to.NPC.GetActivePlayerCount()}"));
                Logger.Info(
                    category: "PlayerTransfer",
                    message: GetParticularString("{0} is player name, {1} is source server name, {2} is destination server name", $"Player '{player.name}' transferred from {from.Name} to {to.Name}."));
            }
        }
    }
}


