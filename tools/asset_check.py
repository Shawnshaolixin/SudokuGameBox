#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CI 资产校验（10 文档 Phase 6.5 任务 6.5-2 / 14 号文档附录 B）
职责：
  1. GameBox/Assets/Art/ 下 PNG 文件名符合命名白名单（_btn/_panel/_icon/_particle/_bg）
  2. PNG 纹理尺寸 ≤ 上限（读 PNG 头 IHDR，不加载整图；O(1) 审计）
豁免：tools/legacy_assets.txt 中的存量文件不参与检查（存量豁免 + 增量约束，面试可讲）。
违反任一规则 → exit 1，Jenkins 构建标红，阻止交付。
设计意图：纯 Python 零 Unity 依赖 = 唯一可廉平常驻的 CI job（10 文档 §13 成本纪律）。
用法：
  python tools/asset_check.py            # 默认上限 2048
  python tools/asset_check.py --max 1024 # 自定义尺寸上限（按需收紧）
"""
import argparse
import re
import struct
import sys
from pathlib import Path

# 仓库根 = 本文件（tools/）的上级目录；Unity 工程在仓库根的 GameBox/ 下
PROJECT_ROOT = Path(__file__).resolve().parent.parent
ART_ROOT = PROJECT_ROOT / "GameBox" / "Assets" / "Art"

# 命名白名单：文件名须含 _btn/_panel/_icon/_particle/_bg（10 文档 6.5-2；增补需同步文档）
WHITELIST = re.compile(r'_(btn|panel|icon|particle|bg)(?:\.|_)')
# 纹理尺寸上限（像素）。UI 图一般 ≤2048；超限会撑大首包/内存，由 CI 卡死成本
MAX_DIMENSION = 2048
# 存量资产豁免清单：开发期遗留的临时命名，规则只约束增量；后续清理后从清单移除（见 14 号文档附录 B）
LEGACY_FILE = PROJECT_ROOT / "tools" / "legacy_assets.txt"


def read_png_size(path):
    """读 PNG 头 IHDR 中的宽高（PNG 规范：前 8 字节签名，IHDR 宽高为偏移 16 处的大端 uint32）。"""
    try:
        with open(path, 'rb') as f:
            header = f.read(24)
        if header[:8] != b'\x89PNG\r\n\x1a\n':
            return None  # 非 PNG（如损坏/伪装文件），交由其他手段处理
        width, height = struct.unpack('>II', header[16:24])
        return width, height
    except (OSError, struct.error):
        return None


def load_legacy():
    """读取存量豁免清单（每行一个仓库相对路径，支持 # 注释行）；清单不存在则返回空集。"""
    if not LEGACY_FILE.exists():
        return set()
    legacy = set()
    for line in LEGACY_FILE.read_text(encoding='utf-8').splitlines():
        line = line.strip()
        if line and not line.startswith('#'):
            legacy.add(line.replace('\\', '/'))
    return legacy


def check_png(png_path, max_dimension):
    """单文件检查：返回违规说明列表（空 = 通过）。max_dimension 为本次运行的尺寸上限。"""
    problems = []
    if not WHITELIST.search(png_path.name):
        problems.append(
            f'文件名不在白名单（需含 _btn/_panel/_icon/_particle/_bg）：{png_path.relative_to(PROJECT_ROOT)}')
    size = read_png_size(png_path)
    if size:
        w, h = size
        if w > max_dimension or h > max_dimension:
            problems.append(
                f'纹理超限 {w}x{h} > {max_dimension}：{png_path.relative_to(PROJECT_ROOT)}')
    return problems


def main():
    parser = argparse.ArgumentParser(description='CI 资产校验（命名白名单 + 纹理尺寸上限）')
    parser.add_argument('--max', type=int, default=MAX_DIMENSION,
                        help='纹理尺寸上限像素（默认 2048）')
    args = parser.parse_args()

    legacy = load_legacy()
    if not ART_ROOT.exists():
        print(f'[asset_check] 目录不存在（跳过本 job，需人工确认）: {ART_ROOT}')
        return 0

    total = 0
    skipped = 0
    failed = 0
    for png in sorted(ART_ROOT.rglob('*.png')):
        total += 1
        if png.relative_to(PROJECT_ROOT).as_posix() in legacy:
            skipped += 1  # 存量豁免：历史遗留命名不拦增量契约（清理后从清单移除）
            continue
        for problem in check_png(png, args.max):
            print('[FAIL] ' + problem)
            failed += 1

    print(f'[asset_check] 共 {total} 个 PNG（豁免 {skipped}），违规 {failed} 个')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())