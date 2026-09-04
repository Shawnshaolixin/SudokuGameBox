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
    /// 事件一律在操作调用点同步触发(主线程),视图直接刷新即可;
    /// 提示/额外瓶(M1.4 接金币与配置)与每日挑战(M2)由本类外扩,不改状态机骨架。
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

        /// <summary>盘面变更(倒水/撤销/重开成功后)——视图刷新试管区与步数。 </summary>
        public event Action BoardChanged;

        /// <summary>过关(未解→解瞬间触发一次)——视图进结算面板。撤销不会回补该事件(见 _solvedNotified)。</summary>
        public event Action LevelSolved;

        // 撤销反演:WaterSortBoard 不可变(Apply 返回新盘面),_history 只存"操作前快照"引用,
        // 无深拷贝开销;步数与快照一一对应,Undo 弹出即回退计数。
        readonly Stack<WaterSortBoard> _history = new Stack<WaterSortBoard>();
        bool _solvedNotified; // 防同一盘面重复上报(仅 StartLevel 复位,供结算停留期安全)

        public WaterSortSession(bool isDaily)
        {
            IsDaily = isDaily;
        }

        /// <summary>
        /// 开局(选关/重开共用):关卡数据解码失败(损坏/版本不符)返回 false 不动状态,界面提示。
        /// 过关标记与历史随新局复位 —— 重玩本关 = 全新盘面。
        /// </summary>
        public bool StartLevel(WaterSortLevelData data)
        {
            if (data == null || !WaterSortLevelCodec.TryDecode(data, out var board)) return false;
            LevelData = data;
            LevelId = data.id;
            Difficulty = data.difficulty;
            Board = board;
            MoveCount = 0;
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
                _history.Push(Board); // 操作前快照(不可变引用)
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

        /// <summary>撤销一步(快照栈非空);盘面回到上一步,步数回退。撤销后若之前已过关,过关上报不重发。</summary>
        public bool Undo()
        {
            if (!IsInLevel || _history.Count == 0) return false;
            Board = _history.Pop();
            MoveCount--;
            BoardChanged?.Invoke();
            return true;
        }

        /// <summary>重开本关:从关卡源重建(等价再开局一次)。</summary>
        public void Restart()
        {
            if (LevelData != null) StartLevel(LevelData);
        }
    }
}
