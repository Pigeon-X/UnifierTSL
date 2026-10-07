using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PGame.UTSLManager;

public sealed class ManagedServer : INotifyPropertyChanged
{
    private Process? _process;
    private StreamWriter? _stdin;
    private bool _isRunning;
    private string _statusText = "已停止";
    private DateTime? _startedAt;

    public ManagedServer(ServerProfile profile)
    {
        Profile = profile;
        Document = new FlowDocument(new Paragraph())
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12.5
        };
        AppendSystemLine($"已加载配置：{RootPath}");
    }

    public ServerProfile Profile { get; }
    public FlowDocument Document { get; }

    public string Name => Profile.Name;
    public string RootPath
    {
        get
        {
            var path = Expand(Profile.RootPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return AppContext.BaseDirectory;
            }

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path);
        }
    }
    public string ExecutablePath
    {
        get
        {
            var executable = Expand(Profile.Executable);
            return Path.IsPathRooted(executable)
                ? executable
                : Path.Combine(RootPath, executable);
        }
    }

    public string Arguments => Profile.Arguments ?? "";
    public string Remark => Profile.Remark ?? "";
    public string ConfigPath => Path.Combine(RootPath, "config", "config.json");
    public string PluginsPath => Path.Combine(RootPath, "plugins");
    public string LogsPath => Path.Combine(RootPath, "logs");
    public string ProfilePath => RootPath;
    public string DisplayPath => RootPath;
    public int PortNumber => ResolvePort();
    public string PortText => PortNumber > 0 ? PortNumber.ToString() : "-";
    public string ProcessIdText => _process?.Id.ToString() ?? "-";
    public string UptimeText
    {
        get
        {
            if (_startedAt == null || !IsRunning)
            {
                return "-";
            }

            var span = DateTime.UtcNow - _startedAt.Value;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(ProcessIdText));
            OnPropertyChanged(nameof(UptimeText));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBrush => IsRunning
        ? ResourceBrush("Ok", Brushes.LightGreen)
        : ResourceBrush("TextDim", Brushes.Gray);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ManagedServer>? TextChanged;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException(
                $"找不到 UnifierTSL 可执行文件：{ExecutablePath}。请编辑 manager.json 的 rootPath / executable。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = Arguments,
            WorkingDirectory = RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => AppendProcessLine(e.Data, false);
        process.ErrorDataReceived += (_, e) => AppendProcessLine(e.Data, true);
        process.Exited += (_, _) => OnProcessExited(process);

        _process = process;
        _startedAt = DateTime.UtcNow;
        IsRunning = true;
        StatusText = "运行中";
        AppendSystemLine($"> {ExecutablePath} {Arguments}".TrimEnd());
        process.Start();
        _stdin = process.StandardInput;
        _stdin.AutoFlush = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        OnPropertyChanged(nameof(ProcessIdText));
        OnPropertyChanged(nameof(PortText));
    }

    public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        var port = PortNumber;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (_process == null || _process.HasExited)
            {
                return false;
            }

            if (port > 0 && await CanConnectAsync(port, cancellationToken).ConfigureAwait(false))
            {
                StatusText = "已就绪";
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        var process = _process;
        if (process == null)
        {
            IsRunning = false;
            StatusText = "已停止";
            return;
        }

        StatusText = "正在停止";
        try
        {
            if (_stdin != null)
            {
                await _stdin.WriteLineAsync("stop");
                await _stdin.FlushAsync();
            }
        }
        catch
        {
            // 进程可能已经退出，继续走强制停止兜底。
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch
            {
                // 退出事件会负责统一更新状态。
            }
        }
    }

    public void SendText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _stdin == null || !IsRunning)
        {
            return;
        }

        var command = text.TrimEnd();
        AppendSystemLine($"> {command}");
        _stdin.WriteLine(command);
        _stdin.Flush();
    }

    public void AppendSystemLine(string text)
    {
        AppendLine(text, ResourceBrush("LogCmd", Brushes.Gold));
    }

    public void AppendError(string text)
    {
        AppendLine(text, ResourceBrush("LogError", Brushes.Red));
    }

    private void AppendProcessLine(string? text, bool isError)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AppendLine(text, isError ? ResourceBrush("LogError", Brushes.Red) : ColorFor(text));
    }

    private void AppendLine(string text, Brush foreground)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            var paragraph = (Paragraph)Document.Blocks.FirstBlock!;
            paragraph.Inlines.Add(new Run(text + Environment.NewLine) { Foreground = foreground });
            while (paragraph.Inlines.Count > 4096 && paragraph.Inlines.FirstInline != null)
            {
                paragraph.Inlines.Remove(paragraph.Inlines.FirstInline);
            }

            TextChanged?.Invoke(this);
        }));
    }

    private void OnProcessExited(Process process)
    {
        if (!ReferenceEquals(_process, process))
        {
            return;
        }

        _process = null;
        _stdin = null;
        _startedAt = null;
        IsRunning = false;
        StatusText = "已停止";
        AppendSystemLine($"进程已退出，退出码：{SafeExitCode(process)}");
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private int ResolvePort()
    {
        var args = Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "-port", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[i + 1], out var port))
                {
                    return port;
                }
            }
        }

        try
        {
            if (!File.Exists(ConfigPath))
            {
                return -1;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            return ReadInt(document.RootElement, "listenPort") is var port && port > 0
                ? port
                : ReadInt(document.RootElement, "port");
        }
        catch
        {
            return -1;
        }
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.Number &&
                           property.Value.TryGetInt32(out var value)
                        ? value
                        : -1;
                }

                var nested = ReadInt(property.Value, propertyName);
                if (nested > 0)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadInt(item, propertyName);
                if (nested > 0)
                {
                    return nested;
                }
            }
        }

        return -1;
    }

    private static string Expand(string value)
    {
        return Environment.ExpandEnvironmentVariables(value ?? "");
    }

    private static Brush ColorFor(string line)
    {
        var text = line.TrimStart();
        if (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("失败") ||
            text.Contains("错误"))
        {
            return ResourceBrush("LogError", Brushes.Red);
        }

        if (text.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("警告") ||
            text.Contains("超时"))
        {
            return ResourceBrush("LogWarn", Brushes.Orange);
        }

        if (text.Contains("启动成功") ||
            text.Contains("已就绪") ||
            text.Contains("started successfully", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("listening", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceBrush("LogOk", Brushes.LightGreen);
        }

        if (text.StartsWith(">", StringComparison.Ordinal))
        {
            return ResourceBrush("LogCmd", Brushes.DeepSkyBlue);
        }

        return ResourceBrush("LogNormal", Brushes.Gold);
    }

    private static Brush ResourceBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    public void NotifyOverviewChanged()
    {
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(ProcessIdText));
        OnPropertyChanged(nameof(PortText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
