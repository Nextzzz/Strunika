"""Python port of Strunika.Neural.KeyPrior (diatonic second pass): pick
the key that covers the most decoded frames, then add a log bonus to
its diatonic chords. Mirrors the C# product pipeline so mode_eval sees
what the app would show.
"""
import numpy as np

MAJ_Q = {"", "maj", "maj6", "maj7", "7"}
MIN_Q = {"min", "min6", "min7", "minmaj7"}
ROOTS = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5,
         "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}
# (root offset, is_minor_chord)
MAJOR_KEY = [(0, False), (2, True), (4, True), (5, False), (7, False), (9, True)]
MINOR_KEY = [(0, True), (3, False), (5, True), (7, True), (7, False), (8, False), (10, False)]


def _family(label):
    if label in ("N", "X"):
        return None
    root, _, q = label.partition(":")
    pc = ROOTS.get(root)
    if pc is None:
        return None
    if q in MAJ_Q:
        return pc, False
    if q in MIN_Q:
        return pc, True
    return None


def _families(labels):
    return [_family(l) for l in labels]


def estimate_key(argmax_labels):
    counts = {}
    total = 0
    first = None
    for fam in (f for f in argmax_labels if f is not None):
        first = first or fam
        total += 1
        counts[fam] = counts.get(fam, 0) + 1
    if total < 50:
        return None
    best, best_score, best_cov = None, -1e9, -1
    for tonic in range(12):
        for minor in (False, True):
            chords = MINOR_KEY if minor else MAJOR_KEY
            cov = sum(counts.get(((tonic + o) % 12, m), 0) for o, m in chords)
            tonic_frames = counts.get((tonic, minor), 0)
            score = cov + 0.30 * tonic_frames + (0.15 * total if first == (tonic, minor) else 0)
            if score > best_score:
                best, best_score, best_cov = (tonic, minor), score, cov
    return best if best_cov >= 0.6 * total else None


def apply_key_prior(log_probs, labels, strength):
    fams = _families(labels)
    argmax_fams = [fams[i] for i in log_probs.argmax(axis=1)]
    key = estimate_key(argmax_fams)
    if key is None:
        return log_probs
    tonic, minor = key
    diatonic = {((tonic + o) % 12, m) for o, m in (MINOR_KEY if minor else MAJOR_KEY)}
    bonus = np.zeros(len(labels), dtype=np.float64)
    for i, fam in enumerate(fams):
        if fam is not None and fam in diatonic:
            bonus[i] = strength
    return log_probs + bonus
