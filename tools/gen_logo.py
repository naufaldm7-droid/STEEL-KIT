"""Logo Steel Kit: kepala baut segi enam + penampang WF, gradasi sian -> nila dengan kilap kaca.

Geometri sama dengan Logo.xaml (satuan 0..100). Menghasilkan:
    assets/logo.ico   (16..256, untuk installer & jendela)
    assets/logo.png   (512)
    assets/logo_wordmark.png (logo + tulisan, untuk README / situs)

    python tools/gen_logo.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "assets"))
C1 = (56, 189, 248)    # sian  #38BDF8
C2 = (99, 102, 241)    # nila  #6366F1
EDGE = (15, 23, 42)    # #0F172A

HEX = [(50 + 50 * math.cos(math.radians(a)), 50 + 50 * math.sin(math.radians(a))) for a in range(0, 360, 60)]
# penampang WF (putih) di tengah
BEAM = [(28, 25), (72, 25), (72, 34), (54.5, 34), (54.5, 66), (72, 66), (72, 75), (28, 75), (28, 66), (45.5, 66), (45.5, 34), (28, 34)]


def render(size):
    ss = 4  # supersampling
    n = size * ss
    k = n / 100.0
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    # gradasi diagonal
    grad = Image.new("RGBA", (n, n))
    px = grad.load()
    for y in range(n):
        for x in range(n):
            t = min(1.0, max(0.0, (x + y) / (2.0 * n)))
            px[x, y] = tuple(int(C1[i] + (C2[i] - C1[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (n, n), 0)
    hexpts = [(x * k, (y * 0.94 + 3) * k) for x, y in HEX]
    ImageDraw.Draw(mask).polygon(hexpts, fill=255)
    img.paste(grad, (0, 0), mask)
    # kilap kaca: setengah atas putih transparan
    gloss = Image.new("L", (n, n), 0)
    gd = ImageDraw.Draw(gloss)
    gd.polygon(hexpts, fill=58)
    gd.rectangle([0, n * 0.5, n, n], fill=0)
    white = Image.new("RGBA", (n, n), (255, 255, 255, 255))
    img = Image.composite(white, img, gloss)
    img.putalpha(mask)
    d = ImageDraw.Draw(img)
    d.polygon(hexpts, outline=EDGE + (90,), width=max(1, int(1.6 * k)))
    # bayangan lembut WF lalu WF putih
    sh = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon([(x * k + 1.2 * k, y * k + 1.6 * k) for x, y in BEAM], fill=(15, 23, 42, 110))
    sh = sh.filter(ImageFilter.GaussianBlur(1.5 * k))
    img = Image.alpha_composite(img, sh)
    ImageDraw.Draw(img).polygon([(x * k, y * k) for x, y in BEAM], fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    big = render(512)
    big.save(os.path.join(OUT, "logo.png"))
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [render(s) for s in sizes]
    imgs[-1].save(os.path.join(OUT, "logo.ico"), sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    # wordmark
    W, H = 1400, 360
    wm = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    wm.paste(render(300), (30, 30))
    font_dir = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
    def font(name, size):
        try:
            return ImageFont.truetype(os.path.join(font_dir, name), size)
        except OSError:
            return ImageFont.load_default()
    d = ImageDraw.Draw(wm)
    d.text((370, 70), "STEEL KIT", font=font("seguisb.ttf", 150), fill=(30, 41, 59, 255))
    d.text((376, 245), "profil  \u00b7  sambungan  \u00b7  rangka  \u00b7  AutoCAD", font=font("segoeui.ttf", 52), fill=(71, 85, 105, 255))
    wm.save(os.path.join(OUT, "logo_wordmark.png"))
    print("logo:", OUT)


if __name__ == "__main__":
    main()
