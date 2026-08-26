# Strunika.Mobile — handoff notes

MAUI app for iPhone (product target) with a Windows head for fast UI
iteration on a PC without a phone. The heavy lifting (DSP, chord
recognition) lives in the shared, platform-neutral projects:

- **Strunika.Core** — DSP, tuner (YIN), streaming chord detection, no deps.
- **Strunika.Neural** — ONNX chord recognition (CQT + BTC + Viterbi + key prior).

Both are referenced directly; nothing Windows-specific leaks into them.

## Model strategy (product decision, Aug 2026)

| Where | Model | File | Why |
|-------|-------|------|-----|
| **Live play** | **base** | `btc_large_voca` | steadiest generalist for real-time strumming |
| **Song analysis** | **self** | `btc_self` | best *legally clean* model (+0.6pp over base on a 808-song held-out set); trained on our own pseudo-labels + GuitarSet (CC-BY) + Billboard (CC0) |
| mic/solo option | guitar2 | `btc_guitar2` | selectable in the live picker |

**Do NOT ship `btc_full`.** It is the strongest model (+2.3pp majmin, best
major/minor accuracy) but was fine-tuned on the HookTheory dataset, which is
CC BY-NC-SA. It stays a desktop-only research prototype until a commercial
licence from HookTheory arrives (a request has been sent). It is intentionally
not in this project's MauiAssets.

The `key prior` (diatonic second Viterbi pass, strength 0.5) is on by default
inside `NeuralChordRecognizer` — it is the surgical lever for the residual
major/minor confusion (measured: ensembles do NOT help, key prior does).

## What exists

- **Тюнер** (`MainPage`) — YIN pitch, smoothed needle with hold, ±8-cent
  green zone, Guitar-Tuna feel.
- **Наживо** (`LivePage`) — DSP provisional guess (grey) + neural confirm
  (big), model picker (base default), «Прості акорди» (triads, on) and
  «Уточнювати» (same-root history revision) options.
- `IMicrophoneSource` — 44100 Hz mono float stream:
  `Platforms/iOS/IosMicrophoneSource` (AVAudioEngine + AVAudioConverter),
  `Platforms/Windows/WindowsMicrophoneSource` (NAudio, dev head only).
- `Services/ModelStore` — unpacks bundled ONNX models to the cache dir on
  first use (ONNX needs real file paths, not asset streams).

## What's left (rough)

- **Пісня** page: file / recording input → `NeuralChordRecognizer`
  (use **self** here) → chord timeline with a synced player, like the WPF
  Song tab. `ChordTimeline.SnapToBeats` and `ChordLabels.Simplify/Transpose`
  already exist in the shared projects.
- Chord diagrams (fretboard shapes) for the shown chords.
- Polish: blur/glass panels via `UIVisualEffectView` from platform code,
  haptics (`HapticFeedback`), SF-symbol icons.

## Build & deploy (no Mac)

- Toolchain: **Visual Studio 2022** (net9 targets; Hot Restart was removed
  from VS 2026). SDK 9 pinned via `global.json` in this folder.
- iOS on-device: **Hot Restart** over USB — needs a **paid** Apple Developer
  account ($99/yr; free tier is Xcode/Mac-only) and iTunes installed.
- Windows head: `dotnet build -f net9.0-windows10.0.19041.0` and F5 in VS.
- App Store distribution later: GitHub Actions macOS runners → TestFlight.

Native rewrite escape hatch: if the UI ever needs true SwiftUI polish, the
Core/Neural math can be exposed via .NET NativeAOT as a static lib and called
from Swift — but that needs a Mac and is not planned.
