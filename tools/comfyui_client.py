"""
comfyui_client.py — ComfyUI 本地生成客户端(接入 AIGC 精灵管线)

依赖: 无(纯标准库 urllib)。需要 ComfyUI 已启动:
    d:/Projects/AI/ComfyUI/run_nvidia_gpu.bat

用法:
    # 连通性自检
    python comfyui_client.py test

    # 文生图:生成按钮素材(命名 _btn 自动触发 Unity 9-slice 导入)
    python comfyui_client.py txt2img --prompt "..." --filename btn_play

    # 生成背景,指定尺寸
    python comfyui_client.py txt2img --prompt "..." --filename bg_main --width 1536 --height 1024

    # 随机种子 / 固定种子 / 自定义采样步数
    python comfyui_client.py txt2img --prompt "..." --seed 42 --steps 30

工作流:
    ComfyUI 出图 → tools/ai_output/ → sprite_pipeline.py batch 去背景/裁切/缩放 → Unity Art 目录
"""

import argparse
import json
import sys
import time
import urllib.request
import urllib.error
import uuid
from pathlib import Path

# Windows 控制台默认 GBK,emoji 打印会崩;强制 UTF-8 输出
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_SERVER = "http://127.0.0.1:8188"
DEFAULT_CKPT = "sd_xl_base_1.0.safetensors"

# 输出目录:与 sprite_pipeline.py 的 batch 输入约定一致
OUTPUT_DIR = Path(__file__).resolve().parent / "ai_output"

# 通用负面提示词(SDXL 避免的常见瑕疵)
DEFAULT_NEGATIVE = (
    "lowres, bad anatomy, bad hands, text, watermark, signature, blurry, "
    "jpeg artifacts, cropped, out of frame, duplicate, error, deformed"
)


# ============================================================
# ComfyUI API 基础封装
# ============================================================

def api_get(server: str, path: str) -> dict:
    with urllib.request.urlopen(f"{server}{path}", timeout=30) as resp:
        return json.loads(resp.read())


def api_post(server: str, path: str, body: dict) -> dict:
    req = urllib.request.Request(
        f"{server}{path}",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read())


def check_server(server: str) -> dict:
    """自检:服务器在线 + 模型就位 + GPU 可用"""
    stats = api_get(server, "/system_stats")
    ckpts = api_get(server, "/object_info/CheckpointLoaderSimple")
    names = list(ckpts["CheckpointLoaderSimple"]["input"]["required"]["ckpt_name"][0])
    if DEFAULT_CKPT not in names:
        raise RuntimeError(
            f"模型 {DEFAULT_CKPT} 未找到,可用: {names[:5]}...\n"
            f"请确认已下载到 d:/Projects/AI/ComfyUI/models/checkpoints/"
        )
    devices = stats.get("devices", [])
    gpu_ok = any(d.get("type") == "cuda" for d in devices)
    return {"gpu": gpu_ok, "ckpts": names, "stats": stats}


# ============================================================
# SDXL 文生图工作流(API JSON 格式)
# ============================================================

def build_txt2img_workflow(prompt: str, negative: str, width: int, height: int,
                           steps: int, cfg: float, seed: int, prefix: str) -> dict:
    """SDXL 标准工作流: Checkpoint → 双 CLIP 条件 → KSampler → VAE → SaveImage"""
    return {
        "3": {"class_type": "KSampler", "inputs": {
            "seed": seed, "steps": steps, "cfg": cfg,
            "sampler_name": "euler", "scheduler": "normal", "denoise": 1.0,
            "model": ["4", 0], "positive": ["6", 0], "negative": ["7", 0],
            "latent_image": ["5", 0],
        }},
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": DEFAULT_CKPT}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {
            "width": width, "height": height, "batch_size": 1,
        }},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["4", 1]}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {
            "filename_prefix": prefix, "images": ["8", 0],
        }},
    }


