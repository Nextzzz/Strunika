# Strunika.Mobile — handoff notes

MAUI app for iPhone (product target) with a Windows head for fast UI
iteration on a PC without a phone. The heavy lifting (DSP, chord
recognition) lives in the shared, platform-neutral projects:

- **Strunika.Core** — DSP, tuner (YIN), streaming chord detection, no deps.
- **Strunika.Neural** — ONNX chord recognition (CQT + BTC + Viterbi + key prior).

Both are referenced directly; nothing Windows-specific leaks into them.

Design system, product decisions and the Pro/free split: `.claude/skills/strunika-ui/SKILL.md`.
Implementation plan (milestones M0–M8): `PLAN.md`. Mockups:
https://claude.ai/code/artifact/776fc75b-7d00-4987-a13f-3d07d4954c22

## Model strategy (product decision, Aug 2026)

| Where | Model | File | Why |
|-------|-------|------|-----|
| **Live play** | **base** | `btc_large_voca` | steadiest generalist for real-time strumming |
| **Song analysis** | **self** | `btc_self` | best *legally clean* model (+0.6pp over base on a 808-song held-out set); trained on our own pseudo-labels + GuitarSet (CC-BY) + Billboard (CC0) |
| mic/solo option | guitar2 | `btc_guitar2` | selectable in the live picker (expert settings) |

**Do NOT ship `btc_full`.** It is the strongest model (+2.3pp majmin, best
major/minor accuracy) but was fine-tuned on the HookTheory dataset, which is
CC BY-NC-SA. It stays a desktop-only research prototype until a commercial
licence from HookTheory arrives (a request has been sent). It is intentionally
not in this project's MauiAssets.

The `key prior` (diatonic second Viterbi pass, strength 0.5) is on by default
inside `NeuralChordRecognizer` — it is the surgical lever for the residual
major/minor confusion (measured: ensembles do NOT help, key prior does).

## Structure (after M0)

```
App.xaml(.cs)        theme resources; first launch → WelcomePage, else RootPage
Pages/               RootPage (4 tabs + PillTabBar), WelcomePage, PaywallSheet
Views/               TunerView, LiveView, LibraryView, SettingsView (tab content)
Controls/            PillTabBar, IconView + Icons (stroke icons drawn with Maui.Graphics),
                     WaveMark (logo wave), Segmented, LockBadge
ViewModels/          TunerViewModel, LiveViewModel, SettingsViewModel (CommunityToolkit.Mvvm)
Services/            IMicrophoneSource (+ Platforms/*), ModelStore, AppSettings, Haptics, Motion
Localization/        Loc (runtime language switch) + {loc:Str Key} markup; Resources/Strings/*.resx (en, uk)
Theme/               Tokens (brand colours, source of truth) + {t:Theme Key} markup extension
Pro/                 Feature, IProGate, DevProGate
Resources/Fonts      Vollkorn SemiBold/Bold (OFL) as "Display"/"DisplayBold"; UI text = system font
```

Conventions that bit us on Windows (keep them):

- Themed colours: `{t:Theme TextSec}` / `IconView.ThemeKey` — never raw hex, and never
  an `AppThemeBinding` object inside a ResourceDictionary (the XAML loader rejects it).
- `Span` does not inherit `FontFamily` from the Label style — set it on the span.
- Do not replace `Window.Page` at runtime (the WinUI window collapses); use
  `Navigation.InsertPageBefore` + `PopAsync`, as WelcomePage does.
- WinUI `ToggleSwitch` reserves ~154 px for captions; `MauiProgram` maps `MinWidth = 0`.
- The dev window is pinned to the top of the screen (`App.CreateWindow`); a
  window taller than the display looks like clipped layout.
- `STRUNIKA_RESET=1` environment variable wipes preferences (re-test first launch).
- Unhandled exceptions are written to `Documents\Strunika\logs\strunika-<date>.log`.

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
