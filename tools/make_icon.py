#!/usr/bin/env python3
"""Erzeugt die Markenbilder ohne externe Abhängigkeiten:
  src/App/app.ico                  Programmsymbol (16-256 px)
  installer/wizard-large.png       Seitenbild für Willkommen-/Abschlussseite des Setups (Seitenverhältnis 164:314)
  installer/wizard-small.png       kleines Bild oben rechts auf den übrigen Setup-Seiten

Motiv: dunkle, abgerundete Kachel mit drei QR-Suchecken und ein paar Datenmodulen in Schleimgrün.
Kleine Größen werden aus einer 8-fach überabgetastet gerenderten Vorlage per Flächenmittelung berechnet.
Aufruf: python3 tools/make_icon.py            (schreibt alle drei Dateien)
"""
import struct, sys, zlib

BG = (30, 41, 59)        # Schiefer
FG = (132, 204, 22)      # Schleimgrün
GRID = 64                # Entwurfsraster
SS = 8                   # Überabtastung der Vorlage
MASTER = GRID * SS       # 512 px Vorlage (8 px je Rasterpunkt)


def rounded_rect(x, y, r=12.0):
    """True, wenn (x,y) im Raster [0,64]^2 mit abgerundeten Ecken liegt."""
    cx = min(max(x, r), GRID - r)
    cy = min(max(y, r), GRID - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def rect(x, y, x0, y0, w, h):
    return x0 <= x < x0 + w and y0 <= y < y0 + h


def finder(x, y, ox, oy):
    # 22x22 außen, 14x14 Aussparung, 6x6 Mitte (wie in TrayIcon.cs)
    if rect(x, y, ox, oy, 22, 22) and not rect(x, y, ox + 4, oy + 4, 14, 14):
        return True
    return rect(x, y, ox + 8, oy + 8, 6, 6)


MODULES = [(37, 37), (49, 37), (43, 43), (37, 49), (49, 49), (55, 43)]


def pixel(x, y):
    if not rounded_rect(x, y):
        return (0, 0, 0, 0)
    for ox, oy in ((5, 5), (37, 5), (5, 37)):
        if finder(x, y, ox, oy):
            return FG + (255,)
    for mx, my in MODULES:
        if rect(x, y, mx, my, 6, 6):
            return FG + (255,)
    return BG + (255,)


def render_master():
    px = []
    for j in range(MASTER):
        row = []
        y = (j + 0.5) / SS
        for i in range(MASTER):
            row.append(pixel((i + 0.5) / SS, y))
        px.append(row)
    return px


def downsample(master, size):
    """Flächenmittelung mit vormultipliziertem Alpha; liefert size x size RGBA."""
    n = MASTER
    out = []
    for j in range(size):
        y0, y1 = j * n / size, (j + 1) * n / size
        row = []
        for i in range(size):
            x0, x1 = i * n / size, (i + 1) * n / size
            r = g = b = a = wsum = 0.0
            for yy in range(int(y0), min(n, int(y1) + 1)):
                wy = min(y1, yy + 1) - max(y0, yy)
                if wy <= 0:
                    continue
                for xx in range(int(x0), min(n, int(x1) + 1)):
                    wx = min(x1, xx + 1) - max(x0, xx)
                    if wx <= 0:
                        continue
                    w = wx * wy
                    pr, pg, pb, pa = master[yy][xx]
                    r += pr * pa * w; g += pg * pa * w; b += pb * pa * w; a += pa * w
                    wsum += w
            if a <= 0:
                row.append((0, 0, 0, 0))
            else:
                row.append((round(r / a), round(g / a), round(b / a), round(a / wsum)))
        out.append(row)
    return out


def png_bytes(img):
    h, w = len(img), len(img[0])
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in img)

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def dib_bytes(img):
    """Klassischer ICO-Bildeintrag: BITMAPINFOHEADER (Höhe verdoppelt), BGRA bottom-up, leere AND-Maske."""
    h, w = len(img), len(img[0])
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    pixels = b"".join(bytes(c for (r, g, b, a) in row for c in (b, g, r, a)) for row in reversed(img))
    mask = b"\x00" * (((w + 31) // 32) * 4 * h)
    return header + pixels + mask


def build_ico(sizes=(16, 24, 32, 48, 64, 128, 256)):
    master = render_master()
    entries = []
    for s in sizes:
        img = downsample(master, s)
        data = png_bytes(img) if s >= 256 else dib_bytes(img)
        entries.append((s, data, img))
    head = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    directory, blobs = b"", b""
    for s, data, _ in entries:
        directory += struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return head + directory + blobs, entries


PANEL = (15, 23, 42)  # Seitenbild-Hintergrund, etwas dunkler als die Kachel; im Setup als WizardImageBackColor=$2A170F


def compose_small(master, size=147, icon_size=120):
    """Kleines Seitenbild: Kachel mit transparentem Rand, damit sie nicht an der Fensterkante klebt."""
    icon = downsample(master, icon_size)
    pad = (size - icon_size) // 2
    img = [[(0, 0, 0, 0)] * size for _ in range(size)]
    for j, row in enumerate(icon):
        img[pad + j][pad:pad + icon_size] = row
    return img


def compose_large(master, width=410, height=785, icon_size=300):
    """Seitenbild: Kachel mittig im oberen Drittel auf dunklem Grund (undurchsichtig)."""
    icon = downsample(master, icon_size)
    x0, y0 = (width - icon_size) // 2, (height - icon_size) // 2 - 40
    img = [[PANEL + (255,) for _ in range(width)] for _ in range(height)]
    for j, row in enumerate(icon):
        for i, (r, g, b, a) in enumerate(row):
            if a:
                br, bg, bb, _ = img[y0 + j][x0 + i]
                k = a / 255.0
                img[y0 + j][x0 + i] = (round(r * k + br * (1 - k)), round(g * k + bg * (1 - k)), round(b * k + bb * (1 - k)), 255)
    return img


if __name__ == "__main__":
    import os
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    ico, entries = build_ico()
    outputs = {os.path.join(root, "src", "App", "app.ico"): ico}
    master = render_master()
    outputs[os.path.join(root, "installer", "wizard-large.png")] = png_bytes(compose_large(master))
    outputs[os.path.join(root, "installer", "wizard-small.png")] = png_bytes(compose_small(master))
    for path, data in outputs.items():
        with open(path, "wb") as f:
            f.write(data)
        print(f"{os.path.relpath(path, root)}: {len(data)} Bytes")
