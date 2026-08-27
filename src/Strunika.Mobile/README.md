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

## M2 notes (song library)

- **Windows head decodes through NAudio** (`Strunika.Media.AudioLoader`), with an **ffmpeg fallback** (`%LocalAppData%\Strunika\tools\ffmpeg.exe` or PATH) because MediaFoundation opens some YouTube DASH m4a files and then yields zero samples (e.g. `BdzZ_9QHQNQ`, AAC-LC, fine in ffmpeg); iOS uses `Platforms/iOS/IosAudioDecoder`
  (AVAudioFile → mono → `Strunika.Core.Audio.Resampler`). The iOS decoder was written against the binding docs on the
  Windows head and is **unverified until the first device build** — check `AVAudioPcmBuffer.FloatChannelData` layout and
  `AVAudioFile.ReadIntoBuffer` first.
- WinUI `TextBox` paints its own border; `MauiProgram` maps `EntryHandler` to clear it (theme-resource brushes).
  Setting `TextControlBorderThemeThicknessFocused` there crashed navigation with `COMException 0x8000FFFF` — brushes only.
- A bare `GraphicsView` (`IconView`) does not receive tap gestures on Windows; wrap tappable icons in a `Border`.
- YouTube: plain YoutubeExplode, audio to `CacheDirectory/yt`, deleted after decoding. When it breaks, the card shows
  "YouTube is temporarily unavailable" and can be retried by tapping it. **Keep the package current**: 6.6.1 threw
  `VideoUnavailableException` from `GetManifestAsync` for most licensed music videos (metadata still worked), 6.6.2
  resolves them. Only MP4/AAC audio streams are accepted (WebM/Opus is undecodable on both platforms). If YoutubeExplode
  breaks for good, the escape hatch is our own Innertube `player` request with the client yt-dlp uses without PO tokens
  (`android vr` as of yt-dlp 2026.07) — see `Strunika.Media/YoutubeAudioService` for the desktop tiers.
- Free quota counters live in `SecureStorage`; the unpackaged Windows head falls back to `Preferences` when the
  secure store throws.
- Test harness: `shot.ps1` gained `~play:<wav>` / `~mute` (in-process SoundPlayer), `~keys:<SendKeys>`, `~click:fx:fy`
  (no screenshot) and `~wait:ms` steps — file dialogs are driven with `~click` on the file-name box + `~keys:path{ENTER}`.
- **Switch off-state colours**: `ToggleSwitchFillOff`/`StrokeOff` are *Brushes*, but `…PointerOver`/`…Pressed`
  are plain *Colors* — the template's `LinearColorKeyFrame` animates `(Shape.Fill).(SolidColorBrush.Color)` of
  `OuterBorder` with them, so a Brush under those keys renders as nothing (the "track vanishes on hover" bug).
  They are written into the control's own `Resources` after `Loaded` (inserting in the handler mapping throws
  `0x800F0902`) and mirrored in the application `ThemeDictionaries` in `Platforms/Windows/App.xaml`. The knob is one cream colour in both states (MAUI's On/Off visual states did not switch `ThumbColor`
  reliably); iOS paints the off track with a rounded `BackgroundColor` on the UISwitch.
- `LaunchPage` is the first page on every start (models unpacked, then Welcome/Root); the Windows head holds it
  for 3 s so it can be seen — on iPhone it shows only as long as the work takes (≥0.5 s).
- Buttons have `UseSystemFocusVisuals=false` on Windows: WinUI drew a dotted focus rectangle on the first button
  after every page navigation.
- Dev launches: `STRUNIKA_RESET=1` wipes preferences (harness `-Reset`), `STRUNIKA_WELCOME=1` only forces the
  welcome screen (harness only; not set for Visual Studio — F5 behaves like a real install: Welcome on the first
  launch or when "Skip the welcome screen" is off).
- Paywall: from a locked control it is a modal sheet; from Settings → "Learn more" it is *pushed* (slides in from
  the right, swipe-back on iPhone) with `HasBackButton=False` so the WinUI title-bar arrow stays hidden.
- Shadows: put them on a rounded `Border`, not on a `Button` (WinUI casts the button shadow from its rectangular
  bounds), and give the parent layout room on every side (`Margin="-24,-64,-24,-24" Padding="24,64,24,24"`) — layouts clip to their bounds on
  Windows, which cuts a glow into a hard-edged band (Welcome's Start button).
- Fonts: Vollkorn SemiBold/Bold (`Display`/`DisplayBold`), OFL. The welcome greeting is the user's SVG lettering (`Resources/Images/hello_uk.svg`, `hello_en.svg`), not a font.

