using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PGame.UTSLManager;

public partial class MainWindow : Window
{
    private readonly ManagerConfig _config;
    private readonly DispatcherTimer _statusTimer;
    private readonly ManagedServer?[] _consoleTargets = new ManagedServer?[3];
    private bool _closing;

    public ObservableCollection<ManagedServer> Servers { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        _config = ManagerConfig.Load();
        _config.Save();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += (_, _) => RefreshStatus();
    }

    private ManagedServer? Current => ServerCombo.SelectedItem as ManagedServer;

    private RichTextBox ConsoleTextBox(int index) => index switch
    {
        0 => Console1TextBox,
        1 => Console2TextBox,
        _ => Console3TextBox
    };

    private TextBlock ConsoleName(int index) => index switch
    {
        0 => Console1Name,
        1 => Console2Name,
        _ => Console3Name
    };

    private TextBlock ConsoleStatus(int index) => index switch
    {
        0 => Console1Status,
        1 => Console2Status,
        _ => Console3Status
    };

    private Ellipse ConsoleDot(int index) => index switch
    {
        0 => Console1Dot,
        1 => Console2Dot,
        _ => Console3Dot
    };

    private Button ConsoleStartButton(int index) => index switch
    {
        0 => Console1StartButton,
        1 => Console2StartButton,
        _ => Console3StartButton
    };

    private Button ConsoleStopButton(int index) => index switch
    {
        0 => Console1StopButton,
        1 => Console2StopButton,
        _ => Console3StopButton
    };

