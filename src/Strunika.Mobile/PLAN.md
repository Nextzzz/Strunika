# Strunika.Mobile — план реалізації v1

Дизайн і продуктові рішення: `.claude/skills/strunika-ui/SKILL.md` (§5 Decisions log) і макети
https://claude.ai/code/artifact/776fc75b-7d00-4987-a13f-3d07d4954c22. Цей файл — *як* це збудувати
в MAUI, у якому порядку, і що вважати готовим. Оновлюйте його разом зі скілом.

## 0. Вихідна точка (перевірено 2026-08-26)

- Windows-голова збирається: `dotnet build -f net9.0-windows10.0.19041.0` у `src/Strunika.Mobile`
  (SDK 9.0.317 через `global.json`; встановлені воркоуди `maui-ios`, `maui-windows`).
- Є: `TunerViewModel` (YIN + згладжена стрілка, hold), `LiveViewModel` (DSP-здогадка +
  нейронне підтвердження, історія), `IMicrophoneSource` (iOS `AVAudioEngine`, Windows NAudio),
  `ModelStore` (розпаковка ONNX у кеш), Shell на 2 вкладки, стандартні кольори/шрифти шаблону.
- Спільні API, які використовуємо без змін:
  `PitchDetector.Detect`, `Notes.Describe`, `StreamingChordDetector`, `SlidingNeuralChordDetector`
  (`AddSamples` + `Tick` + `ConfirmedChanged`), `NeuralChordRecognizer.Recognize(samples22050)`,
  `HalfbandDecimator.Decimate` (44.1k→22.05k), `OnsetDetector`/`TempoEstimator`/`BeatTracker`,
  `ChordTimeline.SnapToBeats`, `ChordLabels.Pretty/Simplify/Transpose`, `KeyPrior` (увімкнений
  усередині рекогнайзера). Еталон конвеєра пісні — `Strunika.App/ViewModels/SongViewModel.cs`
  (рядки ~470–510).
- Чого **немає для iOS**: декодування mp3/m4a (NAudio — лише Windows), плеєр, YouTube без yt-dlp,
  StoreKit, хаптика/blur, SQLite, експорт.

## 1. Архітектура

```
Strunika.Mobile/
  App.xaml(.cs)            ресурси тем, запуск: Welcome або Root
  RootPage.xaml            4 вкладки як ContentView + PillTabBar-оверлей (Shell TabBar не використовуємо)
  Pages/                   WelcomePage, SongPage, EditorPage, PaywallSheet, AddSongSheet, TuningSheet…
  Views/                   TunerView, LiveView, LibraryView, SettingsView (вміст вкладок)
  Controls/                PillTabBar, TunerString, ConfidenceRing, ChordHero, ChordDiagram,
                           StringTimeline, WaveMark (логотип-хвиля), LockBadge, Segmented
  ViewModels/              по одному на екран; CommunityToolkit.Mvvm
  Models/                  Song, ChordSegmentDto, Take, Tuning, ChordShape
  Services/                інтерфейси + Platforms/* реалізації (нижче)
  Data/                    SongRepository (sqlite-net-pcl), міграції
  Pro/                     Feature, IProGate, DevProGate, StoreProGate, FreeQuota
  Resources/Styles/        Colors.xaml (токени зі скіла §1), Styles.xaml, Typography.xaml
  Resources/Strings/       Strings.resx (en), Strings.uk.resx
  Resources/Fonts/         дисплейна антиква (OFL) + SF за замовчуванням (системний)
  Resources/Raw/           models/*.onnx, chords/shapes.json
```

Інтерфейси сервісів (усі — DI-синглтони, реалізації під `Platforms/iOS` і `Platforms/Windows`):

