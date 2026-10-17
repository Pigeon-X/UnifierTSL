using System.Collections.Concurrent;
using System.Text.Json;
using TrProtocol.NetPackets;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace UnifierTSL
{
    /// <summary>
    /// 切换「就绪看门狗」（B-1 收尾缺陷修复；15-UTSL 2026-10-15）：
    /// 切换上线段完成后布防；若阈值内未见该客户端 C2S 12（SpawnPlayer 握手），
    /// 条件重发一枚 S→C 12（与上线段③同参），封堵「客户端初始化偶发卡死」。
    /// 门控（默认关）：UTSL_SWITCH_WATCHDOG=1 开启；
    ///   UTSL_SWITCH_WATCHDOG_DELAY_MS（默认 1500，最小 300）；
    ///   UTSL_SWITCH_WATCHDOG_MAX_RESENDS（默认 1，0=仅观察，最大 3）；
    ///   UTSL_SWITCH_WATCHDOG_TRUST_FIRST=1 允许首见即重发（默认关=fail-safe：仅当该客户端
    ///   本进程内至少确认过一次 C2S 12 后才重发——观察管线若失效则退化为纯观察，不会误重发）。
    /// 参数热载（A 项 15-UTSL 2026-10-15）：config\UnifierTSL\Watchdog.json 每次『布防』重读——
    ///   {"阈值毫秒":1500,"可重发":2,"节拍毫秒":1500,"末枚等待毫秒":6500,"再驱动组":["12"]}（兼容 maxResends/resendIntervalMs/postLastResendWaitMs）；
    ///   再驱动组=按枚序组合清单（token：12 / 129 / "12+129"；末项沿用；未知 token 忽略），默认 ["12"]=现状纯 12 重发；
    ///   缺失=静默沿用上次有效值；解析失败=沿用+10s 节流告警；生效值打进『布防』行。
    /// 卡切探测器＋硬重驱（15-UTSL 2026-10-16 staged；键控）：
    ///   探测器=log-only：未确认被替换/连接变化/额度尽 → 追加 [卡切] 行（不影响现有行）；
    ///   硬重驱=额度尽且客户端仍连接且路由未变时，重放上线段子集（7 号 WorldData＋出生区分块＋12 号 PlayerSpawn），
    ///   键：Watchdog.json「硬重驱」(0-2=上限；缺省回落 env UTSL_SWITCH_HARD_REDRIVE_MAX / UTSL_SWITCH_HARD_REDRIVE=1，默认 0=关)。
    /// kstuck2（2026-10-16 staged；log-only）：[卡切] 行追加"最后活性"（13 号 PlayerControls 时间戳/计数；
    ///   探针=Lower 优先级、仅写静态数组，不发包/不设 HandleMode/不打行；健康路径无新增行）。
    /// kstuck3（2026-10-16 staged；键控）：T3 轻量重进——触发=额度尽＋硬重驱尽＋连接/路由未变（每场≤1、冷却60s，
    ///   键：Watchdog.json「T3」/ env UTSL_SWITCH_T3，默认关）；动作=新 kind=7 帧（保 15s TTL 新鲜）→≥300ms→
    ///   HardDrop（PendingTermination＋Approved 双旗标、关 socket、不发 Kick；v2cg 修复）→ 由代理既有 PendingReconnect 回路完成轻量重进。
    /// kstuck3v2cc（2026-10-17 staged；C 方案 S1）：T3 发帧改「mode=1（C 请求）」——反射 4 参重载（string,string,string,byte）优先、3 参回退（老子插件自动旧式=不写尾字节）。
    /// </summary>
    internal static class SwitchSpawnWatchdog
    {
        private sealed class Pending
        {
            public ServerContext To = null!;
            public int Plr;
            public string Name = "";
            public string From = "";
            public long ArmedMs;
            public int ResendsLeft;
            public int HardLeft;
            public int T3Left;
            public Policy Policy = null!;
            public long NextWaitMs;
        }

        private static readonly ConcurrentDictionary<int, Pending> pending = new();
        private static readonly ConcurrentDictionary<int, byte> ackSeenEver = new();
        // kstuck2：客户端最后活性（log-only）——仅 13 号(PlayerControls) 时间戳/计数
        private static readonly long[] lastC2S13Ms = new long[Terraria.Netplay.MaxConnections];
        private static readonly int[] c2s13Count = new int[Terraria.Netplay.MaxConnections];
        // kstuck3：T3 轻量重进冷却（每玩家 60s）
        private static readonly long[] lastT3Ms = new long[Terraria.Netplay.MaxConnections];
        private static int registered;
        private static int started;

        internal static bool Enabled {
            get {
                string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_WATCHDOG");
                return v is "1" or "true" or "True";
            }
        }
        private static long EnvDelayMs() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_WATCHDOG_DELAY_MS");
            return long.TryParse(v, out long ms) && ms >= 300 ? ms : 1500;
        }
        private static int EnvMaxResends() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_WATCHDOG_MAX_RESENDS");
            return int.TryParse(v, out int n) ? Math.Clamp(n, 0, 3) : 1;
        }
        private static int EnvHardReDriveMax() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_HARD_REDRIVE_MAX");
            if (int.TryParse(v, out int n)) return Math.Clamp(n, 0, 2);
            string? b = Environment.GetEnvironmentVariable("UTSL_SWITCH_HARD_REDRIVE");
            return (b is "1" or "true" or "True") ? 1 : 0;
        }
        private static bool EnvT3() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_T3");
            return v is "1" or "true" or "True";
        }
        private static bool Trace => Environment.GetEnvironmentVariable("UTSL_SWITCH_TRACE") is "1" or "true" or "True";
        private static void TraceInfo(string message) {
            if (Trace) {
                UnifierApi.Logger.Info(category: "PlayerTransfer", message: message);
            }
        }
        private static bool EnvT3Hint() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_T3_HINT");
            return !(v is "0" or "false" or "False");
        }
        private static int EnvT3CooldownS() {
            string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_T3_COOLDOWN_S");
            return int.TryParse(v, out int n) ? Math.Clamp(n, 5, 600) : 60;
        }

        // ── A 项（15-UTSL 2026-10-15）：参数热载 ────────────────────────────
        // config\UnifierTSL\Watchdog.json 于【每次『布防』】重读；文件缺失=静默沿用上次有效值；
        // 解析失败=沿用上次有效值 + 10s 节流告警。env 仍为冷配基线（json 缺项时回落到 env/默认）。
        private sealed record Policy(long DelayMs, long CadenceMs, int MaxResends, long PostWaitMs, string[] ReDrive, int HardReDriveMax, bool T3, bool T3Hint, int T3CooldownS);
        private static Policy lastGoodPolicy = PolicyFromEnv();
        private static long lastPolicyWarnMs;

        private static Policy PolicyFromEnv() {
            long delay = EnvDelayMs();
            return new Policy(delay, delay, EnvMaxResends(), delay + 5000, new[] { "12" }, EnvHardReDriveMax(), EnvT3(), EnvT3Hint(), EnvT3CooldownS());
        }

        private static long TryGetInt64(JsonElement obj, params string[] names) {
            foreach (string n in names) {
                if (obj.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long v)) return v;
            }
            return long.MinValue;
        }
        private static bool TryGetBool(JsonElement obj, out bool val, params string[] names) {
            val = false;
            foreach (string n in names) {
                if (!obj.TryGetProperty(n, out JsonElement e)) continue;
                if (e.ValueKind == JsonValueKind.True) { val = true; return true; }
                if (e.ValueKind == JsonValueKind.False) { val = false; return true; }
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long iv)) { val = iv != 0; return true; }
            }
            return false;
        }

        private static string? FindPolicyPath() {
            foreach (string root in new[] { UnifierApi.BaseDirectory, Environment.CurrentDirectory, AppContext.BaseDirectory }) {
                try {
                    string p = Path.Combine(root, "config", "UnifierTSL", "Watchdog.json");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        private static Policy LoadPolicy() {
            try {
                string? path = FindPolicyPath();
                if (path is null) return lastGoodPolicy;
                JsonDocumentOptions opt = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), opt);
                JsonElement r = doc.RootElement;
                long delay = EnvDelayMs();
                long cadence = delay;
                long post = delay + 5000;
                int maxR = EnvMaxResends();
                long v1 = TryGetInt64(r, "阈值毫秒", "thresholdMs", "delayMs");
                bool delaySet = v1 >= 300;
                if (delaySet) { delay = v1; cadence = v1; }
                long v2 = TryGetInt64(r, "节拍毫秒", "resendIntervalMs");
                if (v2 >= 300) cadence = v2;
                long v3 = TryGetInt64(r, "可重发", "maxResends");
                if (v3 != long.MinValue) maxR = Math.Clamp((int)v3, 0, 3);
                long v4 = TryGetInt64(r, "末枚等待毫秒", "postLastResendWaitMs");
                if (v4 >= 500) post = v4;
                else if (delaySet) post = delay + 5000;
                string[] reDrive = ["12"];
                if (r.TryGetProperty("再驱动组", out JsonElement rd) && rd.ValueKind == JsonValueKind.Array) {
                    List<string> items = [];
                    foreach (JsonElement it in rd.EnumerateArray()) {
                        if (it.ValueKind == JsonValueKind.String) {
                            string s = (it.GetString() ?? "").Trim();
                            if (s.Length > 0) items.Add(s);
                        }
                    }
                    if (items.Count > 0) reDrive = [.. items];
                }
                int hard = EnvHardReDriveMax();
                long v5 = TryGetInt64(r, "硬重驱", "hardReDriveMax");
                if (v5 != long.MinValue) hard = Math.Clamp((int)v5, 0, 2);
                bool t3 = EnvT3();
                if (TryGetBool(r, out bool t3v, "T3", "轻量重进")) t3 = t3v;
                bool t3h = EnvT3Hint();
                if (TryGetBool(r, out bool t3hv, "T3提示", "T3Hint")) t3h = t3hv;
                int t3cd = EnvT3CooldownS();
                long v6 = TryGetInt64(r, "T3冷却秒", "t3CooldownSeconds");
                if (v6 != long.MinValue) t3cd = Math.Clamp((int)v6, 5, 600);
                Policy p = new(delay, cadence, maxR, post, reDrive, hard, t3, t3h, t3cd);
                lastGoodPolicy = p;
                return p;
            }
            catch (Exception ex) {
                long now = Environment.TickCount64;
                if (now - lastPolicyWarnMs >= 10_000) {
                    lastPolicyWarnMs = now;
                    try { UnifierApi.Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 看门狗 Watchdog.json 读取失败，沿用上次有效值: " + ex.Message); } catch { }
                }
                return lastGoodPolicy;
            }
        }
        private static bool TrustFirst {
            get {
                string? v = Environment.GetEnvironmentVariable("UTSL_SWITCH_WATCHDOG_TRUST_FIRST");
                return v is "1" or "true" or "True";
            }
        }

        internal static void EnsureRegistered() {
            if (Interlocked.Exchange(ref registered, 1) != 0) return;
            try {
                NetPacketHandler.Register<SpawnPlayer>(OnClientSpawnPlayer, HandlerPriority.Lower);
                NetPacketHandler.Register<TrProtocol.NetPackets.SyncPlayer>(OnSyncPlayerProbe, HandlerPriority.Lower);
                NetPacketHandler.Register<TrProtocol.NetPackets.RequestWorldInfo>(OnRequestWorldInfoProbe, HandlerPriority.Lower);
                NetPacketHandler.Register<TrProtocol.NetPackets.PlayerControls>(OnClientPlayerControlsProbe, HandlerPriority.Lower);
                TraceInfo("[TransferTrace] 看门狗 已注册 C2S 12 观察点（Lower）");
                TraceInfo("[TransferTrace] 观察器 已注册 4号(SyncPlayer)/6号(RequestWorldInfo)（Lower）");
                TraceInfo("[TransferTrace] 观察器 已注册 13号(PlayerControls/活性)（Lower）");
            }
            catch (Exception ex) {
                try { UnifierApi.Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 看门狗 注册失败: " + ex.Message); } catch { }
            }
        }

        // —— 丢名线定责观察器（只读）：4号 名字长度 + 6号 到达 ——
        private static void OnSyncPlayerProbe(ref ReceivePacketEvent<TrProtocol.NetPackets.SyncPlayer> args) {
            try {
                string nm = args.Packet.Name ?? "";
                TraceInfo($"[TransferTrace] 观察器 4号 nameLen={nm.Length} slot={args.Who} world={args.Server.Name} t={DateTime.Now:HH:mm:ss.fff}");
            } catch { }
        }
        private static void OnRequestWorldInfoProbe(ref ReceivePacketEvent<TrProtocol.NetPackets.RequestWorldInfo> args) {
            try {
                TraceInfo($"[TransferTrace] 观察器 6号 RequestWorldInfo 到达 slot={args.Who} world={args.Server.Name} t={DateTime.Now:HH:mm:ss.fff}");
            } catch { }
        }
        private static void OnClientSpawnPlayer(ref ReceivePacketEvent<SpawnPlayer> args) {
            try {
                int plr = args.Who;
                ackSeenEver[plr] = 1;
                if (pending.TryRemove(plr, out Pending? p)) {
                    long dt = Environment.TickCount64 - p.ArmedMs;
                    TraceInfo($"[TransferTrace] 看门狗 收到 C2S 12 撤防 +{dt}ms（world={args.Server.Name} 玩家={p.Name}）");
                }
            }
            catch { }
        }
        // kstuck2：13 号活性探针（log-only：仅时间戳/计数；不写 HandleMode、不改包、不打行）
        private static void OnClientPlayerControlsProbe(ref ReceivePacketEvent<TrProtocol.NetPackets.PlayerControls> args) {
            try {
                int plr = args.Who;
                if ((uint)plr >= (uint)Terraria.Netplay.MaxConnections) return;
                Volatile.Write(ref lastC2S13Ms[plr], Environment.TickCount64);
                Interlocked.Increment(ref c2s13Count[plr]);
            }
            catch { }
        }
        /// <summary>kstuck2：活性判词片段（读静态数组；无副作用）。</summary>
        private static string ActivityText(int plr, long now) {
            try {
                if ((uint)plr >= (uint)Terraria.Netplay.MaxConnections) return "最后活性=越界";
                long t = Volatile.Read(ref lastC2S13Ms[plr]);
                int n = Volatile.Read(ref c2s13Count[plr]);
                return t <= 0 ? "最后活性=13号无记录（0次）" : $"最后活性=13号+{now - t}ms前（累计{n}次）";
            }
            catch { return "最后活性=读取异常"; }
        }
        // kstuck3：T3 冷却检查（每玩家 60s）
        private static bool T3CooldownOk(int plr, int cooldownS) {
            try {
                if ((uint)plr >= (uint)Terraria.Netplay.MaxConnections) return false;
                long last = Volatile.Read(ref lastT3Ms[plr]);
                return last == 0 || Environment.TickCount64 - last >= (long)Math.Clamp(cooldownS, 5, 600) * 1000L;
            }
            catch { return false; }
        }
        /// <summary>kstuck3：经插件宿主定位持有 chatClient 的维度子插件，调其 SendWorldSwitchAsync（WSR 同款反射路径；找不到=null）。kstuck3v2cc：4 参重载（mode）优先、3 参回退。</summary>
        private static System.Threading.Tasks.Task<bool>? TrySendWorldSwitchFrame(string player, string from, string to, byte mode, out bool fourArgUsed) {
            fourArgUsed = false;
            try {
                UnifierTSL.PluginHost.PluginOrchestrator orch = UnifierApi.PluginHosts;
                foreach (UnifierTSL.PluginHost.IPluginHost host in orch.RegisteredPluginHosts) {
                    foreach (UnifierTSL.PluginService.IPluginContainer c in host.Plugins) {
                        if (c.Plugin is null) continue;
                        System.Reflection.FieldInfo? field = c.Plugin.GetType().GetField("chatClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (field is null) continue;
                        object? chat = field.GetValue(c.Plugin);
                        if (chat is null) continue;
                        // kstuck3v2cc（C 优先）：先找 4 参重载（string,string,string,byte；C 版子插件才有）；找不到回退 3 参旧式（不写尾字节）。
                        System.Reflection.MethodInfo? m = chat.GetType().GetMethod("SendWorldSwitchAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, binder: null, types: new[] { typeof(string), typeof(string), typeof(string), typeof(byte) }, modifiers: null);
                        object? result;
                        if (m is not null) {
                            fourArgUsed = true;
                            result = m.Invoke(chat, new object?[] { player, from, to, mode });
                        }
                        else {
                            m = chat.GetType().GetMethod("SendWorldSwitchAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, binder: null, types: new[] { typeof(string), typeof(string), typeof(string) }, modifiers: null);
                            if (m is null) continue;
                            result = m.Invoke(chat, new object?[] { player, from, to });
                        }
                        if (result is System.Threading.Tasks.Task<bool> tb) return tb;
                        if (result is System.Threading.Tasks.Task t) return t.ContinueWith(_ => true);
                    }
                }
            }
            catch { }
            return null;
        }
        /// <summary>T3 轻量重进（kstuck3，staged；键控）：①新 kind=7 帧（mode=1 C 请求；4参优先/3参回退；保 15s TTL 新鲜）→②≥300ms→③HardDrop（PendingTermination＋Approved 双旗标、关 socket、不发 Kick；v2cg 修复）→④代理既有 PendingReconnect 回路。</summary>
        private static void T3Reconnect(ServerContext to, int plr, string name, string from, bool hint) {
            try {
                bool frameOk = false;
                bool fourArg = false;
                try {
                    System.Threading.Tasks.Task<bool>? t = TrySendWorldSwitchFrame(name, from ?? "", to.Name, 1, out fourArg);
                    if (t is not null) frameOk = t.Wait(2000) && t.Result;
                }
                catch { }
                UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] T3 轻量重进 ①发帧 ok={frameOk} mode={(fourArg ? "1(C请求)" : "3参回退(旧式)")} 玩家={name}（源={from} 目标={to.Name}；保 15s TTL 新鲜）");
                // T3v2：预前提示（9号 StatusText；drop 前尽力发一句；卡死态可见性 A/B）
                bool hintSent = false;
                if (hint) {
                    try {
                        hintSent = to.NetMessage.TrySendData(
                            msgType: 9,
                            remoteClient: plr,
                            ignoreClient: -1,
                            text: Terraria.Localization.NetworkText.FromLiteral("【传送】检测到卡住：正在为你自动恢复，请稍候（若画面未恢复，请重进游戏）"));
                    }
                    catch { }
                }
                UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] T3 轻量重进 ①b提示 发送={hintSent}（开关={hint}；9号 StatusText）玩家={name}");
                Thread.Sleep(400);
                bool still = false;
                try {
                    Terraria.RemoteClient? g = UnifiedServerCoordinator.globalClients[plr];
                    still = g is not null && g.IsConnected() && ReferenceEquals(UnifiedServerCoordinator.GetClientCurrentlyServer(plr), to);
                }
                catch { }
                if (!still) {
                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] T3 轻量重进 放弃（连接/路由已变）玩家={name}");
                    return;
                }
                bool dropped = false;
                try {
                    Terraria.RemoteClient? g = UnifiedServerCoordinator.globalClients[plr];
                    if (g is not null) {
                        // kstuck3v2cg 幽灵修复（2026-10-17）：HardDrop 必须双旗标齐置（对齐 LocalClientSender.Kick）。
                        // 只置 PendingTermination=true 会落入协调器"待终止未批准"的 continue 分支：该客户端不再被
                        // Update（socket 不再读、也不再经 CheckBytes 批准），且世界因失去全部 routed clients 停跑
                        // Main.Update ⇒ 批准永不发生 ⇒ 僵尸会话（实测 T3 首火后 Pigeon@流光城·东：/who=1、PC=1、ESTAB=0）。
                        g.PendingTermination = true;
                        g.PendingTerminationApproved = true;
                        g.Socket?.Close();
                        dropped = true;
                    }
                }
                catch { }
                UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] T3 轻量重进 ②HardDrop={dropped} 玩家={name} 目标={to.Name}（不发 Kick → 代理重连回路）");
            }
            catch (Exception ex) {
                try { UnifierApi.Logger.Warning(category: "PlayerTransfer", message: "[卡切] T3 轻量重进 异常: " + ex.Message); } catch { }
            }
        }

        internal static void Arm(ServerContext to, int plr, string name, string fromName = "") {
            try {
                EnsureRegistered();
                if (!Enabled) return;
                Policy pol = LoadPolicy();
                string reDriveText = string.Join(",", pol.ReDrive);
                if (pending.TryGetValue(plr, out Pending? prev)) {
                    int usedPrev = prev.Policy.MaxResends - prev.ResendsLeft;
                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 未确认（被下一场替换）玩家={prev.Name} 上一目标={prev.To.Name} 历时={Environment.TickCount64 - prev.ArmedMs}ms 已重发={usedPrev}/{prev.Policy.MaxResends}｜{ActivityText(prev.Plr, Environment.TickCount64)}｜新一场照常布防");
                }
                pending[plr] = new Pending { To = to, Plr = plr, Name = name ?? "", From = fromName ?? "", ArmedMs = Environment.TickCount64, ResendsLeft = pol.MaxResends, HardLeft = pol.HardReDriveMax, T3Left = pol.T3 ? 1 : 0, Policy = pol, NextWaitMs = pol.DelayMs };
                if (Interlocked.Exchange(ref started, 1) == 0) {
                    Thread t = new(WatchLoop) { IsBackground = true, Name = "UTSL-SwitchSpawnWatchdog" };
                    t.Start();
                }
                TraceInfo($"[TransferTrace] 看门狗 布防（→{to.Name} plr={plr} 玩家={name} 阈值={pol.DelayMs}ms 可重发={pol.MaxResends} 节拍={pol.CadenceMs}ms 末枚等待={pol.PostWaitMs}ms 再驱动={reDriveText} 硬重驱={pol.HardReDriveMax} T3={(pol.T3 ? 1 : 0)}）");
            }
            catch (Exception ex) {
                try { UnifierApi.Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 看门狗 布防异常: " + ex.Message); } catch { }
            }
        }

        private static void WatchLoop() {
            while (true) {
                try {
                    Thread.Sleep(200);
                    long now = Environment.TickCount64;
                    foreach (KeyValuePair<int, Pending> kv in pending) {
                        Pending p = kv.Value;
                        long age = now - p.ArmedMs;
                        if (age < p.NextWaitMs) continue;
                        bool connected = false;
                        ServerContext? cur = null;
                        try {
                            Terraria.RemoteClient? g = UnifiedServerCoordinator.globalClients[p.Plr];
                            connected = g is not null && g.IsConnected();
                            cur = UnifiedServerCoordinator.GetClientCurrentlyServer(p.Plr);
                        }
                        catch { }
                        if (!connected || !ReferenceEquals(cur, p.To)) {
                            if (pending.TryRemove(p.Plr, out _)) {
                                int usedRc = p.Policy.MaxResends - p.ResendsLeft;
                                TraceInfo($"[TransferTrace] 看门狗 撤防（连接/路由变化）玩家={p.Name}");
                                UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 未确认（连接/路由变化）玩家={p.Name} 目标={p.To.Name} 历时={age}ms 已重发={usedRc}/{p.Policy.MaxResends}｜{ActivityText(p.Plr, now)}｜本场收尾确认未到（客户端侧）");
                            }
                            continue;
                        }
                        if (p.ResendsLeft <= 0) {
                            if (age > p.Policy.PostWaitMs) {
                                int usedEx = p.Policy.MaxResends - p.ResendsLeft;
                                if (p.HardLeft > 0) {
                                    p.HardLeft--;
                                    bool posted = UnifiedServerCoordinator.HardReDriveSwitch(p.To, (byte)p.Plr, "watchdog-exhausted");
                                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 硬重驱 第{p.Policy.HardReDriveMax - p.HardLeft}枚 投递={posted} 玩家={p.Name} 目标={p.To.Name} 已重发={usedEx}/{p.Policy.MaxResends} 历时={age}ms｜{ActivityText(p.Plr, now)}");
                                    p.ArmedMs = now;
                                    // T3v2：T3 在役时红驱后观察窗压缩到末枚等待（≈1.2s）→ 总时序 ≈5.4s（原 ≈7.3s）
                                    p.NextWaitMs = p.Policy.T3 ? Math.Max(800, p.Policy.PostWaitMs) : 3000;
                                    continue;
                                }
                                if (p.T3Left > 0 && T3CooldownOk(p.Plr, p.Policy.T3CooldownS)) {
                                    p.T3Left--;
                                    lastT3Ms[p.Plr] = now;
                                    ServerContext to3 = p.To; int plr3 = p.Plr; string nm3 = p.Name; string from3 = p.From; bool hint3 = p.Policy.T3Hint;
                                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] T3 轻量重进 触发 玩家={nm3} 目标={to3.Name}（额度尽+硬重驱尽；第1枚；自末枚{age}ms；冷却60s）｜{ActivityText(p.Plr, now)}");
                                    _ = System.Threading.Tasks.Task.Run(() => T3Reconnect(to3, plr3, nm3, from3, hint3));
                                    p.ArmedMs = now;
                                    p.NextWaitMs = 3000;
                                    continue;
                                }
                                if (pending.TryRemove(p.Plr, out _)) {
                                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[TransferTrace] 看门狗 超时未确认（额度已尽）玩家={p.Name} +{age}ms");
                                    UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[卡切] 未确认（额度已尽）玩家={p.Name} 目标={p.To.Name} 已重发={usedEx}/{p.Policy.MaxResends}｜{ActivityText(p.Plr, now)}");
                                }
                            }
                            continue;
                        }
                        if (!(TrustFirst || ackSeenEver.ContainsKey(p.Plr))) {
                            p.ResendsLeft--;
                            UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[TransferTrace] 看门狗 超时（fail-safe：该客户端从未见过 C2S 12 确认，不重发）玩家={p.Name} +{age}ms");
                            continue;
                        }
                        p.ResendsLeft--;
                        int sendNo = p.Policy.MaxResends - p.ResendsLeft;
                        ServerContext to = p.To;
                        int plr = p.Plr;
                        string nm = p.Name;
                        string[] redriveTokens = (p.Policy.ReDrive.Length == 0 ? "12" : p.Policy.ReDrive[Math.Min(sendNo - 1, p.Policy.ReDrive.Length - 1)])
                            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        long armedAt = p.ArmedMs;
                        p.ArmedMs = now;
                        p.NextWaitMs = p.Policy.CadenceMs;
                        try {
                            to.Dispatcher.Post(() => {
                                try {
                                    foreach (string tok in redriveTokens) {
                                        if (tok == "12") {
                                            bool ok = to.NetMessage.TrySendData(
                                                (int)Terraria.ID.MessageID.PlayerSpawn, plr, -1, null, plr,
                                                (byte)Terraria.PlayerSpawnContext.SpawningIntoWorld);
                                            UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[TransferTrace] 看门狗 重发 12（第{sendNo}枚）TrySendData={ok} +{Environment.TickCount64 - armedAt}ms 玩家={nm}");
                                        }
                                        else if (tok == "129") {
                                            bool ok129 = to.NetMessage.TrySendData((int)Terraria.ID.MessageID.FinishedConnectingToServer, plr);
                                            UnifierApi.Logger.Warning(category: "PlayerTransfer", message: $"[TransferTrace] 看门狗 再驱动 129（第{sendNo}枚随发）TrySendData={ok129} +{Environment.TickCount64 - armedAt}ms 玩家={nm}");
                                        }
                                    }
                                }
                                catch (Exception exR) {
                                    try { UnifierApi.Logger.Error(category: "PlayerTransfer", message: "[TransferTrace] 看门狗 重发 12 异常", ex: exR); } catch { }
                                }
                            });
                        }
                        catch (Exception exPost) {
                            try { UnifierApi.Logger.Warning(category: "PlayerTransfer", message: "[TransferTrace] 看门狗 重发投递失败: " + exPost.Message); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
    }
}
