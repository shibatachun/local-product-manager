from pathlib import Path
from PIL import Image, ImageEnhance, ImageDraw, ImageFont
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "LocalProductManager.Api" / "Data" / "product-images" / "INV-75"
WALMART = SRC / "walmart-draft-20260908-111359"
EBAY = SRC / "ebay-draft-20260908-111359"

ORDERED = SRC / "75-20260908-150317399-786378e0ea3c4a389b8af6dc6caca7ef.jpg"
SCATTERED = SRC / "75-20260908-150327829-090ced28fec549ec93b6c798264bea01.jpg"
HAND = SRC / "75-20260908-150339226-00c6cc0a06954c64ba999800a2c7e24d.jpg"
GENERATED_SCATTER = Path(r"C:\Users\jjgqa\.codex\generated_images\01a01a2b-1ac5-7fd0-b715-b3761e812f57\exec-8be93239-bdae-46ef-94a7-28520d17b6fb.png")
GENERATED_HAND = Path(r"C:\Users\jjgqa\.codex\generated_images\01a01a2b-1ac5-7fd0-b715-b3761e812f57\exec-9f8a8478-9b48-4f65-806e-675a0bfb2b6d.png")

def font(size: int):
    for path in [r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf"]:
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()

def clean_white(path: Path, size: int, crop_box=None) -> Image.Image:
    image = Image.open(path).convert("RGB")
    if crop_box:
        image = image.crop(crop_box)
    image = ImageEnhance.Contrast(image).enhance(1.08)
    arr = np.asarray(image).copy()
    hi = arr.max(2)
    lo = arr.min(2)
    sat = hi - lo
    # The photographed backdrop is neutral and bright; preserve colored/glossy product pixels.
    mask = (hi > 150) & (sat < 28)
    arr[mask] = 255
    image = Image.fromarray(arr)
    image.thumbnail((int(size * .88), int(size * .88)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (size, size), "white")
    canvas.paste(image, ((size-image.width)//2, (size-image.height)//2))
    return canvas

def full_frame_scale(path: Path, size: int) -> Image.Image:
    image = Image.open(path).convert("RGB")
    # Preserve the complete hand and every item; never crop a scale-reference photo.
    image = ImageEnhance.Brightness(image).enhance(1.10)
    image = ImageEnhance.Contrast(image).enhance(1.04)
    image.thumbnail((int(size * .84), int(size * .84)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (size, size), "white")
    canvas.paste(image, ((size-image.width)//2, (size-image.height)//2))
    return canvas

WALMART.mkdir(parents=True, exist_ok=True)
EBAY.mkdir(parents=True, exist_ok=True)

# The generated scattered edit was visually checked and contains the same 50-piece assortment.
Image.open(GENERATED_SCATTER).convert("RGB").save(WALMART / "02-real-scattered.jpg", quality=95)
full_frame_scale(HAND, 2200).save(WALMART / "03-real-scale.jpg", quality=95)

# One verified 20 mm cabochon, cropped from the ordered real photo.
source = Image.open(ORDERED).convert("RGB")
eye = source.crop((120, 600, 240, 720)).resize((760, 760), Image.Resampling.LANCZOS)
mask = Image.new("L", eye.size, 0)
ImageDraw.Draw(mask).ellipse((45, 45, 715, 715), fill=255)
info = Image.new("RGB", (2200, 2200), "white")
info.paste(eye, ((2200-eye.width)//2, 400), mask)
draw = ImageDraw.Draw(info)
y = 1325
draw.line((610, y, 1590, y), fill=(18, 103, 137), width=14)
draw.line((610, y-45, 610, y+45), fill=(18, 103, 137), width=14)
draw.line((1590, y-45, 1590, y+45), fill=(18, 103, 137), width=14)
label = "20 mm / 0.79 in"
box = draw.textbbox((0,0), label, font=font(110))
draw.text(((2200-(box[2]-box[0]))/2, 1570), label, fill=(14, 42, 55), font=font(110))
info.save(WALMART / "04-size-infographic.png")

# eBay remains naturally scattered; no alignment or size infographic. The main
# is the verified 50-piece background-replacement edit, not a lifestyle scene.
Image.open(GENERATED_SCATTER).convert("RGB").resize((1600, 1600), Image.Resampling.LANCZOS).save(EBAY / "01-main-real.jpg", quality=95)
full_frame_scale(HAND, 1600).save(EBAY / "02-real-hand-scale.jpg", quality=95)

def padded_generated_hand(size: int) -> Image.Image:
    image = Image.open(GENERATED_HAND).convert("RGB")
    image.thumbnail((int(size * .86), int(size * .86)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (size, size), "white")
    canvas.paste(image, ((size-image.width)//2, (size-image.height)//2))
    return canvas

padded_generated_hand(2200).save(WALMART / "03-real-scale.png")
padded_generated_hand(2200).save(EBAY / "02-real-hand-scale.png")
