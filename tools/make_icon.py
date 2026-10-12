#!/usr/bin/env python3
"""Erzeugt alle Markenbilder ohne externe Abhängigkeiten aus dem SVG-Logo.

Quelle:   assets/code-scanner-solid.svg          schwarzes Original-Logo (24x24)
          assets/logo-mask-1024.png              dessen Alpha-Maske, einmalig gerastert mit:
                                                   rsvg-convert -w 1024 -h 1024 assets/code-scanner-solid.svg -o assets/logo-mask-1024.png
Ergebnis: src/App/app.ico                        Programmsymbol (16-256 px)
          installer/wizard-large.png             Seitenbild für Willkommen-/Abschlussseite des Setups (Seitenverhältnis 164:314)
          installer/wizard-small.png             kleines Bild oben rechts auf den übrigen Setup-Seiten
          assets/logo.png                        Logo für die README

Das schwarze Logo wird eingefärbt (Verlauf Limette -> Cyan) und auf eine dunkle, abgerundete Kachel gesetzt,
damit es auf hellen wie dunklen Hintergründen lesbar bleibt.
Aufruf: python3 tools/make_icon.py
"""
import os
import struct
import sys
import zlib

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
MASK_PATH = os.path.join(ROOT, "assets", "logo-mask-1024.png")

TILE_TOP = (30, 41, 59)       # Schiefer
TILE_BOTTOM = (15, 23, 42)    # dunkler Schiefer (leichter Verlauf nach unten)
GLYPH_FROM = (163, 230, 53)   # Limette #A3E635 (oben links)
GLYPH_TO = (34, 211, 238)     # Cyan    #22D3EE (unten rechts)
PANEL = (15, 23, 42)          # Seitenbild-Hintergrund; im Setup als WizardImageBackColor=$2A170F

# Anteil der Kantenlänge, den die 24-Einheiten-Fläche des Logos einnimmt (kleine Größen: größer = lesbarer)
GLYPH_FRACTION = {16: 0.90, 24: 0.86, 32: 0.82, 48: 0.76, 64: 0.74}
DEFAULT_FRACTION = 0.72


# ---- PNG lesen/schreiben --------------------------------------------------------------------------------------------

def read_png_alpha(path):
    """Liest ein 8-Bit-RGBA-PNG (nicht interlaced) und liefert (Breite, Höhe, Zeilen der Alpha-Werte)."""
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("kein PNG")
    pos, idat, width, height = 8, b"", 0, 0
    while pos < len(data):
        length, tag = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if tag == b"IHDR":
            width, height, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if (depth, ctype, interlace) != (8, 6, 0):
                raise ValueError("erwartet wird 8-Bit-RGBA ohne Interlace")
        elif tag == b"IDAT":
            idat += body
    raw = zlib.decompress(idat)
    bpp, stride = 4, width * 4
    rows, prev = [], bytearray(stride)
    for y in range(height):
        ftype = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        if ftype == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 255
        elif ftype == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 255
        elif ftype == 3:
            for i in range(stride):
                left = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 255
        elif ftype == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i - bpp] if i >= bpp else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 255
        elif ftype != 0:
            raise ValueError(f"unbekannter PNG-Filter {ftype}")
        rows.append(bytes(line[3::4]))  # nur Alpha
        prev = line
    return width, height, rows


def png_bytes(img):
    """img: Zeilen von (r, g, b, a)-Tupeln -> PNG-Bytes (8 Bit RGBA)."""
    h, w = len(img), len(img[0])
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in img)

    def chunk(tag, body):
        c = struct.pack(">I", len(body)) + tag + body
        return c + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# ---- Rendering ------------------------------------------------------------------------------------------------------

class Mask:
    """Alpha-Maske mit Summentabelle: Flächenmittel über beliebige Rechtecke in O(1)."""

    def __init__(self, path):
        self.w, self.h, rows = read_png_alpha(path)
        sat = [[0] * (self.w + 1) for _ in range(self.h + 1)]
        for y in range(self.h):
            run, row, above, cur = 0, rows[y], sat[y], sat[y + 1]
            for x in range(self.w):
                run += row[x]
                cur[x + 1] = above[x + 1] + run
        self.sat = sat

    def average(self, x0, y0, x1, y1):
        """Mittlere Deckkraft (0..1) im Rechteck [x0,x1) x [y0,y1) in Maskenpixeln."""
        xa = min(max(int(round(x0)), 0), self.w)
        ya = min(max(int(round(y0)), 0), self.h)
        xb = min(max(int(round(x1)), xa + 1), self.w)
        yb = min(max(int(round(y1)), ya + 1), self.h)
        if xa >= xb or ya >= yb:
            return 0.0
        s = self.sat
        total = s[yb][xb] - s[ya][xb] - s[yb][xa] + s[ya][xa]
        return total / (255.0 * (xb - xa) * (yb - ya))


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def tile_coverage(x, y, size, radius):
    """Anti-aliaste Deckkraft der abgerundeten Kachel für das Pixel (x, y)."""
    px, py = x + 0.5, y + 0.5
    cx = min(max(px, radius), size - radius)
    cy = min(max(py, radius), size - radius)
    d = ((px - cx) ** 2 + (py - cy) ** 2) ** 0.5 - radius
    return min(1.0, max(0.0, 0.5 - d))


