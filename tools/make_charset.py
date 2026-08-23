# -*- coding: utf-8 -*-
"""Phase 6 字体子集:生成补全后的完整字符集(原字符 + ASCII 0x20-0x7E + 常用中文标点 + 界面补充词)。
输出:docs/字体子集字符集_完整.txt(单行,去重保序)。
"""
import os

BASE = r"D:\Projects\AI\SudokuGameBox\docs"
SRC = os.path.join(BASE, "字体子集字符集.txt")
DST = os.path.join(BASE, "字体子集字符集_完整.txt")

with open(SRC, encoding="utf-8") as f:
    existing = f.read()

# 补全 ASCII 0x20-0x7E(可打印 95 字符,防英文 L10n 文案缺字,如 OK/YES/NO)
ascii_chars = "".join(chr(c) for c in range(0x20, 0x7F))

# 常用中文标点 + 界面高频词(数独/游戏/设置/结算等)
extra = "、。；：！？（）【】《》“”‘’…—·×←→￥#@±"
extra_words = (
    "数字独游戏主菜单设置难度简单中等困难专家开局重来提示擦除完成胜利失败暂停继续"
    "返回退出确定取消保存加载新游戏最高纪录时间成绩勋章成就每日挑战模式选择"
    "主题语言音效音乐振动本地化简体繁体帮助关于版本更新开始结束加载中"
    "网络错误请稍候再试恭喜挑战成功再来一局分享评分"
)

chars = existing + ascii_chars + extra + extra_words
seen, out = set(), []
for ch in chars:
    if ch not in seen:
        seen.add(ch)
        out.append(ch)
result = "".join(out)

with open(DST, "w", encoding="utf-8") as f:
    f.write(result)
print("总字符数:", len(result))