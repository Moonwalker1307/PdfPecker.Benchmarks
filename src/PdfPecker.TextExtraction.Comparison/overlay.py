# Draws the reading order of each engine over a page: every line (PdfPecker, PdfPig layout) or drawn run of letters
# (PdfPig content order) in the order the engine writes it, from blue to red, joined by a path through their centres.
# Needs Pillow and Poppler's pdftoppm.
#
#   python3 overlay.py <pdf> <boxes folder> <page> <out.png> [engine ...]
import json, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFont

pdf, boxes_dir, page, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
engines = sys.argv[5:] or ["pdfpecker", "pdfpig-content", "pdfpig-layout"]
titles = {"pdfpecker": "PdfPecker (reading order)", "pdfpecker-ttb": "PdfPecker (top to bottom)",
          "pdfpig-text": "PdfPig page.Text", "pdfpig-content": "PdfPig ContentOrderTextExtractor",
          "pdfpig-layout": "PdfPig Docstrum + reading order"}
dpi = int(os.environ.get("OVERLAY_DPI", "60"))
stem = os.path.splitext(os.path.basename(pdf))[0]

with tempfile.TemporaryDirectory() as tmp:
    subprocess.run(["pdftoppm", "-r", str(dpi), "-f", str(page), "-l", str(page), "-png", "-singlefile", pdf, os.path.join(tmp, "p")], check=True)
    base = Image.open(os.path.join(tmp, "p.png")).convert("RGB")
scale = dpi / 72
try:
    font = ImageFont.truetype("DejaVuSans-Bold.ttf", 11)
    title_font = ImageFont.truetype("DejaVuSans-Bold.ttf", 15)
except OSError:
    font = title_font = ImageFont.load_default()


def displayed(b, engine):
    return b["X"], b["Y"], b["W"], b["H"]


# Every panel is cropped to the part of the page that holds text, so short pages are not mostly white.
all_boxes = {e: [b for b in json.load(open(os.path.join(boxes_dir, f"{stem}.{e}.boxes.json"))) if b["Page"] == page] for e in engines}
flat = [displayed(b, "") for bs in all_boxes.values() for b in bs]
if flat:
    crop = (max(0, int(min(x for x, _, _, _ in flat) * scale) - 24), max(0, int(min(y for _, y, _, _ in flat) * scale) - 24),
            min(base.width, int(max(x + w for x, _, w, _ in flat) * scale) + 24), min(base.height, int(max(y + h for _, y, _, h in flat) * scale) + 24))
else:
    crop = (0, 0, base.width, base.height)

panels = []
for engine in engines:
    boxes = all_boxes[engine]
    img = base.copy()
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    n = max(1, len(boxes) - 1)
    centres = []
    for i, b in enumerate(boxes):
        x, y, w, h = (v * scale for v in displayed(b, engine))
        t = i / n
        colour = (int(40 + 215 * t), int(90 * (1 - t)), int(230 * (1 - t)))
        d.rectangle([x, y, x + w, y + h], fill=colour + (60,), outline=colour + (200,))
        centres.append((x + min(w, 12) / 2 if w > 0 else x, y + h / 2, colour))
    for (x1, y1, c), (x2, y2, _) in zip(centres, centres[1:]):
        d.line([x1, y1, x2, y2], fill=c + (230,), width=2)
    step = max(1, len(centres) // 40)
    for i, (x, y, c) in enumerate(centres):
        if i % step == 0 or i == len(centres) - 1:
            d.ellipse([x - 8, y - 8, x + 8, y + 8], fill=(255, 255, 255, 230), outline=c + (255,), width=2)
            label = str(i + 1)
            tw = d.textlength(label, font=font)
            d.text((x - tw / 2, y - 7), label, fill=c + (255,), font=font)
    img = Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB").crop(crop)
    panel = Image.new("RGB", (img.width, img.height + 30), "white")
    panel.paste(img, (0, 30))
    ImageDraw.Draw(panel).text((6, 6), titles.get(engine, engine), fill="black", font=title_font)
    panels.append(panel)

gap = 14
sheet = Image.new("RGB", (sum(p.width for p in panels) + gap * (len(panels) - 1), max(p.height for p in panels)), "white")
x = 0
for p in panels:
    sheet.paste(p, (x, 0))
    x += p.width + gap
sheet.save(out)
