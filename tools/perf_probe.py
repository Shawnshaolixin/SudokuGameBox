#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
真机性能体检工具（23 号文档：按需调用，非日常流程）
职责：一条命令完成 Unity 真机包的帧率 / 内存 / CPU / 温度采集与对比，
     为「新包 vs 历史基线」的性能回归判断提供同口径数据。

用法：
  python tools/perf_probe.py probe   [--tag 名称] [--duration 秒] [--pkg 包名]
  python tools/perf_probe.py report  <运行目录>          # 重生成报告(解析 raw 目录)
  python tools/perf_probe.py compare <运行目录A> <运行目录B>
  python tools/perf_probe.py list

采集原理（为什么这么采，见 23 号文档）：
  - 帧率：dumpsys SurfaceFlinger --latency <图层> —— Unity 走 Vulkan/GL 时
    gfxinfo 无 HWUI 帧数据(实测为空)，SF 的 BufferQueue 时间戳是可靠替代。
    注意：SF 只回溯最近 127 帧(30FPS 下 ≈ 4.2s 窗口)，--latency-clear 后统计
    窗口 = min(duration, 127 帧容量)。
  - 内存：dumpsys meminfo <pkg> 取 App Summary 段 PSS 明细，游玩前后各采一次
    算增量（泄漏探针）。
  - CPU：top 三次采样线程级(UnityMain/UnityGfxDeviceWorker 均值)。
  - 温度：dumpsys battery(十分之一摄氏度)。

数据落盘：Build/Logs/perf/<tag>_<时间戳>/{raw/*, summary.json, report.md}
（Build/Logs 已被 git 忽略，产物不入库）。
"""
import argparse
import glob
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import time
from collections import Counter
from datetime import datetime

try:
    sys.stdout.reconfigure(encoding="utf-8")  # Windows 控制台默认 GBK,防中文/emoji 乱码
except Exception:
    pass

# 仓库根 = 脚本目录上两级（tools/perf_probe.py → 仓库根）
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_ROOT = os.path.join(REPO_ROOT, "Build", "Logs", "perf")
PKG = "com.lixingames.rovilo"

# ============ adb 定位与执行 ============

def find_adb():
    """按 环境变量 ADB > Unity 内置 SDK(取最新版本) > PATH > ANDROID_HOME 定位 adb。"""
    cands = []
    env = os.environ.get("ADB")
    if env:
        cands.append(env)
    # Unity Hub 各版本 PlaybackEngines 下的 platform-tools
    cands += sorted(
        glob.glob(r"C:/Program Files/Unity/Hub/Editor/*/Editor/Data/PlaybackEngines/"
                  r"AndroidPlayer/SDK/platform-tools/adb.exe"), reverse=True)
    which = shutil.which("adb")
    if which:
        cands.append(which)
    home = os.environ.get("ANDROID_HOME")
    if home:
        cands.append(os.path.join(home, "platform-tools",
                                  "adb.exe" if os.name == "nt" else "adb"))
    for c in cands:
        if c and os.path.isfile(c):
            return c
    sys.exit("找不到 adb：请设 ADB 环境变量或安装 platform-tools")

def sh(cmd):
    """执行 adb shell 命令，返回去尾部换行的 stdout；失败抛错带 stderr。"""
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", timeout=120)
    if p.returncode != 0:
        raise RuntimeError(f"命令失败 {cmd}: {p.stderr.strip()}")
    return p.stdout.rstrip("\n")

def adb(*args):
    """adb 直连命令（非 shell）。"""
    return sh([ADB_PATH, *args])

def shell_cmd(inner):
    """adb shell <inner>；inner 内路径/图层名含空格，需按设备端 mksh 引号规则处理。"""
    return sh([ADB_PATH, "shell", inner])

# ============ 各数据源采集 ============

def collect_meta():
    return {
        "model": shell_cmd("getprop ro.product.model").strip(),
        "android": shell_cmd("getprop ro.build.version.release").strip(),
        "pkg": PKG,
        "date": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
    }