| Інтерфейс | Що робить | iOS | Windows-голова |
|---|---|---|---|
| `IMicrophoneSource` | є | AVAudioEngine | NAudio |
| `IAudioDecoder` | файл → `float[]` mono 44.1k | `AVAssetReader` + `AVAudioConverter` | NAudio (`AudioLoader.LoadMono`) |
| `IAudioPlayer` | Load/Play/Pause/Seek/Rate/Position-події | `AVPlayer` (rate зі збереженням висоти) | NAudio `AudioPlayer` (+ SoundTouch для швидкості або без швидкості на голові) |
| `IYouTubeSource` | URL → тимчасовий аудіофайл (ніколи не експортується) + метадані | YoutubeExplode | YoutubeExplode (навмисно без yt-dlp — як на iOS) |
| `IMetronome` | клік за темпом, події «клік у момент t» для маскування | AVAudioEngine player node | NAudio |
| `IHaptics` | Click/Success/Selection | `UIImpactFeedbackGenerator` | no-op |
| `IProGate` | `Has(Feature)`, `Changed` | StoreProGate (StoreKit) ∨ DevProGate | DevProGate (завжди Pro) |
| `ISongRepository` | CRUD пісень/сегментів/папок | sqlite-net-pcl | те саме |
| `IExporter` | TXT / PDF / XLSX / share | PDF: `UIGraphicsPDFRenderer`; XLSX: MiniExcel | PDF пропускаємо, XLSX/TXT ті самі |
| `IAppSettings` | Preferences-обгортка (тема, мова, лівша, A4, стрій…) | Preferences | Preferences |

Навігація: `NavigationPage(RootPage)` з прихованим баром. `RootPage` тримає 4 `ContentView`
живими (тюнер і наживо не втрачають стан) і перемикає їх за `PillTabBar.SelectedIndex`;
`SongPage`/`EditorPage`/аркуші — `PushAsync`/`PushModalAsync`. Shell прибираємо — овальний
селектор із pan-жестом і анімацією натиску простіше зробити власним контролом, ніж
переписувати нативний `UITabBar`.

NuGet, які додаємо: `CommunityToolkit.Maui` (Popup для аркушів, `IconTintColorBehavior`,
`MediaElement` не використовуємо), `sqlite-net-pcl` + `SQLitePCLRaw.bundle_green`,
`YoutubeExplode`, `MiniExcel`, `Plugin.InAppBilling` (StoreKit; альтернатива — власний
біндинг StoreKit 2, якщо плагін не покриє offer codes).

## 2. Етапи

Порядок обраний так, щоб спільні контроли (таймлайн, акорд-герой, діаграма) з'явились до
екранів, які їх перевикористовують, а перевірка на Windows-голові була можлива після кожного етапу.

### M0 — Фундамент (≈2 дні) — ✅ виконано 2026-08-26 на Windows-голові
Зроблено: токени (`Theme/Tokens.cs` + `{t:Theme}`), стилі, Vollkorn, uk/en `.resx` + `{loc:Str}` з живим перемиканням, `RootPage` + `PillTabBar` (drag/snap/bounce), `WelcomePage`, `SettingsView`, `IProGate`/`DevProGate`/`LockBadge`/`PaywallSheet`, іконки через `IconView`, App Icon full-bleed, splash. Не перевірено на iPhone (немає Apple Developer). Пастки Windows описані в README.
- `Colors.xaml`: токени зі скіла (Bg/Surface1/Surface2/Separator/TextPri/TextSec/Dim/Accent/
  AccentText/Fill/OnFill/Glow) для Dark і Light через `AppThemeBinding`; `Styles.xaml` —
  Label-стилі (LargeTitle, Title, Body, Footnote), Button (primary gold / secondary surface),
  Chip, Card, Switch, Segmented. Жодного сирого hex у сторінках.
- Шрифти: дисплейна антиква (OFL, з макетів Young Serif; замінюється однією константою)
  для акордів/нот/великих заголовків; решта — системний.
- Локалізація: `Strings.resx` + `Strings.uk.resx`, `LocalizationManager` з подією зміни мови,
  markup-розширення `{loc:Str Key}`; усі рядки з `MainPage`/`LivePage` переносяться в ресурси.
- `RootPage` + `PillTabBar` (GraphicsView + `PanGestureRecognizer`): 4 вкладки, овальний
  селектор, перетягування зі снапом (`SpringOut` ~300 мс), натиск → scale 1.04 (120 мс),
  відпускання → пружинно назад до 1.0 з легким недольотом 0.99 (`SpringOut` ~350 мс), Reduce Motion → без перельоту, хаптика на снапі.
