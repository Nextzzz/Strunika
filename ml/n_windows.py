"""Synthetic "no chord" training windows: silence, white/pink noise at
several levels, mains hum with harmonics, and noise bursts. HookTheory
annotations never contain N, so a model fine-tuned on them alone
unlearns silence (the lamp-hum → A#m case). These windows keep that
lesson alive. Labels are all N, masks all 1.

Output: <out_dir>/n_train.npz   (x f16 [W,108,144], y, m)

Run:
    .venv/Scripts/python n_windows.py [out_dir=bundle_hook_train/data] [windows=240]
"""
import os
import sys

import numpy as np

from btc_features import features_from_wav, SAMPLE_RATE, TIMESTEP

HERE = os.path.dirname(os.path.abspath(__file__))
N_IDX = 169
SECONDS = 10.0   # one 10-s chunk = exactly one 108-frame window


def pink(n, rng):
    spec = rng.standard_normal(n // 2 + 1) + 1j * rng.standard_normal(n // 2 + 1)
    spec /= np.sqrt(np.arange(len(spec)) + 1.0)
    x = np.fft.irfft(spec, n)
    return x / (np.std(x) + 1e-9)


def hum(n, rng):
    t = np.arange(n) / SAMPLE_RATE
    f0 = rng.choice([50.0, 60.0])
    x = sum((0.6 ** k) * np.sin(2 * np.pi * f0 * (k + 1) * t + rng.uniform(0, 6.3))
            for k in range(6))
    return x / (np.abs(x).max() + 1e-9)


def make(kind, rng):
    n = int(SAMPLE_RATE * SECONDS)
    level = 10 ** (rng.uniform(-45, -15) / 20)
    if kind == "silence":
        return np.zeros(n, dtype=np.float32) + rng.standard_normal(n).astype(np.float32) * 1e-4
    if kind == "white":
        return (rng.standard_normal(n) * level).astype(np.float32)
    if kind == "pink":
        return (pink(n, rng) * level).astype(np.float32)
    if kind == "hum":
        return (hum(n, rng) * level + pink(n, rng) * level * 0.05).astype(np.float32)
    if kind == "bursts":
        x = pink(n, rng) * level
        env = (rng.random(n // 2205 + 1) < 0.3).repeat(2205)[:n]
        return (x * env).astype(np.float32)
    raise ValueError(kind)


def main(out_dir, count):
    rng = np.random.default_rng(11)
    kinds = ["silence", "white", "pink", "hum", "bursts"]
    xs = []
    for i in range(count):
        wav = make(kinds[i % len(kinds)], rng)
        feats, _ = features_from_wav(wav)
        feats = feats[:TIMESTEP]
        if len(feats) < TIMESTEP:
            feats = np.pad(feats, ((0, TIMESTEP - len(feats)), (0, 0)),
                           constant_values=float(np.log(1e-6)))
        xs.append(feats.astype(np.float16))
    os.makedirs(out_dir, exist_ok=True)
    np.savez_compressed(os.path.join(out_dir, "n_train.npz"),
                        x=np.stack(xs),
                        y=np.full((count, TIMESTEP), N_IDX, dtype=np.int64),
                        m=np.ones((count, TIMESTEP), dtype=np.float32))
    print(f"{count} N windows -> {out_dir}/n_train.npz")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "bundle_hook_train", "data"),
         int(sys.argv[2]) if len(sys.argv) > 2 else 240)
