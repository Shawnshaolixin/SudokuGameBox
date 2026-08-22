# LiblibAI 提示词库 — 数独游戏整套 UI 素材

> 适配平台：**LiblibAI**（国内 SD 模型聚合平台）
> 配套管线：`tools/sprite_pipeline.py` + `Assets/Editor/SpritePipelineImporter.cs`
> 用法：复制提示词 → LiblibAI 选模型 → 生成 → 下载 PNG → 放 `tools/ai_input/` → 运行管线 → Unity 自动导入

---

## 0. 先用 5 分钟定好 LiblibAI 参数（重要）

### 0.1 模型选择（LiblibAI 首页选模型）

| 目标 | 推荐模型 | 关键词搜索 |
|---|---|---|
| **背景/氛围图** | 2.5D 或写实向 SDXL | 搜「2.5D」「游戏背景」「国风 赛博」 |
| **图标/UI 元素** | 扁平 2D 模型 | 搜「扁平」「2D」「游戏UI」「像素」 |
| **粒子/光效** | 通用 SDXL（Illustrious XL 系） | 搜「Illustrious XL」「Pony」 |
| **折中万能** | SDXL base 系 | 搜「SDXL」 |

> 💡 不确定就先用 **SDXL base 系**（社区默认推荐的那几个），出图稳定、风格可控。

### 0.2 统一参数（每张图都这么设）

| 参数 | 建议值 | 说明 |
|---|---|---|
| **采样器** | DPM++ 2M Karras | 通用稳定 |
| **步数** | 25-30 | 太快粗糙，太慢没必要 |
| **CFG** | 5-7 | 7 以下避免过度饱和 |
| **分辨率** | 按下方各类型的「建议尺寸」 | 出图尺寸尽量贴近目标尺寸 |
| **负面提示词** | 统一用第 3 节的 | 必须复制 |

### 0.3 透明背景（关键）

- LiblibAI 默认出图**不透明** → 你的管线用 `rembg` 去背景兜底，**不用手动抠图**
- 如果所选模型有「透明底」选项（如部分 2D 模型），开了更好 → 管线加 `--skip-bg`
- 下载时选 PNG 格式（不要 JPG）

---

## 1. 全局风格锚点（起手式，每张图都要带）

下面这段**所有提示词都要拼在最前面**，保证整套 UI 风格统一：

```
(mobile game UI asset:1.2), flat design, dark theme, deep navy purple background, main color #1a1a2e, accent gradient #667eea to #764ba2, rounded corners, minimal clean, modern mobile puzzle game, high quality, sharp edges
```

> 生成前建议先在 LiblibAI 搜一个「游戏UI」「扁平」相关 LoRA 挂上（权重 0.6-0.8），风格一致性更好。

---

## 2. 分类提示词

### 🖼️ A. 背景（bg）— AI 生成最值的一张

**主菜单背景**（建议尺寸 832×1216，9:16）
```
(mobile game background:1.3), dark space theme, deep navy purple nebula, subtle star particles, faint geometric line pattern, soft purple glow #667eea, gradient from dark #0f0f1a to deep purple, minimal, not busy, vertical mobile game menu background, no text, no logo
```
→ 命名：`bg_main_menu`

**对局背景**（建议尺寸 832×1216）
```
(mobile game background:1.3), very dark smooth gradient, deep navy to dark purple, extremely subtle faint grid pattern, calm, focused, minimal, low contrast, sudoku gameplay background, no text, no elements
```
→ 命名：`bg_gameplay`

### 📋 B. 面板 / 卡片（panel）— 9-slice 友好是关键

> ⚠️ 9-slice 铁律：面板图案必须**均匀**（纯色/渐变/简单边框）。复杂纹理拉伸会断裂。

**弹窗面板**（建议尺寸 1024×1024）
```
(ui panel background:1.2), rounded rectangle, dark semi-transparent surface, subtle purple border edge, inner shadow, slight glow on border, flat game dialog box, uniform texture, tileable, no text, centered
```
→ 命名：`panel_dialog`

**每日挑战卡片**（建议尺寸 1024×512，横向）
```
(ui card panel:1.2), horizontal rectangle, dark background, purple gradient accent stripe on left side, gold subtle trim, premium feel, flat game ui card, uniform texture, no text
```
→ 命名：`panel_daily_challenge`

**统计卡片 / 设置分组**（建议尺寸 512×256）
```
(ui card panel:1.2), small rounded rectangle, dark surface #16162a, thin subtle border, flat minimal game ui card, uniform dark texture, no text
```
→ 命名：`panel_stat_card` / `panel_settings_group`

