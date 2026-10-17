using System;
using System.IO;

namespace UnifierTSL
{
    /// <summary>
    /// 运行时目录布局。
    ///
    /// 新部署布局：
    ///   UnifierTSL.exe / UnifierTSL.dll / deps / runtimeconfig   （根目录）
    ///   Core/
    ///     app/       控制台客户端
    ///     bin/       程序集依赖（旧名“依赖/lib”）
    ///     config/    启动器与插件配置
    ///     i18n/      翻译
    ///     logs/      日志
    ///     plugins/   插件
    ///     runtimes/  原生运行时
    ///     Worlds/    世界文件
    ///     x64/       原生工具
    ///
    /// 旧布局没有 Core 时保持原行为，便于平滑升级。
    /// </summary>
    internal static class RuntimeLayout
    {
        public const string CoreDirectoryName = "Core";
        public const string BinDirectoryName = "bin";
        public const string LegacyDependencyDirectoryName = "依赖";
        public const string LegacyLibraryDirectoryName = "lib";

        public static string ExecutableDirectory { get; } =
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        public static string CoreDirectory =>
            Path.Combine(ExecutableDirectory, CoreDirectoryName);

        public static bool UsesCoreDirectory => Directory.Exists(CoreDirectory);

        public static string RuntimeDirectory =>
            UsesCoreDirectory ? CoreDirectory : Directory.GetCurrentDirectory();

        public static string BinDirectory =>
            Path.Combine(RuntimeDirectory, BinDirectoryName);

        public static void Initialize() {
            if (!UsesCoreDirectory) {
                return;
            }

            if (!string.Equals(
                    Path.GetFullPath(Directory.GetCurrentDirectory()),
                    Path.GetFullPath(CoreDirectory),
                    StringComparison.OrdinalIgnoreCase)) {
                Directory.SetCurrentDirectory(CoreDirectory);
            }

            Environment.SetEnvironmentVariable("UTSL_RUNTIME_ROOT", CoreDirectory);
            Console.WriteLine($"[Layout] runtime root = {CoreDirectory}");
        }
    }
}
