import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

out = Path(sys.argv[1])
size = 1024
mask = Image.new("L", (size, size))
ImageDraw.Draw(mask).rounded_rectangle((56, 56, 968, 968), radius=221, fill=255)
tile = Image.new("RGBA", (size, size))
draw = ImageDraw.Draw(tile)
for y in range(size):
    t = y / (size - 1)
    color = tuple(round(a + (b - a) * t) for a, b in zip((63, 180, 255), (23, 62, 196)))
    draw.line((0, y, size, y), fill=color)
tile.putalpha(mask)
draw = ImageDraw.Draw(tile)
draw.rounded_rectangle((63, 63, 961, 961), radius=214, outline=(125, 197, 255), width=7)
bolt = [(558, 173), (305, 563), (488, 563), (444, 847), (726, 432), (542, 432)]
shadow = Image.new("RGBA", tile.size)
ImageDraw.Draw(shadow).polygon([(x, y + 25) for x, y in bolt], fill=(12, 38, 131, 125))
tile.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(22)))
draw = ImageDraw.Draw(tile)
draw.polygon(bolt, fill=(255, 255, 255))
tile = tile.resize((256, 256), Image.Resampling.LANCZOS)
tile.save(out / "KuaiApps.png")
tile.save(out / "KuaiApps.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])