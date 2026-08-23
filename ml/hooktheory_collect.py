"""Download audio for a HookTheory sample (direct video ids from
sample.csv — the dataset pins the exact videos its alignments refer to).
Idempotent: existing files are skipped.

Multi-day mode (env):
    HOOK_SUBSET=train     work in datasets/hooktheory/train (or valid)
    MAX_NEW=350           stop after this many successful downloads
                          (pre-emptive rest before YouTube's bot-check)
A run of 5 consecutive bot-check failures ("cookies"/"not a bot") stops
the run early and prints BOT-CHECK so a driver can back off.

Run:
    .venv/Scripts/python hooktheory_collect.py [limit_rows]
"""
import csv
import os
import random
import sys
import time

from billboard_collect import download

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(
    HERE, "..", "datasets", "hooktheory", os.environ.get("HOOK_SUBSET", "")))
AUDIO_DIR = os.path.join(ROOT, "audio")
MAX_NEW = int(os.environ.get("MAX_NEW", "0")) or 10 ** 9
BOT_MARKERS = ("cookies", "not a bot", "Sign in to confirm", "VPN or a proxy")


def main(limit):
    os.makedirs(AUDIO_DIR, exist_ok=True)
    with open(os.path.join(ROOT, "sample.csv"), encoding="utf-8") as f:
        rows = list(csv.DictReader(f))[:limit]

    # Dead videos fail identically every round; after 3 strikes stop
    # retrying them (the log keeps one line per attempt for the record).
    fails_path = os.path.join(ROOT, "failed_ids.txt")
    fail_counts = {}
    if os.path.exists(fails_path):
        with open(fails_path, encoding="utf-8") as f:
            for line in f:
                fail_counts[line.strip()] = fail_counts.get(line.strip(), 0) + 1
    fails_log = open(fails_path, "a", encoding="utf-8")

    ok = fail = skipped = 0
    bot_streak = 0
    for n, row in enumerate(rows):
        out_path = os.path.join(AUDIO_DIR, row["id"] + ".m4a")
        if os.path.exists(out_path):
            skipped += 1
            continue
        if fail_counts.get(row["id"], 0) >= 3:
            skipped += 1
            continue
        try:
            status = download(row["yt_id"], out_path)
        except Exception as ex:  # a stalled yt-dlp must not kill a multi-day run
            status = f"fail: {type(ex).__name__}"
            if os.path.exists(out_path):
                os.remove(out_path)  # partial file
        if status.startswith("ok"):
            ok += 1
            bot_streak = 0
        else:
            fail += 1
            is_bot = any(m in status for m in BOT_MARKERS)
            bot_streak = bot_streak + 1 if is_bot else 0
            if not is_bot:  # bot-check bounces are transient, don't strike them
                fails_log.write(row["id"] + "\n")
                fails_log.flush()
            print(f"  {row['artist']}/{row['song']}: {status}", flush=True)
            if bot_streak >= 5:
                print(f"BOT-CHECK after {ok} new downloads — stopping early", flush=True)
                break
        if ok >= MAX_NEW:
            print(f"BATCH LIMIT {MAX_NEW} reached", flush=True)
            break
        if (n + 1) % 25 == 0:
            print(f"{n + 1}/{len(rows)}: ok={ok} fail={fail} skipped={skipped}",
                  flush=True)
        time.sleep(random.uniform(2.0, 4.0))
    remaining = sum(1 for r in rows if not os.path.exists(os.path.join(AUDIO_DIR, r["id"] + ".m4a")))
    print(f"COLLECTION DONE: ok={ok} fail={fail} skipped={skipped} remaining={remaining}",
          flush=True)


if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 10 ** 9)
