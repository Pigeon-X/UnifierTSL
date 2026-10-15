using UnifierTSL.Servers;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Hosting.Server;
using UnifierTSL.Surface.Hosting.Unified;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Adapter.Cli.Server;

/// <summary>
/// 单窗口模式的世界控制台：
///   · 不建窗口、不拉 UnifierTSL.ConsoleClient 子进程（区别于 CliServerSurfaceConsole）；
///   · 输出全部汇入 UnifiedConsoleHub（启动器那一个窗口，非当前世界带 [世界名] 前缀）；
///   · 输入由 UnifiedConsoleHub 按"当前世界"路由（启动器窗口里用 1/2/3 或 w 系列切换）。
///
/// 与 CliServerSurfaceConsole / HeadlessServerSurfaceConsole 并列，由 ServerContext.ConsoleMode 选择。
/// </summary>
internal sealed class UnifiedServerSurfaceConsole : ServerSurfaceConsole
{
    private readonly UnifiedSurfaceSession session = new();
    private readonly UnifiedConsoleHub.Entry entry;

    public UnifiedServerSurfaceConsole(ServerContext server)
        : base(server, () => PromptRegistry.CreateDefaultCommandPromptSpec(server)) {

        entry = UnifiedConsoleHub.Instance.Register(server);
        InitializeSurfaceRuntime();
    }

    protected override ISurfaceSession Session => session;

    /// <summary>需要交互输入（输入源是启动器窗口，不是子进程管道）。</summary>
    public override bool CanReadInput => true;

    // ==================== 输出 ====================

    public override void Write(string? value) {
        UnifiedConsoleHub.Instance.Write(Server, value, newLine: false);
    }

    public override void WriteLine(string? value) {
        UnifiedConsoleHub.Instance.Write(Server, value, newLine: true);
    }

    public override void WriteAnsi(string? value) {
        UnifiedConsoleHub.Instance.Write(Server, value, newLine: false);
    }

    public override void WriteLineAnsi(string? value) {
        UnifiedConsoleHub.Instance.Write(Server, value, newLine: true);
    }

    public override void Clear() {
        // 单窗口里"清屏"会连别的世界一起清掉；改成重画切换面板
        UnifiedConsoleHub.Instance.RenderPanel();
    }

    // ==================== 输入 ====================

    public override string? ReadLine() {
        return UnifiedConsoleHub.Instance.ReadLineFor(Server);
    }

    public override string ReadLine(PromptSurfaceSpec prompt, bool trim = false) {
        string line = UnifiedConsoleHub.Instance.ReadLineFor(Server);
        return trim ? line.Trim() : line;
    }

    protected override string? ReadLineCore(PromptSurfaceSpec prompt) {
        return UnifiedConsoleHub.Instance.ReadLineFor(Server);
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) {
        return LauncherSurfaceConsole.IsInteractive
            ? LauncherSurfaceConsole.ReadKey(intercept)
            : default;
    }

    public override ConsoleKeyInfo ReadKey() => ReadKey(false);

    public override int Read() {
        ConsoleKeyInfo key = ReadKey(true);
        return key.KeyChar;
    }

    // ==================== 生命周期 ====================

    public override void Dispose(bool disposing) {
        if (disposing) {
            UnifiedConsoleHub.Instance.Unregister(Server);
            session.Dispose();
        }
        base.Dispose(disposing);
    }
}