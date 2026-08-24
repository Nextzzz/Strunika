"""Where did the chords change? Model A vs model B on every YouTube song
the user ever analyzed in the app (Documents/Strunika/analyses), through
the C# product pipeline (overlap, Viterbi, key prior) — the same path
the app uses. Output: a markdown report with per-song agreement and the
time ranges where the two models disagree.

Run:
    .venv/Scripts/python song_diff.py fetch                  # download audio for all analyzed songs
    .venv/Scripts/python song_diff.py compare base self      # models: base | self | guitar2 | hook | any ml/models name
    .venv/Scripts/python song_diff.py compare base self --ens-a self --ens-b guitar2   (optional partners)
"""
import glob
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
SONGS = os.path.normpath(os.path.join(HERE, "..", "datasets", "user_songs"))
ANALYSES = os.path.join(os.environ["USERPROFILE"], "OneDrive", "Документы", "Strunika", "analyses")
REPORTS = os.path.join(os.environ["USERPROFILE"], "OneDrive", "Документы", "Strunika", "reports")
ALIASES = {"base": "btc_large_voca", "self": "btc_self", "guitar2": "btc_guitar2",
           "guitar": "btc_guitar", "hook": "btc_hook", "mix": "btc_mix"}
LINE = re.compile(r"\s+(\d+):(\d+[,.]\d)\s+-\s+(\d+):(\d+[,.]\d)\s+(\S+)")
STEP = 0.1


def analyzed_songs():
    """{video_id: source url} over every saved analysis."""
    songs = {}
    for path in glob.glob(os.path.join(ANALYSES, "*.json")):
        if path.endswith("last.json"):
            continue
        with open(path, encoding="utf-8") as f:
            source = json.load(f).get("Source", "")
        m = re.search(r"(?:v=|youtu\.be/)([\w-]{11})", source)
        if m:
            songs[m.group(1)] = source
    return songs


def fetch():
    from billboard_collect import download
    os.makedirs(SONGS, exist_ok=True)
    songs = analyzed_songs()
    ok = skipped = fail = 0
    for vid in songs:
        out = os.path.join(SONGS, vid + ".m4a")
        if os.path.exists(out):
            skipped += 1
            continue
        status = download(vid, out)
        if status.startswith("ok"):
            ok += 1
        else:
            fail += 1
            print(f"  {vid}: {status[:100]}", flush=True)
        time.sleep(2)
    print(f"user songs: {len(songs)} total, {ok} new, {skipped} cached, {fail} failed")


def analyze(audio, model, ensemble=None):
    """Product-pipeline analysis, cached per (song, model[, ensemble]) so
    old models never need re-running when a new one arrives."""
    cache_dir = os.path.join(SONGS, "analyses")
    os.makedirs(cache_dir, exist_ok=True)
    tag = model + (f"+{ensemble}" if ensemble else "")
    cache = os.path.join(cache_dir, f"{os.path.splitext(os.path.basename(audio))[0]}.{tag}.json")
    if os.path.exists(cache):
        with open(cache, encoding="utf-8") as f:
            data = json.load(f)
        return data["key"], [tuple(s) for s in data["segments"]]

    cmd = ["dotnet", "run", "--project", "src/Strunika.Cli", "--no-build", "--",
           "analyze", audio, f"--neural=ml/models/{model}.onnx", "--ovl"]
    if ensemble:
        cmd.append(f"--ens=ml/models/{ensemble}.onnx")
    out = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True,
                         encoding="utf-8", errors="replace").stdout
    segments, key = [], "?"
    for line in out.splitlines():
        if line.startswith("key:"):
            key = line.split(":", 1)[1].strip()
        m = LINE.match(line)
        if m:
            start = int(m[1]) * 60 + float(m[2].replace(",", "."))
            end = int(m[3]) * 60 + float(m[4].replace(",", "."))
            segments.append((start, end, m[5]))
    if segments:
        with open(cache, "w", encoding="utf-8") as f:
            json.dump({"model": tag, "key": key, "segments": segments,
                       "analyzed": time.strftime("%Y-%m-%d %H:%M")}, f, ensure_ascii=False)
    return key, segments


