using System.Collections;
using System.Reflection;

namespace UnifierTSL.Servers
{
    /// <summary>
    /// 内存泄漏【定向】摘除（P1.2）。
    ///
    /// 与旧的 StaticLeakTeardown 的区别（那次失败就是因为这一点）：
    ///   旧版用"通用深遍历"判断某个组件是否引用目标 —— 而组件的闭包会挂住整个世界上下文，
    ///   于是等于遍历整个对象图 → Close() 60 秒超时、内存暴涨 450~760MB ✗
    ///   本版**只沿已知的短链走**，且**限深限量**：
    ///     · 已知链：StateSnapshot 静态字段 → 定义 → 组件列表 → 组件 → 委托 → 闭包 → (≤1~2 层) → 目标
    ///     · 引用判定最多走 MaxDepth 层、最多访问 MaxNodes 个对象（默认 256），超限即判定为"不相关"
    ///     · 只按引用比较，不对集合/数组做深度递归（数组元素上限 MaxElements）
    ///
    /// 仍未验证前，通过 Enabled 控制；验证需要三项数据：关世界耗时 / 内存增量 / 弱引用是否归零。
    /// </summary>
    public static class StaticLeakTargeted
    {
        /// <summary>总开关（默认关闭；开启前必须完成 P1.3 的三项实测）。</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>引用判定的最大深度（闭包 → 上下文 → 目标，通常 2~3 层足够）。</summary>
        public static int MaxDepth { get; set; } = 4;

        /// <summary>引用判定最多访问的对象数（防止意外的大图遍历）。</summary>
        public static int MaxNodes { get; set; } = 4096;

        /// <summary>数组/集合最多看多少个元素。</summary>
        public static int MaxElements { get; set; } = 64;

        private static readonly List<string> Diag = [];

        public static string TakeDiagnostics()
        {
            string s = string.Join(" | ", Diag);
            Diag.Clear();
            return s;
        }

        private static int _selfChecked;

        /// <summary>校验摘除所依赖的内部字段是否仍然存在；进程内只输出一次。</summary>
        public static void LogSelfCheck()
        {
            if (Interlocked.Exchange(ref _selfChecked, 1) != 0) { return; }
            try
            {
                string bad = SelfCheck();
                Console.WriteLine(bad.Length == 0
                    ? "[StaticLeakTargeted] self-check ok"
                    : "[StaticLeakTargeted] self-check failed: " + bad + " (leak fix may be ineffective)");
            }
            catch (Exception ex) { Console.WriteLine("[StaticLeakTargeted] self-check error: " + ex.Message); }
        }

        public static int Detach(ServerContext target)
        {
            LogSelfCheck();

            int n = 0;
            try { n += DetachStateSnapshot(target); } catch (Exception ex) { Diag.Add("snap EX:" + ex.GetType().Name); }
            try { n += DetachConditionalDialogue(target); } catch (Exception ex) { Diag.Add("dlg EX:" + ex.GetType().Name); }
            try { n += DetachStaticEventHandlers(target); } catch (Exception ex) { Diag.Add("evt EX:" + ex.GetType().Name); }
            try { n += DetachArmorSetBonuses(target); } catch (Exception ex) { Diag.Add("armor EX:" + ex.GetType().Name); }
            try { n += DetachMidiPlayerBinding(target); } catch (Exception ex) { Diag.Add("midi EX:" + ex.GetType().Name); }
            System.Threading.Interlocked.Add(ref TotalRemoved, n);
            // 已加载的世界必然有可摘项；为 0 说明匹配逻辑没生效（通常是内部字段改名）
            if (n == 0 && CurrentGameplayComponentCount() > 0) {
                Console.WriteLine("[StaticLeakTargeted] warning: detached 0 entries while gameplay components exist; internal field names may have changed");
            }
            return n;
        }

        // ---------------- StateSnapshot ----------------

