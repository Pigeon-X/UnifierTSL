using Terraria;
using UnifiedServerProcess;
using UnifierTSL.Surface.Adapter.Cli.Server;
using UnifierTSL.Surface.Hosting.Server;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;
using UnifierTSL.Logging.LogWriters;
using UnifierTSL.Network;
using UnifierTSL.Performance;

namespace UnifierTSL.Servers
{
    public partial class ServerContext : RootContext, IDisposable, ILoggerHost, ILogMetadataInjector
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public readonly IWorldDataProvider worldDataProvider;

        /// <summary>
        /// 世界池复用（PocketWorld）通过 <see cref="SwitchWorld"/> 把它换成另一个世界的数据源。
        /// 保留原字段是为了不破坏既有读取方（其它插件/内部代码都按字段读）。
        /// </summary>
        public IWorldDataProvider WorldDataProvider { get; private set; } = null!;
        public readonly ClientPacketReceiver PacketReceiver;
        public readonly ServerPerformance Performance;
        public readonly RoleLogger Log;

        public Thread? RunningThread { get; private set; }
        public int ActivePlayerCount { get; internal set; }
        internal int routedConnectionCountAccumulator;
        internal int playerCountAccumulator;
        internal volatile bool hasRoutedConnections;
        public bool IsRunning { get; private set; }

        public string? CurrentLogCategory { get; set; }
        string ILoggerHost.Name => $"Log";
        public new ServerSurfaceConsole Console {
            get => (ServerSurfaceConsole)base.Console;
            private set => base.Console = value;
        }

        internal static void Initialize() {
            StaticLeakTargeted.LogSelfCheck();
            ApplyPathOverrides();
            On.Terraria.NetplaySystemContext.StartServer += StartServer;
            On.Terraria.Main.YouCanSleepNow += CloseServer;
            On.UnifiedServerProcess.RootContext.ctor += OnCreateInstance;
        }

        private static int pathOverridesApplied;

        /// <summary>
        /// 自动启动服务器发生在 UnifierApi.PrepareRuntime 阶段，早于 ServerContext.Initialize。
        /// 因此必须在这里先把 Main.WorldPath / SavePath 指向 Core 布局，否则自动启动会在
        /// 旧的根目录 Worlds 下找不到地图并等待/卡住。
        /// </summary>
        internal static void ApplyPathOverrides() {
            if (Interlocked.Exchange(ref pathOverridesApplied, 1) != 0) {
                return;
            }

            ApplySavePathOverride();
            ApplyWorldPathOverride();
        }

