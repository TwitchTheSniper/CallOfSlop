"""Builds Audio/Samples/Guns/ (trimmed 16-bit mono 44.1 kHz WAVs + manifest.json) from the
"Prepared SFX Library" of The Free Firearm Sound Library (CC0).

Pure Python, stdlib only. Reproducible: the same library gives byte-identical output.

    python3 -I tools/audio/make_gun_samples.py "<path to Prepared SFX Library>" Audio/Samples/Guns

Source files are 96 kHz / 24-bit stereo BWF recordings, each holding 2-5 single shots a few seconds
apart ("near" = beside the shooter, "mid" = in front of the shooter at mid distance). For each pick
below we find the Nth shot (onset detection on a 5 ms peak envelope), cut from 3 ms before it,
high-pass at 30 Hz, mix to mono, resample to 44.1 kHz (windowed-sinc), fade the tail out, normalise
the loudness (RMS of the first 200 ms) to a common level and soft-limit the peaks to -0.3 dBFS.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wavio import read_wav, write_wav16  # noqa: E402

OUT_RATE = 44100
TARGET_RMS = 0.18  # RMS of the first 200 ms after the onset, every shot sample (about -15 dBFS)
TAIL_RMS = 0.12  # the same for the mid-distance (tail) samples
CEILING = 0.965

CREDITS = ("The Free Firearm Sound Library (Prepared SFX Library), CC0 1.0; recorded by Ben Jaszczak, "
           "Brian Nelson, Kevin Heras and Matthew Nanney")

# set name -> description, reference caliber (the weapon caliber this set sounds right at), synth blend
SETS = {
    "pistol_light": ("9mm / .380 semi-auto pistols (Walther PPQ, Bersa)", 0.25, 0.22),
    "pistol_heavy": (".45 pistol and .38 revolver (Colt 1911, S&W 642)", 0.5, 0.22),
    "smg": ("9mm / 7.62x25 sub machine guns (Carl Gustav M45, PPSh)", 0.25, 0.22),
    "rifle": ("5.56 / 7.62x39 assault rifles (AR-15, AK-47)", 0.42, 0.2),
    "rifle_heavy": ("7.62x39 rifles for heavy ARs and LMGs (AK-47, SKS)", 0.6, 0.2),
    "dmr": ("semi-auto and light bolt rifles (SKS, Savage 10 .300 BLK)", 0.62, 0.2),
    "lever": ("lever-action rifles (Marlin 336 .30-30, Winchester 1894)", 0.78, 0.2),
    "sniper": ("full-power bolt-action rifles (Mosin Nagant, Tikka T3 .30-06)", 0.85, 0.2),
    "sniper_heavy": ("old military bolt-actions for the .50 and the musket (Springfield 1917, Arisaka)", 0.9, 0.4),
    "shotgun_pump": ("12 gauge pump shotguns (Winchester Model 12, Benelli Nova)", 0.72, 0.2),
    "shotgun_heavy": ("12 gauge shotguns, heavier report (Charles Daly pump, Mossberg 190 bolt)", 0.88, 0.2),
}

# (set, role, source folder, source file, which shot (0-based), length s)
PICKS = [
    ("pistol_light", "shot", "Walther PPQ", "X_39P.wav", 0, 0.7),
    ("pistol_light", "shot", "Walther PPQ", "X_39P.wav", 1, 0.7),
    ("pistol_light", "shot", "Bersa", "F_47P.wav", 0, 0.7),
    ("pistol_light", "shot", "Bersa", "F_47P.wav", 1, 0.7),
    ("pistol_light", "tail", "Walther PPQ", "X_31P.wav", 0, 1.4),
    ("pistol_heavy", "shot", "1911", "A_42P.wav", 0, 0.8),
    ("pistol_heavy", "shot", "1911", "A_42P.wav", 1, 0.8),
    ("pistol_heavy", "shot", "Smith & Wesson 642", "V_27P.wav", 0, 0.8),
    ("pistol_heavy", "shot", "Smith & Wesson 642", "V_27P.wav", 1, 0.8),
    ("pistol_heavy", "tail", "1911", "A_34P.wav", 0, 1.4),
    ("smg", "shot", "Carl Gustav M45", "G_31P.wav", 0, 0.6),
    ("smg", "shot", "Carl Gustav M45", "G_31P.wav", 1, 0.6),
    ("smg", "shot", "PPSh", "P_30P.wav", 0, 0.6),
    ("smg", "shot", "PPSh", "P_30P.wav", 2, 0.6),
    ("smg", "tail", "Carl Gustav M45", "G_20P.wav", 0, 1.4),
    ("rifle", "shot", "AR-15", "D_32P.wav", 0, 0.8),
    ("rifle", "shot", "AR-15", "D_32P.wav", 1, 0.8),
    ("rifle", "shot", "AK-47", "C_28P.wav", 0, 0.8),
    ("rifle", "shot", "AK-47", "C_28P.wav", 1, 0.8),
    ("rifle", "tail", "AR-15", "D_24P.wav", 0, 1.5),
    ("rifle_heavy", "shot", "AK-47", "C_28P.wav", 2, 0.9),
    ("rifle_heavy", "shot", "AK-47", "C_28P.wav", 3, 0.9),
    ("rifle_heavy", "shot", "SKS", "U_14P.wav", 0, 0.9),
    ("rifle_heavy", "shot", "SKS", "U_14P.wav", 1, 0.9),
    ("rifle_heavy", "tail", "AK-47", "C_31P.wav", 0, 1.5),
    ("dmr", "shot", "SKS", "U_14P.wav", 2, 1.0),
    ("dmr", "shot", "SKS", "U_14P.wav", 3, 1.0),
    ("dmr", "shot", "Savage 10 .300 Blackout", "T_27P.wav", 0, 1.0),
    ("dmr", "shot", "Savage 10 .300 Blackout", "T_27P.wav", 1, 1.0),
    ("dmr", "tail", "Savage 10 .300 Blackout", "T_17P.wav", 0, 1.6),
    ("lever", "shot", "Marlin 336", "I_22P.wav", 0, 1.0),
    ("lever", "shot", "Marlin 336", "I_22P.wav", 1, 1.0),
    ("lever", "shot", "Model 1894", "L_23P.wav", 0, 1.0),
    ("lever", "shot", "Model 1894", "L_23P.wav", 1, 1.0),
    ("lever", "tail", "Marlin 336", "I_17P.wav", 0, 1.6),
    ("sniper", "shot", "Mosin Nagant", "M_21P.wav", 0, 1.3),
    ("sniper", "shot", "Mosin Nagant", "M_21P.wav", 1, 1.3),
    ("sniper", "shot", "Tikka", "W_29P.wav", 0, 1.3),
    ("sniper", "shot", "Tikka", "W_29P.wav", 1, 1.3),
    ("sniper", "tail", "Mosin Nagant", "M_26P.wav", 0, 1.8),
    ("sniper_heavy", "shot", "1917", "B_24P.wav", 0, 1.4),
    ("sniper_heavy", "shot", "1917", "B_24P.wav", 1, 1.4),
    ("sniper_heavy", "shot", "Arisaka", "E_25P.wav", 0, 1.4),
    ("sniper_heavy", "shot", "Arisaka", "E_25P.wav", 1, 1.4),
    ("sniper_heavy", "tail", "1917", "B_16P.wav", 0, 1.8),
    ("shotgun_pump", "shot", "Model 12", "K_22P.wav", 0, 0.9),
    ("shotgun_pump", "shot", "Model 12", "K_22P.wav", 1, 0.9),
    ("shotgun_pump", "shot", "Nova", "O_21P.wav", 0, 0.9),
    ("shotgun_pump", "shot", "Nova", "O_21P.wav", 1, 0.9),
    ("shotgun_pump", "tail", "Model 12", "K_17P.wav", 0, 1.6),
    ("shotgun_heavy", "shot", "CD", "H_21P.wav", 0, 1.0),
    ("shotgun_heavy", "shot", "CD", "H_21P.wav", 1, 1.0),
    ("shotgun_heavy", "shot", "Mossberg", "N_30P.wav", 0, 1.0),
    ("shotgun_heavy", "shot", "Mossberg", "N_30P.wav", 1, 1.0),
    ("shotgun_heavy", "tail", "CD", "H_16P.wav", 0, 1.6),
]

# Which set a weapon uses: the first rule that matches (class, optional sound styles, caliber / rpm limits).
# Zap, Toy and Pneumatic weapons match nothing and keep their synthesised sound.
RULES = [
    {"class": "Pistol", "maxCaliber": 0.38, "set": "pistol_light"},
    {"class": "Pistol", "set": "pistol_heavy"},
    {"class": "SMG", "set": "smg"},
    {"class": "AssaultRifle", "maxCaliber": 0.5, "set": "rifle"},
    {"class": "AssaultRifle", "set": "rifle_heavy"},
    {"class": "LMG", "maxCaliber": 0.45, "set": "rifle"},
    {"class": "LMG", "set": "rifle_heavy"},
    {"class": "DMR", "maxRpm": 150, "set": "lever"},
    {"class": "DMR", "set": "dmr"},
    {"class": "Sniper", "sounds": ["Boom"], "set": "sniper_heavy"},
    {"class": "Sniper", "sounds": ["Gun", "Suppressed"], "set": "sniper"},
    {"class": "Shotgun", "minCaliber": 0.85, "set": "shotgun_heavy"},
    {"class": "Shotgun", "set": "shotgun_pump"},
    {"class": "Special", "sounds": ["Boom"], "set": "sniper_heavy"},
]


def onsets(m, rate):
    """Shot onsets (s): the 5 ms peak envelope crossing 30% of the file peak after dropping under 6%,
    at least 1 s apart (later crossings within 1 s are echoes / reflections)."""
    hop = rate // 200
    env = [max(abs(x) for x in m[i:i + hop]) for i in range(0, len(m) - hop, hop)]
    pk = max(env)
    out, armed = [], True
    for i, e in enumerate(env):
        t = i * hop
        if armed and e > 0.3 * pk and (not out or t - out[-1] > rate):
            # refine to the first sample above 20% of this hop's peak
            j = t
            while abs(m[j]) < 0.2 * e:
                j += 1
            out.append(j)
            armed = False
        elif e < 0.06 * pk:
            armed = True
    return out


def highpass(x, rate, fc=30.0):
    a = math.exp(-2 * math.pi * fc / rate)
    y, px, py = [], 0.0, 0.0
    for v in x:
        py = a * (py + v - px)
        px = v
        y.append(py)
    return y


def resample(x, src, dst, taps=24):
    """Windowed-sinc (Blackman) resampler with a cutoff just under the lower Nyquist."""
    if src == dst:
        return list(x)
    g = math.gcd(src, dst)
    up, down = dst // g, src // g
    cutoff = min(1.0, dst / src) * 0.94
    half = taps  # zero crossings each side, in input samples scaled by cutoff
    kernels = []
    for ph in range(up):
        frac = ph / up
        k = []
        for t in range(-half + 1, half + 1):
            d = t - frac
            w = 0.42 + 0.5 * math.cos(math.pi * d / half) + 0.08 * math.cos(2 * math.pi * d / half) if abs(d) < half else 0.0
            s = cutoff if d == 0 else math.sin(math.pi * cutoff * d) / (math.pi * d)
            k.append(s * w)
        kernels.append(k)
    n_out = len(x) * up // down
    out = []
    xl = len(x)
    for n in range(n_out):
        pos = n * down
        i0, ph = divmod(pos, up)
        k = kernels[ph]
        acc = 0.0
        base = i0 - half + 1
        if base >= 0 and base + 2 * half <= xl:
            seg = x[base:base + 2 * half]
            for a, b in zip(seg, k):
                acc += a * b
        else:
            for j, kv in enumerate(k):
                idx = base + j
                if 0 <= idx < xl:
                    acc += x[idx] * kv
        out.append(acc)
    return out


def soft_limit(x, ceiling=CEILING, knee=0.7):
    span = ceiling - knee
    out = []
    for v in x:
        a = abs(v)
        if a > knee:
            a = knee + span * math.tanh((a - knee) / span)
            v = a if v > 0 else -a
        out.append(v)
    return out


def rms(x):
    return math.sqrt(sum(v * v for v in x) / max(1, len(x)))


def main():
    lib, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    cache = {}
    samples = []
    used_sources = {}
    counters = {}
    for set_name, role, folder, fname, shot, length in PICKS:
        key = (folder, fname)
        if key not in cache:
            rate, ch, chans = read_wav(os.path.join(lib, folder, fname))
            mono = [sum(v) / ch for v in zip(*chans)] if ch > 1 else chans[0]
            cache[key] = (rate, ch, mono, onsets(mono, rate))
        rate, ch, mono, ons = cache[key]
        if shot >= len(ons):
            raise SystemExit("%s/%s has only %d shots (wanted #%d)" % (folder, fname, len(ons), shot))
        start = max(0, ons[shot] - int(0.003 * rate))
        end = min(len(mono), start + int(length * rate) + 64)
        if shot + 1 < len(ons):
            end = min(end, ons[shot + 1] - int(0.01 * rate))
        seg = highpass(mono[start:end], rate)
        seg = resample(seg, rate, OUT_RATE)[:int(length * OUT_RATE)]
        n = len(seg)
        # fade: 1 ms fade-in, the last 40% fades out on a raised cosine
        fi = int(0.001 * OUT_RATE)
        fo0 = int(n * 0.6)
        for i in range(n):
            g = 1.0
            if i < fi:
                g = i / fi
            if i >= fo0:
                g *= 0.5 + 0.5 * math.cos(math.pi * (i - fo0) / (n - fo0))
            seg[i] *= g
        level = rms(seg[:int(0.2 * OUT_RATE)])
        target = TARGET_RMS if role == "shot" else TAIL_RMS
        gain = target / max(level, 1e-6)
        seg = soft_limit([v * gain for v in seg])
        counters[(set_name, role)] = counters.get((set_name, role), 0) + 1
        out_name = "%s_%s%d.wav" % (set_name, role, counters[(set_name, role)])
        write_wav16(os.path.join(out_dir, out_name), OUT_RATE, seg)
        clipped = sum(1 for v in mono[ons[shot]:ons[shot] + int(0.05 * rate)] if abs(v) > 0.999)
        samples.append({
            "file": out_name, "set": set_name, "role": role, "gain": 1.0,
            "source": "%s/%s shot %d at %.3f s" % (folder, fname, shot + 1, ons[shot] / rate),
            "seconds": round(n / OUT_RATE, 3),
        })
        used_sources.setdefault("%s/%s" % (folder, fname), []).append(out_name)
        print("%-26s %-30s gain %5.2f  peak-in %.2f  clipped-in %d" % (out_name, samples[-1]["source"], gain,
              max(abs(v) for v in mono[ons[shot]:ons[shot] + int(0.05 * rate)]), clipped))
    manifest = {
        "format": "call-of-slop-gun-samples/1",
        "about": "Recorded gunshot layers blended with the synthesised gunshots (Source/Audio/GunSamples.cs). "
                 "Made by tools/audio/make_gun_samples.py from the library's 'Prepared SFX Library' folder.",
        "credits": CREDITS,
        "license": "CC0 1.0 Universal (public domain dedication): https://creativecommons.org/publicdomain/zero/1.0/",
        "rate": OUT_RATE,
        "sets": {k: {"about": v[0], "refCaliber": v[1], "synthBlend": v[2]} for k, v in SETS.items()},
        "rules": RULES,
        "samples": samples,
    }
    with open(os.path.join(out_dir, "manifest.json"), "w", newline="\n") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")
    total = sum(os.path.getsize(os.path.join(out_dir, s["file"])) for s in samples)
    print("%d samples, %.2f MB; sources: %s" % (len(samples), total / 1e6, ", ".join(sorted(used_sources))))


if __name__ == "__main__":
    main()
