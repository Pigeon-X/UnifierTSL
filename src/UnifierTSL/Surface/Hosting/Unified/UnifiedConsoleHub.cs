using System.Collections.Generic;
using System.Text;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Hosting.Unified;

/// <summary>
/// 单窗口多世界控制台（"内置可切换控制台"）。
///
/// 目标：启动 UnifierTSL 跑多世界时【只留启动器这一个窗口】，
///   1) 每个世界的控制台输出汇总到本窗口（非当前世界加 [世界名] 前缀）；
///   2) 提供一个"当前世界"指针，用户在同一个窗口里用 1/2/3 或 w 系列指令切换；
///   3) 输入行按"当前世界"路由回该世界的控制台输入线程，从而与"每个世界一个窗口"完全等价地执行指令。
///
/// 只由 ServerContext.ConsoleMode.Unified 使用；PerWorld/Headless 行为不变。
/// </summary>
public sealed class UnifiedConsoleHub
{
    public const string SwitchPrefix = "w";     // w 1 / w next / w prev / w 资源 / w all
    public const string GlobalScopeToken = "all";

    private const int MaxBufferedLines = 400;   // 每个世界保留的回放行数（切换时回显）
    private const int ReplayLines = 30;         // 切换时回显的行数

    private static readonly UnifiedConsoleHub instance = new();
    public static UnifiedConsoleHub Instance => instance;

    private readonly object gate = new();
    private readonly List<Entry> entries = [];
    private Entry? active;

    public sealed class Entry
    {
        internal Entry(ServerContext server) => Server = server;

        public ServerContext Server { get; }
        public string Name => Server.Name;
        public int Index { get; internal set; }
        public bool IsActive { get; internal set; }

        internal readonly List<string> Lines = [];
        internal readonly Queue<string> Pending = new();
        internal readonly SemaphoreSlim Signal = new(0);
        internal bool AtLineStart = true;
        internal bool Closed;
        internal int ReadLineCalls;
        internal int ReadKeyCalls;
    }

    /// <summary>世界启动时登记（由 UnifiedServerSurfaceConsole 构造时调用）。</summary>
    public Entry Register(ServerContext server) {
        Entry entry;
        lock (gate) {
            entry = entries.Find(e => ReferenceEquals(e.Server, server));
            if (entry is null) {
                entry = new Entry(server);
                entries.Add(entry);
            }
            entry.Closed = false;
            Renumber();
            active ??= entry;
        }
        return entry;
    }

    /// <summary>世界关闭时注销。</summary>
    public void Unregister(ServerContext server) {
        bool wasActive;
        lock (gate) {
            Entry? entry = entries.Find(e => ReferenceEquals(e.Server, server));
            if (entry is null) {
                return;
            }
            entry.Closed = true;
            wasActive = ReferenceEquals(active, entry);
            entries.Remove(entry);
            Renumber();
            if (wasActive) {
                active = entries.Count > 0 ? entries[0] : null;
            }
        }
    }

    public string? ActiveName {
        get { lock (gate) { return active?.Name; } }
    }

    public IReadOnlyList<string> ServerNames {
        get { lock (gate) { return entries.ConvertAll(e => e.Name); } }
    }

    /// <summary>当前世界的输入提示符（供启动器输入循环使用）。</summary>
    public string CurrentPrompt {
        get {
            lock (gate) {
                return active is null ? "[UTSL] > " : $"[{active.Index}.{active.Name}] > ";
            }
        }
    }

    // ==================== 输出 ====================