### 🔘 C. 按钮（button）— 建议主按钮 AI 做，普通按钮程序化

> 💡 **重要建议**：你项目里有大量普通按钮（难度、工具、数字键盘）。这些在 Unity 里**用 `Image` 纯色 + 圆角 + 一张统一的 9-slice 底图**就能做，完全不需要 AI 每张生成。AI 只需要做**几类"主题按钮底图"**，其他复用。

**主按钮（渐变紫，用于「开始游戏」「继续」）**（建议尺寸 1024×512）
```
(ui game button:1.2), rounded rectangle pill, vibrant gradient #667eea to #764ba2, subtle inner glow, glossy edge highlight, flat mobile game primary button, centered, no text, uniform gradient, 9-slice friendly
```
→ 命名：`btn_primary`

**次按钮（深色细边，用于「取消」「关闭」）**（建议尺寸 1024×512）
```
(ui game button:1.2), rounded rectangle pill, dark surface, thin purple border, subtle border glow, flat mobile game secondary button, centered, no text, uniform texture, 9-slice friendly
```
→ 命名：`btn_secondary`

**危险按钮（红色系，用于「重置」「放弃」）**（建议尺寸 1024×512）
```
(ui game button:1.2), rounded rectangle pill, dark red gradient, subtle red glow border, flat mobile game danger button, centered, no text, uniform, 9-slice friendly
```
→ 命名：`btn_danger`

**提示消耗按钮（金色，用于「看广告得提示」）**（建议尺寸 1024×512）
```
(ui game button:1.2), rounded rectangle pill, dark gold gradient, golden glow, premium reward button, flat mobile game, centered, no text, uniform, 9-slice friendly
```
→ 命名：`btn_reward`

> 数字键盘按钮（1-9）和工具按钮的**底图**：直接用 `btn_secondary` 那一张切 9-slice 复用即可，**数字/图标文字用 TMP 层叠**（AI 生成的数字会歪、会糊，别用）。

### 🎯 D. 图标（icon）— AI 生成的强项

> 统一：单色系（白色/紫色渐变描边）、扁平、128-512px。**一个 prompt 生成一个图标**，便于抠图。

**App 图标**（建议尺寸 1024×1024）
```
(mobile app icon:1.3), rounded square gradient background #667eea to #764ba2, minimalist sudoku 9x9 grid symbol in center, white line art, flat, clean, modern, game app icon, no text
```
→ 命名：`icon_app`

**功能图标**（一个 prompt 换 [描述] 生成多个，建议尺寸 512×512）
```
(flat game ui icon:1.2), white stroke with subtle purple gradient fill, minimalist line icon, [描述], clean, simple shape, small icon, centered, transparent style, no text, no background detail

[描述] 填：
  - lightbulb with spark, for hint → icon_hint
  - undo arrow curving left, for undo → icon_undo
  - eraser, for erase → icon_erase
  - pencil writing, for notes mode → icon_notes
  - pause bars, for pause → icon_pause
  - gear, for settings → icon_settings
  - trophy, for victory → icon_trophy
  - bar chart, for statistics → icon_stats
  - calendar, for daily challenge → icon_daily
  - clock, for timer → icon_timer
  - gold coin, for rewards → icon_coin
  - home, for main menu → icon_home
  - play triangle, for start → icon_play
  - refresh, for new game → icon_new_game
  - trash, for reset → icon_reset
```

**难度图标**（建议尺寸 256×256，一个难度一个）
```
(flat game icon:1.2), [难度形状] with [颜色] gradient fill, minimalist, clean, mobile game difficulty icon, no text

填：
  - green circle, Easy → icon_difficulty_easy（绿 #4CAF50）
  - orange triangle, Medium → icon_difficulty_medium（橙 #FF9800）
  - red diamond, Hard → icon_difficulty_hard（红 #f44336）
```

### ✨ E. 粒子 / 特效贴图（particle）— AI 强项，一个元素一张

**胜利撒花星芒**（建议尺寸 512×512）
```
(particle texture:1.3), single glowing star sparkle, purple and gold gradient glow, soft feathered edges, radiant burst, celebration particle, isolated on black background, centered
```
→ 命名：`particle_star_glow`

**正确反馈光晕**（建议尺寸 512×512）
```
(particle texture:1.3), soft circular glow ring, thin purple blue gradient ring, feathered soft edges, subtle UI feedback glow, isolated on black background, centered
```
→ 命名：`particle_correct_glow`

