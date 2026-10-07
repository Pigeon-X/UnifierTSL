using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PigeonUnifierTSL.Manager;

public partial class MainWindow : Window
{
    private readonly ManagerConfig _config;
    private readonly DispatcherTimer _statusTimer;
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
            CliTextBox.Document = new FlowDocument(new Paragraph(new Run(
                "manager.json 没有启用的服务器实例。\n请点击“管理 → 编辑启动配置 manager.json”添加 UnifierTSL 目录。")));
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
        ServerCombo_SelectionChanged(ServerCombo, null!);
    }

    private void RefreshStatus()
    {
        foreach (var server in Servers)
        {
            server.NotifyOverviewChanged();
        }
    }

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var current = Current;
        if (current == null)
        {
            UpdateActionStates();
            return;
        }

        CliTextBox.Document = current.Document;
        OverviewPanel.DataContext = current;
        CliTextBox.ScrollToEnd();
        UpdateActionStates();
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
        foreach (var server in Servers.Where(s => !s.IsRunning))
        {
            try
            {
                server.Start();
                if (_config.StartAllSequential)
                {
                    await Task.Delay(1200);
                }
            }
            catch (Exception ex)
            {
                server.AppendError($"[启动失败] {ex.Message}");
                MessageBox.Show(ex.Message, $"启动失败：{server.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
            "Pigeon UnifierTSL Manager 1.0.0\n\n" +
            "面向个人维护分支的 TSM 风格 UnifierTSL 多实例管理界面。\n" +
            "支持启动/停止、控制台输出、启动参数、配置路径和插件目录管理。",
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
