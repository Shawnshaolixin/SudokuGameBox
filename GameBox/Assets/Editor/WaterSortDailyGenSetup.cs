using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WaterSort.Core;

/// <summary>
/// 水排序每日题库预生成工具(M2.3,WS-09):离线圈定 ≥2 年日题,按日期种子(yyyyMMdd)索引入库。
/// CLI 无头执行:unity run GameBox -- -executeMethod WaterSortDailyGenSetup.BuildDailyPack
/// (或 VerifyDailyPack 逐日验收)。与常规题库同一生成管线(WaterSortLevelGen 散射 + 快筛 + 落档),
/// 产物结构 WaterSortDailyPack{levels,spares} 与运行期 WaterSortDailyLevelStore 同源。
///
/// 口径:
/// ① 时间轴:固定纪元 2026-08-01(上线前后留余量)起连续 DayCount=800 天(覆盖 ≥2 年,WS-09);
///    每天 id=日期种子,同日期全球同题、UTC 0 点换题;日期在范围外 → 缺条目,运行期兜底备用池。
/// ② 难度轮转:按天循环 7 槽规格表(DailySpecs,难度混合一周内拉开梯度,周末 Hard);
///    轮转槽 = 距纪元天数 % 7 —— 纯日期推导,生成与验收双侧同口径。
/// ③ 备用池:7 条(每槽一条,同规格生成),id=0..6 占位;运行期取 spares[seed % 7]
///    (确定性兜底,防单日条目缺失时全球同日死局);等级感与当周难度无强绑定(兜底是资产异常出口)。
/// ④ 复现:逐日生成种子 = 0xDA11 + 天序号 * 7919 + 平移 k(同 WaterSortViewSetup 平移口径,50 次内命中);
///    同参数重复执行产物逐字节一致(幂等判定:文件存在且天数一致即跳过)。
/// ⑤ 吞吐统计按 M2.2 口径记录(日志留档:总耗时/均 ms/散射尝试分布),入库同 Game_WaterSort 组。
/// </summary>
public static class WaterSortDailyGenSetup
{
    /// <summary>固定纪元(UTC;产物覆盖期 = [EpochDate, EpochDate+DayCount),含首日)。</summary>
    public static readonly DateTime EpochDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    const int DayCount = 800;          // ≥2 年题库(WS-09:离线预生成 ≥2 年;800 天 ≈ 2.2 年,给上线留余量)
    const int SparesCount = 7;         // 备用池大小 = 轮转槽数(每槽一条,兜底任意日)
    const int MaxSeedShift = 50;       // 单日种子平移上限(同 WaterSortViewSetup.GenerateOne)
    const int ReverifyTimeLimitMs = 2000; // 逐日复证限时:同 WaterSortViewSetup.ReverifyTimeLimitMs
    const int SeedBase = 0xDA11;       // 日题种子基(同常规题库 0x5757 的复现纪律)

    /// <summary>难度轮转规格表(7 槽;数值取自 WaterSortGenDefaults 校准后的档窗,WS-09「难度混合」)。</summary>
    static readonly WaterSortGenSpec[] DailySpecs =
    {
        Make(WaterSortDifficulty.Easy,   3, 4, 5, 15),   // 周日:轻量开局
        Make(WaterSortDifficulty.Medium, 5, 7, 15, 30),  // 周一:常态
        Make(WaterSortDifficulty.Medium, 6, 7, 20, 34),  // 周二:渐进带上半
        Make(WaterSortDifficulty.Hard,   7, 9, 25, 42),  // 周三:渐进带下半
        Make(WaterSortDifficulty.Medium, 5, 7, 15, 30),  // 周四:回常态
        Make(WaterSortDifficulty.Medium, 6, 7, 20, 34),  // 周五:周末预热
        Make(WaterSortDifficulty.Hard,   8, 10, 30, 60), // 周六:深水区
    };

    static WaterSortGenSpec Make(WaterSortDifficulty d, int minC, int maxC, int minS, int maxS)
    {
        return new WaterSortGenSpec
        {
            Difficulty = d,
            MinColors = minC,
            MaxColors = maxC,
            MinSteps = minS,
            MaxSteps = maxS,
        };
    }

    /// <summary>日期种子 → 距纪元天数(生成/验收同口径;日期早于纪元为负,调用方保证范围)。
    /// DateTime 减法按纯日历日算术(两侧均不带真实时区换算——DateOf 产出 Unspecified Kind,
    /// 勿对任一侧 ToUniversalTime:本地时区 ≠ UTC 时会把日期整体挪一天)。</summary>
    static int DayIndexOf(int seed)
    {
        return (int)(WaterSortDailySeed.DateOf(seed).Date - EpochDate.Date).TotalDays;
    }

    /// <summary>轮转槽:距纪元天数 % 7(纯日期推导,全球同日同槽同题)。</summary>
    static int SlotOf(int seed) => ((DayIndexOf(seed) % 7) + 7) % 7;

