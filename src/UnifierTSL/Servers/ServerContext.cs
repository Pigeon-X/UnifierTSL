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
        /// 当前世界数据提供者。★ 与 <see cref="worldDataProvider"/> 指向同一实例；
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
            On.Terraria.NetplaySystemContext.StartServer += StartServer;
            On.Terraria.Main.YouCanSleepNow += CloseServer;
            On.UnifiedServerProcess.RootContext.ctor += OnCreateInstance;
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

        protected virtual ServerSurfaceConsole CreateSurfaceConsole() => new CliServerSurfaceConsole(this);

        /// <summary>
        /// ★ 2026-10-12（世界池 / PocketWorld）：该世界是否需要【控制台输入线程】。
        /// 背景（实测）：Terraria 的 DedServ 会为每个世界起一条 "Server Input Thread"，
        /// 循环体是 while (!Netplay.Disconnect) { Console.Write(": "); ReadLineInput(); }。
        /// · 若控制台 ReadLine 阻塞 → 线程【永久阻塞】，栈成为根 → context 永远不可回收；
        /// · 若 ReadLine 立即返回 null（headless）→ 条件不成立时【无限空转】→ 每关一个世界烧一个核心 ✗
        /// 池世界（副本/随身世界）本来也不需要控制台输入 → 直接不进入这个循环。
        /// </summary>
        public virtual bool SupportsConsoleInput => true;

        /// <summary>
        /// ★ 本世界创建时，Terraria 静态 Gameplay 组件表的条目数（基线）。
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
                    // ★ 2026-10-12 修复（P0 内存泄漏）：Dispose 必须在【任何情况下】都执行。
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
            // ★ 2026-10-12 修复（P0 内存泄漏）：整个释放路径幂等 + 不抛。
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

            // ★ 2026-10-12 补（P0 内存泄漏 / 世界池前提）：把这个世界从【全局静态注册表】里摘掉。
            //   只做 Dispatcher/Console 的释放是不够的 —— 实测（2.5GB 堆转储 gcroot）还有一个静态持有者：
            //     UnifiedServerCoordinator.servers (static ImmutableArray<ServerContext>)
            //   它由【调用方】负责 RemoveServer，但关世界时经常被漏掉 → 整个 ServerContext 被钉死 ✗
            try {
                UnifiedServerCoordinator.RemoveServer(this);
            }
            catch (Exception ex) {
                Log.Warning(category: "Lifecycle", message: GetString("Failed to remove the server from the coordinator."), ex: ex);
            }

            // ★ 2026-10-12 补：摘掉 Terraria 侧【没有反注册 API】的两个静态注册表（详见 StaticLeakTeardown 注释）：
            //   A. ConditionalDialogue._registry（PartyGirl 的 FreeCakeDialogue 闭包）
            //   C. Testing.StateSnapshot 静态定义表（各 SystemContext 的字段闭包）
            //   实测：不做这一步，世界跑起来后即使线程退出 + RemoveServer + 反注册事件，对象仍回收不掉 ✗
            // ★ 2026-10-12 实测后【默认关闭】：这个"通用图遍历"式摘除代价过高 ——
            //   组件的闭包会挂住整个世界的上下文，遍历一遍等于遍历整个对象图，
            //   实测导致 Close() 超过 60 秒仍未返回、内存暴涨 450~760MB ✗
            //   结论：静态注册表（StateSnapshot / ConditionalDialogue）**暂不修**，
            //   改由"世界池复用、绝不销毁 context"来把内存限制成【固定开销】。
            //   若将来要修：必须改成"只沿已知的短链定向检查"（不许做通用深遍历），并单独实测。
            // ★ P1.2 定向摘除（限深限量，绝不通用深遍历）。默认关闭；
            //   可用环境变量 UTSL_TARGETED_TEARDOWN=1 为单次运行开启（便于实测）。
            bool targeted = StaticLeakTargeted.Enabled
                || string.Equals(Environment.GetEnvironmentVariable("UTSL_TARGETED_TEARDOWN"), "1", StringComparison.Ordinal);
            if (targeted) {
                try {
                    int n2 = StaticLeakTargeted.Detach(this);
                    // ★ 打到【主日志】（ServerContext.Log 写的是世界控制台，主 txt 日志看不到）
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
