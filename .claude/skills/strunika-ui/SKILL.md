---
name: strunika-ui
description: Strunika mobile (.NET MAUI, iPhone-first) design system and UI rules — brand palette tokens (Gold/Copper/Cream on dark & light bases), iOS-native look-and-feel achieved in MAUI, motion/haptics conventions, icon assets, and the "unique, not a ChordAI clone" rule. Use whenever creating or reviewing any Strunika.Mobile page, style, control, animation, theme, or asset.
---

# Strunika UI — design system & rules

Strunika (from Ukrainian «струна», a string) is a guitarist's companion:
tuner, live chord recognition while you play, chord recognition for a file /
recording / YouTube link. Product target is **iPhone**; a **Windows head** of
the same MAUI project is used for day-to-day UI iteration on a PC. Everything
must look native-iOS on the phone and merely *work* on Windows.

Companion skills: `apple-hig-designer` (how iOS should look/behave),
`apple-design` (review against HIG), `maui-*` plugin skills (how to do it in
MAUI: `maui-theming`, `maui-animations`, `maui-safe-area`, `maui-gestures`,
`maui-graphics-drawing`, `maui-custom-handlers`, `maui-platform-invoke`,
`maui-performance`, `ux-mobile`).

## 1. Brand tokens (source of truth)

| Token | Hex | Role |
|-------|-----|------|
| `Gold` (Accent1) | `#D9AC4C` | primary accent: active tab, the detected chord, in-tune state, primary buttons, waveform |
| `Copper` (Accent2) | `#AE6F32` | secondary accent: guitar body, secondary buttons, pressed/hover states, sliders track |
| `Cream` (Accent3) | `#E9D3A2` | primary text on dark; soft surfaces/highlights on light |
| `DarkBase` | `#16110B` | dark-theme background (warm near-black, *not* neutral grey) |
| `LightBase` | `#FBF3E3` | light-theme background (warm parchment) |

Derived scale (define in `Resources/Styles/Colors.xaml`; use `AppThemeBinding`
via named `Surface*`/`Text*` keys, never raw hex in pages):

```
Dark theme                          Light theme
Bg        #16110B  (DarkBase)       Bg        #FBF3E3  (LightBase)
Surface1  #211A10  (cards)          Surface1  #F3E8CF
Surface2  #2C2316  (elevated)       Surface2  #E9D3A2  (Cream)
Separator #3A2E1C                   Separator #D9C6A0
TextPri   #E9D3A2  (Cream)          TextPri   #16110B  (DarkBase)
TextSec   #A48F66                   TextSec   #66522F
Accent    #D9AC4C  (Gold)           Accent    #AE6F32  (Copper: strokes/icons/large text)
AccentTxt #D9AC4C                   AccentTxt #8A5424  (small copper text on cream — #AE6F32 is only 3.4:1)
Fill      #D9AC4C / on-fill #16110B  Fill      #D9AC4C / on-fill #16110B  (selected chips, current chord bead, primary buttons — Gold with dark text in BOTH themes; cream-on-copper is 3.7:1, fails)
Dim       #7A6543                   Dim       #A08A5C  (past chords — decorative only)
Accent2   #AE6F32                   Accent2   #D9AC4C
```

Semantic states (tuner, confidence): in-tune / confirmed = Gold glow; flat/sharp
/ provisional = Copper or desaturated Cream at 60 % opacity; errors only use a
muted red (`#C4533A`) — never a saturated system red on this warm palette.
Verify contrast ≥ 4.5:1 for text (`apple-design` → `color.md`, `dark-mode.md`).

## 2. Look & feel — iOS-native in MAUI

- **Typography**: system font (SF Pro on iOS, Segoe on Windows). iOS text
  styles: LargeTitle 34/bold, Title1 28, Title2 22, Headline 17/semibold,
  Body 17, Subhead 15, Footnote 13, Caption 12. Chord names and tuner note are
  *display* elements: 64–96 pt, weight Bold/Heavy, tight tracking. Support
  Dynamic Type (`FontAutoScalingEnabled="True"`).
- **Layout**: 8-pt grid, 16-pt page margins, 44-pt minimum tap targets, safe
  areas respected (`maui-safe-area`). Cards: corner radius 16–20, no hard
  borders on dark — separate by surface tone; on light a 1-px `Separator`.
