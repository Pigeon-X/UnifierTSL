using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PigeonUnifierTSL.Manager;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                "界面发生未处理异常：\n\n" + e.Exception.Message,
                "Pigeon UnifierTSL Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void ApplyTheme()
    {
        var light = IsLightTheme();
        var resources = Current.Resources;

        void Brush(string key, string color) =>
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        void ColorValue(string key, string color) =>
            resources[key] = (Color)ColorConverter.ConvertFromString(color);

        if (light)
        {
            ColorValue("Backdrop1", "#EFE9FF");
            ColorValue("Backdrop2", "#E6F0FF");
            ColorValue("Backdrop3", "#F7EDFF");
            ColorValue("Blob1", "#59C4B5FD");
            ColorValue("Blob2", "#4F93C5FD");
            Brush("GlassBg", "#B3FFFFFF");
            Brush("GlassBorder", "#4D7C6BC8");
            Brush("GlassHi", "#80FFFFFF");
            Brush("GlassPanel", "#99FFFFFF");
            Brush("GlassPanelHi", "#E6FFFFFF");
            Brush("ConsoleBg", "#F207070E");
            Brush("AccentBorder", "#66A855F7");
            Brush("Bg", "#F6F5FB");
            Brush("Panel", "#FFFFFF");
            Brush("PanelHi", "#ECEAF6");
            Brush("Border", "#D5D1E8");
            Brush("Text", "#1C1A2B");
            Brush("TextDim", "#6E6A88");
            Brush("InputBg", "#FFFFFF");
            Brush("Accent", "#7C3AED");
            Brush("Accent2", "#A855F7");
            Brush("AccentBlue", "#2563EB");
            Brush("AccentText", "#FFFFFF");
            Brush("ScrollThumb", "#99A78BEA");
            Brush("ScrollThumbHot", "#7C3AED");
            Brush("BtnText", "#FFFFFF");
        }
        else
        {
            ColorValue("Backdrop1", "#17123A");
            ColorValue("Backdrop2", "#0D1030");
            ColorValue("Backdrop3", "#1C1138");
            ColorValue("Blob1", "#8C7C3AED");
            ColorValue("Blob2", "#732563EB");
            Brush("GlassBg", "#1FFFFFFF");
            Brush("GlassBorder", "#3DFFFFFF");
            Brush("GlassHi", "#1FFFFFFF");
            Brush("GlassPanel", "#14FFFFFF");
            Brush("GlassPanelHi", "#24FFFFFF");
            Brush("ConsoleBg", "#CC05050C");
            Brush("AccentBorder", "#59A855F7");
            Brush("Bg", "#0E0D16");
            Brush("Panel", "#171625");
            Brush("PanelHi", "#221F35");
            Brush("Border", "#342F4D");
            Brush("Text", "#E8E6F5");
            Brush("TextDim", "#9B97B8");
            Brush("InputBg", "#3A3159");
            Brush("Accent", "#A855F7");
            Brush("Accent2", "#C084FC");
            Brush("AccentBlue", "#5B9DFF");
            Brush("AccentText", "#FFFFFF");
            Brush("ScrollThumb", "#66C4B5FD");
            Brush("ScrollThumbHot", "#A855F7");
            Brush("BtnText", "#101014");
        }

        Brush("Ok", "#66BB6A");
        Brush("Err", "#FF5252");
        Brush("Warn", "#FFB74D");
        Brush("Info", "#FFD23F");
        Brush("Cmd", "#4FC3F7");
    }

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void LogCrash(string kind, Exception? exception)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "manager-crash.log");
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 崩溃日志失败时不能再让 UI 退出流程继续抛异常。
        }
    }
}
