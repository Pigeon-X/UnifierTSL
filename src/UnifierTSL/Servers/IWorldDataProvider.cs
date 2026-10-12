using Terraria.IO;

namespace UnifierTSL.Servers
{
    public interface IWorldDataProvider
    {
        string WorldName { get; }
        string WorldFileName { get; }
        WorldFileData ApplyMetadata(ServerContext server);

        static IWorldDataProvider GenerateOrLoadExisting(string worldName, int worldSize, int difficulty = 2, int worldEvil = 0, string seed = "")
            => new GenerateOrLoadProvider(worldName, worldSize, difficulty, worldEvil, seed);
        private class GenerateOrLoadProvider(string worldName, int worldSize, int difficulty = 2, int worldEvil = 0, string seed = "") : IWorldDataProvider
        {
            public string WorldName => worldName;
            public string WorldFileName => Path.GetFileName(Utilities.IO.GetWorldPathFromName(worldName, true));

            public WorldFileData ApplyMetadata(ServerContext server) {
                server.Main.worldName = worldName;
                string worldPath = Utilities.IO.GetWorldPathFromName(worldName, true);
                if (File.Exists(worldPath)) {
                    server.Main.ActiveWorldFileData = server.WorldFile.GetAllMetadata(worldPath, false);
                }
                else {
                    server.Main.autoGenFileLocation = worldPath;
                    server.Main.GameMode = difficulty;
                    server.Main.AutogenSeedName = seed;
                    server.WorldGen.WorldGenParam_Evil = worldEvil switch {
                        1 => 0,
                        2 => 1,
                        _ => -1,
                    };
                    switch (worldSize) {
                        case 1:
                            server.Main.maxTilesX = 4200;
                            server.Main.maxTilesY = 1200;
                            break;
                        case 2:
                            server.Main.maxTilesX = 6400;
                            server.Main.maxTilesY = 1500;
                            break;
                        case 3:
                            server.Main.maxTilesX = 8400;
                            server.Main.maxTilesY = 2400;
                            break;
                        default:
                            // 自定义【世界长度】：size >= 1000 时直接作为地图宽度。
                            // 用于生成"超长地图"，再按 TEdit 方式切成若干等宽世界拼接。
                            // 高度保持原版大世界一致（作者建议只拓展长度，不拓展高度）。
                            // 自定义长度：宽度取 worldSize，高度按宽度自动推导（见下方）。
                            // （旧注释写"高度用 1800"，与实现不符，2026-10-11 更正 ✗）
                            // 无论取哪个高度，都必须是 150 的整数倍（客户端区块高 150，必须整除）。
                            // 降低高度是为了换长度：内存 = 宽 × 高，12000x2400 已验证可用，
                            // 12800x2400 会 OOM，12800x1800 只有 2304 万图格（更省）。
                            if (worldSize >= 1000) {
                                // 自定义【世界长度】：宽度取 worldSize，高度按宽度自动推导。
                                // 内存 = 宽 x 高，越长的地图必须越矮，否则生成/保存会 OOM（实测 12800x2400 会挂）。
                                // 高度必须是 150 的整数倍（客户端区块高 150）。
                                //   (2026-10-11 更正：旧注释写的是 1500/1200/900，与实现不一致，已按实现改写)
                                //   <= 13000 -> 2400（16 区块）
                                //   <= 16000 -> 1800（12 区块）
                                //   >  16000 -> 1200（8 区块）
                                server.Main.maxTilesX = worldSize;
                                // 高度优先给足（地形分层按 maxTilesY 等比缩放，太矮会让固定尺寸的结构只生成一半）。
                                // 内存 = 宽 x 高：实测 12000x2400(2880万) 可用、12800x2400(3072万) OOM，
                                // 所以阈值取在 12000 附近。
                                // 显式高度优先（切片世界必须声明成和文件完全一致的尺寸，否则客户端地图基准会错位）
                                int explicitH = 0;
                                try { explicitH = int.Parse(Environment.GetEnvironmentVariable("UTSL_CUSTOM_HEIGHT") ?? "0"); } catch { }
                                server.Main.maxTilesY = explicitH > 0
                                    ? explicitH
                                    : (worldSize <= 13000 ? 2400 : (worldSize <= 16000 ? 1800 : 1200));
                            }                            else {
                                server.Main.maxTilesX = 8400;
                                server.Main.maxTilesY = 2400;
                            }
                            break;
                    }
                    server.Main.autoGen = true;
                    server.Main.ActiveWorldFileData = server.WorldFile.CreateMetadata(worldName, false, difficulty);
                }

                return server.Main.ActiveWorldFileData;
            }
        }

        static IWorldDataProvider FromBytes(string worldName, byte[] worldFileData) => new FromBytesProvider(worldName, worldFileData);
        private class FromBytesProvider(string worldName, byte[] worldFileData) : IWorldDataProvider
        {
            public string WorldName => worldName;
            public string WorldFileName => Path.GetFileName(Utilities.IO.GetWorldPathFromName(worldName, true));

            public WorldFileData ApplyMetadata(ServerContext server) {
                string worldPath = Utilities.IO.GetWorldPathFromName(worldName, true);
                File.WriteAllBytes(worldPath, worldFileData);
                server.Main.ActiveWorldFileData = server.WorldFile.GetAllMetadata(worldPath, false);
                return server.Main.ActiveWorldFileData;
            }
        }
    }
}