def collect_battery():
    """返回 {temp_c, level_pct}。"""
    out = shell_cmd("dumpsys battery")
    t = re.search(r"temperature:\s*(\d+)", out)
    lv = re.search(r"level:\s*(\d+)", out)
    return {"temp_c": round(int(t.group(1)) / 10.0, 1) if t else None,
            "level_pct": int(lv.group(1)) if lv else None}

def collect_low_power():
    """全局省电开关：ColorOS 开启时会压低应用刷新节奏，影响帧率归因。"""
    try:
        return shell_cmd("settings get global low_power").strip()
    except RuntimeError:
        return None

def collect_meminfo(dest):
    """存 dumpsys meminfo 全文，供离线重解析（retention 原则：raw 永远可重跑）。"""
    open(dest, "w", encoding="utf-8").write(shell_cmd(f"dumpsys meminfo {PKG}"))

def collect_top(dest):
    """线程级 + 进程级 top 各 3 次采样（间隔 1.5s）。"""
    pid = shell_cmd(f"pidof {PKG}").strip()
    if not pid:
        sys.exit("游戏未在运行：请先把游戏开到前台再跑体检")
    lines = []
    for _ in range(3):
        lines.append("=== SAMPLE ===")
        lines.append(shell_cmd(f"top -H -n 1 -b -p {pid}"))
        time.sleep(1.5)
    open(dest, "w", encoding="utf-8").write("\n".join(lines))

def find_sf_layer():
    """找产出帧的 SurfaceFlinger 图层名。

    候选顺序：含包名的 (BLAST) SurfaceView 子层 > 普通 SurfaceView > Activity 层；
    Activity 层 latency 全 0（实测），BLAST 层才有真实时间戳。SF 图层句柄(#号)
    每次会话都变，必须在采集时现查。
    """
    out = shell_cmd("dumpsys SurfaceFlinger --list")
    cands = []
    for line in out.splitlines():
        if PKG not in line or "RequestedLayerState" not in line:
            continue
        # RequestedLayerState{NAME parentId=...} —— NAME 可含空格/方括号，从 { 切到 parentId
        m = re.search(r"RequestedLayerState\{(.*?)\s+parentId=", line)
        if m:
            cands.append(m.group(1))
    def score(name):
        return (2 if "(BLAST)" in name else 1 if "SurfaceView[" in name else 0)
    cands.sort(key=score, reverse=True)
    for name in cands:
        out = shell_cmd(f"dumpsys SurfaceFlinger --latency {shlex.quote(name)}")
        # 有效性：存在 actualPresent>0 的帧行（Activity 层全 0 会被此筛掉）
        for row in out.splitlines()[1:]:
            parts = row.split()
            if len(parts) == 3 and int(parts[1]) > 0:
                return name
    sys.exit("找不到可用的渲染图层（游戏是否在前台？）")

def collect_sf_latency(layer, duration, dest):
    """清零延迟环 → 倒计时提示游玩 → 抓取最近窗口帧时间戳。"""
    shell_cmd(f"dumpsys SurfaceFlinger --latency-clear {shlex.quote(layer)}")
    print(f"[perf] 缓冲已清零，请【正常游玩】{duration} 秒（可点格子/切页面/看动画）...",
          flush=True)
    for remain in range(duration, 0, -2):
        print(f"[perf] 剩余 {remain} 秒", flush=True)
        time.sleep(min(2, remain))
    open(dest, "w", encoding="utf-8").write(
        shell_cmd(f"dumpsys SurfaceFlinger --latency {shlex.quote(layer)}"))

# ============ 解析 ============

