using System.Collections.Immutable;
using TShockAPI.Commanding;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Servers;

namespace TShockAPI;

/// <summary>
/// 【本项目补丁】以"服务端控制台身份"执行一条指令，并把真实输出交回调用方。
///
/// 为什么需要它：UTSL 自己的控制台走的是
///   CreateDispatchRequest + BeginTerminalDispatchActivityScope + CompleteTerminalDispatch
/// 这一整套（其中 activity scope 与 TShock 专用的执行上下文都是 internal，外部插件无法复现），
/// 所以插件侧无论怎么拼请求都匹配不到 V2 指令（实测 /help、/time 一律 Matched=false）。
///
/// 用法（外部管理器/插件）：
///   if (TShockConsoleCommand.TryExecute("/help", out var lines)) { ... lines 就是输出 ... }
/// 传 server 则以"该世界的控制台"身份执行（世界相关指令也适用），传 null 则为启动器控制台身份。
/// </summary>
public static class TShockConsoleCommand
{
    /// <returns>true = 指令已被执行（V2 命中或调度器内部老式兜底）；false = 什么都没执行。</returns>
    public static bool TryExecute(string rawInput, out IReadOnlyList<string> lines, ServerContext? server = null)
    {
        lines = [];
        if (string.IsNullOrWhiteSpace(rawInput)) { return false; }

        try
        {
            // ★ 与 UTSL 控制台完全一致：SourceServer(=server) + UserId=255
            CommandExecutor executor = new(server, byte.MaxValue);
            bool silent = TSCommandBridge.IsSilentInvocation(rawInput);
            CommandDispatchRequest request = TSCommandBridge.CreateDispatchRequest(
                executor, TerminalCommandEndpoint.EndpointId, rawInput, silent);

            using TSConsoleActivityScope scope = TSCommandBridge.BeginTerminalDispatchActivityScope(request, CancellationToken.None);
            CancellationToken ct = scope.Activity is null ? CancellationToken.None : scope.CancellationToken;
            CommandDispatchResult result = CommandDispatchCoordinator.DispatchAsync(request, ct).GetAwaiter().GetResult();

            // 先取输出，再照控制台的做法把它写出去（日志/游戏内仍可见）
            if (result.Outcome is not null)
            {
                List<string> collected = [];
                foreach (CommandReceipt receipt in result.Outcome.Receipts)
                {
                    if (!string.IsNullOrWhiteSpace(receipt.Message)) { collected.Add(receipt.Message); }
                }
                lines = collected;
            }

            try { TSCommandBridge.CompleteTerminalDispatch(executor, request, result); } catch { }

            // ★ 返回 Handled 而不是 Matched：调度器内部**自带老式命令兜底**（见 TShockTerminalCommandDispatchAdapter
            //   的 UsesSuccessfulLegacyFallback）—— 老式指令会被兜底执行一次但 Matched=false，
            //   调用方若按 Matched 判断"没执行"，就会再执行一遍（实测 /cg templates 输出两次）。
            //   Handled=true 表示"已经执行过了"，调用方据此决定别再补执行。
            return result.Handled;
        }
        catch (Exception ex)
        {
            // 不静默吞：失败时留一行日志，否则调用方只会看到"没执行"，排障时无从下手
            try { TShock.Log.Warning("[TShockConsoleCommand] 执行失败: " + ex.Message); } catch { }
            return false;
        }
    }
}
