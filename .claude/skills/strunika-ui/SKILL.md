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
AccentTxt #D9AC4C                   AccentTxt #AE6F32  (the guitar colour, same as Accent: "Pro" and the string beside it are one colour by rule; small copper text on cream is 3.4:1 — keep such text ≥ 14 pt bold or use TextSec)
Fill      #D9AC4C / on-fill #16110B  Fill      #AE6F32 / on-fill #FFF8EC  (selected chips, current chord bead, primary buttons — the GUITAR colour in each theme: Gold+ink on dark, Copper+cream on light; rule of 2026-09-02, replaces the old gold-in-both)
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

- App icon: `Resources/AppIcon/appicon.svg` — **Gold** guitar (`#D9AC4C`) + **Cream** waveform (`#F6E6BF`) with dark keylines on the dark radial base and a soft gold halo; the artwork sits at `scale(1.15)` (≈65 pt side margins on the 1024 canvas). Copper on the dark base read as mud at icon size and the old `scale(0.92)` left a strip a third of the height tall (2026-09-02). Resizetizer caches renders: after editing the SVG delete `obj/**/resizetizer` or the old icon ships. Formerly `strunika_guitar_bg.svg` (copper guitar + gold waveform on dark
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
- Smoothing (implemented in M1): attack detector (RMS jump > 3× envelope) → ignore 160 ms of the pluck, then re-seed; YIN clarity ≥ 0.8 gate; median of 5 → EMA (0.6/0.4) → slew 9 ¢ per 80 ms tick; in-tune = |ema| ≤ 6 ¢ for 2 ticks; clarity hysteresis (0.8 to appear, 0.6 to stay); 1.5 s hold when the pitch is lost. **Mute detector**: a damped string loses >18 dB almost at once (peak 10 ms RMS < 0.12× the pre-drop level for 3 consecutive 46 ms chunks ≈ 140 ms, only ≥300 ms after the attack); a freely decaying note never does. That, or real silence (level < max(0.001, 1.5× adaptive noise floor)), clears the reading at once. **Bias = keep the sounding string** (user decision 2026-08-26): a muted string may linger ~200 ms, but a ringing one must never vanish early — do NOT gate on level alone (a 4×-noise gate cut live notes on a real guitar), do not shorten the hold below ~1.5 s. **The string is chosen once per pluck** (median of the first 3 settled frames after an attack) and never changes while the note decays — user decision 2026-08-26. Pitch comes from `Strunika.Core.Analysis.TunerEngine` (unit-tested): YIN + sub-harmonic evidence check (energy at f/2 and its odd multiples, relative to the loudest partial) so the weak fundamental of the low E is not mistaken for E3/E4; the string is the nearest by **real pitch** (fret 3 on low E = G2 → A string, fret 2 → E string), cents fold octaves only for the readout. Flat tunings spell notes with ♭.
- Readout = **points**, not cents: 10 points = one fret (100 ¢); shown as sign + number only ("+3", "−2", "0"), large (52 pt) under the note, gold when in tune. No verdict words, no hints on the tuner screen.
- A string held in tune for 1.5 s is **marked tuned** (peg filled Gold). When the last peg is done: pegs bounce in a wave and the string pulses gold three times — the microphone keeps listening (user decision); only when the user stops does the idle message become "Все в строї — поїхали!" / "All set — let's play!". Idle message otherwise: "Давай затюнемо!" / "Let's tune!" (centred, in place of the note). A gold **"Почати знову" / "Start again"** chip (one wording in each language) sits top-right in the title row once ≥1 string is tuned; when idle with all tuned the main button reads the same and resets + starts listening. A tuned peg that is being tuned again still shows the active state (dark ring + scale 1.12 on the gold fill).
- Stop releases a locked string but keeps the tuned marks. Tapping the locked peg again unlocks it. Leaving the Tuner or Live tab stops the microphone.
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
- **Implemented in M2 (2026-08-26):** library card = 56 pt thumbnail (YouTube jpg) or source glyph on an Accent2→Surface2 gradient tile, title 17 bold, subtitle (artist / "Recording · 26 Aug" in the app language / source), meta row `Key` in Display serif AccentText + `♩ bpm · m:ss`, star (Border-wrapped IconView — bare GraphicsView gets no taps on Windows), analysing card = 4 pt ProgressBar + "Analysing · 64 %" + × instead of the star, failed card = Error-coloured line, tap = retry. Recordings are WAV (not m4a) written by `Core.Audio.WavFile` — same code on both platforms. Sort = action sheet (Date / Title / Key). **Cancelling an analysis of a song without a result removes the card at once** (the worker cleans up later; CQT is not interruptible); cancelling a re-analysis keeps the old chords. Opening a card shows a summary alert until the Song page (M3) lands. Free-tier caption on the add sheet: "Free analyses left: N" / "Free: 1 analysis per day".
- **Ways of adding, revised (user decision 2026-08-26):** the add sheet lists YouTube · File · Record, each with a ★; starred ways are also shown as a row of quick buttons right under the "Songs" title (default ★ = YouTube + File; `AppSettings.PinnedSources`). **One YouTube button**: if the clipboard holds a YouTube link → a small choice sheet ("Add this link" in gold with the trimmed URL, or "Open built-in YouTube"); otherwise the built-in YouTube opens at once. Built-in YouTube = `YouTubeBrowserPage` (full-screen WebView on m.youtube.com; polls `location.href`, and an "Add" bar with the video title appears on any /watch page — the song is then added through the normal metadata + audio path). No URL text field anywhere — the clipboard is the way to paste.
- **Welcome greeting (2026-08-27):** the WaveMark on the welcome screen is replaced by the user's own hand-lettered greeting drawn as a string — `Resources/Images/hello_uk.svg` («Привіт») for Ukrainian and `hello_en.svg` ("Hello") for every other language, shown as an `Image` 342×90 (`Aspect=AspectFit`, Margin 0,16,0,0), source switched live with the language (`WelcomePage.ApplyGreeting`). The SVGs are the user's traced artwork with the background removed via an SVG `<mask>` that keeps the trace's own layer order (gold shapes white, dark counters black, later gold over them) — merging the shapes into one path loses the counters and the dot on «і». Trace outline strokes are dropped; the lettering is filled with the brand Accent per theme (`hello_*.svg` = Gold #D9AC4C for dark, `hello_*_light.svg` = Copper #AE6F32 for light, picked with `SetAppTheme`) so it matches the wave exactly — the trace's own golds (#d1a544 / #d4aa4b) were replaced (user request 2026-08-27). Font-based scripts (Bad Script, Comforter, Caveat) were prototyped and dropped in favour of the custom lettering.
- **Welcome motion + sound (2026-08-27):** the page fades in from the dark background over 0.5 s (the Launch→Welcome pop is not animated), then a left→right wipe (2.0 s, SinInOut) replaces it with the lettered greeting at the same x — the wave is drawn without glow, `Thickness=3.0`, width 326 of 342, margin 13.75 top, and the greeting Image uses `Aspect=Fill` at exactly 342×90 — so the two lines match in weight, length and height (verified by the user) (none of this under Reduce Motion) while a synthesized sound plays (`Resources/Raw/sounds/greeting.wav`, Karplus-Strong, generated in-repo — no third-party audio): the user chose **«harmonic bells»** — three natural 12th-fret harmonics E4 → B4 → E5, 320 ms apart, long ring, 3.0 s — over strums and bass-heavy arpeggios ("too much bass, wants something ringing", 2026-08-27). iOS plays it in the Ambient session category so the silent switch is respected. Settings → Appearance has "Skip the welcome screen" (default on); the first launch always shows Welcome.
- **Launch screen (2026-08-26):** `LaunchPage` opens every session — mark, "Strunika" in Display 46, the WaveMark breathing (opacity 1 → 0.45 → 1, 900 ms each way, off under Reduce Motion), Footnote caption at the bottom ("Tuning up…" / "Preparing recognition…" on first run). It does real work (model unpack) and hands over with the stack swap (`InsertPageBefore` + `PopAsync`). Windows head: ≥3 s so it can be reviewed.
- **Settings order:** the Strunika Pro card sits first, right under the title, and carries a small WaveMark (26 pt) between its title row and the feature list; then Appearance · Tuner · Recognition · About.
- **Pro title suffix (2026-08-27):** when the user has Pro, the tab titles Tuner · Live · Songs read "Тюнер Pro" + the small wave (`ProSuffix` control: "Pro" in Display/AccentText at the title size, wave 54:20 of the font size — the same ratio as in Settings), bound to `IsPro` on each view-model.
- **Song cards (2026-08-27):** top-right corner carries a dim "‹ 🗑" hint (chevL 10 + trash 12, Dim, 70 %) — swipe left to delete. The Songs header (title, quick buttons, search, filters) is pinned over the list on an opaque page background with a 28 pt shade below it; the list's header spacer follows the header height.
- **Paywall layout (2026-08-27):** pinned header = "Strunika Pro" (Display 32) + the 78×29 wave after "Pro" (same wave-to-font ratio as the 54×20 wave at Title2 in Settings) + ×, over a shade that is the page background fading out (opaque to 62 %, gone at 96 pt) so scrolled content dissolves under it like under the tab bar; no big wave. The ScrollView spans the screen (24 pt inset inside the content) so the scroll indicator sits right of the content. Feature order: Unlimited songs · Chord Editor · Export · Alternative tunings · Full chord vocabulary · Transpose & capo, then the rest. Footer links: Restore purchases (Apple-required) · Promo code (App Store offer codes) · Terms.
- **Paywall "Coming soon" block (2026-08-26):** under the feature list, an eyebrow COMING SOON and one card with three rows, each with a small gold "Soon" pill: "Stems: solo, mute and your own track mix" (sliders icon), "Karaoke / backing track" (mic icon) and "Bass tabs" (wave icon), one-line hints in Caption. All come from the same Demucs stem-separation investment; keep it to these three lines — no per-feature promises (guitar minus is unreliable in 6-stem mode).
- **Paywall entry points:** a locked control opens the compact modal sheet; Settings → "Learn more" pushes the full sheet so it slides in from the right edge (user request 2026-08-26).
- **Switches:** off state must stay visible on both themes — knob `TextSec`, track `Separator` with a `Dim` stroke (never `Bg`, which vanishes).
- YouTube: on-device extraction (YoutubeExplode) behind `ISongSource`, "like ChordAI": never store/export the audio, graceful "YouTube тимчасово недоступний" state, never crash. Playback via the official IFrame embed in a WebView (position polled ~100 ms). **Never ship playback of the extracted stream** (that is plainly "play downloaded YouTube media"). **Store risk (2026-08-27):** on-device extraction sits under App Store guideline 5.2.3 ("save, convert, or download media from … YouTube") and the YouTube ToS; the app carries a remote kill switch (`Services/RemoteFlags`, `flags.json` at the repo root) so YouTube analysis can be turned off for every install without a release, the progress caption is a plain "Analysing · N %" (no "fetching/decoding audio"), and the fallback architecture is server-side analysis (Chordify model) reusing the same pipeline. Review notes must describe it as the official player plus on-device analysis of a transient buffer that is never stored or exported.
- **Next chord preview** (user request 2026-08-26): while a song, recording or take plays, show the *next* chord with its diagram beside the current one, visibly de-emphasised (smaller, TextSec, ~60 % opacity); when it arrives it animates into the hero position (slide + scale, ~250 ms) and the following chord takes the preview slot. Same on Live playback.
- Song page layout: top half = ChordHero (huge chord + diagram, capo-aware shapes); bottom third = **string timeline** (chord segments as beads on a string, wave below, fixed centre playhead, ribbon scrolls; segment width ∝ duration, beat ticks) + transport (play, A–B, speed). YouTube player collapsed to a thumbnail strip above the string, expands on tap. Analysis runs in background with progress on the card and cancel.
- Free: simple chords (`Simplify`), "click at song tempo" metronome, beat snapping (on by default). 🔒: transpose/capo (`Transpose`, diagram follows capo), speed 0.5×–1.25× pitch-preserved (AVPlayer rate / YouTube `playbackRate`), A–B loop, export TXT / PDF / XLSX (MiniExcel; row = segment: start, end, bar, chord) / share sheet, **Chord Editor** (uk: «Редактор акордів», user decision 2026-08-27).
- **Chord Editor** (name is literally "Chord Editor" in both languages; hint "редактор акордів"). **Free for the first 3 songs** (lifetime counter in Keychain, shown as "Безкоштовно · пісня 2 з 3"), then 🔒. Must feel first-class: own pushed screen (Скасувати / Готово), selected segment with edge handles, root · quality · bass pill rows, action row (split / merge / insert / delete), undo/redo, loop-audition toggle + play. tap a segment → root · quality · bass wheel; drag segment edges snapping to the beat grid (long-press = free); split / merge / insert / delete; nudge beats; undo/redo; loop-audition the segment. Edited songs get a "правлено вручну" badge; re-analysis asks before overwriting; export uses edited chords.
- Chord diagrams: guitar only in v1, own JSON shape DB (24 triads + 7ths/sus, 2–4 positions each), left-handed mirror free (Settings), capo-aware.
- **Implemented in M3 (2026-08-27, redesigned the same day at the user's request):** song page = header (back "‹ Пісні", title + source, "Редактор"/"Editor" chip → coming soon) · chip row (Тон. · ♩ bpm · 4/4 · Капо 🔒 · speed 🔒 · ✓ Прості, horizontally scrollable) · **now/next panel** (current chord Display 74 + 88×112 diagram, arrow, next chord 30 + 56×72 diagram at 66 %) · **the conveyor** · position slider · transport (metronome round button · « » = previous/next *chord* · ▶ 70 pt · A–B 🔒).
- **The conveyor (`ChordTrack`)** replaced the bead timeline: 208 pt tall, the waveform is the background (peaks stored per song, played part in the Accent, the rest neutral) drawn as **grouped bars** (5 pt wide, 2.5 pt apart) whose heights are precomputed on a grid anchored to the *song*, so a bar never changes height while it scrolls; each chord is a **marker at the moment it starts** — a 3 pt line in `TextSec` (never the accent — it must not read as a second playhead) with a 34 pt pill **centred on that line**, not a bar spanning its duration; the playing chord's pill sticks to the playhead and the next one is pinned to the right edge until it scrolls into frame; beat ticks under the wave; the playhead is a 4 pt Accent bar (no arrow) at 34 % of the width, so most of the track is what is still to come. Tap a pill = seek to that chord, tap the track = seek there, drag = silent scrub. Smoothness rules learned the hard way: the page drives it from the **platform frame ticker** (`Animation.Commit`, i.e. vsync) with a *predicted* position (transport probed every 200 ms); `Position` is assigned **directly, not through a binding** (and the control must never assign its own `Position` during a pan — a manual set clears the one-way binding and the track freezes after the first drag); label widths are cached; the native `Slider` is nudged only every 6th frame.
- **Touch targets (2026-08-27):** the song page's transport follows the platform minimums — Apple HIG 44 pt, Material 48 dp — with the primary controls comfortably above them: play 78, chord skip 60, metronome 56, A–B chip 46, info chips 38.
- **Chord diagrams:** fret numbers are drawn inside the diagram, each centred on its own string (`ShowFrets`) — the old row of numbers under the box did not line up with the strings. Tapping any diagram pauses the song and opens `ChordShapesSheet` with every position for that chord (chosen one outlined in the Accent, caption "Відкрита позиція" / "Лад N"); the choice sticks to that chord until capo, transposition or "simple chords" changes.

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

## 6. Adaptive sizing (rule since 2026-08-27 — applies to every page, control and agent)

The app must look right from iPhone SE (375×667 pt) to iPad Pro 13" (1024×1366 pt). Points are a physical unit, so a hard-coded 60 pt button is 60 pt everywhere: too big on a compact phone, and on a tablet the whole chrome would either look lost or — if scaled linearly — turn into saucers. The design is therefore **class-based, not proportional**:

| Class | Shortest side | Devices | Chrome (`{t:Size}`) | Hero (`{t:Size …, Hero=True}`) | Layout |
|---|---|---|---|---|---|
| **Compact** | < 380 pt | iPhone SE, 12/13 mini | ×0.88, never below 44 pt | ×0.85 | as Regular |
| **Regular** | 380–599 pt | every other iPhone (reference: 393–430 pt) | ×1.0 | ×1.0 | as designed |
| **Wide** | ≥ 600 pt | iPad | **×1.0 — chrome does not grow** | ×1.25 | content column capped at 672 pt (`{t:ContentInset}`); two-pane variants where they pay off (M8) |

The class is decided by the **shortest side** of the window (`Theme/Metrics.cs`), so rotating a phone does not change it; iPad Split View and a resized dev window do.

**How to write sizes (mandatory):**
- Chrome — buttons, chips, icons, thumbnails, sheet rows: `WidthRequest="{t:Size 60}"`, `Size="{t:Size 24}"`. Touch targets: `{t:Size 44, Min=44}` — the sweep adds `Min=44` to anything ≥ 44 automatically; never let a tappable thing go under 44 pt.
- Round buttons: pair `{t:Size 60}` with `StrokeShape="{t:Round 60}"` so circles stay circles.
- Hero content — the chord name, its diagram, the tuner note, the big points readout: `{t:Size 140, Hero=True}`. Drawn controls (`ChordDiagram`, `ChordTrack`, `TunerString`, `LevelMeter`, `PillTabBar`) fit their container: give them star rows and they adapt by themselves — prefer this over any fixed size.
- Ordinary text (12–29 pt) is **not** scaled by class: Dynamic Type owns it (`FontAutoScalingEnabled` is on for every Label) and body text at 15–17 pt is right on every screen.
- Page/section insets (20–24 pt) stay fixed; vertical space is distributed by `*` rows, never by fixed heights.
- Code-behind: `Theme.Metrics.Instance.Size(104)` / `Size(200, hero: true)`; never assign a bound size from code (it clears the binding).
- Columns of content (settings, paywall, the tab views, the song page rows): `Margin="{t:ContentInset}"` and keep `HorizontalOptions="Fill"`; `{t:ContentInset Plus=True}` when the element carried its own 20 pt page inset. **Ribbons stay full-width** — the conveyor spans the whole screen on a tablet while the rows above and below it keep the column. Never centre a column with `HorizontalOptions="Center"` — it takes its natural width and overflows a narrow phone (seen on the 13 mini profile).
- Text drawn on a canvas that must fit a slot (tab-bar labels, chord names, fret numbers): measure with `GetStringSize` and shrink the font to fit, with a floor — the way `PillTabBar.LabelFont` and `ChordDiagram` do.
- Do not use `OnIdiom` for sizes (SE and Pro Max are both "Phone"), `Scale` transforms (blurry, wrong hit areas) or per-page `SizeChanged` maths (unmaintainable across 14 pages) — the tokens above are the only mechanism.

**Orientation (2026-08-27):** iPhone is **portrait only** (`Info.plist` `UISupportedInterfaceOrientations`) — every screen is laid out for a tall viewport. iPad allows all orientations; landscape variants of the song page (player + chords left, conveyor right) are M8 work. The `Landscape · …` launch profiles exist for the iPad case and for checking nothing breaks if a phone layout is squeezed.

**Test every change in at least three profiles** (Visual Studio ▸ launch profile, or `STRUNIKA_WINDOW=375x667`): iPhone SE, iPhone 16, iPad 11". Profiles exist for SE, 13 mini, 16, 16 Pro Max, iPad mini, iPad 11", iPad Pro 13". The welcome and launch pages are deliberately excluded from scaling (pixel-aligned lettering; both fit the smallest screen).

## 7. Per-frame rendering rule (learned the hard way, 2026-08-27)

**Nothing that runs every frame may draw a canvas, update a native control, or create a native object.** The song page stuttered ~100 ms once a second on the Windows head; the GC events showed `gen2 InducedNotForced` collections on the UI thread — the WinUI XAML runtime induces full collections when native objects (Win2D text layouts and brushes, native control updates, MAUI transform objects) churn, and with a ~20 MB managed heap the threshold is tiny. iOS has no such mechanism, but the same code also burns battery there. The rule and the building blocks:

- **Move, do not redraw.** `ChordTrack` renders its ribbon (bars, beat ruler, chord pills) into canvases three screens wide once every few seconds and slides them with a transform every frame. The played/coming bar colours are two full renderings, each in a container clipped to its side of the playhead. Each rendering is double-buffered: the next window is drawn into a spare canvas and shown only after its `Draw` has run (a canvas invalidated and shown in the same frame flashes stale content at the new offset). A far jump (scrub across the song) renders straight into the visible buffer instead.
- **`NativeTransform.TranslateX/ScaleX`** for anything moved per frame: on Windows it writes into one `CompositeTransform` created once (MAUI's own `TranslationX` allocates a transform object on every change); elsewhere it is the MAUI property.
- **No native slider on a per-frame path.** `SeekBar` (track, fill, thumb — three views moved by transforms; two-way `Value`, `Duration`; drag/tap events) replaces `Slider` for the position and the level controls on the song page. Updating a WinUI `Slider.Value` ten times a second alone caused most of the induced collections.
- **Drags through `PointerDrag.Attach`**: on Windows it captures the pointer natively (a MAUI pan released outside the element ends as "cancelled" with the start offset, snapping the thumb back); elsewhere it is the pan gesture. A press without movement is a tap.
- Drawables tolerate teardown (`Handler == null` → return; NRE/E_INVALIDARG/COM caught) and the frame ticker stops on `Unloaded` and `Window.Destroying` — never test `Handler` in the first frames, on Windows they run before it exists.
- Subscriptions from a page or its view-model to long-lived objects (`Window.Destroying`, `IProGate.Changed`, `AppSettings.Changed`) must be removed on dispose, or every opened page stays in memory and every collection gets longer.
- The song page logs a 5 s frame summary (`frames 5 s: … hitches … gc2 … managed …`) and any frame over 50 ms in debug builds — read that before guessing.