def render_icon(mask, size):
    """Kachel mit eingefärbtem Logo, size x size, Zeilen von (r, g, b, a)."""
    fraction = GLYPH_FRACTION.get(size, DEFAULT_FRACTION)
    box = fraction * size                 # Kantenlänge der 24-Einheiten-Fläche in Zielpixeln
    g0 = (size - box) / 2.0
    k = mask.w / box                      # Maskenpixel je Zielpixel
    radius = size * (0.20 if size <= 32 else 0.22)
    img = []
    for y in range(size):
        row = []
        base = lerp(TILE_TOP, TILE_BOTTOM, (y + 0.5) / size)
        for x in range(size):
            cover = tile_coverage(x, y, size, radius)
            if cover <= 0.0:
                row.append((0, 0, 0, 0))
                continue
            u, v = (x - g0) * k, (y - g0) * k
            ga = mask.average(u, v, u + k, v + k) if (-k < u < mask.w and -k < v < mask.h) else 0.0
            if ga > 0.0:
                t = min(1.0, max(0.0, ((x + 0.5 - g0) / box + (y + 0.5 - g0) / box) / 2.0))
                glyph = lerp(GLYPH_FROM, GLYPH_TO, t)
                col = tuple(glyph[i] * ga + base[i] * (1.0 - ga) for i in range(3))
            else:
                col = base
            row.append((round(col[0]), round(col[1]), round(col[2]), round(255 * cover)))
        img.append(row)
    return img


# ---- ICO ------------------------------------------------------------------------------------------------------------

def dib_bytes(img):
    """Klassischer ICO-Bildeintrag: BITMAPINFOHEADER (Höhe verdoppelt), BGRA bottom-up, leere AND-Maske."""
    h, w = len(img), len(img[0])
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    pixels = b"".join(bytes(c for (r, g, b, a) in row for c in (b, g, r, a)) for row in reversed(img))
    return header + pixels + b"\x00" * (((w + 31) // 32) * 4 * h)


def build_ico(mask, sizes=(16, 24, 32, 48, 64, 128, 256)):
    entries = []
    for s in sizes:
        img = render_icon(mask, s)
        entries.append((s, png_bytes(img) if s >= 256 else dib_bytes(img), img))
    offset = 6 + 16 * len(entries)
    directory, blobs = b"", b""
    for s, data, _ in entries:
        directory += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return struct.pack("<HHH", 0, 1, len(entries)) + directory + blobs, entries


# ---- Setup-Bilder ---------------------------------------------------------------------------------------------------

def compose_small(icon_img, size=147):
    """Kleines Seitenbild: Kachel mit transparentem Rand, damit sie nicht an der Fensterkante klebt."""
    n = len(icon_img)
    pad = (size - n) // 2
    img = [[(0, 0, 0, 0)] * size for _ in range(size)]
    for j, row in enumerate(icon_img):
        img[pad + j][pad:pad + n] = row
    return img


def compose_large(icon_img, width=410, height=785):
    """Seitenbild: Kachel mittig im oberen Drittel auf dunklem Grund (undurchsichtig)."""
    n = len(icon_img)
    x0, y0 = (width - n) // 2, (height - n) // 2 - 40
    img = [[PANEL + (255,) for _ in range(width)] for _ in range(height)]
    for j, row in enumerate(icon_img):
        for i, (r, g, b, a) in enumerate(row):
            if a:
                br, bg, bb, _ = img[y0 + j][x0 + i]
                k = a / 255.0
                img[y0 + j][x0 + i] = (round(r * k + br * (1 - k)), round(g * k + bg * (1 - k)), round(b * k + bb * (1 - k)), 255)
    return img


def main():
    mask = Mask(MASK_PATH)
    ico, entries = build_ico(mask)
    by_size = {s: img for s, _, img in entries}
    outputs = {
        os.path.join(ROOT, "src", "App", "app.ico"): ico,
        os.path.join(ROOT, "assets", "logo.png"): png_bytes(by_size[256]),
        os.path.join(ROOT, "installer", "wizard-small.png"): png_bytes(compose_small(render_icon(mask, 120))),
        os.path.join(ROOT, "installer", "wizard-large.png"): png_bytes(compose_large(render_icon(mask, 300))),
    }
    for path, data in outputs.items():
        with open(path, "wb") as f:
            f.write(data)
        print(f"{os.path.relpath(path, ROOT)}: {len(data)} Bytes")


if __name__ == "__main__":
    sys.exit(main())