    /// <summary>日期种子 → 生成种子(稳定复现;平移 k 在 GenerateOne 内累加)。</summary>
    static int GenSeedOf(int seed) => SeedBase + DayIndexOf(seed) * 7919;

    [MenuItem("Box/WaterSort/Build Daily Pack(800 days, M2.3)")]
    public static void BuildDailyPack()
    {
        // 幂等判定:文件已存在且天数一致 → 跳过(重复执行零写入;天数即内容形态)
        if (File.Exists(WaterSortViewSetup.DailyLevelsPath) && CountLevels() == DayCount)
        {
            Debug.Log($"[DailyGen] 每日题库已存在({DayCount} 天),跳过生成: " + WaterSortViewSetup.DailyLevelsPath);
            return;
        }
        WaterSortViewSetup.EnsureFolders();

        var pack = new WaterSortDailyPack();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long attemptsTotal = 0;
        int maxAttempts = 0;
        int dateCursor = WaterSortDailySeed.SeedOf(EpochDate);
        for (int i = 0; i < DayCount; i++)
        {
            int spec = SlotOf(dateCursor);
            var level = GenerateOne(dateCursor, DailySpecs[spec], out int attempts);
            attemptsTotal += attempts;
            maxAttempts = System.Math.Max(maxAttempts, attempts);
            if (level == null)
            {
                Debug.LogError($"[DailyGen] {dateCursor} 生成失败(50 次种子平移全不中档,槽 {spec}),终止;已生成 {i} 天");
                return;
            }
            pack.levels.Add(level);
            dateCursor = WaterSortDailySeed.SeedOf(WaterSortDailySeed.DateOf(dateCursor).AddDays(1)); // 逐日 +1
        }
        // 备用池:每槽一条(独立固定种子,与正式日错开;失败即整批失败——兜底池不可留空)
        for (int i = 0; i < SparesCount; i++)
        {
            var spare = GenerateOne(-(i + 1), DailySpecs[i], out int attempts); // 负 id 占位:与正式日种子域完全错开
            attemptsTotal += attempts;
            if (spare == null)
            {
                Debug.LogError($"[DailyGen] 备用槽 {i} 生成失败,终止(兜底池不允许留空)");
                return;
            }
            pack.spares.Add(spare);
        }
        sw.Stop();
        double totalSec = sw.ElapsedMilliseconds / 1000.0;
        int all = DayCount + SparesCount;
        Debug.Log($"[DailyGen] 每日题库生成吞吐: {all} 关 / {totalSec:F1}s = {all / (totalSec / 60.0):F1} 关/分;" +
                  $"均 {sw.ElapsedMilliseconds / (double)all:F0}ms/关;总散射尝试 {attemptsTotal}" +
                  $"(均 {attemptsTotal / (double)all:F0}/关,单关最多 {maxAttempts})");
        // UTF-8 无 BOM(JsonUtility;与运行期反序列化一一对应)
        File.WriteAllText(WaterSortViewSetup.DailyLevelsPath, JsonUtility.ToJson(pack, true));
        AssetDatabase.ImportAsset(WaterSortViewSetup.DailyLevelsPath); // TextAsset 资产(Addressables 可入库)
        WaterSortViewSetup.EnsureEntry(WaterSortViewSetup.DailyLevelsPath, WaterSortViewSetup.DailyLevelsAddress);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DailyGen] 每日题库已生成: {DayCount} 天 + {SparesCount} 备用 → " +
                  WaterSortViewSetup.DailyLevelsPath + "\n" + BuildSummary(pack));
    }

    /// <summary>按日期生成一关(平移口径同常规 GenerateOne);失败(50 次全不中)返回 null。</summary>
    static WaterSortLevelData GenerateOne(int dateSeed, WaterSortGenSpec spec, out int attempts)
    {
        int baseSeed = dateSeed > 0 ? GenSeedOf(dateSeed) : SeedBase + dateSeed; // 备用:负 id 独立种子域
        for (int k = 0; k < MaxSeedShift; k++)
        {
            var r = WaterSortLevelGen.Generate(spec, baseSeed + k);
            attempts = k + 1;
            if (!r.Succeeded) continue;
            return WaterSortLevelCodec.Encode(r.Board, dateSeed, spec.Difficulty, r.MeasuredSteps);
        }
        attempts = MaxSeedShift;
        return null;
    }

    /// <summary>现有 JSON 天数(损坏/为空 → -1 触发重生成)。</summary>
    static int CountLevels()
    {
        try
        {
            var pack = JsonUtility.FromJson<WaterSortDailyPack>(File.ReadAllText(WaterSortViewSetup.DailyLevelsPath));
            return pack?.levels != null ? pack.levels.Count : -1;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DailyGen] 现有每日题库读取失败,将重生成: " + e.Message);
            return -1;
        }
    }

    /// <summary>难度分布统计行(验收留档)。</summary>
    static string BuildSummary(WaterSortDailyPack pack)
    {
        var sb = new System.Text.StringBuilder();
        int[] count = new int[3];
        long sum = 0;
        int min = int.MaxValue, max = 0;
        foreach (var l in pack.levels)
        {
            count[(int)l.difficulty]++;
            sum += l.measuredSteps;
            if (l.measuredSteps < min) min = l.measuredSteps;
            if (l.measuredSteps > max) max = l.measuredSteps;
        }
        for (int d = 0; d < 3; d++)
            if (count[d] > 0)
                sb.Append($"  [{(WaterSortDifficulty)d}] {count[d]} 关");
        sb.Append($"  全题步数 {min}~{max}(均值 {sum / (double)pack.levels.Count:0.0}),备用 {pack.spares.Count} 条");
        return sb.ToString();
    }

    /// <summary>
    /// M2.3 逐日验收(CLI 无参入口):逐条 TryDecode → 复证可解(2s,吸收墙钟抖动)
    /// → 断言难度/色数/步数落在轮转槽档窗内(日期 → 槽 → 规格,生成验收同口径)
    /// → 备用池逐条复证可解(兜底池不允许混入坏关)。任一失败 LogError;全绿 Log 摘要。
    /// </summary>
    [MenuItem("Box/WaterSort/Verify Daily Pack(逐日验证, M2.3)")]
    public static void VerifyDailyPack()
    {
        if (!File.Exists(WaterSortViewSetup.DailyLevelsPath))
        {
            Debug.LogError("[DailyGen] 每日题库 JSON 缺失,请先 BuildDailyPack: " + WaterSortViewSetup.DailyLevelsPath);
            return;
        }
        var pack = JsonUtility.FromJson<WaterSortDailyPack>(File.ReadAllText(WaterSortViewSetup.DailyLevelsPath));
        if (pack == null || pack.levels == null || pack.levels.Count == 0)
        {
            Debug.LogError("[DailyGen] 每日题库 JSON 为空或损坏,无法验收");
            return;
        }
        var failures = new System.Collections.Generic.List<string>();
        int[] count = new int[3];
        int[] min = { int.MaxValue, int.MaxValue, int.MaxValue };
        int[] max = new int[3];
        long[] sum = new long[3];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var l in pack.levels)
        {
            int d = (int)l.difficulty;
            var spec = DailySpecs[SlotOf(l.id)]; // 轮转槽 → 期望档窗
            if (spec.Difficulty != l.difficulty)
            {
                failures.Add($"#{l.id} 难度标签 {l.difficulty} 与槽窗 {spec.Difficulty} 不一致");
                continue;
            }
            if (l.colors < spec.MinColors || l.colors > spec.MaxColors)
            {
                failures.Add($"#{l.id} 色数 {l.colors} 越槽窗 [{spec.MinColors},{spec.MaxColors}]");
                continue;
            }
            if (l.measuredSteps < spec.MinSteps || l.measuredSteps > spec.MaxSteps)
            {
                failures.Add($"#{l.id} 落档步数 {l.measuredSteps} 越槽窗 [{spec.MinSteps},{spec.MaxSteps}]");
                continue;
            }
            count[d]++;
            min[d] = System.Math.Min(min[d], l.measuredSteps);
            max[d] = System.Math.Max(max[d], l.measuredSteps);
            sum[d] += l.measuredSteps;
            if (!VerifyOne(l)) failures.Add($"#{l.id} 解码失败或复证不可解");
        }
        // 备用池逐条复证(不查档窗:兜底条目不承诺与正式日同规格)
        for (int i = 0; i < pack.spares.Count; i++)
            if (!VerifyOne(pack.spares[i])) failures.Add($"备用 #{pack.spares[i].id} 解码失败或复证不可解");
        sw.Stop();
        var sb = new System.Text.StringBuilder("=== M2.3 每日题库逐日验收 ===\n");
        for (int d = 0; d < 3; d++)
        {
            if (count[d] == 0) continue;
            sb.AppendLine($"  [{(WaterSortDifficulty)d}] {count[d]} 关全过解码+复证,步数 {min[d]}~{max[d]}" +
                          $"(均值 {sum[d] / (double)count[d]:0.0})");
        }
        sb.Append($"  备用池 {pack.spares.Count} 条复证全绿;验收总耗时 {sw.ElapsedMilliseconds}ms");
        if (failures.Count == 0)
            Debug.Log($"[DailyGen] 每日题库验收通过: {pack.levels.Count} 天逐日档窗自洽 + 可解复证全绿\n" + sb.ToString());
        else
            Debug.LogError($"[DailyGen] 每日题库验收失败 {failures.Count} 项:\n" + string.Join("\n", failures));
    }

    /// <summary>单条复证:解码 + SolveAny(2s);返回是否可解。耗时计入调用方统计。</summary>
    static bool VerifyOne(WaterSortLevelData level)
    {
        if (!WaterSortLevelCodec.TryDecode(level, out var board)) return false;
        var res = WaterSortSolver.SolveAny(board, ReverifyTimeLimitMs);
        return res.Solved && !res.TimedOut;
    }
}
