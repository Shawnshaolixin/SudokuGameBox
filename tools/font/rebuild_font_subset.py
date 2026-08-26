# -*- coding: utf-8 -*-
"""
Phase 9 字体子集同步工具(可重复执行):
1. 从 Localization.cs 提取字符串字面量中的 CJK 字符(正确跳过注释)
2. 合并现有子集 TTF 的全部字形(保守保留,避免误删旧字)
3. 用 fontTools.subset 从完整 MiSans OTF 重新生成 Regular/Bold 子集 TTF
4. 自验证:提取集 vs 新 TTF 字形表,缺失必须为 0

用途:任何 L10n 文案改动后重跑本脚本,确保真机字体零缺字。
用法: .venv/Scripts/python.exe Tools/font/rebuild_font_subset.py
"""
import re
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.subset import Options, Subsetter

ROOT = Path(r"d:/Projects/AI/SudokuGameBox")
LC_PATH = ROOT / "GameBox/Assets/Services/Abstractions/Localization.cs"
FONT_SRC = Path(r"C:/Users/slx97/Downloads/MiSans/MiSans/otf")
OUT_DIR = ROOT / "GameBox/Assets/UI/Fonts"
CHARSET_FILE = ROOT / "docs/字体子集字符集_完整.txt"

# 全角标点与常用符号(与旧字符集策略一致)
EXTRA_PUNCT = "，。、；：？！《》「」『』·—…’‘“”（）【】,.?!;:()'\"-+×÷=<>%@#&*|_/\\~^$[]{}"

def extract_l10n_chars(text: str) -> set:
    """提取字符串字面量中的 CJK 字符,跳过 // 与 /* */ 注释(状态机)。"""
    chars: set = set()
    i, n = 0, len(text)
    in_str = False
    in_block = False
    while i < n:
        c = text[i]
        if in_block:
            if c == "*" and i + 1 < n and text[i + 1] == "/":
                in_block = False
                i += 2
            else:
                i += 1
            continue
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_str = False
            elif "\u4e00" <= c <= "\u9fff":
                chars.add(c)
            i += 1
            continue
        # 非字符串/注释状态
        if c == "/" and i + 1 < n:
            nxt = text[i + 1]
            if nxt == "/":
                j = text.find("\n", i)
                i = n if j < 0 else j + 1
                continue
            if nxt == "*":
                in_block = True
                i += 2
                continue
        if c == '"':
            in_str = True
        i += 1
    return chars

def cmap_chars(path: Path) -> set:
    """返回 TTF 全部字形对应的字符集合(cmap 码点转 str 字符)。"""
    font = TTFont(str(path))
    out = set()
    for table in font["cmap"].tables:
        out |= {chr(cp) for cp in table.cmap.keys()}
    return out

def main() -> int:
    lc_chars = extract_l10n_chars(LC_PATH.read_text(encoding="utf-8"))
    print(f"Localization.cs 字符串字面量中文: {len(lc_chars)} 字")

    # 保留现有子集 TTF 全部字形(保守)
    old_regular = OUT_DIR / "MiSans-Regular-Subset.ttf"
    old_chars = cmap_chars(old_regular) if old_regular.exists() else set()
    print(f"现有子集 TTF 字形: {len(old_chars)} 字")

    charset = sorted(lc_chars | old_chars | set(EXTRA_PUNCT))
    CHARSET_FILE.write_text("\n".join(charset), encoding="utf-8")
    print(f"最终字符集: {len(charset)} 字 -> {CHARSET_FILE.name}")

    # 生成 Regular / Bold 子集
    for style in ("Regular", "Bold"):
        src = FONT_SRC / f"MiSans-{style}.otf"
        dst = OUT_DIR / f"MiSans-{style}-Subset.ttf"
        options = Options()
        options.layout_features = "*"
        options.glyph_names = True
        options.symbol_cmap = True
        options.legacy_cmap = True
        options.notdef_glyph = True
        options.notdef_outline = True
        options.recommended_glyphs = True
        options.name_IDs = "*"
        options.name_legacy = True
        options.name_languages = "*"
        options.text = "".join(charset)
        font = TTFont(str(src))
        subsetter = Subsetter(options)
        subsetter.populate(text="".join(charset))
        subsetter.subset(font)
        font.save(str(dst))
        print(f"生成 {dst.name}: {len(cmap_chars(dst))} 字形")

    # 自验证:新 TTF 必须覆盖 L10n 全字
    missing = sorted(lc_chars - cmap_chars(OUT_DIR / "MiSans-Regular-Subset.ttf"))
    if missing:
        print(f"[FAIL] L10n 缺字({len(missing)}): {''.join(missing)}")
        return 1
    print("[OK] L10n 全部字符已覆盖,零缺失")
    return 0

if __name__ == "__main__":
    sys.exit(main())
