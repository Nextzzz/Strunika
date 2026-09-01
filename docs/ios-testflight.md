# Strunika on an iPhone, from a Windows machine

There is no way around macOS: putting a build on a device without a Mac needed
Visual Studio's Hot Restart, which **Visual Studio 2026 removed**, and which
never supported **XCFrameworks** — and Strunika links ONNX Runtime as one. So a
macOS runner on GitHub Actions builds and signs, and TestFlight installs.

Nothing here needs a Mac of your own. The certificate is made with OpenSSL on
Windows (Keychain Access is only the usual way, not the only one), and
TestFlight installs over the air, so the phone's UDID never has to be
registered either.

The workflow is `.github/workflows/ios-testflight.yml`. It is manual
(*Actions ▸ iOS · TestFlight ▸ Run workflow*): macOS minutes bill at ten times
the rate on a private repository.

## Signing material lives outside the repository

`~/Strunika-signing/` (that is `C:\Users\taras\Strunika-signing`) holds:

| file | what it is |
|---|---|
| `ios_dist.key` | the private key. **Losing it means revoking the certificate and starting over.** Never commit it. |
| `ios_dist.csr` | the request you upload to Apple |
| `ios_dist.cer` | what Apple gives back |
| `ios_dist.p12` | key + certificate together, which is what CI imports |

## One-time setup

### 1. Apple Developer portal — <https://developer.apple.com/account>

1. **Identifiers ▸ +** → App IDs → App → Description "Strunika", Bundle ID
   **explicit** `app.strunika.mobile`. No capabilities are needed yet.
2. **Certificates ▸ +** → **Apple Distribution** → upload `ios_dist.csr` →
   download the `.cer` and save it as `~/Strunika-signing/ios_dist.cer`.
3. Ask Claude to build the `.p12` (it runs the OpenSSL steps and prints the
   base64 for the secrets), or do it yourself:

   ```bash
   cd ~/Strunika-signing
   openssl x509 -in ios_dist.cer -inform DER -out ios_dist.pem -outform PEM
   openssl pkcs12 -export -inkey ios_dist.key -in ios_dist.pem -out ios_dist.p12
   ```

4. **Profiles ▸ +** → Distribution → **App Store Connect** → App ID
   `app.strunika.mobile` → the certificate from step 2 → name it
   `Strunika App Store` → download the `.mobileprovision`.

### 2. App Store Connect — <https://appstoreconnect.apple.com>

1. **Apps ▸ +** → New App → iOS → name, language, Bundle ID
   `app.strunika.mobile`, any SKU.
2. **Users and Access ▸ Integrations ▸ App Store Connect API ▸ +** → role
   **App Manager** → download the `AuthKey_XXXXXXXXXX.p8` (**offered once**) and
   note the **Key ID** and, above the list, the **Issuer ID**.
3. **TestFlight ▸ Internal Testing** → add yourself as a tester. Internal
   testing needs no review; external testers do.

### 3. GitHub secrets

*Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret.* Values
that are files go in base64 — in Git Bash, `base64 -w0 <file>` prints one line:

| secret | value |
|---|---|
| `IOS_DIST_CERT_P12` | `base64 -w0 ~/Strunika-signing/ios_dist.p12` |
| `IOS_DIST_CERT_PASSWORD` | the password the `.p12` was exported with |
| `IOS_PROVISION_PROFILE` | `base64 -w0 <the .mobileprovision>` |
| `APPSTORE_KEY_ID` | the 10-character Key ID |
| `APPSTORE_ISSUER_ID` | the issuer UUID |
| `APPSTORE_API_KEY` | `base64 -w0 AuthKey_XXXXXXXXXX.p8` |

## Every build after that

1. *Actions ▸ iOS · TestFlight ▸ Run workflow.* The build number is the run
   number, so it always climbs; the version stays `ApplicationDisplayVersion`
   in the project file.
2. The `.ipa` is attached to the run as an artifact whatever happens, so a
   failed upload never costs the build.
3. App Store Connect processes the build for a few minutes, then it appears in
   TestFlight on the phone (install the TestFlight app, sign in with the same
   Apple ID).

## What to expect on the first device run

The Windows head has carried the app so far, so these are unverified on a
phone: the YouTube player inside WKWebView, the audio session as the tuner and
the song page hand it back and forth, the seek bar's thumb hit area, fonts and
safe-area insets, and the chord strum. Reports beat guesses — the log is on the
device under *Files ▸ Strunika*, and TestFlight collects crashes.

## Store risk, unchanged

Guideline 5.2.3 makes on-device YouTube extraction the risky part of review.
Internal TestFlight builds are not reviewed, so this route does not test that
question either way. `Services/RemoteFlags` still exists to turn the YouTube
path off remotely.

## Why the iOS head is on .NET 10, and why Hot Restart is not the plan

The SDK 9 pin (74fc5a6, 24 Aug 2026) existed for one reason: Visual Studio
2022's Hot Restart, the only way to push a build to an iPhone from Windows
without a Mac. That path is closed on two independent counts, neither of which
the .NET 10 move created:

- Hot Restart **never supported XCFrameworks**, and this app links
  `onnxruntime.xcframework` for the tuner and chord recognition. It would have
  failed on this project on any .NET version.
- Visual Studio 2026 (the version on the workstation now) **dropped Hot
  Restart** altogether; VS 2022 is not installed.

So "F5 from Windows to the phone" was not traded away — it was never available
here. What the .NET 10 move bought is a working trimmer with YoutubeExplode 6.6
(the release that made YouTube import work again), and with it a small app.

Deployment on a device is therefore the macOS runner and TestFlight described
above. The Windows head stays on net9.0 / MAUI 9 so the daily loop on the PC is
unchanged; Core and Neural are net9.0 libraries, which a net10.0 app consumes
without issue.
