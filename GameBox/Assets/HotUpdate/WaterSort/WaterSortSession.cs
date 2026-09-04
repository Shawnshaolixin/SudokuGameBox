using System;
using System.Collections.Generic;
using WaterSort.Core;

namespace Box.HotUpdate.WaterSort
{
    /// <summary>
    /// 水排序对局会话(弹窗单场景制,无跨场景传参需求):
    /// 模块每次 OnEnter 新建实例挂到静态 Instance(旧实例随旧视图退订销毁,无跨局残留);
    /// 状态机:无局 → StartLevel(开局/重开,清历史)→ 对局中(倒水/撤销)→ 过关后仍在对局态
    /// (结算面板停留,再选关/重开才离开)。
    /// 事件一律在操作调用点同步触发(主线程),视图直接刷新即可。
    /// 提示/额外瓶(M1.4):金币扣减在视图层调用点(本类不含货币逻辑),本类只管「每关消耗
    /// 计数 + 盘面效果」;计数随 StartLevel 复位(重开可再购,已付金币不退,见各方法注释)。
    /// 每日挑战(M2)由模块入口 args 驱动,本类外扩,不改状态机骨架。
    /// </summary>
    public sealed class WaterSortSession
    {
        public static WaterSortSession Instance { get; set; }

        /// <summary>每日挑战标记(由模块入口 args 注入;M2 接每日题库,先落位供标题/文案区分)。</summary>
        public bool IsDaily { get; }

        public bool IsInLevel { get; private set; }          // 有可玩盘面(StartLevel 成功后至下次开局)
        public int LevelId { get; private set; }             // 当前关号(常规关 = DTO.id)
        public WaterSortDifficulty Difficulty { get; private set; }
        public WaterSortLevelData LevelData { get; private set; } // 本局关卡源(重开重建 + 结算文案)
        public WaterSortBoard Board { get; private set; }
        public int MoveCount { get; private set; }

        /// <summary>本关已购提示次数(上限/单价在 WaterSortConfig;Undo 提示落子不回退计数,金币不退)。</summary>
        public int HintsUsed { get; private set; }

        /// <summary>本关已购额外空瓶次数(上限在 WaterSortConfig;Undo 加管会回退本计数——管已移除,可再购新管)。</summary>
        public int ExtraTubesUsed { get; private set; }

        /// <summary>盘面变更(倒水/撤销/重开/加管成功后)——视图刷新试管区与步数。 </summary>
        public event Action BoardChanged;

        /// <summary>过关(未解→解瞬间触发一次)——视图进结算面板。撤销不会回补该事件(见 _solvedNotified)。</summary>
        public event Action LevelSolved;

        // 撤销反演:WaterSortBoard 不可变(Apply 返回新盘面),_history 只存"操作前快照"引用,无深拷贝开销。
        // 快照按动作类型入栈(M1.4 起加管也是可撤销项):步数只随倒水动作回退、加管计数只随加管动作回退;
        // 撤销到"加管前盘面" = 该次加管后的一切倒水也一并弹回(快照栈天然一致),金币不随撤销退还。
        enum HistoryKind { Pour, ExtraTube }
        struct HistoryEntry
        {
            public WaterSortBoard Board; // 操作前盘面(不可变引用)
            public HistoryKind Kind;     // 动作类型(决定 Undo 时回退哪个计数)
        }
        readonly Stack<HistoryEntry> _history = new Stack<HistoryEntry>();
        bool _solvedNotified; // 防同一盘面重复上报(仅 StartLevel 复位,供结算停留期安全)

        public WaterSortSession(bool isDaily)
        {
            IsDaily = isDaily;
        }

        /// <summary>
        /// 开局(选关/重开共用):关卡数据解码失败(损坏/版本不符)返回 false 不动状态,界面提示。
        /// 过关标记/历史/金币消耗计数随新局复位 —— 重玩本关 = 全新盘面(已付提示/空瓶金币不退)。
        /// </summary>
        public bool StartLevel(WaterSortLevelData data)
        {
            if (data == null || !WaterSortLevelCodec.TryDecode(data, out var board)) return false;
            LevelData = data;
            LevelId = data.id;
            Difficulty = data.difficulty;
            Board = board;
            MoveCount = 0;
            HintsUsed = 0;
            ExtraTubesUsed = 0;
            _history.Clear();
            _solvedNotified = false;
            IsInLevel = true;
            return true;
        }

