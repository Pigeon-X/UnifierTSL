using Terraria.Localization;
using UnifierTSL.Surface.Adapter.Cli.Terminal;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.PluginHost;

namespace UnifierTSL
{
    internal class Program
    {
        private static void Main(string[] args) {
            RuntimeLayout.Initialize();
            Initializer.InitializeResolver();
            LauncherSurfaceConsole.Initialize(new TerminalLauncherSurfaceHost());
            UnifierApi.HandleCommandLinePreRun(args);
            UnifierApi.PrepareRuntime(args);
            Run();
        }

        private static void Run() {
            VersionHelper version = UnifierApi.VersionHelper;

            Console.Title = "UnifierTSLauncher";

            Console.WriteLine(@" ╔════════════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine(@"╔═══════════════════════════════════════════════════════════════════════════════════╗╗║");
            Console.WriteLine(@"║   ██╗   ██╗███╗   ██╗██╗███████╗██╗███████╗██████╗     ████████╗███████╗██╗       ║║║");
            Console.WriteLine(@"║   ██║   ██║████╗  ██║██║██╔════╝██║██╔════╝██╔══██╗    ╚══██╔══╝██╔════╝██║       ║║║");
            Console.WriteLine(@"║   ██║   ██║██╔██╗ ██║██║█████╗  ██║█████╗  ██████╔╝       ██║   ███████╗██║       ║ ║");
            Console.WriteLine(@"║   ██║   ██║██║╚██╗██║██║██╔══╝  ██║██╔══╝  ██╔══██╗       ██║   ╚════██║██║       ║ ║");
            Console.WriteLine(@"║   ╚██████╔╝██║ ╚████║██║██║     ██║███████╗██║  ██║       ██║   ███████║███████╗  ║║║");
            Console.WriteLine(@"║    ╚═════╝ ╚═╝  ╚═══╝╚═╝╚═╝     ╚═╝╚══════╝╚═╝  ╚═╝       ╚═╝   ╚══════╝╚══════╝  ║║╝");
            Console.WriteLine(@"╚═══════════════════════════════════════════════════════════════════════════════════╝");

            Console.WriteLine();

            UnifierApi.Logger.Info(GetString(
@$"Unifier Terraria-Server-Launcher Running
Version Info:
  Terraria v{version.TerrariaVersion} & Protocol {Terraria.Main.curRelease}
  Unified-Server-Process v{version.USPVersion} & OTAPI v{version.OTAPIVersion}
  UnifierApi v{version.UnifierApiVersion} & PluginApi v{PluginOrchestrator.ApiVersion}
Current Process ID: {Environment.ProcessId}"));

            WorkRunner.RunSurfaceActivity("Init", GetString("Global initialization started..."), () => {
                Initializer.Initialize();
                UnifierApi.InitializeCore();
            });

            UnifierApi.CompleteLauncherInitialization();

            UnifiedServerCoordinator.Launch(UnifierApi.ListenPort, UnifierApi.ServerPassword);

            UnifierApi.UpdateTitle();

            string currentServers = "";
            if (UnifiedServerCoordinator.Servers.Length > 0) {
                currentServers = GetString("Current Servers: ") + "\r\n";
                foreach (Servers.ServerContext server in UnifiedServerCoordinator.Servers) {
                    currentServers += GetParticularString("{0} is server name, {1} is world file name", $"  {server.Name} Running on world: {server.worldDataProvider.WorldFileName}") + "\r\n";
                }
            }

            UnifiedServerCoordinator.Logger.Info(
                category: "Startup",
                message: GetString($"UnifierTSL started successfully!") + "\r\n" +
                         currentServers +
                         Language.GetTextValue("CLI.ListeningOnPort", UnifiedServerCoordinator.ListenPort) + "\r\n" +
                         (string.IsNullOrEmpty(UnifiedServerCoordinator.ServerPassword)
                         ? GetString($"Server is running without a password. Anyone can join.")
                         : GetParticularString("{0} is server password", $"Server is running with password: '{UnifiedServerCoordinator.ServerPassword}'")));

            UnifierApi.EventHub.Coordinator.Started.Invoke(default);

            // 单窗口模式：起完世界就把"可切换控制台面板"画一次
            if (Servers.ServerContext.DefaultConsoleMode == Servers.ServerContext.ConsoleMode.Unified) {
                Surface.Hosting.Unified.UnifiedConsoleHub.Instance.RenderPanel(
                    GetString("单窗口控制台已启用：输入世界编号 1/2/3 或 w next / w prev / w <名字> 切换"));
            }

            UnifierApi.EventHub.Chat.KeepReadingInput();
        }
    }
}
