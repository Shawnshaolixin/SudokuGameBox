# -*- coding: utf-8 -*-
"""验证子集字体覆盖所有关键字符。"""
from fontTools.ttLib import TTFont

def check(path, label):
    f = TTFont(path)
    cmap = f.getBestCmap()
    test = "数独设置难度开始退出确定取消OKYesNO%.·「」~1234567890"
    missing = [c for c in test if ord(c) not in cmap]
    print(f"[{label}] 字形数: {len(cmap)} | 缺失关键字符: {missing if missing else '无'}")
    f.close()

check(r"D:\Projects\AI\SudokuGameBox\GameBox\Assets\UI\Fonts\MiSans-Regular-Subset.ttf", "Regular")
check(r"D:\Projects\AI\SudokuGameBox\GameBox\Assets\UI\Fonts\MiSans-Bold-Subset.ttf", "Bold")