using UnifierTSL.Surface.Hosting;

namespace UnifierTSL.Surface.Hosting.Unified;

/// <summary>
/// 单窗口控制台的输出出口：统一走启动器自己的终端面（LauncherSurfaceConsole），
/// 这样"世界日志"和"启动器日志"落在同一个窗口里。
/// </summary>
internal static class UnifiedConsoleOutput
{
    public static void Write(string text) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }

        try {
            LauncherSurfaceConsole.WriteAnsi(text);
        }
        catch {
        }
    }

    /// <summary>未登记的世界（启动/关闭竞态）直接带名字前缀输出。</summary>
    public static void WriteRaw(string serverName, string? text, bool newLine) {
        try {
            string line = text ?? string.Empty;
            if (!string.IsNullOrEmpty(line)) {
                LauncherSurfaceConsole.WriteAnsi("[" + serverName + "] " + line);
            }
            if (newLine) {
                LauncherSurfaceConsole.Write(Environment.NewLine);
            }
        }
        catch {
        }
    }
}