def submit_and_wait(server: str, workflow: dict, timeout: int = 600) -> dict:
    """提交工作流并轮询 /history 直到出图,返回图片清单"""
    client_id = str(uuid.uuid4())
    resp = api_post(server, "/prompt", {"prompt": workflow, "client_id": client_id})
    prompt_id = resp.get("prompt_id")
    if not prompt_id:
        raise RuntimeError(f"提交失败: {resp}")

    deadline = time.time() + timeout
    while time.time() < deadline:
        hist = api_get(server, f"/history/{prompt_id}")
        if prompt_id in hist:
            entry = hist[prompt_id]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                raise RuntimeError(f"生成失败: {json.dumps(status, ensure_ascii=False)}")
            outputs = entry.get("outputs", {})
            images = []
            for node in outputs.values():
                images.extend(node.get("images", []))
            if images:
                return {"prompt_id": prompt_id, "images": images}
        time.sleep(2)
    raise TimeoutError(f"等待 {timeout}s 未出图,请检查 ComfyUI 控制台日志")


def download_image(server: str, image: dict, out_path: Path):
    """从 ComfyUI /view 下载生成图片"""
    url = (f"{server}/view?filename={image['filename']}"
           f"&subfolder={image.get('subfolder', '')}&type={image.get('type', 'output')}")
    with urllib.request.urlopen(url, timeout=120) as resp, open(out_path, "wb") as f:
        f.write(resp.read())
    print(f"  ✅ 已保存: {out_path} ({out_path.stat().st_size // 1024} KB)")


# ============================================================
# CLI
# ============================================================

def main():
    ap = argparse.ArgumentParser(description="ComfyUI 本地生成客户端")
    ap.add_argument("--server", default=DEFAULT_SERVER, help=f"ComfyUI 地址(默认 {DEFAULT_SERVER})")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_test = sub.add_parser("test", help="连通性自检")
    p_test.add_argument("--verbose", action="store_true")

    p_gen = sub.add_parser("txt2img", help="文生图(UI 素材)")
    p_gen.add_argument("--prompt", required=True, help="正向提示词")
    p_gen.add_argument("--negative", default=DEFAULT_NEGATIVE, help="负面提示词")
    p_gen.add_argument("--filename", default="output", help="输出文件名(不带扩展名,建议带 _btn/_panel/_icon/_bg 后缀)")
    p_gen.add_argument("--width", type=int, default=1024, help="宽度(默认 1024, SDXL 训练尺寸)")
    p_gen.add_argument("--height", type=int, default=1024, help="高度")
    p_gen.add_argument("--steps", type=int, default=25)
    p_gen.add_argument("--cfg", type=float, default=7.0)
    p_gen.add_argument("--seed", type=int, default=-1, help="-1 = 随机")
    p_gen.add_argument("--timeout", type=int, default=600)

    args = ap.parse_args()

    try:
        if args.cmd == "test":
            info = check_server(args.server)
            print(f"✅ ComfyUI 在线: {args.server}")
            print(f"✅ GPU 可用 (CUDA)")
            print(f"✅ 模型就位: {info['ckpts'][:5]}...")
            if args.verbose:
                print(json.dumps(info["stats"], indent=2, ensure_ascii=False))
            return

        if args.cmd == "txt2img":
            check_server(args.server)
            seed = args.seed if args.seed >= 0 else int(time.time() * 1000) % 2**31
            OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
            prefix = f"sdxl_{args.filename}_{seed % 100000}"
            wf = build_txt2img_workflow(
                args.prompt, args.negative, args.width, args.height,
                args.steps, args.cfg, seed, prefix,
            )
            print(f"🎨 提交任务 seed={seed} {args.width}x{args.height} steps={args.steps} ...")
            result = submit_and_wait(args.server, wf, timeout=args.timeout)
            images = result["images"]
            if not images:
                raise RuntimeError("任务完成但未找到输出图片")
            out = OUTPUT_DIR / f"{args.filename}.png"
            download_image(args.server, images[0], out)
            print(f"🎉 完成: {out}")
            print(f"💡 后续处理: python tools/sprite_pipeline.py batch {OUTPUT_DIR} --type icon")

    except urllib.error.URLError as e:
        print(f"❌ 无法连接 ComfyUI ({e.reason})")
        print(f"   请先启动: d:/Projects/AI/ComfyUI/run_nvidia_gpu.bat")
        sys.exit(1)
    except (RuntimeError, TimeoutError) as e:
        print(f"❌ {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
