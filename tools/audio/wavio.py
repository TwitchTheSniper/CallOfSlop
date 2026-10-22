"""Tiny stdlib-only WAV reader/writer helpers for the gun-sample tool (no audioop, no numpy)."""
import struct
from array import array


def read_wav(path):
    """Returns (rate, channels, [per-channel float lists in -1..1]). PCM 16/24/32-bit and float32, RIFF/BWF."""
    with open(path, "rb") as f:
        head = f.read(12)
        if head[:4] != b"RIFF" or head[8:12] != b"WAVE":
            raise ValueError("not a RIFF WAVE file: " + path)
        fmt = None
        data = None
        while True:
            c = f.read(8)
            if len(c) < 8:
                break
            cid, size = c[:4], struct.unpack("<I", c[4:])[0]
            if cid == b"fmt ":
                fmt = struct.unpack("<HHIIHH", f.read(size)[:16])
                if size & 1:
                    f.read(1)
            elif cid == b"data":
                data = f.read(size)
                break
            else:
                f.seek(size + (size & 1), 1)
    if fmt is None or data is None:
        raise ValueError("missing fmt or data chunk: " + path)
    tag, ch, rate, _, align, bits = fmt
    n = len(data) // align
    if tag == 1 and bits == 16:
        a = array("h"); a.frombytes(data[:n * align]); scale = 1 / 32768.0
        vals = a
    elif tag == 1 and bits == 24:
        # widen 24-bit to 32-bit by inserting a low zero byte
        b = bytearray(n * ch * 4)
        b[1::4] = data[0::3][:n * ch]
        b[2::4] = data[1::3][:n * ch]
        b[3::4] = data[2::3][:n * ch]
        vals = array("i"); vals.frombytes(bytes(b)); scale = 1 / 2147483648.0
    elif tag == 1 and bits == 32:
        vals = array("i"); vals.frombytes(data[:n * align]); scale = 1 / 2147483648.0
    elif tag == 3 and bits == 32:
        vals = array("f"); vals.frombytes(data[:n * align]); scale = 1.0
    else:
        raise ValueError("unsupported WAV format tag %d / %d bits: %s" % (tag, bits, path))
    chans = [[v * scale for v in vals[c::ch]] for c in range(ch)]
    return rate, ch, chans


def write_wav16(path, rate, samples):
    """Mono 16-bit PCM WAV from floats (clamped)."""
    a = array("h", (max(-32768, min(32767, int(round(s * 32767)))) for s in samples))
    data = a.tobytes()
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16))
        f.write(b"data" + struct.pack("<I", len(data)) + data)