- **Navigation**: Shell tab bar (SF Symbols on iOS via `Platforms/iOS`
  handler / `FontImageSource`), large titles on root pages, sheets for
  secondary actions (model picker, options) — bottom sheet with grabber, not
  a full-screen modal.
- **Materials**: translucent blur bars/panels on iOS through
  `UIVisualEffectView` in platform code (`maui-platform-invoke`); on Windows
  degrade to a solid `Surface1` at 92 % opacity. Never ship a look that
  depends on blur.
- **Motion**: purposeful, short. Springs for state changes
  (`Easing.SpringOut`, 250–400 ms), `CubicOut` for enters, `CubicIn` for
  exits. The tuner needle and live chord label animate continuously (≤ 16 ms
  per frame budget — use `GraphicsView` + `Invalidate()`, not per-frame
  `TranslateTo`). Honor Reduce Motion (`UIAccessibility.IsReduceMotionEnabled`).
- **Haptics**: `HapticFeedback.Perform(Click)` when the tuner locks in tune,
  `LongPress` when a new chord is confirmed live; no haptics on Windows.
- **Themes**: dark and light are first-class; follow the system by default,
  with an override in settings. Test every screen in both.
- **Windows head**: acceptable to look "iOS on Windows"; do not add
  WinUI-specific chrome. Platform-specific code lives under `Platforms/`, the
  XAML stays shared.

## 3. Uniqueness rule (ChordAI is the *functional* reference only)

Feature parity with ChordAI (chords from song / YouTube / live, tuner) is the
goal; the *interface must not be recognisable as ChordAI*. Do not reproduce
its layouts, iconography, colour scheme, naming, onboarding copy or
screen structure. Design from Strunika's own world instead: strings, wood
and brass, the waveform-through-the-guitar logo motif, warm parchment/ink
contrast. When in doubt, ask "would a ChordAI user say this is a re-skin?" —
if yes, change the structure, not only the colours.

## 4. Assets

- App icon: `strunika_guitar_bg.svg` (copper guitar + gold waveform on dark
  radial gradient). For iOS the icon must be a **full-bleed square with no
  transparency** — iOS applies its own superellipse mask; strip the baked-in
  rounded rect before using it as `MauiIcon`.
- In-app logo: `strunika_guitar.svg` (no background) — crop its viewBox to
  the artwork before use so it scales predictably; use as splash/onboarding
  mark and empty-state illustration, tinted per theme.
