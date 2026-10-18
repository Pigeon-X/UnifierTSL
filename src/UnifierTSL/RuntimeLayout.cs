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

        /// <summary>配置、日志、插件与世界等用户数据固定在安装根目录。</summary>
        public static string DataDirectory => ExecutableDirectory;

        public static string AppDirectory =>
            Path.Combine(RuntimeDirectory, "app");

        public static string BinDirectory =>
            Path.Combine(RuntimeDirectory, BinDirectoryName);

        public static string TranslationsDirectory =>
            Path.Combine(RuntimeDirectory, "i18n");

        public static void Initialize() {
            if (!UsesCoreDirectory) {
                return;
            }

            Environment.SetEnvironmentVariable("UTSL_RUNTIME_ROOT", CoreDirectory);
            Environment.SetEnvironmentVariable("UTSL_DATA_ROOT", DataDirectory);
            Console.WriteLine($"[Layout] runtime root = {CoreDirectory}; data root = {DataDirectory}");
        }
    }
}
