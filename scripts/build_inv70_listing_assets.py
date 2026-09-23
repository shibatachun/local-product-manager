from pathlib import Path
from PIL import Image, ImageEnhance, ImageDraw, ImageFont
import shutil

base = Path(r"C:\Users\jjgqa\local-product-manager\LocalProductManager.Api\Data\product-images\INV-70")
ebay = base / "ebay-draft-20260915-145000"
walmart = base / "walmart-draft-20260915-145000"
ebay.mkdir(exist_ok=True)
walmart.mkdir(exist_ok=True)

sources = {
    "main": base / "70-20260915-182748652-516ea6b19f9c4f1b8fe8fca8afbd21cd.jpg",
    "scatter": base / "70-20260915-182814562-d9f66a04b8444708a327e7fc6fc3279a.jpg",
    "hand": base / "70-20260915-182820652-0643cf9379214233a7fa9cf812f998eb.jpg",
}

def square(src, dst):
    im = Image.open(src).convert("RGB")
    w, h = im.size
    side = min(w, h)
    left = (w - side) // 2
    top = (h - side) // 2
    im = im.crop((left, top, left + side, top + side)).resize((2200, 2200), Image.Resampling.LANCZOS)
    im = ImageEnhance.Brightness(im).enhance(1.04)
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = px[x, y]
            if min(r, g, b) > 140 and max(r, g, b) - min(r, g, b) < 38:
                px[x, y] = (255, 255, 255)
    im.save(dst, "JPEG", quality=93, optimize=True)

for folder in (ebay, walmart):
    square(sources["main"], folder / "01-main-real.jpg")
    square(sources["scatter"], folder / "02-real-scattered.jpg")
    square(sources["hand"], folder / "03-real-hand-scale.jpg")

# Size infographic uses a verified 15 mm diameter and a crop from the real main photo.
im = Image.open(sources["main"]).convert("RGB")
crop = im.crop((55, 500, 255, 700)).resize((1250, 1250), Image.Resampling.LANCZOS)
canvas = Image.new("RGB", (2200, 2200), "white")
canvas.paste(crop, (475, 300))
d = ImageDraw.Draw(canvas)
font = ImageFont.truetype("arial.ttf", 135)
d.line((475, 1700, 1725, 1700), fill="#222222", width=16)
d.line((475, 1640, 475, 1760), fill="#222222", width=16)
d.line((1725, 1640, 1725, 1760), fill="#222222", width=16)
label = "15 mm / 0.59 in"
b = d.textbbox((0, 0), label, font=font)
d.text(((2200-(b[2]-b[0]))/2, 1820), label, fill="#111111", font=font)
canvas.save(walmart / "04-size-infographic.jpg", "JPEG", quality=94, optimize=True)

generated = Path(r"C:\Users\jjgqa\.codex\generated_images\01a0a541-5a94-79f1-9ae7-41019f8bd1da")
mapping = [
    (generated / "exec-f93f4dfb-72f7-4f5e-a2c1-dc5ef356cc96.png", walmart / "01-main-real.jpg"),
    (generated / "exec-956b7181-d99b-4582-83dd-531d35996497.png", ebay / "04-generated-dragon-figurines.jpg"),
    (generated / "exec-dcb56ba6-def3-4188-8e91-11992a10d99a.png", ebay / "05-generated-crafting-use.jpg"),
    (generated / "exec-4de9afb2-8b08-40f7-b568-5b3e62626d39.png", walmart / "05-generated-creature-magnets.jpg"),
    (generated / "exec-bc53f7d6-c9e5-4897-84d9-3d421dc9004e.png", walmart / "06-generated-jewelry-workspace.jpg"),
]
for src, dst in mapping:
    Image.open(src).convert("RGB").resize((2200, 2200), Image.Resampling.LANCZOS).save(dst, "JPEG", quality=93, optimize=True)

print(ebay)
print(walmart)
