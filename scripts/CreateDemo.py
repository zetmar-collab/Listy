"""Tworzy krótkie, syntetyczne dźwięki do sprawdzenia działania Listy."""
from pathlib import Path
import math
import struct
import wave

root = Path(__file__).resolve().parents[1] / "Przykładowa muzyka"
for album, names, frequency in [
    ("01 — Spokojny poranek", ["01 — Początek", "02 — Chwila", "10 — Zakończenie"], 330),
    ("02 — Wieczór/CD 1", ["01 — Światła", "02 — Powrót"], 440),
]:
    folder = root / album
    folder.mkdir(parents=True, exist_ok=True)
    for index, name in enumerate(names):
        target = folder / (name + ".wav")
        if target.exists():
            continue
        with wave.open(str(target), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(16000)
            output.writeframes(b"".join(struct.pack("<h", int(2500 * math.sin(2 * math.pi * (frequency + index * 55) * t / 16000) * min(t / 800, (16000 - t) / 800, 1))) for t in range(16000)))
old = root / "01 — Spokojny poranek" / "Przykładowa stara lista.m3u8"
if not old.exists():
    old.write_text("#EXTM3U\n01 — Początek.wav\n02 — Chwila.wav\n", encoding="utf-8")
print(root)
