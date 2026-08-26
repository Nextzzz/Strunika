"""Isolate the major/minor problem: on the VALID pool, for each config
measure (a) overall majmin WCSR and (b) MODE ACCURACY — among frames
where the truth is a maj/min chord AND the predicted root is correct,
how often the predicted mode (maj vs min) matches. (b) is exactly the
"D vs Dm" question, stripped of root and no-chord errors.

Configs: single models + full-containing ensembles + full with a
stronger diatonic key prior (the surgical lever for mode: in a clear
key, the diatonic chord on a degree beats its parallel).

Run:
    .venv/Scripts/python mode_eval.py [rows]
"""
import csv
import json
import os
import sys

import numpy as np
import onnxruntime as ort

os.environ.setdefault("HOOK_SUBSET", "valid")
from btc_features import features_from_wav
from hooktheory_eval import ROOT, load_audio, read_lab, STEP
from infer_tricks_eval import probs_pass, combine, T
from viterbi_eval import build_log_transitions, viterbi
from key_prior_py import apply_key_prior  # local helper below

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = {"base": "btc_large_voca", "self": "btc_self", "full": "btc_full"}
MAJ_Q = {"", "maj", "maj6", "maj7", "7"}
MIN_Q = {"min", "min6", "min7", "minmaj7"}
ROOTS = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5,
         "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}


def root_mode(label):
    """Handles both model labels ('D', 'D:min') and majmin truth ('D', 'Dm')."""
    if label in ("N", "X", "—", None):
        return None
    if ":" in label:
        root, _, q = label.partition(":")
        pc = ROOTS.get(root)
        if pc is None:
            return None
        if q in MAJ_Q:
            return pc, "maj"
        if q in MIN_Q:
            return pc, "min"
        return None
    # majmin form: trailing 'm' = minor, else major
    if label.endswith("m"):
        pc = ROOTS.get(label[:-1])
        return (pc, "min") if pc is not None else None
    pc = ROOTS.get(label)
    return (pc, "maj") if pc is not None else None


def main(rows_limit):
    sessions = {k: ort.InferenceSession(os.path.join(HERE, "models", v + ".onnx"))
                for k, v in MODELS.items()}
    with open(os.path.join(HERE, "models", "btc_large_voca.json")) as f:
        labels = json.load(f)["labels"]
    log_trans = build_log_transitions(labels, None, 0.9)

    configs = {
        "base": (["base"], 0.0), "self": (["self"], 0.0), "full": (["full"], 0.0),
        "full+base": (["full", "base"], 0.0),
        "full+self": (["full", "self"], 0.0),
        "full+base+self": (["full", "base", "self"], 0.0),
        "full+keyprior0.5": (["full"], 0.5),
        "full+keyprior1.0": (["full"], 1.0),
    }

    with open(os.path.join(ROOT, "sample.csv"), encoding="utf-8") as f:
        rows = list(csv.DictReader(f))[:rows_limit]

    # majmin scored/correct, mode scored/correct
    tally = {c: [0, 0, 0, 0] for c in configs}
    done = 0
    for row in rows:
        audio = os.path.join(ROOT, "audio", row["id"] + ".m4a")
        mm_path = os.path.join(ROOT, "labs", row["id"] + ".lab")
        if not (os.path.exists(audio) and os.path.exists(mm_path)):
            continue
        wav = load_audio(audio)
        if len(wav) < 22050:
            continue
        features, spf = features_from_wav(wav)
        passes = {m: [probs_pass(sessions[m], features, 0, 0),
                      probs_pass(sessions[m], features, T // 2, 0)] for m in MODELS}
        truth = read_lab(mm_path)

        preds = {}
        for name, (members, kp) in configs.items():
            probs = combine([p for m in members for p in passes[m]])
            lp = np.log(probs + 1e-12)
            if kp > 0:
                lp = apply_key_prior(lp, labels, kp)
            preds[name] = [labels[i] for i in viterbi(lp, log_trans)]

        t = truth[0][0]
        while t < truth[-1][1]:
            tl = next((l for s, e, l in truth if s <= t < e), None)
            if tl is None:
                t += STEP
                continue
            trm = root_mode(tl)
            a = root_mode(tl)
            idx = min(int(t / spf), len(features) - 1)
            for name in configs:
                pl = preds[name][idx]
                b = root_mode(pl)
                if a is not None:
                    tally[name][0] += 1
                    if b == a or (b and b[0] == a[0] and b[1] == a[1]):
                        tally[name][1] += 1
                # mode accuracy: truth maj/min, predicted root == truth root
                if trm is not None and b is not None and b[0] == trm[0]:
                    tally[name][2] += 1
                    if b[1] == trm[1]:
                        tally[name][3] += 1
            t += STEP
        done += 1
        if done % 50 == 0:
            print(f"{done} songs...", flush=True)

    print(f"\n=== mode isolation, {done} songs ===")
    print(f"{'config':18} {'majmin':>8}  {'mode-acc':>8}")
    for name, (ms, mc, ds, dc) in tally.items():
        print(f"{name:18} {mc / max(ms, 1):7.2%}  {dc / max(ds, 1):7.2%}")


if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 10 ** 9)