- `WelcomePage` (перший запуск, прапорець у Preferences): логотип-хвиля (`WaveMark`),
  мова з прапорцями (SVG), тема з іконками ◐ ☾ ☀, «Почати» → тюнер.
- `SettingsView` (скелет усіх груп з макета) + `IAppSettings`.
- `Pro/`: `Feature` enum (AltTunings, A4Reference, TransposeCapo, Speed, ABLoop, Export,
  ChordEditor, Folders, FullChordVocabulary, UnlimitedSongs), `IProGate`, `DevProGate`
  (прапорець в «Експертних», у Windows-голові завжди true), `LockBadge` контрол, `PaywallSheet`
  (компактний + повний, ціни поки заглушки з ресурсів — StoreKit у M6).
- Іконки: SVG у `Resources/Images` (MauiImage) + `IconTintColorBehavior`; App Icon —
  full-bleed варіант `strunika_guitar_bg.svg` без скруглених кутів; splash з тим самим тлом.
- ✅ Готово, коли: Windows-голова показує Welcome → Root з 4 вкладками, таб-бар тягнеться
  і пружинить, тема/мова перемикаються з Налаштувань, замочки відкривають paywall.

### M1 — Тюнер (≈2 дні) — ✅ виконано 2026-08-26 на Windows-голові
Зроблено: `Models/Tuning` (9 строїв, Standard free), авто-визначення струни + фіксація тапом, детектор атаки (160 мс пауза) + поріг чистоти YIN + згладжування (медіана 5 → EMA → slew 9 ¢/тик), струна обирається один раз на щипок; висота — `Core.TunerEngine` (YIN + субгармонічна перевірка, 14 NUnit-тестів), `TunerString` (струна провисає/випрямляється, спалах + хаптика в строї), кілочки з октавою лише для повторів (E₂/E₄), `TuningSheet`, `A4Sheet` (430–450, Pro), стрій за замовчуванням у Налаштуваннях. Показ у балах (10 = лад), позначки «налаштовано» після 1,5 с + ефект на останній струні, «Почати спочатку/знову», фрази «Давай затюнемо!»/«Все в строї — поїхали!», зупинка мікрофона при зміні вкладки. Перевірено тонами з динаміка.
- Модель строїв (`Tuning`: назва, струни з MIDI-нотами): Standard (free), Drop D, Half-step,
  Full-step, DADGAD, Open G, Open D, Ukulele, Bass (🔒).
- `TunerViewModel`: авто-визначення струни (найближча струна обраного строю), ручна
  фіксація тапом по кілочку, A4 430–450 (🔒, зберігається), відхилення в центах відносно
  цільової струни; існуючі згладжування/hold зберігаються.