- Source files live in `C:\Users\taras\OneDrive\Рабочий стол\icons\`; copies
  belong in `src/Strunika.Mobile/Resources/AppIcon` and `Resources/Images`.

## 5. Decisions log (settled in the design interview, 2026-08-26)

🔒 = Pro feature. Treat every line as decided; anything not here is open — ask.

**Platforms & policy**
- iPhone-only (`UIDeviceFamily` = 1), iOS 16+, portrait only. Windows head for dev. Android "maybe later": every iOS-specific piece has a neutral fallback, `Platforms/Android` kept but not built.
- No third-party analytics or crash SDK — "we collect no data". Apple's built-in crash reports only.
- Localisation uk + en via `.resx` (`Strings.resx`, `Strings.uk.resx`); no hard-coded UI strings.
- First launch: ONE screen — animated wave logo, language (with flag icons: UA / GB, drawn as vector, never emoji) + theme pickers pre-filled from the system, "Почати" → lands on the Tuner with a "play any string" hint. Mic permission requested in context (first "Listen"), with a one-line pre-prompt.

**Navigation**: 4 tabs — Тюнер · Наживо · Пісні · Налаштування. NO icons in the nav bars of root pages (a gear read as a "sun" in mockups); theme + language live inside Settings.
- **Tab bar = custom `PillTabBar` control**, not the native Shell TabBar (`Shell.TabBarIsVisible=False`, tabs switched via `Shell.Current.GoToAsync`): a floating capsule (14 pt side margins, 22 pt above the home indicator, 66 pt tall, radius 33, `Surface1` + 1-px separator + soft shadow) holding 4 items; the active item sits in an **oval Gold selector** (52 pt tall, dark icon+label, glow). The selector is **draggable**: a `PanGestureRecognizer` slides it left/right with the finger, it snaps to the nearest item on release (spring, ~300 ms) and switches the tab; tap also works; light haptic on snap. **Press feedback on the whole capsule**: on touch-down the bar GROWS to ~1.04 (`ScaleTo`, ~120 ms, `CubicOut`); on release it springs back DOWN to 1.0 with a small undershoot (~0.99 → 1.0, `Easing.SpringOut`, ~350 ms). A plain tap therefore reads as a "bounce"; a drag keeps the bar enlarged until the finger lifts. Honor Reduce Motion (no overshoot, 150 ms fade instead). Root pages keep ≥104 pt bottom padding so content clears the floating bar.
- Theme switch is instant (a snapshot cross-fade was tried on 2026-08-26 and read as "staged" on Windows — the user asked to revert it).
- The capsule casts **no shadow** (user decision 2026-08-26). Instead a bottom gradient zone (`BottomFade`, ~100 pt: a few points above the bar + the bar + the gap below) fades transparent → Bg top-to-bottom, so scrolling content dims and slips under the bar.
- Theme picker (first launch + Settings) shows three vector icons: half-filled circle = Системна, moon = Темна, sun = Світла.

**Tuner** (`MainPage` → rename `TunerPage`)
- Chromatic + tuning presets with 6 "peg" pips: Standard (free), Drop D, Half-step down, Full-step down, DADGAD, Open G, Open D, Ukulele GCEA, Bass EADG (🔒). Auto string detection, tap a peg to lock it.
- Smoothing: the raw YIN readout is too jittery (user feedback 2026-08-26 — "його трясе"); M1 must add a median/EMA stage and a slew limit so the string moves calmly, plus hold on decay.
- Indicator = **the string**: a horizontal line that sags/tightens with cents offset and snaps straight + gold flash (+ `HapticFeedback.Click`) when in tune. No audible beep.
- A4 reference 430–450 Hz 🔒 (Settings). NO metronome on the tuner tab (user decision 2026-08-26); the metronome lives on Live and on the Song page ("клік у темпі пісні"). Peg labels: E₂ A D G B E₄ — octave digits only where the tuning has two strings of the same name.

**Live** (`LivePage`)
- Hero chord (display serif, smaller than on the Song page — ring ≈172 pt) + guitar diagram beside it (shared `ChordHero`); confidence ring (grey while DSP guess → fills gold on neural confirm).
- **The live session is a recording.** Below the hero: the same `StringTimeline` as the Song page — the real audio waveform of the take with chord beads aligned to their positions; playhead = "now", the right half is empty (dotted baseline). Dragging the track or tapping a chord moves the playhead; NO audio while dragging, playback starts from there on release (live listening pauses while reviewing; "Слухати" resumes/starts a new take). No decorative reactive wave.
- "Прості акорди" is ON by default; turning it OFF (full vocabulary) is 🔒 — the toggle carries a lock badge.
- Recognition modes shown with human names + hint, only when "Експертні налаштування" is on (release default off → auto model choice: live=base, guitar solo=guitar2, songs=self).
- Metronome available here too: clicks are short high-frequency ticks and their frames are masked out of the analysis; "headphones are more accurate" hint on first use. No iOS voice-processing/echo cancellation (it damages the guitar signal).

**Songs** (`LibraryPage` → `SongPage`)
- Library cards: thumbnail (YouTube) / source glyph, title, artist, key, tempo, duration, date. Search, sort (date/title/key), favourites — free. Folders/setlists 🔒. Swipe to delete. Empty state = wave logo + "Додай першу пісню".
- "+" in the nav bar → bottom sheet: Файл (Files/iCloud: mp3/m4a/wav) · Запис (mic, wave + timer, saved as m4a in app data) · YouTube (URL field; auto-fill from clipboard when it holds a YouTube URL). iOS Share Extension → v1.1 (needs macOS CI).
- YouTube: on-device extraction (YoutubeExplode) behind `ISongSource`, "like ChordAI": never store/export the audio, graceful "YouTube тимчасово недоступний" state, never crash. Playback via the official IFrame embed in a WebView (position polled ~100 ms); "play extracted audio stream" exists only as an expert option.
- **Next chord preview** (user request 2026-08-26): while a song, recording or take plays, show the *next* chord with its diagram beside the current one, visibly de-emphasised (smaller, TextSec, ~60 % opacity); when it arrives it animates into the hero position (slide + scale, ~250 ms) and the following chord takes the preview slot. Same on Live playback.
- Song page layout: top half = ChordHero (huge chord + diagram, capo-aware shapes); bottom third = **string timeline** (chord segments as beads on a string, wave below, fixed centre playhead, ribbon scrolls; segment width ∝ duration, beat ticks) + transport (play, A–B, speed). YouTube player collapsed to a thumbnail strip above the string, expands on tap. Analysis runs in background with progress on the card and cancel.
- Free: simple chords (`Simplify`), "click at song tempo" metronome, beat snapping (on by default). 🔒: transpose/capo (`Transpose`, diagram follows capo), speed 0.5×–1.25× pitch-preserved (AVPlayer rate / YouTube `playbackRate`), A–B loop, export TXT / PDF / XLSX (MiniExcel; row = segment: start, end, bar, chord) / share sheet, **Chord Editor**.
- **Chord Editor** (name is literally "Chord Editor" in both languages; hint "редактор акордів"). **Free for the first 3 songs** (lifetime counter in Keychain, shown as "Безкоштовно · пісня 2 з 3"), then 🔒. Must feel first-class: own pushed screen (Скасувати / Готово), selected segment with edge handles, root · quality · bass pill rows, action row (split / merge / insert / delete), undo/redo, loop-audition toggle + play. tap a segment → root · quality · bass wheel; drag segment edges snapping to the beat grid (long-press = free); split / merge / insert / delete; nudge beats; undo/redo; loop-audition the segment. Edited songs get a "правлено вручну" badge; re-analysis asks before overwriting; export uses edited chords.
- Chord diagrams: guitar only in v1, own JSON shape DB (24 triads + 7ths/sus, 2–4 positions each), left-handed mirror free (Settings), capo-aware.

**Pro / monetisation**
- Auto-renewable subscription, monthly + yearly, **no free trial**. App Store Offer Codes for gifting (button "Ввести код" → `presentCodeRedemptionSheet`); TestFlight before release.
- Free song analyses: **20 lifetime, then 1 per calendar day** (device local time). Re-analysing the same song doesn't count; already-analysed songs stay open forever. Counter + last-free-date in Keychain (`SecureStorage`).
- `IProGate` with sources: StoreKit entitlement (offer codes arrive here) OR dev override (Debug/TestFlight only; always on in the Windows head — no StoreKit there). UI asks only `Pro.Has(Feature.X)`.
- Locked controls stay visible with a small gold lock badge; tap → compact half-sheet (feature name, price, CTA, "Усі можливості Pro"). Full paywall (wave hero, feature list, Year highlighted with "−N %", Month, Restore, Redeem, Terms) from Settings and after the song limit. Prices always from StoreKit, never hard-coded.

**Settings** (4th tab; iOS inset-grouped list): Вигляд (theme Системна/Темна/Світла, language with flag, left-handed) · Тюнер (A4 🔒, default tuning) · Розпізнавання (beat snapping, Експертні налаштування toggle → model pickers, key prior, YouTube audio-stream playback) · Strunika Pro card (feature line, "Дізнатися більше", restore purchases, redeem code) · Про застосунок (version, privacy + licences: display font OFL, models, YoutubeExplode).

**Design**
- Typography: SF Pro for all UI; a warm display serif for chord names, tuner note and large titles — mockups use **Young Serif** (OFL; Fraunces was dropped as an over-used AI default). Verify ♭/♯/m7 legibility on device before locking; diagrams stay SF.
- Signature element: the wave-string (from the logo) on the first-launch screen; on Tuner it is the sagging string; on Live/Song it is the real audio waveform under the chord beads. Mockups: https://claude.ai/code/artifact/776fc75b-7d00-4987-a13f-3d07d4954c22
- Storage: SQLite (sqlite-net-pcl), chord timeline as JSON per song; song metadata from YouTube (title/author/thumbnail) or ID3/filename.
- Light theme: Copper for text/icons, Gold for fills/glows only (contrast).