def simplify(pretty):
    """Same collapse as the app's «Прості акорди»: extensions → triad."""
    if not pretty or pretty == "—":
        return pretty
    root_len = 2 if len(pretty) > 1 and pretty[1] == "#" else 1
    root, suffix = pretty[:root_len], pretty[root_len:]
    if suffix.startswith("dim") or "m7b5" in suffix:
        return root + "dim"
    if suffix.startswith("aug"):
        return root + "aug"
    if suffix.startswith("m") and not suffix.startswith("maj"):
        return root + "m"
    return root


def at(segments, t):
    for s, e, label in segments:
        if s <= t < e:
            return label
    return None


def clock(t):
    return f"{int(t) // 60}:{t % 60:04.1f}"


def compare(model_a, model_b, ens_a=None, ens_b=None):
    songs = analyzed_songs()
    os.makedirs(REPORTS, exist_ok=True)
    name_a = model_a + (f"+{ens_a}" if ens_a else "")
    name_b = model_b + (f"+{ens_b}" if ens_b else "")
    lines = [f"# {name_a} vs {name_b} — {len(songs)} songs the user analyzed", ""]
    totals = []
    for vid, source in sorted(songs.items()):
        audio = os.path.join(SONGS, vid + ".m4a")
        if not os.path.exists(audio):
            lines.append(f"## {vid} — audio missing (run `song_diff.py fetch`)")
            continue
        key_a, seg_a = analyze(audio, ALIASES.get(model_a, model_a), ALIASES.get(ens_a, ens_a) if ens_a else None)
        key_b, seg_b = analyze(audio, ALIASES.get(model_b, model_b), ALIASES.get(ens_b, ens_b) if ens_b else None)
        duration = max((seg_a or [(0, 0, "")])[-1][1], (seg_b or [(0, 0, "")])[-1][1])
        same = same_triad = total = 0
        diffs = []  # [start, end, a, b]
        t = 0.0
        while t < duration:
            a, b = at(seg_a, t), at(seg_b, t)
            if a is not None and b is not None:
                total += 1
                if a == b:
                    same += 1
                    same_triad += 1
                else:
                    if simplify(a) == simplify(b):
                        same_triad += 1
                    if diffs and diffs[-1][2] == a and diffs[-1][3] == b and abs(diffs[-1][1] - t) < 0.2:
                        diffs[-1][1] = t + STEP
                    else:
                        diffs.append([t, t + STEP, a, b])
            t += STEP
        agreement = same / max(total, 1)
        triad_agreement = same_triad / max(total, 1)
        totals.append((agreement, triad_agreement))
        lines.append(f"## {source}")
        lines.append(f"key: {name_a} **{key_a}** / {name_b} **{key_b}** — agreement **{agreement:.1%}**, "
                     f"triads **{triad_agreement:.1%}** over {total / 10:.0f}s")
        long_diffs = [d for d in diffs if d[1] - d[0] >= 1.0]
        for s, e, a, b in long_diffs:
            flavor = "  _(септима)_" if simplify(a) == simplify(b) else ""
            lines.append(f"- {clock(s)}–{clock(e)}  {name_a}: `{a}`  →  {name_b}: `{b}`{flavor}")
        if not long_diffs:
            lines.append("- (no disagreement ≥ 1 s)")
        lines.append("")
        real = sum(1 for d in long_diffs if simplify(d[2]) != simplify(d[3]))
        print(f"{vid}: {agreement:.1%} agreement ({triad_agreement:.1%} triads), "
              f"{len(long_diffs)} spans ≥1s, {real} real chord changes", flush=True)

    if totals:
        lines.insert(2, f"Mean agreement: **{sum(a for a, _ in totals) / len(totals):.1%}** exact, "
                        f"**{sum(b for _, b in totals) / len(totals):.1%}** at triad level, over {len(totals)} songs")
        lines.insert(3, "")
    report = os.path.join(REPORTS, f"song_diff_{name_a}_vs_{name_b}_{time.strftime('%Y-%m-%d')}.md")
    with open(report, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print(f"report -> {report}")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "fetch":
        fetch()
    elif len(sys.argv) >= 4 and sys.argv[1] == "compare":
        args = sys.argv[2:]
        ens_a = args[args.index("--ens-a") + 1] if "--ens-a" in args else None
        ens_b = args[args.index("--ens-b") + 1] if "--ens-b" in args else None
        compare(args[0], args[1], ens_a, ens_b)
    else:
        print(__doc__)
