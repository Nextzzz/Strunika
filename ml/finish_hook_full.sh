#!/usr/bin/env bash
# One-shot finisher once the TRAIN collection is done:
# window packs (parallel) -> bundle -> zip. ~2-3 h CPU.
set -eu
cd "$(dirname "$0")"
echo "=== building packs (HOOK_SUBSET=train, f16) ==="
HOOK_SUBSET=train WORKERS=${WORKERS:-4} ./.venv/Scripts/python hooktheory_train_prep.py
echo "=== assembling bundle ==="
cp finetune_hook.py bundle_hook_train/
ls -la bundle_hook_train/data/
echo "=== zipping ==="
./.venv/Scripts/python -c "import shutil; shutil.make_archive('hook_full_gpu_bundle', 'zip', 'bundle_hook_train'); print('zipped')"
ls -la hook_full_gpu_bundle.zip
