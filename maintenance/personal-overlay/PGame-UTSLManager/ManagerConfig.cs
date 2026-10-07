using System.IO;
using System.Text.Json;

namespace PGame.UTSLManager;

public sealed class ManagerConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string ManagerName { get; set; } = "PGame-UTSLManager";
    public List<ServerProfile> Servers { get; set; } = [];
    public bool StartAllSequential { get; set; } = true;
    public int StopTimeoutSeconds { get; set; } = 6;
    public int StartReadyTimeoutSeconds { get; set; } = 120;

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "manager.json");

    public static ManagerConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = JsonSerializer.Deserialize<ManagerConfig>(
                    File.ReadAllText(ConfigPath),
                    JsonOptions);
                if (loaded != null)
                {
                    loaded.Servers ??= [];
                    if (loaded.Servers.Count == 0)
                    {
                        loaded.Servers.Add(CreateDefaultProfile());
                    }

                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认配置，并保留原文件供用户检查。
        }

        var fallback = new ManagerConfig();
        fallback.Servers.Add(CreateDefaultProfile());
        fallback.Save();
        return fallback;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // UI 不应因为 manager.json 写入失败而崩溃。
        }
    }

    private static ServerProfile CreateDefaultProfile()
    {
        return new ServerProfile
        {
            Name = "S1 生存服",
            RootPath = ".",
            Executable = "UnifierTSL.Core.exe",
            Arguments = "",
            Enabled = true,
            Remark = "由 UnifierTSL.exe 管理器调用同目录核心"
        };
    }
}

public sealed class ServerProfile
{
    public string Name { get; set; } = "UnifierTSL";
    public string RootPath { get; set; } = "";
    public string Executable { get; set; } = "UnifierTSL.exe";
    public string Arguments { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Remark { get; set; } = "";
}
