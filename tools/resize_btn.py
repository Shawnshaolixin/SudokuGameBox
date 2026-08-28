"""Crop to non-transparent bounding box, then resize to target dimensions with LANCZOS."""
import sys
from PIL import Image

src = sys.argv[1]
dst = sys.argv[2]
w, h = int(sys.argv[3]), int(sys.argv[4])

img = Image.open(src)
if img.mode != "RGBA":
    img = img.convert("RGBA")

# Auto-crop to non-transparent region
bbox = img.getbbox()
if bbox:
    img = img.crop(bbox)

print(f"Cropped to: {img.width}x{img.height}, aspect={img.width/img.height:.2f}, target aspect={w/h:.2f}")

img = img.resize((w, h), Image.LANCZOS)
img.save(dst, "PNG")
print(f"Done: {dst} ({w}x{h})")
