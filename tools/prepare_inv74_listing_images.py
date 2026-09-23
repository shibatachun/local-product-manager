from pathlib import Path
from PIL import Image, ImageEnhance, ImageDraw, ImageFont
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "LocalProductManager.Api" / "Data" / "product-images" / "INV-74"
STAMP = "20260909-140000"
EBAY = SRC / f"ebay-draft-{STAMP}"
WALMART = SRC / f"walmart-draft-{STAMP}"
MAIN = SRC / "74-20260909-180204364-caf64a9728b24841bc44ddcb28c51edf.jpg"
HAND = SRC / "74-20260909-180223563-695eb70b95b1453b8ab90b84e495f8d7.jpg"
GEN1 = Path(r"C:\Users\jjgqa\.codex\generated_images\019fae62-fdb3-7283-81ef-02be5286483b\exec-693cfdc2-3bc5-4618-9aa6-feabf5aec1dc.png")
GEN2 = Path(r"C:\Users\jjgqa\.codex\generated_images\019fae62-fdb3-7283-81ef-02be5286483b\exec-1f17ea84-3342-49de-8e7e-393d43b790aa.png")
HAND_EDIT = Path(r"C:\Users\jjgqa\.codex\generated_images\019fae62-fdb3-7283-81ef-02be5286483b\exec-28db9ad2-6a06-429f-a5dc-91a51490ea5e.png")


def font(size: int):
    for candidate in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf"):
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def clean_and_square(path: Path, size: int, preserve_hand: bool = False) -> Image.Image:
    image = Image.open(path).convert("RGB")
    image = ImageEnhance.Contrast(image).enhance(1.06)
    arr = np.asarray(image).copy()
    hi = arr.max(axis=2)
    lo = arr.min(axis=2)
    saturation = hi - lo
    # The source is photographed on a neutral light sweep. Whiten only the
    # bright, low-saturation background so colored glass and skin stay intact.
    threshold = 145 if preserve_hand else 135
    background = (hi >= threshold) & (saturation <= 18)
    arr[background] = 255
    image = Image.fromarray(arr)
    image.thumbnail((int(size * 0.92), int(size * 0.92)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (size, size), "white")
    canvas.paste(image, ((size - image.width) // 2, (size - image.height) // 2))
    return canvas


def generated_square(path: Path, size: int) -> Image.Image:
    image = Image.open(path).convert("RGB")
    return image.resize((size, size), Image.Resampling.LANCZOS)


def square_crop(path: Path, size: int) -> Image.Image:
    image = Image.open(path).convert("RGB")
    side = min(image.size)
    # Keep the hand and the dense product group at the top of the portrait edit.
    # Cropping instead of padding avoids a visible inner white rectangle/seam.
    left = (image.width - side) // 2
    top = 0 if image.height > image.width else (image.height - side) // 2
    return image.crop((left, top, left + side, top + side)).resize(
        (size, size), Image.Resampling.LANCZOS)


EBAY.mkdir(parents=True, exist_ok=True)
WALMART.mkdir(parents=True, exist_ok=True)

clean_and_square(MAIN, 1600).save(EBAY / "01-main-real.jpg", quality=95)
square_crop(HAND_EDIT, 1600).save(EBAY / "02-real-hand-scale.jpg", quality=95)
generated_square(GEN1, 1600).save(EBAY / "03-generated-reference-craft-use.jpg", quality=95)
generated_square(GEN2, 1600).save(EBAY / "04-generated-reference-scrapbook.jpg", quality=95)

clean_and_square(MAIN, 2200).save(WALMART / "01-main-real.jpg", quality=95)
square_crop(HAND_EDIT, 2200).save(WALMART / "02-real-hand-scale.jpg", quality=95)

# Crop one verified 10 mm piece from the real main photo for the measurement graphic.
source = Image.open(MAIN).convert("RGB")
piece = source.crop((500, 205, 625, 330)).resize((760, 760), Image.Resampling.LANCZOS)
mask = Image.new("L", piece.size, 0)
ImageDraw.Draw(mask).ellipse((50, 50, 710, 710), fill=255)
graphic = Image.new("RGB", (2200, 2200), "white")
graphic.paste(piece, (720, 360), mask)
draw = ImageDraw.Draw(graphic)
y = 1300
draw.line((650, y, 1550, y), fill=(18, 103, 137), width=14)
draw.line((650, y - 45, 650, y + 45), fill=(18, 103, 137), width=14)
draw.line((1550, y - 45, 1550, y + 45), fill=(18, 103, 137), width=14)
label = "10 mm / 0.39 in"
label_font = font(112)
box = draw.textbbox((0, 0), label, font=label_font)
draw.text(((2200 - (box[2] - box[0])) / 2, 1550), label, fill=(14, 42, 55), font=label_font)
graphic.save(WALMART / "03-size-infographic.png")

generated_square(GEN1, 2200).save(WALMART / "04-generated-reference-craft-use.jpg", quality=95)
generated_square(GEN2, 2200).save(WALMART / "05-generated-reference-scrapbook.jpg", quality=95)

print(EBAY)
print(WALMART)
