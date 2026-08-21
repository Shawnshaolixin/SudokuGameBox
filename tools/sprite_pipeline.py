"""
sprite_pipeline.py — AIGC → Unity Sprite 半自动后处理管线

用法：
    # 方式1：处理单张图
    python sprite_pipeline.py single input.png --type button --name btn_play

    # 方式2：批量处理一个文件夹
    python sprite_pipeline.py batch ./ai_output/ --type icon

    # 方式3：交互式（引导你一步步操作）
    python sprite_pipeline.py interactive

工作流：
    AI 生成的原始图片 → [rembg 去背景] → [Pillow 裁切/缩放] → Unity Art 目录
"""

import argparse
import os
import sys
from pathlib import Path

# ============================================================
# 路径配置 — 按你的项目结构修改这里
# ============================================================
PROJECT_ROOT = Path(__file__).resolve().parent.parent  # SudokuGameBox/
UNITY_ART_DIR = PROJECT_ROOT / "GameBox" / "Assets" / "Art"

# 资源类型 → Unity 子目录映射
TYPE_DIR_MAP = {
    "button":     UNITY_ART_DIR / "UI" / "Buttons",
    "panel":      UNITY_ART_DIR / "UI" / "Panels",
    "icon":       UNITY_ART_DIR / "UI" / "Icons",
    "particle":   UNITY_ART_DIR / "Effects" / "Particles",
    "bg":         UNITY_ART_DIR / "UI" / "Panels",        # 背景归入 Panels
}

# 每种类型的默认输出尺寸（宽x高，像素；0 表示保持比例不强制缩放）
TYPE_SIZE_MAP = {
    "button":    (256, 96),
    "panel":     (512, 512),
    "icon":      (128, 128),
    "particle":  (256, 256),
    "bg":        (1080, 1920),
}

# 命名约定：文件名后缀 → 自动启用 Sprite 导入特性
# 这些后缀会被 Unity Editor 脚本读取（见 SpritePipelineImporter.cs）
NAMING_CONVENTIONS = {
    "_btn":     "按钮 → 自动设 9-slice",
    "_panel":   "面板 → 自动设 9-slice",
    "_icon":    "图标 → 单张 Sprite, Pivot 居中",
    "_particle": "粒子贴图 → 单张 Sprite, 无压缩",
    "_bg":      "背景 → 单张 Sprite, 大尺寸",
}


# ============================================================
# 核心处理函数
# ============================================================

def remove_background(input_path: Path, output_path: Path):
    """用 rembg 去除背景，输出透明 PNG"""
    from rembg import remove

    with open(input_path, "rb") as f_in:
        input_bytes = f_in.read()

    output_bytes = remove(input_bytes)

    with open(output_path, "wb") as f_out:
        f_out.write(output_bytes)

    print(f"  ✅ 去背景完成: {output_path.name}")


def crop_to_content(image_path: Path, padding: int = 4):
    """裁切到内容边界（去背景后裁掉多余的透明边）"""
    from PIL import Image

    img = Image.open(image_path)
    if img.mode != "RGBA":
        img = img.convert("RGBA")

    # 获取 alpha 通道的非零区域
    alpha = img.split()[-1]
    bbox = alpha.getbbox()

    if bbox:
        left = max(bbox[0] - padding, 0)
        top = max(bbox[1] - padding, 0)
        right = min(bbox[2] + padding, img.width)
        bottom = min(bbox[3] + padding, img.height)
        img = img.crop((left, top, right, bottom))
    else:
        print(f"  ⚠️ 未检测到内容边界，跳过裁切")

    img.save(image_path)
    print(f"  ✅ 裁切完成: {img.width}x{img.height}")


def resize_to_target(image_path: Path, target_size: tuple):
    """缩放到目标尺寸（保持比例，用透明填充补齐）"""
    from PIL import Image

    target_w, target_h = target_size
    if target_w == 0 and target_h == 0:
        return

    img = Image.open(image_path)
    if img.mode != "RGBA":
        img = img.convert("RGBA")

    original_w, original_h = img.size

    # 等比例缩放
    scale = min(target_w / original_w, target_h / original_h)
    new_w = int(original_w * scale)
    new_h = int(original_h * scale)
    img = img.resize((new_w, new_h), Image.LANCZOS)

    # 创建目标尺寸的画布，居中放置
    canvas = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    offset_x = (target_w - new_w) // 2
    offset_y = (target_h - new_h) // 2
    canvas.paste(img, (offset_x, offset_y))

    canvas.save(image_path)
    print(f"  ✅ 缩放完成: {original_w}x{original_h} → {target_w}x{target_h}")


