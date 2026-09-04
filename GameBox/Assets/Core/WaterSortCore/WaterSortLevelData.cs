using System;

namespace WaterSort.Core
{
    /// <summary>
    /// 难度档位。档位 ↔ 数值(颜色数区间/步数区间)的映射属运营配置
    /// (默认表 + 远程覆盖,19 文档 WS-03),本核心程序集只持枚举与关卡数据模型,不含运营数值。
    /// </summary>
    public enum WaterSortDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
    }

    /// <summary>
    /// 关卡数据(纯数据模型,JsonUtility 友好):固化预生成关卡,设备端只反序列化不生成。
    /// tubes 扁平存储约定:仅存"混合满管"(colors 支 × 每管 4 滴,管序从 0 起),
    /// 空管(2 支)不入库,Decode 时补齐——见 <see cref="WaterSortLevelCodec"/>。
    /// </summary>
    [Serializable]
    public sealed class WaterSortLevelData
    {
        public int id;                    // 常规关编号(1 起);每日关另行建模(按日期种子,同结构)
        public int colors;                // 颜色数
        public WaterSortDifficulty difficulty; // 落档结果(由生成规格决定)
        public int measuredSteps;         // ≤3 色=IDA* 精确最优步数;≥4 色=SolveAny 首解深度代理
        public int[] tubes;               // 扁平滴值:colors × Capacity,管主序、每管 0=底部 3=顶部
    }

    /// <summary>关卡数据编解码(纯逻辑,零引擎依赖)。</summary>
    public static class WaterSortLevelCodec
    {
        /// <summary>编码棋盘为入库数据。要求棋盘形态=生成器产物:前 colors 支为混合满管、其后为空管。</summary>
        public static WaterSortLevelData Encode(WaterSortBoard board, int id, WaterSortDifficulty difficulty, int measuredSteps)
        {
            if (board.TubeCount != board.Colors + 2)
                throw new ArgumentException($"编码要求 2 支空管形态,实际管数 {board.TubeCount}(颜色 {board.Colors})");
            int flat = board.Colors * WaterSortBoard.Capacity;
            var tubes = new int[flat];
            int w = 0;
            for (int t = 0; t < board.Colors; t++)
            {
                if (board.TopCount(t) != WaterSortBoard.Capacity)
                    throw new ArgumentException($"编码要求前 {board.Colors} 支均为满管,管 {t} 不满");
                for (int i = 0; i < WaterSortBoard.Capacity; i++) tubes[w++] = board.Get(t, i);
            }
            for (int t = board.Colors; t < board.TubeCount; t++)
                if (board.TopCount(t) != 0)
                    throw new ArgumentException($"编码要求后 2 支为空管,管 {t} 非空");
            return new WaterSortLevelData
            {
                id = id,
                colors = board.Colors,
                difficulty = difficulty,
                measuredSteps = measuredSteps,
                tubes = tubes,
            };
        }

        /// <summary>解码入库数据为可玩棋盘。形状不符返回 false(供每日挑战兜底等容错路径判定损坏条目)。</summary>
        public static bool TryDecode(WaterSortLevelData data, out WaterSortBoard board)
        {
            board = null;
            if (data == null || data.tubes == null) return false;
            int colors = data.colors;
            int expected = colors * WaterSortBoard.Capacity;
            if (colors <= 0 || colors > 12 || data.tubes.Length != expected) return false; // 色数上限随配置走,12 为安全界
            var arrays = new int[colors + 2][];
            for (int t = 0; t < colors; t++)
            {
                arrays[t] = new int[WaterSortBoard.Capacity];
                for (int i = 0; i < WaterSortBoard.Capacity; i++)
                {
                    int v = data.tubes[t * WaterSortBoard.Capacity + i];
                    if (v <= 0 || v > colors) return false; // 滴值必须落在 1..colors
                    arrays[t][i] = v;
                }
            }
            arrays[colors] = new int[0]; // 2 支空管
            arrays[colors + 1] = new int[0];
            board = new WaterSortBoard(colors, 2, arrays);
            return true;
        }
    }
}