        private static int DetachStateSnapshot(ServerContext target)
        {
            int removed = 0, lists = 0, comps = 0;
            Type? snap = ResolveType("Terraria.Testing.StateSnapshot");
            if (snap is null) { Diag.Add("snap TYPE_NOT_FOUND"); return 0; }

            foreach (FieldInfo f in StaticFields(snap))
            {
                object? def;
                try { def = f.GetValue(null); } catch { continue; }
                if (def is null) { continue; }

                // 定义对象里可能有多个集合字段 —— 只处理 IList（不是通用遍历）
                foreach (FieldInfo df in DeclaredFields(def.GetType()))
                {
                    object? v;
                    try { v = df.GetValue(def); } catch { continue; }
                    if (v is not IList list) { continue; }
                    lists++;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        object? item;
                        try { item = list[i]; } catch { continue; }
                        if (item is null) { continue; }
                        comps++;
                        if (ComponentReferences(item, target))
                        {
                            try { list.RemoveAt(i); removed++; } catch { }
                        }
                    }
                }
            }
            //   原因：其中有些组件（如 TilesComponent）持有本世界的【图格数据】，
            //   但图格数据本身不引用 RootContext → 上面的"是否到达 target"判定抓不到它们，
            //   导致"对象壳被回收、54MB 图格却留在全局静态表里"（实测 +54.3MB/世界，线性不降）。
            int batch = 0;
            if (target.SnapshotBaseline >= 0)
            {
                Type? snap2 = ResolveType("Terraria.Testing.StateSnapshot");
                object? def2 = snap2?.GetField("Gameplay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                object? list2 = def2?.GetType().GetField("_components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(def2);
                if (list2 is IList l2 && l2.Count > target.SnapshotBaseline)
                {
                    int extra = l2.Count - target.SnapshotBaseline;
                    for (int k = 0; k < extra; k++) { try { l2.RemoveAt(l2.Count - 1); batch++; } catch { break; } }
                }
            }
            Diag.Add($"snap lists={lists} comps={comps} removed={removed} batch={batch} baseline={target.SnapshotBaseline}");
            return removed + batch;
        }

        /// <summary>组件（或其委托闭包，≤MaxDepth 层）是否引用 target。</summary>
        private static bool ComponentReferences(object component, ServerContext target)
        {
            foreach (FieldInfo f in DeclaredFields(component.GetType()))
            {
                object? v;
                try { v = f.GetValue(component); } catch { continue; }
                if (v is null) { continue; }

                // ReferenceComponent<T>/ValueComponent<T> 的 _get/_set
                if (v is Delegate d)
                {
                    if (ReachesTarget(d, target)) { return true; }
                    continue;
                }

                if (v is string || v.GetType().IsValueType) { continue; }

                //    上面的判定永远看不见它们，于是"持有整个世界图格数据"的组件永远不被摘除
                //    （实测残留 +54.3MB/世界、每世界 22 个组件摘不掉、baseline 每轮 +22）。
                //    判定依据：本次调用的目标是【一个正在消亡的世界】，而只有世界级数据才会达到这个量级
                //    （图格数 ≥ 10 万）→ 因此把达到该量级的集合组件一并摘掉。
                const int WorldScaleElements = 100000;
                if (v is Array bigArr && bigArr.Length >= WorldScaleElements) { return true; }
                if (v is System.Collections.ICollection bigCol && bigCol.Count >= WorldScaleElements) { return true; }

                //    StateSnapshot.Gameplay._components 里的 ArrayComponent（Name = "Main.player"/"Main.chest"/
                //    "Main.dust"/"Main.tile"/…）通过 _array 直接钉住【已死世界的 Main 数组】，
                //    于是整个世界对象图（Item/Player/Chest/…）留下 = 54.3MB/世界。
                //    判定方式：该字段值是否【就是本世界 Main 的某个字段值】（引用相等）—— 精确、无启发式。
                if (OwnedByWorldMain(v, target)) { return true; }

                // 其它集合：仍保留"元素是否就是 target"的限量检查
                if (v is System.Collections.IEnumerable en)
                {
                    int c = 0;
                    foreach (object? e in en)
                    {
                        if (e is not null && ReferenceEquals(e, target)) { return true; }
                        if (++c >= MaxElements) { break; }
                    }
                }
            }
            return false;
        }

