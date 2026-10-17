namespace UnifierTSL
{
    /// <summary>[U-4912] 过境打点 v3（log-only 探针；15-UTSL 20261016；staged）：
    /// 出站白名单 7(WorldData)/10(TileSection)/12(SpawnPlayer)/49；入站白名单 6(RequestWorldInfo)/8(RequestTileData)/12；
    /// 附 payload 头十六进制 head=（分析侧自行解码，探针不做语义解析）。</summary>
    internal static class U4912
    {
        private sealed class Host : Logging.ILoggerHost
        {
            public string Name => "U-4912";
            public string? CurrentLogCategory => null;
        }

        private static Logging.RoleLogger? log;
        private static Logging.RoleLogger? Log {
            get {
                if (log is null) { try { log = UnifierApi.CreateLogger(new Host()); } catch { } }
                return log;
            }
        }

        private static string Now() => System.DateTime.Now.ToString("HH:mm:ss.fff");

        private static unsafe string Head(byte* p, int n) {
            try {
                if (p == null || n <= 0) return "";
                var sb = new System.Text.StringBuilder(" head=");
                for (int i = 0; i < n; i++) { if (i > 0) sb.Append('-'); sb.Append(p[i].ToString("X2")); }
                return sb.ToString();
            } catch { return ""; }
        }

        /// <summary>出站帧：type 在 index+2（[len:2][type:1] 帧式）；len=整帧；head 自 index+3 起 8B。</summary>
        public static unsafe void RawOut(byte[] data, int index, int size, int clientId, string via) {
            try {
                if (data is null || size < 3 || index < 0 || index + 2 >= data.Length) return;
                int t = data[index + 2];
                if (t != 7 && t != 10 && t != 12 && t != 49) return;
                string head = "";
                fixed (byte* bp = data) { head = Head(bp + index + 3, 8); }
                Log?.Info(category: "U-4912", message: $"[U-4912] -> type={t} len={size} ms={Now()} who={clientId} via={via}{head}");
            } catch { }
        }

        /// <summary>入站：白名单 8/12；payload 指针由调用点定位（sock＝类型字节+1；inject＝整帧+3）。</summary>
        public static unsafe void RawIn(int type, int len, int who, string src, byte* payload) {
            try {
                if (type != 6 && type != 8 && type != 12) return;
                Log?.Info(category: "U-4912", message: $"[U-4912] <- type={type} len={len} ms={Now()} who={who} src={src}{Head(payload, 8)}");
            } catch { }
        }
    }
}