        /// <summary>
        /// 世界目录固定到程序目录内（base\Worlds；环境变量 UTSL_WORLD_PATH 可覆盖）——2026-10-16 目录分类改造。
        /// </summary>
        private static void ApplyWorldPathOverride() {
            try {
                System.Reflection.FieldInfo? F(string n) => typeof(Terraria.Main).GetField(n,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                string? forced = Environment.GetEnvironmentVariable("UTSL_WORLD_PATH");
                string worldDir = !string.IsNullOrWhiteSpace(forced) ? forced : Path.Combine(UnifierApi.BaseDirectory, "Worlds");
                Directory.CreateDirectory(worldDir);
                F("WorldPath")?.SetValue(null, worldDir);
                System.Console.WriteLine("[WorldPath] world dir = " + worldDir);
            }
            catch (Exception ex) {
                System.Console.WriteLine("[WorldPath] override failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 默认存档目录（Documents\My Games\Terraria）在某些环境下不可写，而 Terraria 在创建
        /// 世界元数据时会写 favorites.json，写失败会直接终止进程。所以启动时先探测一次：
        /// 不可写就整体换到程序目录下的 Saves（并把已有 .wld 复制过去）。
        /// 也可用环境变量 UTSL_SAVE_PATH 强制指定。
        /// </summary>
        private static void ApplySavePathOverride()
        {
            try
            {
                System.Reflection.FieldInfo? F(string n) => typeof(Terraria.Main).GetField(n,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                string def = Terraria.Main.SavePath;
                string? forced = Environment.GetEnvironmentVariable("UTSL_SAVE_PATH");
                string? target = !string.IsNullOrWhiteSpace(forced) ? forced : null;

                if (target is null)
                {
                    try
                    {
                        Directory.CreateDirectory(def);
                        string probe = Path.Combine(def, "_write_probe.tmp");
                        File.WriteAllText(probe, "x");
                        File.Delete(probe);
                        return;                       // 默认可写 -> 什么都不做
                    }
                    catch
                    {
                        target = Path.Combine(UnifierApi.BaseDirectory, "Saves");
                    }
                }

                string oldWorlds = Terraria.Main.WorldPath;
                Directory.CreateDirectory(target);
                string newWorlds = Path.Combine(target, "Worlds");
                Directory.CreateDirectory(newWorlds);
                F("SavePath")?.SetValue(null, target);
                F("WorldPath")?.SetValue(null, newWorlds);
                F("PlayerPath")?.SetValue(null, Path.Combine(target, "Players"));

                int copied = 0;
                try
                {
                    if (Directory.Exists(oldWorlds))
                    {
                        foreach (string f in Directory.GetFiles(oldWorlds, "*.wld"))
                        {
                            string dst = Path.Combine(newWorlds, Path.GetFileName(f));
                            if (!File.Exists(dst)) { File.Copy(f, dst); copied++; }
                        }
                    }
                }
                catch { }

                System.Console.WriteLine($"[SavePath] '{def}' 不可写 -> 改用 '{target}'（复制 {copied} 个世界文件）");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[SavePath] override failed: " + ex.Message);
            }
        }

        private static void CloseServer(On.Terraria.Main.orig_YouCanSleepNow orig, Main self, RootContext root) {
            orig(self, root);
            root.ToServer().IsRunning = false;
        }

        private static void StartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, Terraria.NetplaySystemContext self) {
            orig(self);
            self.root.ToServer().IsRunning = true;
        }

        private static void OnCreateInstance(On.UnifiedServerProcess.RootContext.orig_ctor orig, RootContext self, string name) {
            if (self is not ServerContext) {
                throw new InvalidOperationException(GetParticularString("{0} is class name (SampleServer)", $"Under the UnifierTSL API, only instances of ServerContext (or its derivatives) can be created. To create a sample context, use '{typeof(SampleServer).FullName}' instead."));
            }
            orig(self, name);
        }

        /// <summary>
        /// 世界控制台模式：
        ///   Unified  = 单窗口（默认）：所有世界共用一个启动器窗口，窗口内用 1/2/3 或 w 系列切换；
        ///   PerWorld = 每个世界一个独立窗口（每个世界还会额外拉一个 ConsoleClient 进程）；
        ///   Headless = 完全无窗口（连汇总输出也不进启动器窗口）。
        /// </summary>
        public enum ConsoleMode { Unified, PerWorld, Headless }

        /// <summary>
        /// 新建世界的默认控制台模式。环境变量 UTSL_CONSOLE_MODE 可覆盖：unified / perworld / headless。
        /// 默认 Unified —— 启动多世界不再弹出 N 个窗口，也不再为每个世界拉起 ConsoleClient 子进程。
        /// </summary>
        public static ConsoleMode DefaultConsoleMode { get; set; } = ParseConsoleMode(Environment.GetEnvironmentVariable("UTSL_CONSOLE_MODE"));

        private static ConsoleMode ParseConsoleMode(string? raw) {
            if (string.IsNullOrWhiteSpace(raw)) {
                return ConsoleMode.Unified;
            }

            return raw.Trim().ToLowerInvariant() switch {
                "perworld" or "per-world" or "window" or "windows" => ConsoleMode.PerWorld,
                "headless" or "none" or "silent" => ConsoleMode.Headless,
                "unified" or "single" or "single-window" => ConsoleMode.Unified,
                _ => ConsoleMode.Unified,
            };
        }

        protected virtual ServerSurfaceConsole CreateSurfaceConsole()
            => DefaultConsoleMode switch {
                ConsoleMode.Unified => new UnifierTSL.Surface.Adapter.Cli.Server.UnifiedServerSurfaceConsole(this),
                ConsoleMode.Headless => new UnifierTSL.Surface.Hosting.Server.HeadlessServerSurfaceConsole(this),
                _ => new CliServerSurfaceConsole(this),
            };

        /// <summary>是否需要为该世界启动控制台输入线程（由控制台是否支持交互输入决定）。</summary>
        public virtual bool SupportsConsoleInput => Console.CanReadInput;

        /// <summary>
        /// 世界加载会往那个全局静态表追加约 117 个组件（含持有本世界图格数据的组件），
        /// 关世界时用"当前条目数 - 基线"就能精确圈出【本世界追加的那一批】并整批摘掉。
        /// -1 表示未取到（不影响其它逻辑）。
        /// </summary>
        public int SnapshotBaseline { get; private set; } = -1;

        public ServerContext(string serverName, IWorldDataProvider worldData, Logger? overrideLogCore = null) : base(serverName) {
            try { SnapshotBaseline = StaticLeakTargeted.CurrentGameplayComponentCount(); } catch { SnapshotBaseline = -1; }
            Dispatcher = CreateDispatcher();
            Console = CreateSurfaceConsole();
            PacketReceiver = new ClientPacketReceiver(this);
            Performance = new(this);

            Logger logCore = overrideLogCore ?? UnifierApi.LogCore.CreateSibling(new ServerConsoleLogWriter(this), historyEnabled: false);
            Log = UnifierApi.CreateLogger(this, logCore);
            Log.AddMetadataInjector(injector: this);

            worldDataProvider = worldData;
            WorldDataProvider = worldData;
            worldData.ApplyMetadata(this);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = -1;
            Netplay.UseUPNP = true;

            InitializeExtension();
        }

        public virtual Thread Run(string[] args) {
            Thread result = RunningThread = new Thread(() => RunBlocking(args)) {
                IsBackground = true,
            };
            result.Name = $"Server Instance: {Name}";
            result.Start();
            return result;
        }
        public virtual void RunBlocking(string[] args) {
            RunningThread = Thread.CurrentThread;
            RunningThread.Name = $"Server Instance: {Name}";
            ThreadLocalInitializer.Initialize();
            Program.LaunchGame(args);
        }

        public virtual Task Stop() {
            Netplay.Disconnect = true;
            return Task.Run(() => {
                if (RunningThread != null && RunningThread.IsAlive) {
                    RunningThread.Join();
                }
                RunningThread = null;
            });
        }

        public virtual Task Close() {
            Netplay.Disconnect = true;
            Task task = Task.Run(() => {
                try {
                    if (RunningThread != null && RunningThread.IsAlive) {
                        RunningThread.Join();
                    }
                }
                catch (Exception ex) {
                    Log.Warning(
                        category: "Lifecycle",
                        message: GetString("Exception while waiting for the server update thread to stop; continuing with disposal."),
                        ex: ex);
                }
                finally {
                    RunningThread = null;
                    //   旧实现把 Dispose() 放在 Join() 之后且没有 try/finally ——
                    //   线程从未启动、或 Join 抛异常时 Dispose 永不执行，
                    //   ServerDispatcher 注册在静态事件表上的 preUpdateHandler 就永远留着，
                    //   把整个 ServerContext 钉死在内存里（实测：Close+RemoveServer+强制 GC 后弱引用仍存活 ✗）。
                    Dispose();
                }
            });
            return task;
        }

        public override string ToString() => $"{{ Type:ServerContext, WorldName:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";

        public void InjectMetadata(scoped ref LogEntry entry) {
            entry.SetMetadata("ServerContext", Name);
        }

        private int disposedValue;
        protected virtual void Dispose(bool disposing) {
            //   旧实现把 Dispatcher.Dispose()/Console.Dispose() 放在幂等检查【之外】，
            //   重复调用会重复释放；而任何一步抛异常又会中断后面的释放
            //   （Dispatcher.Dispose 里正是"反注册静态事件处理器"那一步，漏掉就把对象钉死）。
            if (Interlocked.Exchange(ref disposedValue, 1) != 0) {
                return;
            }

            Netplay.Disconnect = true;

            try {
                Dispatcher.Dispose();
            }
            catch (Exception ex) {
                Log.Warning(category: "Lifecycle", message: GetString("Failed to dispose the server dispatcher."), ex: ex);
            }

            try {
                Console.Dispose();
            }
            catch (Exception ex) {
                Log.Warning(category: "Lifecycle", message: GetString("Failed to dispose the server console surface."), ex: ex);
            }

            if (disposing) {
                try {
                    DisposeExtension();
                }
                catch (Exception ex) {
                    Log.Warning(category: "Lifecycle", message: GetString("Failed to dispose server extensions."), ex: ex);
                }
            }

            //   只做 Dispatcher/Console 的释放是不够的 —— 实测（2.5GB 堆转储 gcroot）还有一个静态持有者：
            //     UnifiedServerCoordinator.servers (static ImmutableArray<ServerContext>)
            //   它由【调用方】负责 RemoveServer，但关世界时经常被漏掉 → 整个 ServerContext 被钉死 ✗
            try {
                UnifiedServerCoordinator.RemoveServer(this);
            }
            catch (Exception ex) {
                Log.Warning(category: "Lifecycle", message: GetString("Failed to remove the server from the coordinator."), ex: ex);
            }

            //   A. ConditionalDialogue._registry（PartyGirl 的 FreeCakeDialogue 闭包）
            //   C. Testing.StateSnapshot 静态定义表（各 SystemContext 的字段闭包）
            //   实测：不做这一步，世界跑起来后即使线程退出 + RemoveServer + 反注册事件，对象仍回收不掉 ✗
            //   组件的闭包会挂住整个世界的上下文，遍历一遍等于遍历整个对象图，
            //   实测导致 Close() 超过 60 秒仍未返回、内存暴涨 450~760MB ✗
            //   结论：静态注册表（StateSnapshot / ConditionalDialogue）**暂不修**，
            //   改由"世界池复用、绝不销毁 context"来把内存限制成【固定开销】。
            //   若将来要修：必须改成"只沿已知的短链定向检查"（不许做通用深遍历），并单独实测。
            //   可用环境变量 UTSL_TARGETED_TEARDOWN=1 为单次运行开启（便于实测）。
            bool targeted = StaticLeakTargeted.Enabled
                || string.Equals(Environment.GetEnvironmentVariable("UTSL_TARGETED_TEARDOWN"), "1", StringComparison.Ordinal);
            if (targeted) {
                try {
                    int n2 = StaticLeakTargeted.Detach(this);
                    Log.Info(category: "Lifecycle", message: "Targeted detach removed=" + n2 + " | " + StaticLeakTargeted.TakeDiagnostics());
                }
                catch (Exception ex) {
                    Log.Warning(category: "Lifecycle", message: "Targeted detach failed.", ex: ex);
                }
            }

        }

        // ~ServerContext()
        // {
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