        /// <summary>倒水:仅放行规则允许的移动(Apply 本身不校验合法性)。成功 → 记账 + 上报;失败返回 false 由界面抖动。</summary>
        public bool TryPour(int srcTube, int dstTube)
        {
            if (!IsInLevel || Board == null) return false;
            foreach (var m in Board.LegalMoves()) // 管数 ≤ 12+额外,枚举开销可忽略
            {
                if (m.Src != srcTube || m.Dst != dstTube) continue;
                _history.Push(new HistoryEntry { Board = Board, Kind = HistoryKind.Pour }); // 操作前快照(不可变引用)
                Board = Board.Apply(m);
                MoveCount++;
                BoardChanged?.Invoke();
                if (Board.IsSolved() && !_solvedNotified)
                {
                    _solvedNotified = true; // 一次性:结算停留期间撤销不可达(面板已切走),此防重入保险
                    LevelSolved?.Invoke();
                }
                return true;
            }
            return false;
        }

        /// <summary>撤销一步(快照栈非空):盘面回到上一步;倒水动作回退步数,加管动作回退计数并移除该空管。过关上报不重发。</summary>
        public bool Undo()
        {
            if (!IsInLevel || _history.Count == 0) return false;
            var e = _history.Pop();
            Board = e.Board;
            if (e.Kind == HistoryKind.Pour) MoveCount--;
            else ExtraTubesUsed--;
            BoardChanged?.Invoke();
            return true;
        }

        /// <summary>重开本关:从关卡源重建(等价再开局一次)。</summary>
        public void Restart()
        {
            if (LevelData != null) StartLevel(LevelData);
        }

        /// <summary>
        /// 提示(WS-06):对当前盘求首解并自动走出第一步(SolveAny ≤400ms 预算,Spike 全色数性能背书,
        /// 低端机真机复测留 M3)。成功才计次;失败(死局/超时)返回 false,视图不扣币并引导撤销
        /// —— 玩家自走路径可能走进"无解死角"(合法但不聪明的倒水),此时提示不可用是诚实反馈。
        /// 撤销提示 = 普通倒水撤销(盘面/步数回退),金币与计数不退(消费型动作)。
        /// </summary>
        public bool TryHint()
        {
            if (!IsInLevel || Board == null || Board.IsSolved()) return false;
            if (HintsUsed >= WaterSortConfig.HintLimitPerLevel) return false; // 每关上限(视图按钮亦禁用,双保险)
            var result = WaterSortSolver.SolveAny(Board, WaterSortConfig.HintSolveTimeLimitMs);
            if (!result.Solved || result.Solution == null || result.Solution.Count == 0) return false;
            var m = result.Solution[0]; // 首解第一步 = 当前盘面合法移动(与 TryPour 同规则引擎)
            if (!TryPour(m.Src, m.Dst)) return false; // 求解/落子同源,理论不可达;防御保留
            HintsUsed++;
            return true;
        }

        /// <summary>
        /// 额外空瓶(WS-06/13):尾部追加 1 支空管(原管索引不变,新管为最后一支,盘面可倒空间 +1)。
        /// 加管动作入历史栈 → Undo 可整支弹回(恢复到加管前盘面,金币不退)。
        /// 盘面扩张仅运行时有效:不写回 LevelData,重开/重玩即回到关卡原始形态。
        /// </summary>
        public bool TryAddExtraTube()
        {
            if (!IsInLevel || Board == null) return false;
            if (ExtraTubesUsed >= WaterSortConfig.ExtraTubeLimitPerLevel) return false; // 每关上限(运营表)
            var src = Board;
            int n = src.TubeCount;
            var tubes = new int[n][];
            for (int t = 0; t < n; t++)
            {
                int k = src.TopCount(t);
                tubes[t] = new int[k]; // 只拷贝非空段(值 0=空,Ctor 会先清空再填充)
                for (int i = 0; i < k; i++) tubes[t][i] = src.Get(t, i);
            }
            _history.Push(new HistoryEntry { Board = src, Kind = HistoryKind.ExtraTube }); // 操作前快照(同倒水反演)
            Board = new WaterSortBoard(src.Colors, src.TubeCount - src.Colors + 1, tubes); // 空管数 +1 → 末支新管自然为空
            ExtraTubesUsed++;
            BoardChanged?.Invoke();
            return true;
        }
    }
}
