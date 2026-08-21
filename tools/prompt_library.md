# AI Prompt 模板库 — 数独游戏 UI 素材

> 用法：复制下方的 Prompt → 粘贴到 Midjourney/ComfyUI/DALL-E → 下载原图 → 放到 `tools/ai_input/` → 运行 `python sprite_pipeline.py batch ./ai_input/ --type <类型>`

---

## 🌈 1. 风格参考图（锁风格用）

先用下面这个 prompt 在 Midjourney 里生成几张"风格参考图"，挑一张满意的作为 `--style reference` 的种子图，之后所有 prompt 都加 `--sref <种子图URL>` 来保持统一。

```
prompt: mobile game UI kit, sudoku puzzle game, dark purple theme,
flat design, minimalist, clean rounded buttons, soft gradients #667eea to #764ba2,
dark background #1a1a2e, sci-fi neon accents, game HUD elements,
sprite sheet style, transparent background --ar 16:9 --v 6.1
```

---

## 🔘 2. 按钮 (button)

### 通用按钮（常态/按下/悬停）
```
Prompt:
game UI button, rounded rectangle, no text, solid dark surface with thin purple border,
subtle gradient from dark to darker, slight inner glow at edges when hover state, flat design,
mobile game style, sudoku puzzle UI element, centered, transparent background --style raw --v 6.1 --sref <风格参考图URL>

输出命名建议:
  btn_play, btn_pause, btn_settings, btn_hint, btn_undo, btn_erase, btn_notes
```

### 难度选择按钮
```
Prompt:
game UI selection button, rounded rectangle pill shape, labeled Easy/Medium/Hard,
color-coded: green gradient for easy, yellow-orange for medium, red-purple for hard,
flat design mobile game, sudoku difficulty selector, transparent background --v 6.1 --sref <风格参考图URL>

输出命名建议:
  btn_easy, btn_medium, btn_hard
```

### 数字键盘按钮
```
Prompt:
single number keypad button, square rounded corners, dark surface with light purple edge glow,
large centered digit 1-9, clean sans-serif font, sudoku keypad UI,
mobile game, transparent background, one button per image --v 6.1 --sref <风格参考图URL>

输出命名建议:
  btn_num_1 ~ btn_num_9（建议批量生成 9 张）
```

---

## 📋 3. 面板背景 (panel)

### 弹窗/对话框背景
```
Prompt:
game UI panel background, rounded rectangle with 12px corner radius,
dark semi-transparent surface #16162a with subtle purple edge border,
slight inner shadow for depth, clean minimal style, sudoku game dialog box background,
mobile game UI, transparent background, 9-slice friendly --v 6.1 --sref <风格参考图URL>

输出命名建议:
  panel_dialog, panel_popup, panel_settings
```

### 每日挑战卡片
```
Prompt:
game UI card panel, horizontal layout, rounded rectangle,
dark background with purple gradient accent on left side,
subtle gold trim for premium feel, sudoku daily challenge card,
mobile game UI element, transparent background --v 6.1 --sref <风格参考图URL>

输出命名建议:
  panel_daily_challenge
```

### 数独盘面背景
```
Prompt:
sudoku grid background, 9x9 subtle grid lines on dark surface,
thin white-grey grid lines, 3x3 block separation with slightly thicker borders,
clean minimal style, sudoku board UI background,
mobile game, transparent background --v 6.1 --sref <风格参考图URL>

输出命名建议:
  panel_game_board
```

---

## 🎯 4. 图标 (icon)

### 功能图标
```
Prompt:
game icon, flat design, white-purple gradient stroke, simple clean shape,
[具体描述], mobile game sudoku UI icon, small 128x128, transparent background --v 6.1 --sref <风格参考图URL>

填入 [具体描述] 的例子:
  - lightbulb for hint
  - backward arrow for undo
  - eraser for erase
  - pencil for notes mode
  - gear for settings
  - trophy for victory
  - clock for timer
  - bar chart for statistics
  - calendar for daily challenge
```

### 难度图标
```
Prompt:
game difficulty icon, simple geometric shape, [颜色] gradient fill,
sudoku game UI icon, small 128x128, transparent background --v 6.1 --sref <风格参考图URL>

填入:
  - green circle for easy
  - orange-yellow triangle for medium
  - red-purple diamond for hard
```

---

## ✨ 5. 粒子/特效贴图 (particle)

### 胜利撒花
```
Prompt:
particle sprite sheet element, single glowing sparkle, star burst shape,
purple-gold gradient glow, soft edges, celebration effect,
game particle texture, isolated on transparent background --v 6.1

单个粒子元素分别生成: star_glow, circle_spark, diamond_confetti, line_streak
```

### 填数反馈光晕
```
Prompt:
soft circular glow ring, thin purple-blue gradient ring, feathered edges,
subtle UI feedback effect for correct answer, transparent background --v 6.1

输出命名: particle_correct_glow, particle_error_flash
```

### 按钮点击涟漪
```
Prompt:
circular ripple ring expanding, subtle white-purple, fading edges,
mobile UI tap feedback effect, transparent background --v 6.1

输出命名: particle_tap_ripple
```

---

## 🖼️ 6. 背景 (bg)

### 主菜单背景
```
Prompt:
mobile game background, dark space theme, subtle purple nebula gradient,
minimal geometric patterns, dark blue-purple tones #0f0f1a,
non-distracting, suitable for sudoku main menu, vertical 9:16 aspect ratio --v 6.1

输出命名: bg_main_menu
```

### 对局背景
```
Prompt:
subtle dark gradient background, very faint grid pattern,
dark navy to deep purple, simple clean, sudoku gameplay background,
non-distracting for focused puzzle solving, mobile vertical 9:16 --v 6.1

输出命名: bg_gameplay
```

---

## 🛠️ ComfyUI 专用工作流提示

如果你用 ComfyUI + SDXL，关键节点组合：

```
Load Checkpoint (SDXL 或游戏专用模型如 Pony Diffusion)
  → CLIP Text Encode (正面 prompt)
  → CLIP Text Encode (负面 prompt: "text, letters, numbers, watermark, signature, blurry, low quality")
  → KSampler
  → IP-Adapter (加载风格参考图)        ← 保持风格一致的关键
  → ControlNet Canny (可选:从线框图控制形状) ← 精确控制 UI 元素形状
  → Remove Background (rembg node)     ← 可选，也可以用 Python 脚本做
  → Save Image
```

---

## ⚠️ 实用建议

1. **先生成风格参考图**，调满意了再批量出其他元素——风格不一致是最大痛点
2. **Midjourney 的 `--sref`** 是保持风格一致的银弹，比反复改 prompt 高效得多
3. **按钮和面板**最依赖 9-slice，所以生成时确保图案是均匀/可平铺的（避免复杂纹理）
4. **图标**最好用扁平化风格，icon 尺寸小（128x128），复杂纹理看不清
5. **粒子贴图**可以在 Kenney Particle Pack 基础上用 AI 生成补充，混合使用效率最高
6. **背景**建议先试 Kenney，不够满意再用 AI 生成——背景消耗大量 tokens 但很容易过时