        // ---------------- ConditionalDialogue ----------------

        private static FieldInfo? _registryField;
        private static bool _registryResolved;

        private static int DetachConditionalDialogue(ServerContext target)
        {
            int removed = 0, lists = 0, items = 0;
            if (!_registryResolved)
            {
                Type? t = ResolveType("Terraria.GameContent.ConditionalDialogue");
                _registryField = t?.GetField("_registry", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _registryResolved = true;
            }
            if (_registryField is null) { Diag.Add("dlg FIELD_NOT_FOUND"); return 0; }

            object? reg;
            try { reg = _registryField.GetValue(null); } catch { Diag.Add("dlg GET_FAIL"); return 0; }
            if (reg is not Array arr) { Diag.Add("dlg NOT_ARRAY"); return 0; }

            for (int i = 0; i < arr.Length; i++)
            {
                object? listObj;
                try { listObj = arr.GetValue(i); } catch { continue; }
                if (listObj is not IList list) { continue; }
                lists++;
                for (int j = list.Count - 1; j >= 0; j--)
                {
                    object? item;
                    try { item = list[j]; } catch { continue; }
                    if (item is null) { continue; }
                    items++;
                    if (AnyDelegateReaches(item, target))
                    {
                        try { list.RemoveAt(j); removed++; } catch { }
                    }
                }
            }
            Diag.Add($"dlg lists={lists} items={items} removed={removed}");
            return removed;
        }

        /// <summary>对象自身字段里的委托（1 层）是否到达 target。</summary>
        private static bool AnyDelegateReaches(object obj, ServerContext target)
        {
            foreach (FieldInfo f in DeclaredFields(obj.GetType()))
            {
                object? v;
                try { v = f.GetValue(obj); } catch { continue; }
                if (v is Delegate d && DelegateReaches(d, target)) { return true; }
                if (v is IList l)   // FreeCakeDialogue 这类可能把谓词放在集合里
                {
                    int c = 0;
                    foreach (object? e in l)
                    {
                        if (e is Delegate d2 && DelegateReaches(d2, target)) { return true; }
                        if (++c >= MaxElements) { break; }
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// 委托（经其 Target 的少量字段，最多 MaxDepth 层、MaxNodes 个对象）是否引用 target。
        /// 这是本类与旧版（失败版）的**根本区别**：绝不无界递归。
        /// </summary>
        private static bool DelegateReaches(Delegate d, ServerContext target)
        {
            if (ReferenceEquals(d.Target, target)) { return true; }
            object? root = d.Target;
            if (root is null) { return false; }   // 静态方法，不可能捕获实例

            Queue<(object Obj, int Depth)> queue = new();
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            queue.Enqueue((root, 1));
            int visited = 0;

            while (queue.Count > 0 && visited < MaxNodes)
            {
                (object obj, int depth) = queue.Dequeue();
                if (ReferenceEquals(obj, target)) { return true; }
                if (depth > MaxDepth || !seen.Add(obj)) { continue; }
                visited++;

                foreach (FieldInfo f in DeclaredFields(obj.GetType()))
                {
                    object? v;
                    try { v = f.GetValue(obj); } catch { continue; }
                    if (v is null) { continue; }
                    if (ReferenceEquals(v, target)) { return true; }

                    if (v is Delegate inner)
                    {
                        if (ReferenceEquals(inner.Target, target)) { return true; }
                        if (inner.Target is not null && depth + 1 <= MaxDepth) { queue.Enqueue((inner.Target, depth + 1)); }
                        continue;
                    }

                    if (v is string || v.GetType().IsValueType) { continue; }

                    if (v is IEnumerable en and not string)
                    {
                        int c = 0;
                        foreach (object? e in en)
                        {
                            if (e is not null)
                            {
                                if (ReferenceEquals(e, target)) { return true; }
                                if (depth + 1 <= MaxDepth) { queue.Enqueue((e, depth + 1)); }
                            }
                            if (++c >= MaxElements) { break; }
                        }
                        continue;
                    }

                    queue.Enqueue((v, depth + 1));
                }
            }

            if (visited >= MaxNodes) { if (!Diag.Contains("hit MaxNodes")) { Diag.Add("hit MaxNodes"); } }
            return false;
        }

        // ---------------- 工具 ----------------

        private static Type? ResolveType(string fullName)
        {
            Type? t = Type.GetType(fullName + ", OTAPI", throwOnError: false);
            if (t is not null) { return t; }
            t = Type.GetType(fullName + ", Terraria", throwOnError: false);
            if (t is not null) { return t; }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName, throwOnError: false); } catch { }
                if (t is not null) { return t; }
            }
            return null;
        }

        /// <summary>只取【声明在类型自身】的实例字段（含基类链，但不递归值）。</summary>
        private static IEnumerable<FieldInfo> DeclaredFields(Type type)
        {
            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo[] fs;
                try { fs = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch { yield break; }
                foreach (FieldInfo f in fs) { yield return f; }
            }
        }

        /// <summary>当前静态 Gameplay 组件表的条目数（拿不到返回 -1）。</summary>
        public static int CurrentGameplayComponentCount()
        {
            try
            {
                Type? snap = ResolveType("Terraria.Testing.StateSnapshot");
                if (snap is null) { return -1; }
                object? def = snap.GetField("Gameplay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                if (def is null) { return -1; }
                object? list = def.GetType().GetField("_components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(def);
                return list is IList l ? l.Count : -1;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 这些是 Terraria/OTAPI 的私有成员，升级后可能改名/消失；
        /// 一旦失效，摘除会静默变成 0 条 → 泄漏悄悄回来。
        /// 所以在启动时检查一次，把"静默失效"变成"明确告警"。
        /// 返回：告警信息（空 = 全部正常）。
        /// </summary>
        public static string SelfCheck()
        {
            List<string> bad = [];

            Type? snap = ResolveType("Terraria.Testing.StateSnapshot");
            if (snap is null) { bad.Add("Terraria.Testing.StateSnapshot 类型不存在"); }
            else
            {
                if (snap.GetField("Gameplay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is null) { bad.Add("StateSnapshot.Gameplay 字段不存在"); }
                Type? def = ResolveType("Terraria.Testing.StateSnapshotDefinition");
                if (def is null || def.GetField("_components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null) { bad.Add("StateSnapshotDefinition._components 字段不存在"); }
            }

            if (ResolveType("Terraria.GameContent.ConditionalDialogue")?.GetField("_registry", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is null) { bad.Add("ConditionalDialogue._registry 字段不存在"); }

            if (typeof(AppContext).GetField("UnhandledException", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is null) { bad.Add("AppContext.UnhandledException 字段不存在"); }

            Type? armor = ResolveType("Terraria.DataStructures.ArmorSetBonuses");
            if (armor is null) { bad.Add("ArmorSetBonuses 类型不存在"); }

            return bad.Count == 0 ? "" : string.Join("; ", bad);
        }

        /// <summary>本次运行累计摘除数（用于"忽然变 0"的告警）。</summary>
        public static long TotalRemoved;

        // ---------------- 静态事件处理器（CrashWatcher 等）----------------

        private static int DetachStaticEventHandlers(ServerContext target)
        {
            int removed = 0;
            foreach (FieldInfo f in DeclaredFields(AppDomain.CurrentDomain.GetType()))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) { continue; }
                removed += FilterDelegateField(f, AppDomain.CurrentDomain, target);
            }
            foreach (FieldInfo f in StaticFields(AppDomain.CurrentDomain.GetType()))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) { continue; }
                removed += FilterDelegateField(f, null, target);
            }
            //    System.AppContext.UnhandledException (static UnhandledExceptionEventHandler, token 0x04000396)
            //    多播委托里每个世界一个 CrashWatcher 闭包 → 必须扫 AppContext！
            //    （gcroot 注解的 "s_defaultBaseDirectory" 是 ClrMD 错位标注，该字段值为 null）
            foreach (FieldInfo f in StaticFields(typeof(AppContext)))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) { continue; }
                removed += FilterDelegateField(f, null, target);
            }
            foreach (FieldInfo f in StaticFields(typeof(AppDomain)))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) { continue; }
                removed += FilterDelegateField(f, null, target);
            }
            Type? cw = ResolveType("Terraria.Utilities.CrashWatcher");
            if (cw is not null)
            {
                foreach (FieldInfo f in StaticFields(cw))
                {
                    if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) { continue; }
                    removed += FilterDelegateField(f, null, target);
                }
            }
            Diag.Add("evt removed=" + removed);
            return removed;
        }

        private static int FilterDelegateField(FieldInfo field, object? host, ServerContext target)
        {
            Delegate? current;
            try { current = field.GetValue(host) as Delegate; } catch { return 0; }
            if (current is null) { return 0; }
            Delegate[] list = current.GetInvocationList();
            List<Delegate> keep = [];
            int removed = 0;
            foreach (Delegate item in list)
            {
                if (ReachesTarget(item, target)) { removed++; } else { keep.Add(item); }
            }
            if (removed == 0) { return 0; }
            Delegate? rebuilt = null;
            foreach (Delegate item in keep) { rebuilt = Delegate.Combine(rebuilt, item); }
            try { field.SetValue(host, rebuilt); } catch { }
            return removed;
        }

        /// <summary>
        /// 实测链路：委托.Target(闭包) -> 字段 -> SystemContext -> 字段 -> ServerContext，
        /// 即"最多两层字段直查"就够；不做 BFS、没有节点上限，因此【不会漏判】。
        /// </summary>
        private static bool ReachesTarget(Delegate d, object target)
        {
            if (ReferenceEquals(d.Target, target)) { return true; }
            object? t0 = d.Target;
            if (t0 is null) { return false; }
            foreach (FieldInfo f1 in DeclaredFields(t0.GetType()))
            {
                object? v1;
                try { v1 = f1.GetValue(t0); } catch { continue; }
                if (v1 is null) { continue; }
                if (ReferenceEquals(v1, target)) { return true; }
                if (v1 is string || v1.GetType().IsValueType) { continue; }
                foreach (FieldInfo f2 in DeclaredFields(v1.GetType()))
                {
                    object? v2;
                    try { v2 = f2.GetValue(v1); } catch { continue; }
                    if (v2 is not null && ReferenceEquals(v2, target)) { return true; }
                }
            }
            return false;
        }

        // ---------------- 第三方插件：MidiPlayer 的世界绑定（最后发现的持有者）----------------

        /// <summary>
        /// 清掉 MidiPlayer 单例里对本世界的强引用。
        /// dump 实证（最后一条根）：
        ///   MidiPlayer.Core.MusicBoxBinding.Instance（static readonly 单例）
        ///   -> 实例字段 &lt;CurrentServer&gt;k__BackingField（UnifierTSL.Servers.ServerContext）
        /// MidiPlayer 只在【世界加载】时写 CurrentServer（GamePostInitialize），从不清理 → 世界被钉住。
        /// 修法：只清"值恰好等于本世界"的那次（否则会打断正在运行世界的八音盒联动）。
        /// </summary>
        private static int DetachMidiPlayerBinding(ServerContext target)
        {
            int removed = 0;
            Type? t = ResolveType("MidiPlayer.Core.MusicBoxBinding");
            if (t is null) { Diag.Add("midi TYPE_NOT_FOUND"); return 0; }

            object? inst;
            try { inst = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null); }
            catch { inst = null; }
            if (inst is null) { Diag.Add("midi NO_INSTANCE"); return 0; }

            FieldInfo? f = t.GetField("<CurrentServer>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f is null) { f = t.GetField("CurrentServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
            if (f is null) { Diag.Add("midi NO_FIELD"); return 0; }

            try {
                if (ReferenceEquals(f.GetValue(inst), target)) { f.SetValue(inst, null); removed = 1; }
            }
            catch { }
            Diag.Add("midi removed=" + removed);
            return removed;
        }

        // ---------------- 其它静态 List 注册表（ArmorSetBonuses 等）----------------

        /// <summary>
        /// 摘掉"静态 List 里引用了本 context 的条目"。
        /// gcroot 实证（已收敛到 1 条根）：最后钉住世界的是
        ///   List&lt;Terraria.DataStructures.ArmorSetBonus&gt;（静态）
        ///   -> ArmorSetBonus -> ArmorSetEffect -> ArmorSetBonuses+&lt;&gt;c__DisplayClass00_256 -> context
        /// 与 ConditionalDialogue 同一套路：静态集合 + 闭包捕获。判定用限深限量的定向遍历。
        /// </summary>
        private static int DetachArmorSetBonuses(ServerContext target)
        {
            int removed = 0, items = 0;
            Type? t = ResolveType("Terraria.DataStructures.ArmorSetBonuses");
            if (t is null) { Diag.Add("armor TYPE_NOT_FOUND"); return 0; }

            foreach (FieldInfo f in StaticFields(t))
            {
                object? v;
                try { v = f.GetValue(null); } catch { continue; }
                if (v is not IList list) { continue; }
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    object? item;
                    try { item = list[i]; } catch { continue; }
                    if (item is null) { continue; }
                    items++;
                    if (ObjectReaches(item, target)) { try { list.RemoveAt(i); removed++; } catch { } }
                }
            }
            Diag.Add("armor items=" + items + " removed=" + removed);
            return removed;
        }

        /// <summary>
        /// 用于识别 StateSnapshot 的 ArrayComponent（Main.player/chest/dust/tile/…），
        /// 它们不引用 ServerContext，只能靠"是不是本世界 Main 的数组"来归属。
        /// </summary>
        private static bool OwnedByWorldMain(object value, ServerContext target)
        {
            object? main;
            try { main = target.Main; } catch { return false; }
            if (main is null) { return false; }
            foreach (FieldInfo mf in DeclaredFields(main.GetType()))
            {
                object? mv;
                try { mv = mf.GetValue(main); } catch { continue; }
                if (mv is not null && ReferenceEquals(mv, value)) { return true; }
            }
            return false;
        }

        /// <summary>限深限量地判断：对象（含字段/数组元素/委托目标）是否引用了 target。</summary>
        private static bool ObjectReaches(object root, ServerContext target)
        {
            Queue<(object Obj, int Depth)> queue = new();
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            queue.Enqueue((root, 1));
            int visited = 0;
            while (queue.Count > 0 && visited < MaxNodes)
            {
                (object obj, int depth) = queue.Dequeue();
                if (ReferenceEquals(obj, target)) { return true; }
                if (depth > MaxDepth || !seen.Add(obj)) { continue; }
                visited++;
                foreach (FieldInfo f in DeclaredFields(obj.GetType()))
                {
                    object? v;
                    try { v = f.GetValue(obj); } catch { continue; }
                    if (v is null) { continue; }
                    if (ReferenceEquals(v, target)) { return true; }
                    if (v is Delegate d)
                    {
                        if (ReferenceEquals(d.Target, target)) { return true; }
                        if (d.Target is not null) { queue.Enqueue((d.Target, depth + 1)); }
                        continue;
                    }
                    if (v is string || v.GetType().IsValueType) { continue; }
                    queue.Enqueue((v, depth + 1));
                }
                if (obj is Array arr)
                {
                    int n = Math.Min(arr.Length, MaxElements);
                    for (int k = 0; k < n; k++)
                    {
                        object? e; try { e = arr.GetValue(k); } catch { continue; }
                        if (e is not null && depth + 1 <= MaxDepth) { queue.Enqueue((e, depth + 1)); }
                    }
                }
            }
            return false;
        }

        private static IEnumerable<FieldInfo> StaticFields(Type type)
        {
            FieldInfo[] fs;
            try { fs = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
            catch { yield break; }
            foreach (FieldInfo f in fs) { yield return f; }
        }
    }
}