def process_single(
    input_path: str,
    asset_type: str,
    output_name: str,
    skip_bg_removal: bool = False,
    skip_crop: bool = False,
    skip_resize: bool = False,
):
    """
    处理单张图片的完整管线

    参数：
        input_path:   AI 生成的原始图片路径
        asset_type:   资源类型 (button/panel/icon/particle/bg)
        output_name:  输出文件名（不含扩展名，会自动加 .png）
        skip_*:       跳过某些步骤（比如已经是透明 PNG 就不需要去背景）
    """
    input_path = Path(input_path)
    if not input_path.exists():
        print(f"❌ 文件不存在: {input_path}")
        return

    # 确认输出目录
    out_dir = TYPE_DIR_MAP.get(asset_type)
    if out_dir is None:
        print(f"❌ 未知类型 '{asset_type}'，可选: {list(TYPE_DIR_MAP.keys())}")
        return
    out_dir.mkdir(parents=True, exist_ok=True)

    output_path = out_dir / f"{output_name}.png"

    # 步骤 1: 去背景
    if not skip_bg_removal:
        print(f"\n🔹 步骤 1/3: 去背景 [{output_name}]")
        remove_background(input_path, output_path)
    else:
        print(f"\n🔹 步骤 1/3: 跳过（已透明）→ 直接复制 [{output_name}]")
        from PIL import Image
        img = Image.open(input_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        img.save(output_path)

    # 步骤 2: 裁切
    if not skip_crop:
        print(f"🔹 步骤 2/3: 裁切透明边 [{output_name}]")
        crop_to_content(output_path)
    else:
        print(f"🔹 步骤 2/3: 跳过裁切 [{output_name}]")

    # 步骤 3: 缩放
    if not skip_resize:
        target_size = TYPE_SIZE_MAP.get(asset_type, (256, 256))
        print(f"🔹 步骤 3/3: 缩放到目标尺寸 [{output_name}]")
        resize_to_target(output_path, target_size)
    else:
        print(f"🔹 步骤 3/3: 跳过缩放 [{output_name}]")

    print(f"\n🎉 完成! 输出: {output_path}")
    print(f"   之后切换到 Unity，它会自动检测新文件并配置 Sprite 导入设置。")


def process_batch(input_dir: str, asset_type: str):
    """批量处理一个文件夹里的所有图片"""
    input_dir = Path(input_dir)
    if not input_dir.exists():
        print(f"❌ 目录不存在: {input_dir}")
        return

    image_exts = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
    files = [f for f in input_dir.iterdir() if f.suffix.lower() in image_exts]

    if not files:
        print(f"❌ 在 {input_dir} 中没有找到图片文件")
        return

    print(f"\n📦 找到 {len(files)} 张图片，类型={asset_type}")
    print(f"   输出目录: {TYPE_DIR_MAP[asset_type]}")
    print("=" * 50)

    for i, file in enumerate(files, 1):
        # 输出名用原文件名（去掉扩展名）+ 类型后缀
        stem = file.stem
        output_name = f"{stem}_{asset_type}"
        print(f"\n[{i}/{len(files)}] 处理: {file.name} → {output_name}.png")
        process_single(
            str(file),
            asset_type,
            output_name,
            skip_bg_removal=False,
        )

    print(f"\n{'=' * 50}")
    print(f"✅ 批量处理完成! 共 {len(files)} 张 → {TYPE_DIR_MAP[asset_type]}")


def interactive():
    """交互式引导模式"""
    print("=" * 50)
    print("🎨 AIGC → Unity Sprite 管线 — 交互式引导")
    print("=" * 50)
    print()

    # Step 1: 选择模式
    print("你要处理单张还是批量？")
    print("  [1] 单张图片")
    print("  [2] 批量处理整个文件夹")
    choice = input("输入 1 或 2: ").strip()

    # Step 2: 选择资源类型
    print("\n选择资源类型:")
    for key, desc in {
        "button": "按钮（自动 9-slice）",
        "panel": "面板背景（自动 9-slice）",
        "icon": "图标",
        "particle": "粒子/特效贴图",
        "bg": "全屏背景",
    }.items():
        print(f"  [{key}] {desc}")
    asset_type = input("输入类型: ").strip()
    if asset_type not in TYPE_DIR_MAP:
        print(f"❌ 无效类型: {asset_type}")
        return

    if choice == "1":
        input_path = input("输入图片路径（拖入文件即可）: ").strip().strip('"')
        output_name = input("输出文件名（不含 .png）: ").strip()
        process_single(input_path, asset_type, output_name)
    elif choice == "2":
        input_dir = input("输入文件夹路径（拖入文件夹即可）: ").strip().strip('"')
        process_batch(input_dir, asset_type)
    else:
        print("❌ 无效选择")


# ============================================================
# CLI 入口
# ============================================================

def main():
    parser = argparse.ArgumentParser(
        description="AIGC → Unity Sprite 半自动后处理管线",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
示例:
  python sprite_pipeline.py single ai_btn.png --type button --name btn_play
  python sprite_pipeline.py batch ./midjourney_output/ --type icon
  python sprite_pipeline.py interactive
  python sprite_pipeline.py single particle.png --type particle --name star_glow --skip-bg
        """,
    )

    subparsers = parser.add_subparsers(dest="command", help="子命令")

    # --- single ---
    single_parser = subparsers.add_parser("single", help="处理单张图片")
    single_parser.add_argument("input", help="输入图片路径")
    single_parser.add_argument("--type", required=True, choices=TYPE_DIR_MAP.keys(), help="资源类型")
    single_parser.add_argument("--name", required=True, help="输出文件名（不含 .png）")
    single_parser.add_argument("--skip-bg", action="store_true", help="已是透明 PNG，跳过去背景")
    single_parser.add_argument("--skip-crop", action="store_true", help="跳过裁切")
    single_parser.add_argument("--skip-resize", action="store_true", help="跳过缩放")

    # --- batch ---
    batch_parser = subparsers.add_parser("batch", help="批量处理文件夹")
    batch_parser.add_argument("input_dir", help="输入文件夹路径")
    batch_parser.add_argument("--type", required=True, choices=TYPE_DIR_MAP.keys(), help="资源类型")

    # --- interactive ---
    subparsers.add_parser("interactive", help="交互式引导模式")

    args = parser.parse_args()

    if args.command == "single":
        process_single(
            args.input,
            args.type,
            args.name,
            skip_bg_removal=args.skip_bg,
            skip_crop=args.skip_crop,
            skip_resize=args.skip_resize,
        )
    elif args.command == "batch":
        process_batch(args.input_dir, args.type)
    elif args.command == "interactive":
        interactive()
    else:
        parser.print_help()


if __name__ == "__main__":
    main()