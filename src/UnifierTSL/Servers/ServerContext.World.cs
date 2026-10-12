using Terraria;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        /// <summary>
        /// ★ 世界热换：把**已经存在**的这个 ServerContext 换成另一个世界的数据（不重启进程、不新建 context）。
        ///
        /// 为什么需要它（P0 实测结论，详见 UTSL 开发历史整理 §5.3.6）：
        ///   \`ServerContext\` 一旦创建，会被 Terraria 的静态注册表（\`ConditionalDialogue._registry\`、
        ///   \`Testing.StateSnapshot\`）钉住，**GC 永远回收不掉** →
        ///   随身世界（PocketWorld）不能"每个玩家建一个世界"，只能**固定若干实例轮流复用**，
        ///   于是必须能在同一个实例上换世界。
        ///
        /// 原理（子代理反编译 OTAPI.USP 确认）：USP 把 Terraria 静态类拆成「静态门面 + 每实例 XxxSystemContext」，
        ///   世界数据（maxTilesX/Y、tile、ActiveWorldFileData、worldName、player[]）**全是实例字段** →
        ///   所以换世界 = 改实例字段 + 重新读一次 .wld，不涉及任何静态状态，**也不新增泄漏** ✓
        ///
        /// 两步就是全部：\`provider.ApplyMetadata(this)\`（选世界）→ \`WorldGen.serverLoadWorld()\`（读进当前 Main）。
        /// </summary>
        /// <param name="next">新的世界数据源（每次传新实例即可，\`IWorldDataProvider\` 无需改动）。</param>
        /// <param name="saveCurrent">换之前是否先保存当前世界（默认 true；否则旧世界的改动会丢）。</param>
        /// <param name="allowOnline">
        /// 是否允许在该 world 还有玩家时换世界。默认 false（**强烈建议保持 false**）：
        /// 玩家仍在时换世界，客户端拿到的会是「旧地形 + 新区块覆盖」，不是干净重载。
        /// </param>
        public void SwitchWorld(IWorldDataProvider next, bool saveCurrent = true, bool allowOnline = false)
        {
            ArgumentNullException.ThrowIfNull(next);

            // R4：必须在本 context 自己的更新线程上执行 —— Main.__rand/_rngs 是 [ThreadStatic]，
            //     只在 RunBlocking 线程由 ThreadLocalInitializer 初始化；在别的线程动世界会踩到错的 RNG。
            if (!Dispatcher.CheckAccess())
            {
                throw new InvalidOperationException(
                    "SwitchWorld must run on this server's update thread (use Dispatcher.InvokeAsync).");
            }

            // R3：clearWorld 不碰 Main.player[] → 有玩家时换世界必须由调用方自己把玩家搬走/重同步。
            if (!allowOnline && ActivePlayerCount > 0)
            {
                throw new InvalidOperationException(
                    "SwitchWorld requires an idle ServerContext; players are still routed to it.");
            }

            // R6：换之前先存盘，否则旧世界的改动全丢。
            if (saveCurrent && !string.IsNullOrEmpty(Main.worldPathName))
            {
                WorldFile.SaveWorld(resetTime: false, useTemps: true);
            }

            // 顺序必须是「先 ApplyMetadata 再 LoadWorld」：worldPathName 是 ActiveWorldFileData.Path 的只读投影。
            next.ApplyMetadata(this);
            WorldDataProvider = next;

            // 服务器正规加载路径 = LoadWorld + 重试/回滚 + SetOngoingToTemps + Hooks.WorldLoaded。
            // R5：它是异步 Task（跑线程池），期间主循环可能看到半加载世界 → 调用方应避免此时放玩家进来。
            WorldGen.serverLoadWorld().GetAwaiter().GetResult();

            if (WorldGen.loadFailed)
            {
                throw WorldFile.LastThrownLoadException
                    ?? new InvalidOperationException("LoadWorld failed without an exception.");
            }

            // R7：通知插件清掉 per-world 缓存（否则会拿上一个世界的数据处理新世界）。
            UnifierApi.EventHub.Server.WorldSwitched.Invoke(new(this, next));
        }

        /// <summary>
        /// 确保磁盘上存在一个【同尺寸的空白世界文件】，用于世界池（PocketWorld）。
        ///
        /// 为什么需要它：`GenerateWorld` 生成一个 4200x2400 的原版世界要很久（实测玩家侧会"启动超时"），
        /// 而随身世界实验阶段只需要一个**尺寸正确、地形全空**的世界。
        /// 做法：设好 maxTilesX/Y → `ResetWorldSize()` 按新尺寸重建 tile 数组 → `clearWorld()` 清空 →
        ///       `SaveWorld()` 写盘。之后用 `GenerateOrLoadExisting`/`FromBytes` 加载就是**纯读文件**，秒级完成。
        ///
        /// ★ 必须在本 context 的更新线程上调用（与 SwitchWorld 同理）。
        /// </summary>
        /// <param name="worldName">世界名（决定落盘路径）。</param>
        /// <param name="tileW">宽度（像素格数）。</param>
        /// <param name="tileH">高度（必须是 150 的整数倍）。</param>
        /// <returns>该世界文件路径。</returns>
        public string EnsureBlankWorld(string worldName, int tileW, int tileH)
        {
            if (!Dispatcher.CheckAccess())
            {
                throw new InvalidOperationException(
                    "EnsureBlankWorld must run on this server's update thread (use Dispatcher.InvokeAsync).");
            }

            if (tileW <= 0 || tileH <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileW), "World dimensions must be positive.");
            }

            string path = Utilities.IO.GetWorldPathFromName(worldName, true);
            if (File.Exists(path))
            {
                return path;
            }

            // 1) 设好尺寸与元数据（ApplyMetadata 会把 worldName / ActiveWorldFileData 指到这个路径）
            Main.worldName = worldName;
            Main.maxTilesX = tileW;
            Main.maxTilesY = tileH;
            Main.autoGen = false;
            Main.ActiveWorldFileData = WorldFile.CreateMetadata(worldName, false, Main.GameMode);

            // 2) 按新尺寸重建 tile 数组，然后整体清空
            WorldGen.ResetWorldSize();
            WorldGen.clearWorld();

            // 3) 写盘（之后加载就是纯读文件）
            WorldFile.SaveWorld(resetTime: false, useTemps: false);

            Log.Info(category: "World", message: $"Blank world written: {worldName} ({tileW}x{tileH}) -> {path}");
            return path;
        }

        /// <summary>
        /// 在**空白世界**里造一个最小的"能站人的出生平台"。
        ///
        /// 为什么需要：全空气世界玩家进去会一直往下掉（没法站着看东西），实验阶段很不方便。
        /// 这里只放一层实心方块 + 出生点，不做地形生成（依然秒级）。
        ///
        /// ★ 必须在本 context 的更新线程上调用；也必须在 EnsureBlankWorld/SwitchWorld 之后调用
        ///   （此时 Main.tile 已经是新世界尺寸的数组）。
        /// </summary>
        /// <param name="halfWidth">平台半宽（方块数）。</param>
        public void BuildSpawnPlatform(int halfWidth = 20)
        {
            if (!Dispatcher.CheckAccess())
            {
                throw new InvalidOperationException(
                    "BuildSpawnPlatform must run on this server's update thread.");
            }

            int cx = Math.Clamp(Main.maxTilesX / 2, halfWidth + 2, Main.maxTilesX - halfWidth - 2);
            int cy = Math.Clamp(Main.maxTilesY / 2, 4, Main.maxTilesY - 4);

            // 一层实心地面（用石块，稳定、不需要支撑判定）
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
            {
                WorldGen.PlaceTile(x, cy, Terraria.ID.TileID.Stone, mute: true, forced: true);
            }

            // 出生点放在地面上方
            Main.spawnTileX = cx;
            Main.spawnTileY = cy - 1;

            Log.Info(category: "World", message: $"Spawn platform built at ({cx},{cy}) halfWidth={halfWidth}");
        }
    }
}
