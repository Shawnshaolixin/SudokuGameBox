"""Remove near-white background and make it transparent, then resize."""
import sys
from PIL import Image

src = sys.argv[1]
dst = sys.argv[2]
w, h = int(sys.argv[3]), int(sys.argv[4])

img = Image.open(src).convert("RGBA")
pixels = img.load()

# Treat near-white (R>235, G>235, B>235) as background -> transparent
threshold = 235
for y in range(img.height):
    for x in range(img.width):
        r, g, b, a = pixels[x, y]
        if r > threshold and g > threshold and b > threshold:
            pixels[x, y] = (r, g, b, 0)

img = img.resize((w, h), Image.LANCZOS)
img.save(dst, "PNG")
print(f"Done: {dst} ({w}x{h})")
