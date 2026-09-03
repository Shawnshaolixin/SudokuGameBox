using System.Collections.Generic;
using System.Text;

namespace Box.WaterSortSpike
{
    /// <summary>一次倒水操作:源管 Src → 目标管 Dst,Count = 实际倒出滴数。</summary>
    public readonly struct WaterSortMove
    {
        public readonly int Src;
        public readonly int Dst;
        public readonly int Count;

        public WaterSortMove(int src, int dst, int count)
        {
            Src = src;
            Dst = dst;
            Count = count;
        }

        public override string ToString() => $"{Src}→{Dst}(×{Count})";
    }

    /// <summary>
    /// 水排序棋盘(不可变):每管容量固定 4,管内索引 0=底部、3=顶部;滴值 0=空、1..Colors=颜色。
    /// 倒水规则:一次倒入源管顶部整个同色连续块(受目标管剩余容量限制,可部分倒出);
    /// 满管(4 滴同色)禁止倒入空管(经典规则,避免无意义移动)。
    /// 过关判定:所有非空管均已同色聚合(允许空管存在)。
    /// </summary>
    public sealed class WaterSortBoard
    {
        public const int Capacity = 4;

        public readonly int TubeCount; // 总管数(含空管)
        public readonly int Colors;    // 颜色数
        private readonly byte[][] _tubes; // [管][0..3],0=底部

        /// <summary>构造终态:每色一管满 4 滴,其余为空管。</summary>
        public WaterSortBoard(int colors, int emptyTubes)
        {
            Colors = colors;
            TubeCount = colors + emptyTubes;
            _tubes = new byte[TubeCount][];
            for (int t = 0; t < colors; t++)
            {
                _tubes[t] = new byte[Capacity];
                for (int i = 0; i < Capacity; i++) _tubes[t][i] = (byte)(t + 1);
            }
            for (int t = colors; t < TubeCount; t++) _tubes[t] = new byte[Capacity];
        }

        /// <summary>
        /// 从管配置直接构造(测试/生成器用)。每管数组长度 ≤ Capacity,值 0=空,1..Colors=颜色。
        /// 注意:链式构造会把每管预置为"颜色满管",填充前必须清空,否则未写满的位置残留初值。
        /// </summary>
        public WaterSortBoard(int colors, int emptyTubes, params int[][] tubes) : this(colors, emptyTubes)
        {
            for (int t = 0; t < tubes.Length && t < TubeCount; t++)
            {
                System.Array.Clear(_tubes[t], 0, Capacity);
                for (int i = 0; i < tubes[t].Length && i < Capacity; i++)
                    _tubes[t][i] = (byte)tubes[t][i];
            }
        }

        private WaterSortBoard(WaterSortBoard src, byte[][] tubes)
        {
            Colors = src.Colors;
            TubeCount = src.TubeCount;
            _tubes = tubes;
        }

        /// <summary>读取滴值;level 0=底部。</summary>
        public byte Get(int tube, int level) => _tubes[tube][level];

        /// <summary>管顶非空滴数(0~4)。</summary>
        public int TopCount(int tube)
        {
            int n = 0;
            while (n < Capacity && _tubes[tube][n] != 0) n++;
            return n;
        }

        /// <summary>管顶颜色;空管返回 0。</summary>
        public byte TopColor(int tube) => TopCount(tube) == 0 ? (byte)0 : _tubes[tube][TopCount(tube) - 1];

        /// <summary>管顶同色连续块长度(空管返回 0)。</summary>
        public int TopRun(int tube)
        {
            int n = TopCount(tube);
            if (n == 0) return 0;
            byte c = _tubes[tube][n - 1];
            int run = 0;
            for (int i = n - 1; i >= 0 && _tubes[tube][i] == c; i--) run++;
            return run;
        }

        /// <summary>是否过关:所有非空管均已满 4 滴且同色(每色 4 滴,聚合完成必然满管)。</summary>
        public bool IsSolved()
        {
            for (int t = 0; t < TubeCount; t++)
            {
                int n = TopCount(t);
                if (n == 0) continue; // 空管允许存在
                if (n != Capacity) return false; // 未满说明该色还有液体在别处,未聚合完成
                byte c = _tubes[t][0];
                for (int i = 1; i < Capacity; i++)
                    if (_tubes[t][i] != c) return false;
            }
            return true;
        }

        /// <summary>枚举所有合法移动(含剪枝:满块禁止倒入空管)。</summary>
        public List<WaterSortMove> LegalMoves()
        {
            var moves = new List<WaterSortMove>();
            for (int src = 0; src < TubeCount; src++)
            {
                int k = TopRun(src);
                if (k == 0) continue;
                byte srcTop = TopColor(src);
                for (int dst = 0; dst < TubeCount; dst++)
                {
                    if (dst == src) continue;
                    int dstN = TopCount(dst);
                    if (dstN == Capacity) continue; // 目标已满
                    if (dstN > 0 && TopColor(dst) != srcTop) continue; // 目标顶颜色不同
                    if (dstN == 0 && k == Capacity) continue; // 满块禁止倒入空管
                    int cap = Capacity - dstN;
                    moves.Add(new WaterSortMove(src, dst, k < cap ? k : cap));
                }
            }
            return moves;
        }

        /// <summary>执行移动,返回新局面(不可变)。</summary>
        public WaterSortBoard Apply(WaterSortMove m)
        {
            var next = new byte[TubeCount][];
            for (int t = 0; t < TubeCount; t++) next[t] = (byte[])_tubes[t].Clone();
            int srcN = TopCount(m.Src);
            int dstN = TopCount(m.Dst);
            for (int i = 0; i < m.Count; i++)
            {
                next[m.Dst][dstN + i] = next[m.Src][srcN - 1 - i];
                next[m.Src][srcN - 1 - i] = 0;
            }
            return new WaterSortBoard(this, next);
        }

        /// <summary>紧凑状态键,用于去重/哈希。</summary>
        public string EncodeKey()
        {
            var sb = new StringBuilder(TubeCount * (Capacity + 1));
            for (int t = 0; t < TubeCount; t++)
            {
                if (t > 0) sb.Append('|');
                for (int i = 0; i < Capacity; i++) sb.Append((char)('0' + _tubes[t][i]));
            }
            return sb.ToString();
        }
    }
}