    private Button ConsoleConfigButton(int index) => index switch
    {
        0 => Console1ConfigButton,
        1 => Console2ConfigButton,
        _ => Console3ConfigButton
    };

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadServers();
        _statusTimer.Start();
    }

    private void ReloadServers()
    {
        var selectedName = Current?.Name;
        var running = Servers.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        Servers.Clear();

        foreach (var profile in _config.Servers.Where(s => s.Enabled))
        {
            if (running.TryGetValue(profile.Name, out var existing)
                && string.Equals(existing.RootPath, Expand(profile.RootPath), StringComparison.OrdinalIgnoreCase))
            {
                Servers.Add(existing);
                continue;
            }

            Servers.Add(new ManagedServer(profile));
        }

        if (Servers.Count == 0)
        {
            ServerCombo.ItemsSource = Servers;
            ServerCombo.SelectedIndex = -1;
            AssignConsoleSlots();
            UpdateActionStates();
            return;
        }

        var selectedIndex = selectedName == null
            ? 0
            : Math.Max(0, Servers
                .Select((server, index) => (server, index))
                .FirstOrDefault(pair => string.Equals(pair.server.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                .index);

        ServerCombo.SelectedIndex = selectedIndex;
        AssignConsoleSlots();
        ServerCombo_SelectionChanged(ServerCombo, null!);
    }

    private void RefreshStatus()
    {
        foreach (var server in Servers)
        {
            server.NotifyOverviewChanged();
        }

        RefreshConsoleHeaders();
    }

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var current = Current;
        if (current == null)
        {
            UpdateActionStates();
            return;
        }

        OverviewPanel.DataContext = current;
        UpdateActionStates();
    }

    private void AssignConsoleSlots()
    {
        for (var i = 0; i < _consoleTargets.Length; i++)
        {
            if (_consoleTargets[i] != null)
            {
                _consoleTargets[i]!.TextChanged -= ConsoleTextChanged;
            }

            var server = i < Servers.Count ? Servers[i] : null;
            _consoleTargets[i] = server;
            if (server != null)
            {
                server.TextChanged += ConsoleTextChanged;
            }

            var textBox = ConsoleTextBox(i);
            textBox.Document = server?.Document ?? CreateEmptyConsoleDocument(
                $"实例 {i + 1} 未配置。\n请在 manager.json 的 servers 列表中添加第 {i + 1} 个 UnifierTSL 实例。");
            textBox.ScrollToEnd();
        }

        RefreshConsoleHeaders();
    }

    private void RefreshConsoleHeaders()
    {
        for (var i = 0; i < _consoleTargets.Length; i++)
        {
            var server = _consoleTargets[i];
            ConsoleName(i).Text = server?.Name ?? $"实例 {i + 1}";
            ConsoleStatus(i).Text = server?.StatusText ?? "未配置";
            ConsoleDot(i).Fill = server?.StatusBrush ?? ResourceBrush("TextDim");
            ConsoleStartButton(i).IsEnabled = server != null && !server.IsRunning;
            ConsoleStopButton(i).IsEnabled = server?.IsRunning == true;
            ConsoleConfigButton(i).IsEnabled = server != null;
        }
    }

    private void ConsoleTextChanged(ManagedServer server)
    {
        for (var i = 0; i < _consoleTargets.Length; i++)
        {
            if (ReferenceEquals(_consoleTargets[i], server))
            {
                ConsoleTextBox(i).ScrollToEnd();
                break;
            }
        }
    }

    private static FlowDocument CreateEmptyConsoleDocument(string text)
    {
        var document = new FlowDocument(new Paragraph(new Run(text))
        {
            Foreground = Brushes.Gray
        })
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12.5
        };
        return document;
    }

    private static Brush ResourceBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private ManagedServer? SlotTarget(object sender)
    {
        if (sender is not Button button ||
            !int.TryParse(button.Tag?.ToString(), out var index) ||
            index < 0 ||
            index >= _consoleTargets.Length)
        {
            return null;
        }

        return _consoleTargets[index];
    }

    private void ConsolePanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border ||
            !int.TryParse(border.Tag?.ToString(), out var index) ||
            index < 0 ||
            index >= _consoleTargets.Length ||
            _consoleTargets[index] == null)
        {
            return;
        }

        ServerCombo.SelectedItem = _consoleTargets[index];
        UpdateActionStates();
    }

    private async void SlotStartButton_Click(object sender, RoutedEventArgs e)
    {
        var target = SlotTarget(sender);
        if (target == null)
        {
            return;
        }

        StartServer(target);
        RefreshConsoleHeaders();
        UpdateActionStates();
    }

    private async void SlotStopButton_Click(object sender, RoutedEventArgs e)
    {
        var target = SlotTarget(sender);
        if (target == null || !target.IsRunning || !ConfirmStop(target.Name))
        {
            return;
        }

        await StopServerAsync(target);
        RefreshConsoleHeaders();
        UpdateActionStates();
    }

    private void SlotOpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var target = SlotTarget(sender);
        if (target != null)
        {
            OpenPath(target.ConfigPath);
        }
    }

    private void WorkspacePage_Checked(object sender, RoutedEventArgs e)
    {
        var showOverview = OverviewPageButton?.IsChecked == true;
        if (ConsolePage != null)
        {
            ConsolePage.Visibility = showOverview ? Visibility.Collapsed : Visibility.Visible;
        }

        if (OverviewPage != null)
        {
            OverviewPage.Visibility = showOverview ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CommandBar != null)
        {
            CommandBar.Visibility = showOverview ? Visibility.Collapsed : Visibility.Visible;
        }

        if (showOverview)
        {
            OverviewPanel.DataContext = Current;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Current == null)
        {
            return;
        }

        StartServer(Current);
        UpdateActionStates();
    }

    private async void KillButton_Click(object sender, RoutedEventArgs e)
    {
        if (Current == null || !Current.IsRunning)
        {
            return;
        }

        if (!ConfirmStop(Current.Name))
        {
            return;
        }

        await StopServerAsync(Current);
        UpdateActionStates();
    }

    private async void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAllSequentialAsync();
    }

    private async Task StartAllSequentialAsync()
    {
        foreach (var server in Servers.Where(s => !s.IsRunning))
        {
            try
            {
                server.Start();
                if (_config.StartAllSequential)
                {
                    var ready = await server.WaitUntilReadyAsync(
                        TimeSpan.FromSeconds(Math.Max(10, _config.StartReadyTimeoutSeconds)));
                    if (!ready)
                    {
                        server.AppendError("[启动] 等待端口就绪超时，继续下一台实例。");
                    }
                }
            }
            catch (Exception ex)
            {
                server.AppendError($"[启动失败] {ex.Message}");
                MessageBox.Show(ex.Message, $"启动失败：{server.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateActionStates();
        }

        UpdateActionStates();
    }

    private async void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Servers.Any(s => s.IsRunning) || !ConfirmStop("全部 UnifierTSL 实例"))
        {
            return;
        }

        foreach (var server in Servers.Where(s => s.IsRunning).ToList())
        {
            await StopServerAsync(server);
        }

        UpdateActionStates();
    }

    private void StartServer(ManagedServer server)
    {
        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            server.AppendError($"[启动失败] {ex.Message}");
            MessageBox.Show(ex.Message, $"启动失败：{server.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopServerAsync(ManagedServer server)
    {
        try
        {
            await server.StopAsync(TimeSpan.FromSeconds(Math.Max(1, _config.StopTimeoutSeconds)));
        }
        catch (Exception ex)
        {
            server.AppendError($"[停止失败] {ex.Message}");
        }
    }

    private bool ConfirmStop(string name)
    {
        return MessageBox.Show(
            $"确定要停止 {name} 吗？\n如果实例正在运行，相关世界和玩家连接会中断。",
            "确认停止",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void UpdateActionStates()
    {
        var current = Current;
        var hasRunning = Servers.Any(s => s.IsRunning);
        var hasStopped = Servers.Any(s => !s.IsRunning);

        if (StartButton != null)
        {
            StartButton.IsEnabled = current != null && !current.IsRunning;
        }

        if (KillButton != null)
        {
            KillButton.IsEnabled = current?.IsRunning == true;
        }

        if (StartAllButton != null)
        {
            StartAllButton.IsEnabled = hasStopped;
        }

        if (StopAllButton != null)
        {
            StopAllButton.IsEnabled = hasRunning;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadServers();
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e) => OpenCurrentPath(s => s.RootPath);

    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenCurrentPath(s => s.ConfigPath);

    private void OpenPlugins_Click(object sender, RoutedEventArgs e) => OpenCurrentPath(s => s.PluginsPath);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenCurrentPath(s => s.LogsPath);

    private void OpenManagerConfig_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(ManagerConfig.ConfigPath);
    }

    private void OpenCurrentPath(Func<ManagedServer, string> selector)
    {
        if (Current == null)
        {
            return;
        }

        OpenPath(selector(Current));
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return;
            }

            MessageBox.Show($"路径不存在：\n{path}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyLaunchCommand_Click(object sender, RoutedEventArgs e)
    {
        if (Current == null)
        {
            return;
        }

        var command = $"\"{Current.ExecutablePath}\" {Current.Arguments}".TrimEnd();
        Clipboard.SetText(command);
        Current.AppendSystemLine("[工具] 启动命令已复制到剪贴板。");
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "PGame-UTSLManager 1.2.0\n\n" +
            "TSM 风格的单 EXE 多实例管理界面。\n" +
            "支持 3 个控制台同屏、独立启停、配置入口和运行概览。",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(TextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        SendCurrentCommand();
    }

    private void SendCurrentButton_Click(object sender, RoutedEventArgs e) => SendCurrentCommand();

    private void SendCurrentCommand()
    {
        var current = Current;
        if (current == null || !current.IsRunning || string.IsNullOrWhiteSpace(TextBox.Text))
        {
            return;
        }

        current.SendText(TextBox.Text);
        TextBox.Clear();
    }

    private void SendAllButton_Click(object sender, RoutedEventArgs e)
    {
        var text = TextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var server in Servers.Where(s => s.IsRunning))
        {
            server.SendText(text);
        }

        TextBox.Clear();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        if (!Servers.Any(server => server.IsRunning))
        {
            _statusTimer.Stop();
            return;
        }

        var result = MessageBox.Show(
            "关闭管理器会一并停止所有由它启动的 UnifierTSL 实例。\n是否继续关闭？",
            "确认退出",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closing = true;
        _statusTimer.Stop();
        foreach (var server in Servers.Where(server => server.IsRunning).ToList())
        {
            await StopServerAsync(server);
        }

        Close();
    }

    private static string Expand(string value)
    {
        return Environment.ExpandEnvironmentVariables(value ?? "");
    }
}
