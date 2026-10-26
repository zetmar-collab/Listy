"""Odtwarzalna ikona Listy; opcjonalne narzędzie deweloperskie (Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parents[1] / "src" / "Listy.App" / "Assets"
assets.mkdir(parents=True, exist_ok=True)

scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)

def box(coords):
    return tuple(round(n * scale) for n in coords)

draw.rounded_rectangle(box((12, 12, 244, 244)), radius=52 * scale, fill="#172F36")
draw.rounded_rectangle(box((57, 64, 151, 79)), radius=7.5 * scale, fill="#A8E5C6")
draw.rounded_rectangle(box((57, 101, 137, 116)), radius=7.5 * scale, fill="#A8E5C6")
draw.rounded_rectangle(box((57, 138, 114, 153)), radius=7.5 * scale, fill="#A8E5C6")
draw.rounded_rectangle(box((172, 98, 187, 183)), radius=7.5 * scale, fill="#FFFFFF")
draw.polygon([box(p) for p in [(177, 97), (208, 110), (208, 132), (180, 118)]], fill="#FFFFFF")
draw.ellipse(box((144, 166, 187, 197)), fill="#FFFFFF")

image.save(assets / "Listy.png")
image.save(assets / "Listy.ico", format="ICO", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
(assets / "Listy.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect x="12" y="12" width="232" height="232" rx="52" fill="#172F36"/>
  <g fill="#A8E5C6">
    <rect x="57" y="64" width="94" height="15" rx="7.5"/>
    <rect x="57" y="101" width="80" height="15" rx="7.5"/>
    <rect x="57" y="138" width="57" height="15" rx="7.5"/>
  </g>
  <g fill="#FFF">
    <rect x="172" y="98" width="15" height="85" rx="7.5"/>
    <path d="M177 97 208 110 208 132 180 118Z"/>
    <ellipse cx="165.5" cy="181.5" rx="21.5" ry="15.5"/>
  </g>
</svg>
''', encoding="utf-8")
print(assets)
