"""
part_download.py — 多线程分片下载(专治国内大文件限速)

用法:
    python tools/part_download.py <url> <out_path> [parts]

原理:
    把文件按 Content-Length 切成 N 段,每段一个独立 Range 请求并行下载,
    各段 seek 写入主文件。限速源(如 hf-mirror)下大模型时速度可提升数倍。

    - 支持断点续传:已存在的分片文件(.partN)自动跳过
    - 支持 Range 服务器(阿里云 OSS / ModelScope 均支持)
"""

import concurrent.futures
import sys
import time
import urllib.request
from pathlib import Path

# Windows 控制台默认 GBK,emoji 打印会崩;强制 UTF-8 输出
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

PARTS = 4


def get_content_length(url: str) -> int:
    # 部分镜像(如 ModelScope)对 HEAD 不返回 Content-Length,改用 Range GET 探测
    req = urllib.request.Request(url, headers={"Range": "bytes=0-0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        cr = resp.headers.get("Content-Range")  # 形如 "bytes 0-0/6938087424"
        if cr:
            return int(cr.split("/")[1])
        length = resp.headers.get("Content-Length")
        if length is not None:
            return int(length)
        raise RuntimeError("无法获取文件大小(无 Content-Range / Content-Length)")


def download_part(url: str, out_path: Path, index: int, start: int, end: int, timeout: int = 600):
    """下载 [start, end) 字节区间,写入 out_path 的 seek 偏移处"""
    part_file = Path(f"{out_path}.part{index}")
    # 断点续传:分片文件已满则跳过
    if part_file.exists() and part_file.stat().st_size >= (end - start):
        print(f"  [分段 {index}] 已存在,跳过 ({end - start} bytes)")
        return

    for attempt in range(5):
        try:
            headers = {"Range": f"bytes={start}-{end - 1}"}
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=timeout) as resp, open(part_file, "wb") as f:
                while True:
                    chunk = resp.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
            print(f"  [分段 {index}] ✅ 完成 ({end - start} bytes)")
            return
        except Exception as e:
            print(f"  [分段 {index}] 第 {attempt + 1} 次失败: {e}")
            time.sleep(3)
    raise RuntimeError(f"分段 {index} 重试 5 次仍失败")


def merge_parts(out_path: Path, total: int):
    """把分片文件按序号拼回主文件(先校验分片完整性,防坏文件)"""
    chunk = total // PARTS
    expected = []
    for i in range(PARTS):
        start = i * chunk
        end = (i + 1) * chunk if i < PARTS - 1 else total
        part_file = Path(f"{out_path}.part{i}")
        actual = part_file.stat().st_size
        want = end - start
        if actual < want:
            raise RuntimeError(
                f"分片 {i} 不完整: {actual} < {want},不能合并,请重跑本工具续传")
        expected.append((start, part_file))

    with open(out_path, "r+b") as f:
        for start, part_file in expected:
            with open(part_file, "rb") as pf:
                f.seek(start)
                while True:
                    chunk = pf.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
            part_file.unlink()
    # 合并后抽验: 文件大小正确 + 开头/结尾非空洞(全 0 说明有分片内容丢失)
    size = out_path.stat().st_size
    with open(out_path, "rb") as f:
        head = f.read(8)
        f.seek(max(0, size - 8))
        tail = f.read(8)
    if size != total or head == b"\x00" * 8 or tail == b"\x00" * 8:
        raise RuntimeError(f"合并后校验失败 (size={size}, head={head.hex()}),请重跑本工具")
    print(f"✅ 合并校验通过: {out_path} ({size // 1048576} MB)")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    url = sys.argv[1]
    out_path = Path(sys.argv[2])
    global PARTS
    if len(sys.argv) >= 4:
        PARTS = int(sys.argv[3])

    out_path.parent.mkdir(parents=True, exist_ok=True)

    # 已完整下载则直接退出
    if out_path.exists() and out_path.stat().st_size > 0:
        try:
            total = get_content_length(url)
            if out_path.stat().st_size == total:
                print(f"✅ 文件已完整: {out_path}")
                return
            partial = out_path.stat().st_size
            print(f"断点续传: 已有 {partial // 1048576} MB / {total // 1048576} MB")
        except Exception:
            pass

    total = get_content_length(url)
    print(f"总大小: {total // 1048576} MB,分 {PARTS} 段并行下载...")

    # 创建主文件骨架(预分配)
    if not out_path.exists():
        with open(out_path, "wb") as f:
            f.truncate(total)

    chunk = total // PARTS
    ranges = [(i * chunk, (i + 1) * chunk if i < PARTS - 1 else total) for i in range(PARTS)]

    t0 = time.time()
    with concurrent.futures.ThreadPoolExecutor(max_workers=PARTS) as pool:
        futures = [
            pool.submit(download_part, url, out_path, i, start, end)
            for i, (start, end) in enumerate(ranges)
        ]
        for fut in concurrent.futures.as_completed(futures):
            fut.result()  # 抛错会冒泡

    merge_parts(out_path, total)
    elapsed = time.time() - t0
    print(f"🎉 下载完成,耗时 {elapsed // 60:.0f} 分 {elapsed % 60:.0f} 秒,"
          f"平均 {total / elapsed / 1048576:.1f} MB/s")


if __name__ == "__main__":
    main()