**错误反馈闪烁**（建议尺寸 512×512）
```
(particle texture:1.3), soft red glow burst, feathered edges, subtle error feedback flash, isolated on black background, centered
```
→ 命名：`particle_error_flash`

**点击涟漪**（建议尺寸 512×512）
```
(particle texture:1.3), single expanding circle ripple ring, subtle white purple gradient, fading edges, tap feedback effect, isolated on black background, centered
```
→ 命名：`particle_tap_ripple`

> ⚠️ 粒子贴图用**黑色背景**生成即可——粒子材质会用 Additive 混合模式，黑色自动变透明。**不需要 rembg 抠图**（管线 `--skip-bg`）。

---

## 3. 统一负面提示词（每张都复制）

```
text, letters, words, numbers, watermark, signature, logo, copyright, blurry, low quality, jpeg artifacts, distorted, deformed, extra limbs, bad anatomy, cropped, out of frame, busy background, complex texture, ui elements, buttons, icons, frame, border
```

---

## 4. 参数与尺寸速查表（生成前对着设）

| 资源类型 | 建议尺寸 | 采样步数 | CFG | 背景要求 |
|---|---|---|---|---|
| 背景 bg | 832×1216 | 30 | 6 | 不透明即可（管线抠不抠都行，建议保留全图） |
| 面板 panel | 1024×1024 或 1024×512 | 28 | 6.5 | 尽量均匀 |
| 按钮 button | 1024×512 | 28 | 6.5 | 均匀渐变 |
| 图标 icon | 512×512 | 25 | 6 | 单色干净，背景简单（rembg 好抠） |
| 粒子 particle | 512×512 | 25 | 5.5 | **黑色背景**（Additive 混合变透明） |

---

## 5. 完整工作流（生成 → 到 Unity 用时 3 分钟）

```powershell
# 1. LiblibAI 生成后下载 PNG → 放 tools/ai_input/

# 2. 批量处理（按类型分别跑）
cd d:\Projects\AI\SudokuGameBox\tools

python sprite_pipeline.py batch ./ai_input/bg/ --type bg
python sprite_pipeline.py batch ./ai_input/panel/ --type panel
python sprite_pipeline.py batch ./ai_input/btn/ --type button
python sprite_pipeline.py batch ./ai_input/icon/ --type icon

# 粒子特殊处理：黑底图跳过去背景
python sprite_pipeline.py batch ./ai_input/particle/ --type particle --skip-bg
# （批量模式下 rembg 对黑底粒子无效，粒子的黑色由材质 Additive 处理）

# 3. 切回 Unity → 自动导入 + 9-slice ✅
```

---

## 6. 省时策略（新手必看）

| 素材 | 用 AI 生成？ | 理由 |
|---|---|---|
| 主菜单/对局**背景** | ✅ 必须 AI | 程序化做不出氛围 |
| **图标**（功能/难度/成就） | ✅ AI | 强项、量大、风格统一 |
| **粒子/光效** | ✅ AI | 强项 |
| **主/次/危险/奖励按钮**（4 张底图） | ✅ AI | 做一次，复用全部按钮 |
| **面板/卡片** | ⚠️ 半 AI | 只生成 3-4 张底图，其余复用 |
| **普通按钮（难度/工具/数字）** | ❌ 不要 AI | Unity 里用 `btn_secondary` 9-slice 复用 + TMP 文字 |
| **数字盘数字 / 文本** | ❌ 不要 AI | 用 TextMeshPro，AI 生成的数字会歪 |

> 结论：**整套 UI 其实只需要 AI 生成约 30-40 张**（2 背景 + 4 按钮 + 3-4 面板 + 20 图标 + 4 粒子），其余靠复用和 TMP 程序化补齐。比逐张生成 100 张高效得多，风格也统一。

---

## 7. 生成清单（照着打勾）

- [ ] 背景：`bg_main_menu`、`bg_gameplay`
- [ ] 按钮：`btn_primary`、`btn_secondary`、`btn_danger`、`btn_reward`
- [ ] 面板：`panel_dialog`、`panel_daily_challenge`、`panel_stat_card`、`panel_settings_group`
- [ ] 图标：`icon_app` + 15 个功能图标 + 3 个难度图标
- [ ] 粒子：`particle_star_glow`、`particle_correct_glow`、`particle_error_flash`、`particle_tap_ripple`
- [ ] 跑管线 → 切 Unity 验证 → 搭主菜单