    /// <summary>把某个世界的控制台文本写进单窗口。</summary>
    public void Write(ServerContext server, string? text, bool newLine) {
        if (string.IsNullOrEmpty(text) && !newLine) {
            return;
        }

        Entry? entry;
        lock (gate) {
            entry = entries.Find(e => ReferenceEquals(e.Server, server));
        }

        if (entry is null || entry.Closed) {
            // 未登记（例如世界正在启动/关闭）：仍然输出，避免丢日志
            UnifiedConsoleOutput.WriteRaw(server.Name, text, newLine);
            return;
        }

        string? prefixed;
        lock (gate) {
            prefixed = Prefix(entry, text ?? string.Empty);
        }

        lock (entry.Lines) {
            foreach (string line in SplitLines(prefixed ?? string.Empty, newLine)) {
                entry.Lines.Add(line);
            }
            if (entry.Lines.Count > MaxBufferedLines) {
                entry.Lines.RemoveRange(0, entry.Lines.Count - MaxBufferedLines);
            }
        }

        if (!string.IsNullOrEmpty(prefixed)) {
            UnifiedConsoleOutput.Write(prefixed);
        }
        if (newLine) {
            UnifiedConsoleOutput.Write(Environment.NewLine);
        }
    }

    /// <summary>给非当前世界的每一行加 [世界名] 前缀。</summary>
    private string Prefix(Entry entry, string text) {
        if (entry.IsActive) {
            return text;
        }

        string tag = "[" + entry.Name + "] ";
        if (text.Length == 0) {
            return entry.AtLineStart ? string.Empty : string.Empty;
        }

        var sb = new StringBuilder(text.Length + tag.Length);
        foreach (char ch in text) {
            if (entry.AtLineStart && ch != '\r' && ch != '\n') {
                sb.Append(tag);
                entry.AtLineStart = false;
            }
            sb.Append(ch);
            if (ch == '\n') {
                entry.AtLineStart = true;
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitLines(string text, bool newLine) {
        string[] parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++) {
            string part = parts[i].TrimEnd('\r');
            if (i == parts.Length - 1 && !newLine) {
                if (part.Length > 0) {
                    yield return part;
                }
                yield break;
            }
            yield return part;
        }
    }

    // ==================== 输入 ====================

    /// <summary>世界控制台输入线程调用：只有"当前世界"才拿得到输入行。</summary>
    public string ReadLineFor(ServerContext server) {
        while (true) {
            Entry? entry;
            bool isActive;
            lock (gate) {
                entry = entries.Find(e => ReferenceEquals(e.Server, server));
                isActive = entry is not null && ReferenceEquals(active, entry);
            }

            if (entry is null || entry.Closed) {
                Thread.Sleep(250);
                continue;
            }

            entry.ReadLineCalls++;
            if (entry.ReadLineCalls == 1) {
                Diagnostic("[" + entry.Name + "] 控制台输入线程已接入（ReadLine 首次调用）");
            }

            if (!isActive) {
                // 非当前世界：不抢输入，等待被切换为当前世界
                entry.Signal.Wait(250);
                continue;
            }

            if (entry.Signal.Wait(250)) {
                string? line;
                lock (gate) {
                    line = entry.Pending.Count > 0 ? entry.Pending.Dequeue() : null;
                }
                if (line is not null) {
                    return line;
                }
            }
        }
    }

    /// <summary>是否启用了单窗口模式（供启动器输入循环选择提示符/路由）。</summary>
    public static bool IsEnabled => ServerContext.DefaultConsoleMode == ServerContext.ConsoleMode.Unified;

    /// <summary>
    /// 启动器窗口收到一行输入：先判"世界切换"，否则按当前世界路由。
    /// 返回 true = 本行已被单窗口控制台消费；返回 false = 调用方按旧行为处理（协调器级分发）。
    /// </summary>
    public bool TryConsumeLauncherInput(string? rawLine) {
        string line = (rawLine ?? string.Empty).Trim();
        if (line.Length == 0) {
            return false;
        }

        if (TrySwitch(line, out string switchReport)) {
            RenderPanel(switchReport);
            return true;
        }

        Entry? target;
        lock (gate) {
            target = active;
        }

        if (target is null) {
            // 没有当前世界（只有 /w all 会这样）：交回协调器级分发
            return false;
        }

        lock (gate) {
            target.Pending.Enqueue(line);
        }
        target.Signal.Release();
        return true;
    }

    /// <summary>切换当前世界：1/2/3、w 1、w next、w prev、w 名字、w all（回到协调器级）。</summary>
    public bool TrySwitch(string line, out string report) {
        report = string.Empty;
        string token = line.Trim();

        if (!int.TryParse(token, out _)) {
            string[] parts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], SwitchPrefix, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            token = parts.Length > 1 ? string.Join(' ', parts[1..]) : "next";
        }

        lock (gate) {
            if (entries.Count == 0) {
                report = "当前没有已启动的世界。";
                return true;
            }

            if (string.Equals(token, GlobalScopeToken, StringComparison.OrdinalIgnoreCase)) {
                active = null;
                report = "已切到【协调器级】输入：指令不再绑定单个世界（等价旧行为）。";
                return true;
            }

            if (string.Equals(token, "next", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "prev", StringComparison.OrdinalIgnoreCase)) {
                if (active is null) {
                    active = entries[0];
                }
                else {
                    int i = entries.IndexOf(active);
                    int step = string.Equals(token, "next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                    active = entries[(i + step + entries.Count) % entries.Count];
                }
                report = "已切到 " + Descriptor(active!);
                return true;
            }

            if (int.TryParse(token, out int index)) {
                Entry? found = entries.Find(e => e.Index == index);
                if (found is null) {
                    report = $"没有编号为 {index} 的世界。";
                    return true;
                }
                active = found;
                report = "已切到 " + Descriptor(found);
                return true;
            }

            Entry? byName = entries.Find(e => e.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (byName is null) {
                report = $"找不到世界「{token}」。";
                return true;
            }
            active = byName;
            report = "已切到 " + Descriptor(byName);
            return true;
        }
    }

    private static string Descriptor(Entry entry) => $"[{entry.Index}.{entry.Name}]";

    /// <summary>诊断输出（写 stderr，不进单窗口画面）。</summary>
    public static void Diagnostic(string message) {
        try {
            System.Console.Error.WriteLine("[UnifiedConsole] " + message);
        }
        catch {
        }
    }

    private void Renumber() {
        for (int i = 0; i < entries.Count; i++) {
            entries[i].Index = i + 1;
            entries[i].IsActive = false;
        }
        if (active is not null) {
            active.IsActive = true;
        }
    }

    // ==================== 渲染 ====================

    /// <summary>画一次"世界切换面板"（标题行 + 当前世界最近输出）。</summary>
    public void RenderPanel(string? notice = null) {
        string header;
        string list;
        Entry? current;
        lock (gate) {
            Renumber();
            current = active;
            list = entries.Count == 0
                ? "(无)"
                : string.Join("   ", entries.ConvertAll(e => e.IsActive ? $"[{e.Index}] {e.Name} ←" : $"[{e.Index}] {e.Name}"));
            header = current is null
                ? "当前: 协调器级（全局）"
                : $"当前: [{current.Index}] {current.Name}";
        }

        var sb = new StringBuilder();
        sb.Append(Environment.NewLine);
        sb.Append("======================= UnifierTSL 内置控制台 =======================").Append(Environment.NewLine);
        sb.Append(" 世界: ").Append(list).Append(Environment.NewLine);
        sb.Append(' ').Append(header).Append(Environment.NewLine);
        if (!string.IsNullOrEmpty(notice)) {
            sb.Append(" ").Append(notice).Append(Environment.NewLine);
        }
        sb.Append(" 切换: 直接输入编号 1/2/3  或  w next / w prev / w <名字> / w all(全局)").Append(Environment.NewLine);
        sb.Append(" 输入其它内容 = 在当前世界的控制台里执行").Append(Environment.NewLine);
        sb.Append("====================================================================").Append(Environment.NewLine);

        if (current is not null) {
            List<string> snapshot;
            lock (current.Lines) {
                int take = Math.Min(ReplayLines, current.Lines.Count);
                snapshot = current.Lines.GetRange(current.Lines.Count - take, take);
            }
            if (snapshot.Count > 0) {
                sb.Append(Environment.NewLine).Append($"----- {current.Name} 最近 {snapshot.Count} 行 -----").Append(Environment.NewLine);
                foreach (string line in snapshot) {
                    sb.Append(line).Append(Environment.NewLine);
                }
            }
        }

        UnifiedConsoleOutput.Write(sb.ToString());
    }
}