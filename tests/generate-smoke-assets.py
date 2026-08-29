"""Generate small offline files used by the manual API smoke test."""

from __future__ import annotations

import math
import struct
import sys
import tempfile
import wave
from pathlib import Path


def write_pdf(path: Path) -> None:
    stream = b"BT /F1 24 Tf 72 720 Td (Mofang DAM PDF Preview) Tj ET"
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources 5 0 R >>",
        b"<< /Length %d >>\nstream\n%s\nendstream" % (len(stream), stream),
        b"<< /Font << /F1 6 0 R >> >>",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]
    output = bytearray(b"%PDF-1.4\n")
    offsets = [0]
    for index, body in enumerate(objects, start=1):
        offsets.append(len(output))
        output.extend(f"{index} 0 obj\n".encode())
        output.extend(body)
        output.extend(b"\nendobj\n")
    xref = len(output)
    output.extend(f"xref\n0 {len(objects) + 1}\n".encode())
    output.extend(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode())
    output.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
    path.write_bytes(output)


def write_wav(path: Path) -> None:
    rate = 16000
    frames = bytearray()
    for index in range(rate):
        sample = int(8000 * math.sin(2 * math.pi * 440 * index / rate))
        frames.extend(struct.pack("<h", sample))
    with wave.open(str(path), "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(rate)
        audio.writeframes(frames)


def main() -> None:
    output = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(tempfile.gettempdir(), "mofang-dam-smoke")
    output.mkdir(parents=True, exist_ok=True)
    write_pdf(output / "shooting-notice.pdf")
    write_wav(output / "location-tone.wav")
    (output / "readme.md").write_text("# Mofang DAM\n\nMarkdown preview smoke test.\n", encoding="utf-8")
    (output / "notes.txt").write_text("Mofang DAM text preview smoke test.\n", encoding="utf-8")
    (output / "castle.obj").write_text("o Cube\nv 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 3\n", encoding="utf-8")


if __name__ == "__main__":
    main()
