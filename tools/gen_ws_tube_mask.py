# -*- coding: utf-8 -*-
"""重生成水排序内腔剪影遮罩 ws_tube_mask.png(修复底弧液体毛边/外露)。

问题:旧剪影底弧与玻璃壁线之间有 1→6px 渐宽的透明间隙,且该处玻璃封底
渐变 alpha 仅 0.14~0.23,液块被 UGUI Mask 硬裁出的边界直接裸露。

规则(与玻璃实测 alpha 对齐,换贴图须按同法重测):
- 壁线判定:玻璃 alpha >= 0.55(侧壁峰值 0.94~0.97、底弧肩部 0.84~0.9 均覆盖;
  内壁高光线峰值 ~0.59 不误触,且从外侧搜索时必先命中真壁线)。
- 左/右边界:每行取最左/最右壁线列,剪影边缘推进到壁线内侧 1px —— 液体裁剪边
  连同 2px 羽化全部藏进壁线半透明段之下,由玻璃壁压住。
- 底部边界:每列自 row 384 向下找第一行壁线(底弧/封底描边),剪影底边推进到
  该行下方 1px(进入描边),液柱底缘藏进封底渐变。
- 边缘 0.8px 高斯羽化:alpha 0→255 渐变,供软裁剪 shader 采样做抗锯齿。
- 画布保持 96×400 与 ws_tube 同构,行 0..15 清空(杯口区无液体,防止溢出管外)。

用法: python tools/gen_ws_tube_mask.py(仓库根目录执行)
"""
from PIL import Image, ImageFilter
import numpy as np

SRC = 'GameBox/Assets/Modules/WaterSort/UI/ws_tube.png'
DST = 'GameBox/Assets/Modules/WaterSort/UI/ws_tube_mask.png'

WALL_A = 0.55    # 壁线 alpha 阈值(见文件头说明)
TOP_ROW = 16     # 剪影顶界(杯口以下);旧行 18,留 2px 余量
BOTTOM_SCAN_FROM = 384  # 底部描边扫描起点(内腔直段结束处)

tube = np.array(Image.open(SRC).convert('RGBA'), dtype=np.float64) / 255
g = tube[:, :, 3]
H, W = g.shape
mask = np.zeros((H, W), dtype=np.uint8)

# 左/右边界:每行最外圈壁线,剪影推进到壁线内侧 1px
for r in range(TOP_ROW, H):
    cols = np.nonzero(g[r] >= WALL_A)[0]
    if len(cols) == 0:
        continue
    left = min(cols.min() + 1, W - 1)   # 壁线内侧 1px
    right = max(cols.max() - 1, 0)
    if right >= left:
        mask[r, left:right + 1] = 255

# 底部边界:每列自 BOTTOM_SCAN_FROM 向下找第一行壁线 B(c),行号 > B(c)+1 的
# 像素清除 —— 剪影底边推进到描边内侧 1px,液柱底缘藏进封底渐变。
for c in range(W):
    rows = np.nonzero(g[BOTTOM_SCAN_FROM:, c] >= WALL_A)[0]
    b = (BOTTOM_SCAN_FROM + rows.min() + 1) if len(rows) else H
    mask[b + 1:, c] = 0

# 0.8px 高斯羽化 → alpha 渐变边(软裁剪 shader 的抗锯齿来源)
alpha = Image.fromarray(mask, 'L').filter(ImageFilter.GaussianBlur(1.1))
a = np.array(alpha)

# 羽化尾迹约束:二值剪影之外、玻璃壁线带之外(alpha<0.4,即外壁柔光/背景)的
# 尾迹一律归零 —— 防止淡色液边溢出壁线落在管外背景上(羽化只许留在描边内)
a = np.where((mask == 0) & (g < 0.4), 0, a)

out = np.zeros((H, W, 4), dtype=np.uint8)
out[:, :, 0:3] = 255          # 白芯(遮罩 RGB 不参与渲染,仅 alpha 生效)
out[:, :, 3] = a
out[:TOP_ROW, :, 3] = 0       # 杯口区清零(双保险,防羽化回渗)
Image.fromarray(out, 'RGBA').save(DST)

# 自检输出:底部弧区每行 [新剪影左缘 | 玻璃壁线左缘] 的贴合度
m2 = out[:, :, 3] > 128
print('bbox alpha>10:', [(ys.min(), ys.max(), xs.min(), xs.max()) for ys, xs in
      [np.nonzero(out[:, :, 3] > 10)]][0])
for r in range(360, 397, 4):
    mc = np.nonzero(m2[r])[0]
    wc = np.nonzero(g[r] >= WALL_A)[0]
    if len(mc) and len(wc):
        print(f'row {r}: maskL={mc.min()} wallL={wc.min()} maskR={mc.max()} wallR={wc.max()}')