- Згладжування: поточний YIN «трясе» (відгук користувача) — медіанний фільтр по 3–5 вимірах + EMA + обмеження швидкості руху струни (slew), hold 1,5 с при втраті висоти, гістерезис чіткості (0,8 щоб з'явитись, 0,6 щоб триматись); детектор глушіння (обвал рівня >18 дБ три чанки поспіль ≈140 мс, чого природне згасання не дає) або справжня тиша (рівень < max(0,001; 1,5× шумовий поріг)) → показання зникає одразу. Пріоритет — не гасити струну, що ще звучить (рішення користувача 2026-08-26): краще заглушена «повисить» ~200 мс; гейт лише по рівню відсікав живу ноту.
- `TunerString` (GraphicsView, `Invalidate` з таймера 60 Гц): струна, що провисає на
  `cents`, зона ±8 ¢, бусина; у строї — випрямляється, спалахує золотом, `IHaptics.Success`
  один раз на «вхід у стрій»; Reduce Motion — без спалаху.
- Ряд кілочків (E₂ A D G B E₄ — цифри лише при повторюваних назвах), аркуш вибору строю.
- ✅ Windows-голова: реальна гітара/YouTube-тон у мікрофон — струна визначається сама, фіксація працює.

### M2 — Бібліотека пісень + аналіз (≈3 дні) — ✅ виконано 2026-08-26 на Windows-голові
- SQLite: `Song` (id, title, artist, source(File/Record/YouTube), sourceRef, thumbnailPath,
  durationSec, key, bpm, createdAt, favourite, folderId, edited), `SongSegment` (songId, start,
  end, label, bass?) — або JSON-колонка сегментів; `Folder` (🔒); `Take` для «Наживо».
- `AnalysisService`: черга завдань з прогресом і скасуванням (`IProgress<double>`,
  `CancellationToken`): decode 44.1k → `HalfbandDecimator` → `NeuralChordRecognizer(self,
  OverlapWindows=true).Recognize` → `ChordLabels.Pretty` → onset/tempo/beats →
  `SnapToBeats` → збереження. На фоновому потоці; ONNX-сесія кешується на модель.
- `FreeQuota`: 20 пісень назавжди + 1/день (локальна дата), у `SecureStorage`
  (Keychain); повторний аналіз тієї самої пісні не рахується; вже проаналізоване не блокується.
- `LibraryView`: картки (мініатюра/гліф джерела, назва, виконавець, тональність, темп,
  тривалість, зірочка, «змінено»), картка в процесі аналізу з прогресом і скасуванням, пошук,
  сортування, фільтри Усі/Обрані/Папки(🔒), свайп-видалення, порожній стан.
- `AddSongSheet`: Файл (FilePicker: mp3/m4a/wav), Запис (мікрофон → m4a/wav в AppData, хвиля
  й таймер), YouTube (поле URL, автопідстановка з буфера, метадані й мініатюра через
  YoutubeExplode; graceful «YouTube тимчасово недоступний»).
- ✅ Windows-голова: файл і YouTube-посилання аналізуються, з'являються в бібліотеці, ліміт рахується.
- Як зроблено: `Models/Song` (SQLite, сегменти JSON-колонкою) + `Data/SongRepository` (sqlite-net-pcl);
  `Services/AnalysisService` — послідовна фонова черга (один ONNX-інференс за раз), прогрес по вікнах
  (`NeuralChordRecognizer.Recognize(progress, ct)`), скасування; **скасування пісні без результату = видалення
  картки**, повторний аналіз зі скасуванням лишає старий результат; YouTube-аудіо — тимчасовий файл у кеші,
  видаляється після декодування; `Services/IAudioDecoder` (Windows: NAudio через `Strunika.Media`; iOS:
  `IosAudioDecoder` на AVAudioFile + `Core.Audio.Resampler` — **не перевірено на пристрої**); `IYouTubeSource` →
  `YoutubeExplodeSource` (metadata, mq-мініатюра, best m4a); `Pro/FreeQuota` над `Core.Library.FreeQuotaPolicy`
  (юніт-тести; SecureStorage з fallback на Preferences); `TakeRecorder` → WAV (`Core.Audio.WavFile`) у AppData/recordings;
  `LibraryViewModel` + `LibraryView` (пошук, Усі/Обрані/Папки🔒, сортування через action sheet, свайп-видалення,
  картка з прогресом і ×), `AddSongSheet` (файл / запис / YouTube з автопідстановкою з буфера), `RecordSheet` (таймер + `LevelMeter`).
- Способи додавання (уточнення користувача): у «+» три ряди YouTube · Файл · Запис із ★; позначені ★ дублюються рядом
  швидких кнопок під заголовком «Пісні» (типово YouTube + Файл). Одна кнопка YouTube: посилання в буфері → маленьке
  вікно вибору «додати посилання» / «відкрити вбудований YouTube», інакше одразу вбудований YouTube
  (`YouTubeBrowserPage`, WebView на m.youtube.com, панель «Додати» з'являється на сторінці відео). Поля для URL немає.
- Швидкий ряд: кнопки ділять ширину порівну; якщо остання не вміщує свій підпис (вимір `Measure`) — переноситься
  на другий рядок на всю ширину (прохання користувача). Перерахунок при зміні ширини, мови й ★.
- Прогрес аналізу — справжній, не за часом: CQT і novelty звітують по кадрах (`CqtExtractor.Extract(progress, ct)`,
  `OnsetDetector.NoveltyCurve(progress, ct)`), частки стадій з вимірів на ПК для 3,5-хв пісні (декод 1,0 с · halfband
  0,2 с · CQT 1,8 с · ONNX-вікна 0,9 с · novelty 1,05 с): завантаження 20 % (YouTube) → декод 20 % → розпізнавання 55 %
  (CQT 65 % з них) → ритм 22 % → збереження.
- `LaunchPage`: перша сторінка кожного запуску — логотип, назва, «дихаюча» хвиля, підпис «Налаштовуюсь…» /
  «Готую розпізнавання…» (перший запуск); під ним реальна робота (розпакування ONNX-моделей у кеш), потім
  Welcome або кореневі вкладки. На Windows тримається ≥3 с (прохання користувача), на iPhone — скільки триває робота.
- Налаштування: блок Strunika Pro перенесено нагору, після слова «Pro» — мала струна-хвиля; «Learn more» відкриває
  пейвол як сторінку (виїжджає справа). У пейволі — блок «Незабаром»: «Стеми: соло, мут і власний мікс доріжок»,
  «Караоке / мінусовка» і «Бас-таби» (обидва на базі Demucs; майбутні милстоуни після M8).
- Welcome: замість хвилі — леттеринг користувача «Привіт»/«Hello» (SVG зі знятим фоном через маску), анімація
  «струна дописує слово» + короткий синтезований звук — три флажолети E4→B4→E5 з довгим дзвоном (варіант «7» з відслуханих 9; струми та басові перебори відхилено) (`ISoundPlayer`: NAudio на Windows,
  AVAudioPlayer/Ambient на iOS). Налаштування «Пропускати вітальне вікно» (типово так; перший запуск — завжди показ).
- Dev-запуск: `STRUNIKA_WELCOME=1` (лише для тест-скрипта) примусово відкриває привітання; з Visual Studio — як у користувача.
- Відкриття картки поки показує зведення (тональність · темп · тривалість · акорди) — екран пісні в M3.
- Відомі дрібниці на потім: назва тональності з дієзами («A#m» замість «B♭m») — узгодити зі спелінгом у M3;
  «Нічого не знайдено» для порожнього пошуку; налаштування моделі пісень (`AppSettings.SongModel`) без UI.

### M3 — Екран пісні (≈3 дні) — ✅ Windows-голова (2026-08-27)
- `SongPage` (push з бібліотеки): шапка «‹ Пісні · назва/джерело · Редактор» (чіп редактора показує «незабаром» до M5);
  ряд чіпів (горизонтальний скрол): Тон.·♩ bpm · 4/4·Капо N 🔒·швидкість 🔒·✓ Прості.
- **Панель «зараз / далі»** над доріжкою: зліва поточний акорд (Display 74) + діаграма 88×112, стрілка, справа
  наступний акорд (30) + діаграма 56×72 при 66 % — тап по будь-якій діаграмі ставить пісню на паузу і відкриває
  `ChordShapesSheet` з усіма аплікатурами (вибір лишається за акордом, поки не змінили капо/транспонування/«Прості»).
- **`ChordTrack` — конвеєр** (замінив `StringTimeline`): на задньому плані аудіохвиля (`Song.PeaksB64`,
  `Core.Audio.Waveform`, 40 значень/с, зігране — акцентом, майбутнє — нейтрально), акорди позначені **на початку
  свого звучання** (вертикальна риска + пігулка), поточний тримається біля курсора, наступний «прикручений» до
  правого краю, поки не в'їде у кадр; бітові рисочки знизу; курсор нерухомий на 34 % ширини (більше місця для
  того, що попереду). Тап по пігулці — на початок акорду, тап по доріжці — на цю секунду, перетягування — скраб.
- **Плавність**: малюємо на кадровому тікері платформи (`Animation.Commit`, vsync), позиція **прогнозується** з
  годинника і лише кожні 200 мс звіряється з транспортом (`SongViewModel.Frame`): стрибок > 0,35 с — різкий синк,
  інакше м'яке підтягування. Стовпчики хвилі прораховані наперед на сітці, привʼязаній до пісні (сталі висоти),
  ширини пігулок кешовані, слайдер оновлюється раз на 6 кадрів.
- Розміри кнопок: Apple HIG — мінімум 44 pt, Material — 48 dp; у нас ▶ 78, « / » 60, метроном 56, A–B 46, чіпи 38.
- Слайдер під доріжкою (позиція · повзунок · тривалість), скраб через нього теж беззвучний.
- Транспорт: метроном (кругла кнопка ліворуч) · « / » — **на початок попереднього/наступного акорду** · ▶ 70 pt ·
  A–B 🔒. Швидкість переїхала у ряд чіпів.
- `ChordShapes` (`Models/ChordShapes.cs`): таблиця форм у коді, а не JSON — відкриті C/A/G/E/D-родини, Fmaj7, B7 +
  рухомі E/A-форми для «», m, 7, m7, maj7, sus4, sus2, dim, dim7, m7b5, aug, 6, m6, 9, add9; капо-усвідомлений вибір
  (звучний корінь = корінь − капо), дзеркало для лівші; номери ладів малюються **всередині** діаграми, вирівняні
  по своїх струнах (`ChordDiagram.ShowFrets`).
- YouTube: офіційний плеєр (IFrame API у власній сторінці з реальним origin), для ютуб-пісень розгорнутий одразу при
  відкритті; смужка з мініатюрою згортає/розгортає його. Відтворення витягнутого аудіопотоку **не робимо ніколи**
  (це відверте «play downloaded YouTube media»).
- **Ризик App Store (2026-08-27):** витяг аудіо на пристрої підпадає під Guideline 5.2.3 («save, convert, or download
  media from … YouTube») і ToS YouTube. Запобіжники: віддалений вимикач (`Services/RemoteFlags`, `flags.json` у корені
  репозиторію — має бути на публічному хостингу), підпис прогресу лише «Аналізую · N %», у Review Notes — «офіційний
  плеєр + аналіз тимчасового буфера на пристрої, нічого не зберігається». План Б при відхиленні — серверний аналіз
  (модель Chordify) тим самим пайплайном (`Strunika.Core`/`Neural` у контейнері), застосунок лише надсилає посилання.
- **Адаптивність (2026-08-27):** розміри хрому в XAML — токени `{t:Size}` / `{t:Round}` (`Theme/Metrics.cs`): клас
  екрана за коротшою стороною — Compact (< 380 pt, ×0,88, не нижче 44) · Regular (×1,0) · Wide (iPad: хром ×1,0,
  герой ×1,25, колонка ≤ 672 pt через `{t:ContentInset}`). Профілі вікна від iPhone SE до iPad Pro 13" у
  `launchSettings.json` (`STRUNIKA_WINDOW`). Правила — у skill §6; iPhone — лише портрет (plist), iPad — усі орієнтації, двоколонковий ландшафт — M8.
- Не зроблено: анімація «наступний → герой» (250 мс); хвиля для ютуб-пісень зʼявиться лише після повторного аналізу
  (аудіо ми не зберігаємо), для файлів/записів рахується при першому відкритті.

### M4 — «Наживо» як запис (≈3 дні)
- `TakeRecorder`: мікрофонні чанки → кільцевий буфер + файл дубля (wav у AppData; дубль
  зберігається в бібліотеку як джерело «Запис» за бажанням користувача).
- Сегменти з часом: підтвердження `SlidingNeuralChordDetector` штампуються позицією в семплах
  (не `DateTime`), DSP-здогадка — тільки для героя.
- `LiveView` = `ChordHero` (менший, кільце впевненості з `StreamingChordDetector.Confidence`)
  + той самий `StringTimeline` у режимі «запис»: курсор = «зараз», праворуч порожньо; pan/тап
  ставлять прослуховування на паузу, відпускання грає дубль з цієї точки; «Слухати» продовжує
  запис новим дублем. Режими розпізнавання — лише за прапорцем «Експертні».
- «Прості акорди» увімкнено; вимкнення — 🔒 `FullChordVocabulary`.
- Метроном: `IMetronome` + маскування кадрів кліку в детекторах (невелика зміна в Core/Neural:
  `MuteRange(sampleStart, sampleEnd)` перед `AddSamples`), підказка про навушники.
- ✅ Windows-голова: сесія записується, після стопу можна відмотати і переслухати з акордами.

### M5 — Chord Editor (≈4 дні)
- `EditorPage` (push зі сторінки пісні або дубля): картка сегмента, undo/redo (стек команд:
  Relabel, MoveBoundary, Split, Merge, Insert, Delete, NudgeBeat), збільшений `StringTimeline`
  у режимі редагування: вибір сегмента, ручки країв з прилипанням до бітів (утримання — вільно),
  ряди корінь · тип · бас, дії, луп сегмента (`IAudioPlayer` A–B на межах сегмента).
- Лічильник «безкоштовно 3 пісні» (`SecureStorage`, за songId), далі 🔒 `ChordEditor`;
  збереження ставить `edited=true`, повторний аналіз питає перед перезаписом; експорт бере правлені акорди.
- ✅ Windows-голова: цикл «змінити акорд → пересунути межу → undo → зберегти → знову відкрити».

### M6 — Pro і Store (≈2 дні; потребує Apple Developer)
- Продукти: `pro.monthly`, `pro.yearly` (auto-renewable, без пробного періоду), Offer Codes.
- `StoreProGate` (`Plugin.InAppBilling` або власний StoreKit 2 біндинг): купівля, відновлення,
  статус підписки при старті, `PresentCodeRedemptionSheet` для кнопки «Ввести код».
- Paywall отримує локалізовані ціни зі Store; DevProGate вимикається в Release (крім TestFlight).
- ✅ Sandbox-покупка на пристрої відкриває всі 🔒.

### M7 — Експорт (≈1–2 дні)
- TXT (акорди по тактах), XLSX (MiniExcel: початок, кінець, такт, акорд), PDF (iOS
  `UIGraphicsPDFRenderer`; на Windows-голові — пропуск з повідомленням), Share Sheet.

### M8 — iOS-полірування і реліз (постійно, фінал ≈3 дні)
- Blur-панелі через `UIVisualEffectView` (аркуші, плаваючий таб-бар) з fallback Surface1@92%.
- Хаптика на ключових подіях; Reduce Motion; Dynamic Type; портрет; `UIDeviceFamily=1`;
  iOS 16+; App Icon full-bleed; Launch Screen; Privacy manifest (мікрофон, без збору даних).
- Продуктивність: заміри аналізу 4-хв пісні на iPhone; `GraphicsView` без алокацій у `Draw`.
- Пайплайн: GitHub Actions macOS → TestFlight (для друзів і релізу); Hot Restart — для щоденних хотфіксів.

## 3. Ризики й що перевірити першим на айфоні

1. **ONNX Runtime + Hot Restart.** `Microsoft.ML.OnnxRuntime` тягне нативний xcframework;
   Hot Restart історично не підтримує нативні статичні бібліотеки/фреймворки з NuGet. Перший
   тест на пристрої — саме запуск моделі. Якщо не піде — збірка через macOS CI (TestFlight)
   стає основним шляхом на пристрій.
2. **Час аналізу** на iPhone (BTC + CQT на 22.05k): треба заміряти; можливо, потрібен
   `OverlapWindows=false` для довгих треків або прогрес по вікнах.
3. **YouTube**: YoutubeExplode ламається періодично — оновлення пакета + graceful-стан;
   у Store не заявляємо «завантаження».
4. **Швидкість відтворення на Windows-голові**: NAudio без time-stretch; або SoundTouch.Net,
   або тестувати швидкість лише на iOS.
5. **Маскування кліку метронома** потребує маленького API в `StreamingChordDetector`/
   `SlidingNeuralChordDetector` — узгодити з десктопом (той самий Core).
6. **StoreKit через плагін** може не покривати offer codes — тоді тонкий власний біндинг.

## 4. Definition of Done для v1

Усі екрани з макетів працюють на Windows-голові та iPhone у темній і світлій темі, uk/en;
безкоштовний шлях: тюнер (Standard), наживо, 20 пісень + 1/день, прості акорди, метроном,
3 пісні в редакторі; Pro відкриває решту; жодних сторонніх SDK аналітики; TestFlight-збірка
доступна друзям.
