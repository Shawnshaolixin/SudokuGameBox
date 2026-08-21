# 12 — AIGC 精灵管线落地指南

> 版本:v1.0 | 状态:Ready | 依赖:[09_免费资源清单](./09_免费资源清单.md) | 配套脚本:`tools/sprite_pipeline.py`、`Assets/Editor/SpritePipelineImporter.cs`

---

## 0. 一句话结论

> **"AI 生成图片 → Python 后处理 → Unity 自动导入"三步管线已就绪，你只需要复制 prompt、下载图片、运行一行命令，Unity 端自动完成精灵配置。**

---

## 1. 管线全景

```
┌─────────────────────────────────────────────────────────────────┐
│                      🎨 AI 生图（你手动做）                       │
│  Midjourney / ComfyUI / Leonardo.ai                             │
│  参考: tools/prompt_library.md 的 Prompt 模板                    │
│  下载原图 → 放到 tools/ai_input/                                │
├─────────────────────────────────────────────────────────────────┤
│                    ✂️  Python 后处理（自动）                      │
│  python sprite_pipeline.py batch ./ai_input/ --type button      │
│  ┌──────────┐    ┌──────────┐    ┌──────────┐                   │
│  │ rembg    │ →  │ Pillow   │ →  │ Pillow   │                   │
│  │ 去背景    │    │ 裁切透明边│    │ 缩放到目标│                   │
│  └──────────┘    └──────────┘    └──────────┘                   │
│  输出: Assets/Art/UI/Buttons/*.png                               │
├─────────────────────────────────────────────────────────────────┤
│                  🔧  Unity 自动导入（自动）                       │
│  AssetPostprocessor 检测新 PNG                                  │
│  → Texture Type = Sprite                                        │
│  → 按命名约定自动 9-slice / Pivot / 压缩                         │
│  → 即可拖入 Canvas 使用                                         │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 文件清单（我已经帮你建好了）

| 文件 | 位置 | 作用 |
|---|---|---|
| **Python 管线脚本** | `tools/sprite_pipeline.py` | 去背景 + 裁切 + 缩放的自动化后处理 |
| **Python 依赖** | `tools/requirements.txt` | 运行脚本需要安装的 Python 包 |
| **Prompt 模板库** | `tools/prompt_library.md` | 针对数独 UI 各元素的 AI prompt 模板 |
| **AI 输入目录** | `tools/ai_input/` | AI 生成的原始图丢这里 |
| **Unity 自动导入器** | `Assets/Editor/SpritePipelineImporter.cs` | 自动设 Texture Type = Sprite + 9-slice |
| **Unity 快捷菜单** | `Assets/Editor/SpritePipelineMenu.cs` | Unity 菜单栏里的一键操作面板 |
| **资源输出目录** | `Assets/Art/UI/*`, `Assets/Art/Effects/*`, `Assets/Art/Audio/*` | 管线处理的最终产出 |

---

## 3. 首次使用 — 10 分钟跑通全流程

### 3.1 安装 Python 依赖（只需一次）

打开终端，进入 `tools/` 目录：

```powershell
cd d:\Projects\AI\SudokuGameBox\tools
pip install -r requirements.txt
```

> `rembg` 首次运行会下载 AI 模型（约 100MB），需要等一小会。

### 3.2 生成你的第一张 AI 资源

以 **"开始游戏按钮"** 为例：

1. 打开 Midjourney / ComfyUI / Leonardo.ai
2. 从 `tools/prompt_library.md` 找到按钮的 prompt 模板
3. 粘贴生成，下载原图，放到 `tools/ai_input/`

### 3.3 运行 Python 管线

```powershell
cd d:\Projects\AI\SudokuGameBox\tools

# 交互式（推荐新手）
python sprite_pipeline.py interactive

# 或者直接命令行
python sprite_pipeline.py single ai_input/start_button.png --type button --name btn_play
```

脚本会：
1. **去背景**（rembg，3-5 秒）→ 透明 PNG
2. **裁切**透明边（自动，< 1 秒）
3. **缩放**到 256×96（按钮标准尺寸）并居中

输出到 `GameBox/Assets/Art/UI/Buttons/btn_play.png`

### 3.4 切回 Unity — 自动完成

切回 Unity 编辑器窗口，它会：
- 检测到新文件 `btn_play.png`
- 自动设 **Texture Type = Sprite (2D and UI)**
- 因为文件名以 `_btn` 结尾 → 自动设 **9-slice border**
- Console 里你会看到日志：`[SpritePipeline] 自动 9-slice: btn_play (256x96) → border=(77,29,77,29)`

**然后你就可以直接把它拖到 Button 组件的 Source Image 上了。**

### 3.5 设 Sliced 模式（让按钮可拉伸）

在场景里选中 Button：
- Image 组件 → `Image Type` 改为 **Sliced**
- 按钮就能任意拉伸而圆角不变形了 ✅

---

## 4. 日常使用工作流

| 步骤 | 你做什么 | 自动化什么 |
|---|---|---|
| **1** | AI 工具生成图片，下载到 `tools/ai_input/` | — |
| **2** | 运行 `python sprite_pipeline.py batch ./ai_input/ --type icon` | rembg 去背景、Pillow 裁切缩放、输出到 Assets/Art/ |
| **3** | 切回 Unity | AssetPostprocessor 自动设 Sprite 类型 + 9-slice |
| **4** | 拖到 UI Canvas，设 Sliced（按钮/面板） | — |

> 💡 **捷径**：在 Unity 菜单栏点 `Tools → Sprite Pipeline → Run Python Pipeline`，可以在 Unity 里一键启动，不用切窗口。

---

## 5. 命名约定（重要！）

Python 脚本输出的文件名**必须**以下列后缀结尾，Unity 端才能自动识别：

| 后缀 | 自动行为 | 典型用途 |
|---|---|---|
| `*_btn.png` | Texture Type=Sprite, 自动 9-slice(30%), Compressed | 按钮背景 |
| `*_panel.png` | Texture Type=Sprite, 自动 9-slice(30%), Compressed | 面板/弹窗背景 |
| `*_icon.png` | Texture Type=Sprite, Pivot=Center, CompressedHQ | 图标 |
| `*_particle.png` | Texture Type=Sprite, Uncompressed, Clamp | 粒子/特效贴图 |
| `*_bg.png` | Texture Type=Sprite, MaxSize=2048, Compressed | 全屏背景 |

> 示例：`btn_play.png` ✅ → 自动 9-slice | `button_play.png` ❌ → 不会自动 9-slice

---

## 6. Unity 菜单栏快捷操作

切到 Unity，顶栏 `Tools → Sprite Pipeline`：

| 菜单项 | 作用 |
|---|---|
| **Pipeline Guide Window** | 打开引导面板（步骤提示 + 快捷按钮） |
| **Open AI Input Folder** | 打开 `tools/ai_input/`（AI 原图放置处） |
| **Open Art Folder** | 打开 `Assets/Art/`（处理后的资源） |
| **Run Python Pipeline (Interactive)** | 一键启动 Python 交互式管线 |
| **Refresh All Art Assets** | 批量重新导入所有 Art 资源（改了命名规则后用） |

---

## 7. AI 工具选择速查

| 工具 | 适合 | 成本 | 上手难度 |
|---|---|---|---|
| **Midjourney** | UI 风格探索、全套 UI 主题 | $30/月 | ⭐ 低 |
| **ComfyUI + SDXL** | 本地批量生成、精细控制 | 免费（需 8GB VRAM） | ⭐⭐⭐ 高 |
| **Leonardo.ai** | 图标、道具、快速出图 | 有免费额度 | ⭐ 低 |
| **DALL-E 3** | 单张高精度、补充元素 | ChatGPT Plus 内置 | ⭐ 低 |

> **推荐路线**：先用 Midjourney 锁定风格（`--sref` 风格参考），再用 Leonardo.ai 批量补图标——性价比最高。

---

## 8. 常见问题

### Q: Python 脚本报错 "No module named 'rembg'"
```powershell
pip install -r tools/requirements.txt
```
确保已安装依赖。如果网络问题，用国内镜像：
```powershell
pip install -r tools/requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
```

### Q: rembg 去背景效果不好
- 用 Midjourney 时加 `--style raw`（减少"艺术化"倾向，边缘更清晰）
- 或者在 prompt 末尾加 `transparent background`
- 如果某张图效果不行，用 `--skip-bg` 参数跳过，手动用 PS / remove.bg 处理

### Q: 9-slice 效果不对
- 按钮/面板的图案必须是**均匀的**（纯色/渐变/简单纹理），复杂图案 9-slice 拉伸会断裂
- 在 Unity 里选中 Sprite → Sprite Editor → 手动微调 Border（绿线）

### Q: 想换资源类型或调整尺寸
- 编辑 `tools/sprite_pipeline.py` 开头的 `TYPE_SIZE_MAP` 字典即可
- 改完重新运行 `python sprite_pipeline.py batch ...`

### Q: Unity 没有自动设 Sprite 类型
- 确认文件在 `Assets/Art/` 目录下
- 确认命名后缀正确（`_btn`、`_panel` 等）
- 菜单栏 `Tools → Sprite Pipeline → Refresh All Art Assets` 手动刷新
- 如果还不行，右键 PNG → Reimport

---

## 9. 与免费素材的关系

本管线**不替代**免费素材，而是**互补**：

| 场景 | 用免费素材 | 用 AIGC 管线 |
|---|---|---|
| 快速出 MVP（功能验证） | ✅ Kenney UI Pack 直接拖 | ❌ 没必要 |
| 需要独特风格（品牌感） | ❌ 免费素材风格有限 | ✅ AI 生成 + 管线 |
| 需要大量图标 | ✅ Kenney Game Icons 够用 | ⚠️ 量大时 AI 更快 |
| 粒子贴图 | ✅ Kenney Particle Pack | ⚠️ 补充特定效果 |
| 背景音乐 | ✅ Kenney Music / Incompetech | ⚠️ 许可证风险 |

> **最佳实践**：先用 Kenney 跑通 MVP，AIGC 管线用于后续"换皮"打造独特品牌风格。免费素材负责"能用"，AI 负责"好看"。

---

## 10. 下一步

- [ ] 首次安装 Python 依赖
- [ ] 跑通一张按钮的完整管线（试一次就懂了）
- [ ] 生成全套 UI 主题（按钮 + 面板 + 图标，约 30 张）
- [ ] 在 Unity 里搭建主菜单 UI 验证效果
- [ ] 将满意 prompt 补充到 `tools/prompt_library.md`