def parse_frames(text):
    """SF latency 文本 → 帧统计。

    首行刷新周期(ns)；之后每行三列 desired/actual/ready(仅取 actual>0 的帧)。
    长帧阈值 = 2.2×刷新周期（60Hz≈36.7ms，90/120Hz 按周期缩放，不写死 40ms）。
    """
    lines = text.splitlines()
    refresh_ns = int(lines[0].strip()) if lines else 0
    acts = []
    for line in lines[1:]:
        parts = line.split()
        if len(parts) == 3 and parts[1].isdigit():
            a = int(parts[1])
            if a > 0:
                acts.append(a)
    if len(acts) < 2:
        return None
    iv = [(b - a) / 1e6 for a, b in zip(acts, acts[1:])]  # 帧间隔 ms
    iv_sorted = sorted(iv)
    n = len(iv)
    refresh_ms = refresh_ns / 1e6
    # 主节奏：间隔是刷新周期的几倍(取众数)——1=满刷新率, 2=锁半速(vSync 双 vblank)
    dom = Counter(round(x / refresh_ms) for x in iv).most_common(1)[0][0]
    long_th = 2.2 * refresh_ms
    long_n = sum(1 for x in iv if x > long_th)
    return {
        "refresh_ms": round(refresh_ms, 2),
        "frames": n + 1,
        "window_s": round((acts[-1] - acts[0]) / 1e9, 1),
        "fps_avg": round((n * 1e3) / ((acts[-1] - acts[0]) / 1e6), 1),
        "dominant_bin": dom,
        "iv_ms": {"min": round(iv_sorted[0], 2),
                  "p50": round(iv_sorted[n // 2], 2),
                  "p95": round(iv_sorted[int(n * 0.95)], 2),
                  "max": round(iv_sorted[-1], 2)},
        "long_n": long_n,
        "long_pct": round(100.0 * long_n / n, 2),
    }

def parse_meminfo(path):
    """dumpsys meminfo 全文 → PSS 明细（App Summary 段）。"""
    text = open(path, encoding="utf-8", errors="replace").read()
    tail = text[text.find("App Summary"):]
    keys = ["Native Heap", "Code", "Stack", "Graphics", "Private Other", "System"]
    val = {}
    for k in keys:
        m = re.search(rf"^\s*{re.escape(k)}:\s*(\d+)", tail, re.M)
        if m:
            val[k.replace(" ", "_").lower()] = int(m.group(1))
    # TOTAL PSS 与 TOTAL RSS 同行(dumpsys 布局),不能用行首锚点,单独整行正则
    t = re.search(r"TOTAL PSS:\s*(\d+).*?TOTAL RSS:\s*(\d+)", tail, re.S)
    if t:
        val["total_pss"], val["total_rss"] = int(t.group(1)), int(t.group(2))
    return val

def parse_top(path):
    """top 采样文本 → UnityMain / UnityGfxDeviceWorker 平均 CPU%。"""
    main_cpu, gfx_cpu = [], []
    for line in open(path, encoding="utf-8", errors="replace"):
        parts = line.split()
        if len(parts) < 12 or not parts[0].isdigit():
            continue
        # 线程行: [0]=pid [8]=%cpu [11]=线程名 [12]=进程名
        name = parts[11]
        cpu = float(parts[8])
        if name == "UnityMain":
            main_cpu.append(cpu)
        elif name.startswith("UnityGfxDevice"):
            gfx_cpu.append(cpu)
    def avg(xs):
        return round(sum(xs) / len(xs), 1) if xs else None
    return {"unity_main_pct": avg(main_cpu), "unity_gfx_pct": avg(gfx_cpu)}

def parse_top_samples(path):
    return sum(1 for line in open(path, encoding="utf-8", errors="replace")
               if line.startswith("=== SAMPLE ==="))

# ============ 报告生成 ============

def analyze_run(run_dir):
    """run_dir/raw 下原始文件 → summary dict；帧文件缺失时该段为 None。"""
    raw = os.path.join(run_dir, "raw")
    summary = {"meta": json.load(open(os.path.join(raw, "meta.json"),
                                      encoding="utf-8"))}
    sf_path = os.path.join(raw, "sf_latency.txt")
    if os.path.exists(sf_path):
        summary["frames"] = parse_frames(open(sf_path, encoding="utf-8",
                                              errors="replace").read())
    mem_start = os.path.join(raw, "mem_start.txt")
    mem_end = os.path.join(raw, "mem_end.txt")
    mem = {}
    if os.path.exists(mem_start):
        mem["start"] = parse_meminfo(mem_start)
    if os.path.exists(mem_end):
        mem["end"] = parse_meminfo(mem_end)
    if mem:
        # 泄漏探针：游玩前后 TOTAL PSS 增量
        s, e = mem.get("start", {}).get("total_pss"), mem.get("end", {}).get("total_pss")
        mem["delta_pss_kb"] = round((e - s) / 1024, 1) if s and e else None
        summary["mem"] = mem
    top_path = os.path.join(raw, "top_threads.txt")
    if os.path.exists(top_path):
        cpu = parse_top(top_path)
        cpu["samples"] = parse_top_samples(top_path)
        summary["cpu"] = cpu
    bat = [os.path.join(raw, "battery_start.txt"), os.path.join(raw, "battery_end.txt")]
    temps = []
    for p in bat:
        if os.path.exists(p):
            d = json.load(open(p, encoding="utf-8"))
            if d.get("temp_c"):
                temps.append(d["temp_c"])
    if temps:
        summary["power"] = {"temp_c": temps[0] if len(temps) == 1
                            else round(sum(temps) / len(temps), 1)}
    return summary

def fmt_kb(v):
    return f"{v / 1024:.0f}MB" if v else "-"

def render_report(s):
    """summary → 人类可读 report.md（判定口径与 23 号文档一致）。"""
    L = []
    L.append("# 真机性能体检报告")
    m = s["meta"]
    L.append(f"- 设备：{m.get('model')} · Android {m.get('android')} · {m.get('date')}")
    L.append(f"- 包：{m.get('pkg')}")
    f = s.get("frames")
    if f:
        dom = {1: "满刷新率", 2: "锁半速(vSync×2)"}.get(f["dominant_bin"],
               f"间隔 {f['dominant_bin']}×刷新周期")
        L.append("\n## 帧率")
        L.append(f"- 刷新周期 {f['refresh_ms']}ms · 统计 {f['frames']} 帧 / "
                 f"{f['window_s']}s · 平均 {f['fps_avg']} FPS")
        L.append(f"- 主节奏：{dom}")
        iv = f["iv_ms"]
        L.append(f"- 帧间隔(ms)：min {iv['min']} · p50 {iv['p50']} · p95 {iv['p95']} · "
                 f"max {iv['max']}")
        if f["long_n"] == 0:
            verdict = "✅ 零长帧，节奏平稳"
        elif f["long_pct"] < 2:
            verdict = f"⚠️ 长帧 {f['long_n']} 次({f['long_pct']}%)，个别可忽略"
        else:
            verdict = "🔴 长帧占比偏高，需定位环节"
        L.append(f"- 长帧(>{2.2 * f['refresh_ms']:.0f}ms)：{f['long_n']} 次 "
                 f"({f['long_pct']}%)——{verdict}")
    mem = s.get("mem")
    if mem:
        L.append("\n## 内存(PSS)")
        e = mem.get("end") or mem.get("start") or {}
        if e.get("total_pss"):
            L.append(f"- TOTAL PSS {fmt_kb(e['total_pss'])} · RSS "
                     f"{fmt_kb(e.get('total_rss'))}")
            L.append(f"- 构成：Native Heap {fmt_kb(e.get('native_heap'))} · "
                     f"Code {fmt_kb(e.get('code'))} · Private Other "
                     f"{fmt_kb(e.get('private_other'))} · System {fmt_kb(e.get('system'))}")
        d = mem.get("delta_pss_kb")
        if d is not None:
            leak = "✅ 增量极小，无泄漏信号" if abs(d) < 30 else "⚠️ 增量需关注"
            L.append(f"- 游玩前后增量：{d:+.1f}MB——{leak}")
    cpu = s.get("cpu")
    if cpu:
        L.append("\n## CPU")
        L.append(f"- UnityMain {cpu['unity_main_pct']}% · 渲染线程 "
                 f"{cpu['unity_gfx_pct']}% (均值/{cpu['samples']} 采样)")
    p = s.get("power")
    if p:
        t = p.get("temp_c")
        L.append(f"\n## 温度")
        L.append(f"- 电池 {t}°C——" + ("✅ 正常" if t and t < 40 else "⚠️ 偏高"))
    return "\n".join(L)

# ============ 子命令 ============

def cmd_probe(args):
    os.makedirs(OUT_ROOT, exist_ok=True)
    tag = args.tag or datetime.now().strftime("run")
    run_dir = os.path.join(OUT_ROOT, f"{tag}_{datetime.now().strftime('%Y%m%d-%H%M%S')}")
    raw = os.path.join(run_dir, "raw")
    os.makedirs(raw)
    print(f"[perf] 运行目录：{run_dir}")
    json.dump(collect_meta(), open(os.path.join(raw, "meta.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    devs = [l for l in adb("devices").splitlines()[1:] if l.strip() and "offline" not in l]
    if len(devs) != 1:
        sys.exit(f"需恰好一台在线设备，当前 {len(devs)} 台")
    # 前置提醒：未前台运行时 pidof 为空会在 collect_top 里兜底报错
    layer = find_sf_layer()
    print(f"[perf] 渲染图层：{layer}")

    # 1) 游玩前基线：电量/温度 + 内存 + CPU 线程采样
    json.dump(collect_battery(), open(os.path.join(raw, "battery_start.txt"), "w"))
    print(f"[perf] 低电模式={collect_low_power()} · "
          f"{collect_battery()['temp_c']}°C", flush=True)
    collect_meminfo(os.path.join(raw, "mem_start.txt"))
    print("[perf] 内存基线已采", flush=True)
    collect_top(os.path.join(raw, "top_threads.txt"))
    print("[perf] CPU 采样完成", flush=True)

    # 2) 游玩窗口：清零 SF 延迟环并倒计时
    collect_sf_latency(layer, args.duration, os.path.join(raw, "sf_latency.txt"))
    print("[perf] 帧数据已采", flush=True)

    # 3) 游玩后：内存增量 + 温度
    collect_meminfo(os.path.join(raw, "mem_end.txt"))
    json.dump(collect_battery(), open(os.path.join(raw, "battery_end.txt"), "w"))
    print("[perf] 结束采样，正在生成报告...", flush=True)

    finish_run(run_dir)
    print(f"\n[perf] 完成：{run_dir}\\report.md", flush=True)

def finish_run(run_dir):
    s = analyze_run(run_dir)
    json.dump(s, open(os.path.join(run_dir, "summary.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    open(os.path.join(run_dir, "report.md"), "w", encoding="utf-8").write(
        render_report(s))

def cmd_report(args):
    """重生成报告：raw 文件在就能离线重跑，数据永远可复核。"""
    if not os.path.exists(args.run_dir):
        sys.exit(f"目录不存在：{args.run_dir}")
    finish_run(args.run_dir)
    print(open(os.path.join(args.run_dir, "report.md"), encoding="utf-8").read())

def cmd_compare(args):
    """两个运行目录逐项对比，输出 Δ 与回归判定。"""
    def load(p):
        if not os.path.exists(p):
            sys.exit(f"目录不存在：{p}")
        return json.load(open(os.path.join(p, "summary.json"), encoding="utf-8"))
    a, b = load(args.run_a), load(args.run_b)
    print(f"对比：{os.path.basename(args.run_a)} → {os.path.basename(args.run_b)}\n")
    rows = []
    def add(k, fa, fb, better):
        va, vb = fa(a), fb(b)
        if va is None or vb is None:
            return
        rows.append((k, va, vb, better(va, vb)))
    add("平均 FPS", lambda s: s.get("frames", {}).get("fps_avg"),
        lambda s: s.get("frames", {}).get("fps_avg"),
        lambda x, y: "✅" if y >= x - 1 else "🔴 下降")
    add("帧间隔 p50(ms)", lambda s: s.get("frames", {}).get("iv_ms", {}).get("p50"),
        lambda s: s.get("frames", {}).get("iv_ms", {}).get("p50"),
        lambda x, y: "✅" if y <= x + 0.5 else "🔴 变差")
    add("帧间隔 p95(ms)", lambda s: s.get("frames", {}).get("iv_ms", {}).get("p95"),
        lambda s: s.get("frames", {}).get("iv_ms", {}).get("p95"),
        lambda x, y: "✅" if y <= x + 1 else "⚠️")
    add("长帧占比(%)", lambda s: s.get("frames", {}).get("long_pct"),
        lambda s: s.get("frames", {}).get("long_pct"),
        lambda x, y: "✅" if y <= x else "🔴 掉帧增多")
    add("TOTAL PSS(MB)", lambda s: (s.get("mem", {}).get("end") or
                                    s.get("mem", {}).get("start", {})).get("total_pss", 0) / 1024,
        lambda s: (s.get("mem", {}).get("end") or
                   s.get("mem", {}).get("start", {})).get("total_pss", 0) / 1024,
        lambda x, y: "✅" if y <= x + 30 else "⚠️ 增长 >30MB")
    add("电池温度(°C)", lambda s: s.get("power", {}).get("temp_c"),
        lambda s: s.get("power", {}).get("temp_c"),
        lambda x, y: "✅" if y <= x + 2 else "⚠️ 升温")
    add("UnityMain CPU(%)", lambda s: s.get("cpu", {}).get("unity_main_pct"),
        lambda s: s.get("cpu", {}).get("unity_main_pct"),
        lambda x, y: "✅" if y <= x + 5 else "⚠️")
    if not rows:
        print("两个目录都缺少可比数据")
        return
    w = max(len(r[0]) for r in rows) + 2
    print(f"{'指标':<{w}}{os.path.basename(args.run_a):>14}"
          f"{os.path.basename(args.run_b):>14}  判定")
    for k, va, vb, vd in rows:
        fa = f"{va:.1f}" if isinstance(va, float) else str(va)
        fb = f"{vb:.1f}" if isinstance(vb, float) else str(vb)
        print(f"{k:<{w}}{fa:>14}{fb:>14}  {vd}")
    print("\n说明：FPS/帧间隔基准差方向看「增加帧或降低间隔」；内存/温度越低越好。")

def cmd_list(args):
    """列出全部历史运行，方便挑两个做对比。"""
    runs = sorted(glob.glob(os.path.join(OUT_ROOT, "*/summary.json")))
    if not runs:
        print("（尚无运行记录）")
        return
    for p in runs:
        s = json.load(open(p, encoding="utf-8"))
        f = s.get("frames") or {}
        m = s.get("mem", {}).get("end") or s.get("mem", {}).get("start") or {}
        pss = fmt_kb(m.get("total_pss"))
        fps = f.get("fps_avg")
        cpu = s.get("cpu", {})
        print(f"{os.path.dirname(p)}  FPS={fps}  PSS={pss}  "
              f"main={cpu.get('unity_main_pct')}%  temp={s.get('power', {}).get('temp_c')}°C")

if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="Unity 真机性能体检(23 号文档)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("probe", help="采集一轮(游戏需在前台)")
    p.add_argument("--tag", default=None, help="运行标签，如 dev-phase9 / release-v15")
    p.add_argument("--duration", type=int, default=12, help="游玩采样秒数(默认 12)")
    p.add_argument("--pkg", default=PKG)
    sub.add_parser("list", help="列出历史运行")
    r = sub.add_parser("report", help="重生成指定目录报告")
    r.add_argument("run_dir")
    c = sub.add_parser("compare", help="对比两个运行")
    c.add_argument("run_a")
    c.add_argument("run_b")
    args = ap.parse_args()
    PKG = args.pkg if hasattr(args, "pkg") else PKG
    global ADB_PATH  # 只有 probe 需要真机；report/compare/list 可完全离线
    ADB_PATH = find_adb() if args.cmd == "probe" else None
    {"probe": cmd_probe, "list": cmd_list, "report": cmd_report,
     "compare": cmd_compare}[args.cmd](args)
