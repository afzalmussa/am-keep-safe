# AM Keep Safe: design decisions

The product brief is the spec. This file records the decisions made while turning it into a build plan, so they're not lost in chat history. Update it whenever a decision changes.

## Agreed before phase 1 (25 October 2026)

**Repository and branches.** `main` starts from the first commit. Each phase is pushed to a working branch for review. No pull requests unless asked for.

**Licence.** The same as AM Speed Up: PolyForm Noncommercial 1.0.0, plus extra permission for anyone, businesses included, to run unmodified official releases.

**Shape of the solution.** Follows AM Speed Up:

- `AMKeepSafe.Core`: logic, wording rules, licensing, undo and service interfaces. Targets plain `net10.0`, so it builds and tests on Linux. CI proves this with a separate Linux job.
- `AMKeepSafe.Windows`: the real Windows versions of the Core interfaces.
- `AMKeepSafe`: the WPF app.
- Test projects: `AMKeepSafe.Tests` (Core and repository files, any OS), `AMKeepSafe.UI.Tests` (Windows only). `AMKeepSafe.Windows.Tests` arrives in phase 2 with the first Windows services. The brief suggested one test project. It's split so the Core tests can run anywhere.

**Copied from AM Speed Up, not shared.** Logging, undo journal, settings, theme, workflows and (in phase 6) licensing are copied and adapted rather than moved into a shared package. A shared package would need its own versioning and publishing for two small apps.

**Lemon Squeezy.** Product id, variant id and checkout URL are given at the start of phase 6. Until then the shipped settings refuse every key and hide "Get Pro". The 3-PC limit is set on the product in the Lemon Squeezy dashboard. Same rules as Speed Up: check online every 7 days, 30 days of grace without a successful check.

**No AM Software prompt next to results.** A red result never shows "Contact AM Software". Paid help is offered only in the footer, Scam help and Send to AM Software.

**Actions that can't be undone.** Uninstalling a remote access tool, closing browsers, running a Defender scan and updating an app with winget can't be reversed. The preview says "This can't be undone" before the user confirms. Every setting change can be undone.

**Weekly check location.** As with Speed Up's monthly tune-up, the scheduled check runs from a copy of the app in Program Files, so it keeps working if the download is deleted.

**Assets.** `am-icon-512.png` and `am-logo-wide.png` were supplied by the owner. `am-icon-white-edge.ico` and `am-icon-on-dark.png` are generated from the 512px shield with a thin white outline, matching Speed Up's `logo-on-dark.png`, so the dark shield shows on the dark header and on dark taskbars. The header uses `am-icon-on-dark.png` rather than `am-icon-512.png` for that reason.

## Research that shaped the wording

**Windows 10 support (checked 25 October 2026).** Free consumer Extended Security Updates were extended in June 2026 to 12 October 2027. PCs not yet enrolled can still enrol and receive the updates they missed. Businesses can buy ESU to October 2028. To confirm on Microsoft's own page in phase 2. The dates go in `data/checks.json`, not in code.

**Reporting fraud in the UK.** Report Fraud (reportfraud.police.uk, 0300 123 2040) replaced Action Fraud in December 2025 for England, Wales and Northern Ireland. In Scotland, report to Police Scotland on 101. Scam texts can be forwarded free to 7726. To recheck before release.

## How the trust rules are enforced in code

| Rule | Where |
| --- | --- |
| 1 No scare tactics, 3 never over-promise | `Core/Wording/WordingRules.cs`. `RepositoryWordingTests` runs every screen, data file and string in the app through it. |
| 9 Log everything | `Core/Logging/FileAppLog.cs`, one file a day under `%ProgramData%\AMSoftware\KeepSafe\logs`. |
| 5 Record and undo | `Core/Undo/UndoJournal.cs`. `Add` throws if the record can't be saved, so a change never goes ahead without one. |
| 10 Safety features always free | `Core/Features/Feature.cs`. `FeatureCatalogTests` fails if a safety feature is ever marked Pro. |
| 11 Dry run | `--dry-run`, parsed in `Core/Configuration/AppOptions.cs`. Every log line is marked `[DRY RUN]`. Fixes honour it from phase 3. |

Rules 2, 4, 6, 7 and 8 get their enforcement as the features they apply to are built.

**Undo against rule 6 (decided after phase 1).** Undoing a fix that switched a protection on (firewall, User Account Control, Defender real-time protection, the Windows Update service) would switch it off again, which rule 6 forbids. For those, the undo screen explains the change and opens the right Windows Settings page, so the person can change it back themselves if they really need to. Every other change (Remote Desktop, SMB1, browser policies) undoes inside the app.

## Phase 2: the Safety check

**How a check works.** Each check gathers facts (from the registry, or from a PowerShell script in the Windows project) and passes them to a pure rule in Core that returns an outcome code, such as `firewall.someOff`. `data/checks.json` maps each code to a colour, words, steps and buttons. Thresholds and dates live there too. So the wording and the severity can both be changed without touching code, and every rule is unit-tested with plain facts.

**Registry reading lives in Core, behind `IRegistryReader`.** The Windows project only provides the reader. Which keys mean what is business logic, and keeping it in Core means it's tested on any computer with a fake registry. PowerShell scripts live in the Windows project, as the brief asks, with their output parsed in Core so the parsing is tested too.

**Choices made on wording and colour:**

- Encryption on is blue ("For your information"), with the recovery key reminder and a button to Microsoft's recovery key page. The brief says the key matters more than whether encryption is on, and an amber result on every encrypted PC would be noise.
- Windows 11 24H2 Home and Pro ended on 13 October 2026. That shows amber with free steps to move to 25H2, not red, because Windows Update normally offers it automatically.
- Windows 10 that's had updates in the last 45 days is green with a note about 12 October 2027, since that's the best available sign it's enrolled in Extended Security Updates. Windows doesn't offer a reliable way to read enrolment directly.
- SMB1 and Remote Desktop are amber, not red. Red is kept for antivirus off, firewall off, permission prompts off and updates not working.
- Remote control apps found are blue, never called harmful. The brief's "tell us" became "go to Scam help" to keep AM Software's paid help away from results, as agreed. The owner can change this in `checks.json`.
- Results are sorted with red first, then amber, couldn't check, information and green, so nobody has to scroll to find what needs doing. The headline never contains a number.

**Status colours.** Red `#A12A1E`, amber `#8A4B00`, green `#0B6E4A`, blue `#1F4E8C`, grey `#4A4C66`, each on its own light background. `ThemeContrastTests` checks every one is at least 4.5:1 against white and against its background. Every status also has its own icon shape (triangle for amber, circle for the rest), symbol and label.

## Phase 3: fixes, preview and undo

**One route for every change.** A fix plans a list of exact setting changes (`SettingChange`: this setting, from this value, to that value). The person sees them in a preview. `FixRunner` then makes each one, and is the only code that asks the Windows project to change anything. For each change it:

1. refuses anything that would weaken protection (`ProtectionGuard`, trust rule 6),
2. records the old value in the undo journal, and stops if it can't (trust rule 5),
3. makes the change,
4. reads the setting back to be sure it took. Tamper Protection, for example, can quietly ignore a Defender change. A change that didn't take is marked "not made" and left off the undo list,
5. logs every step (trust rule 9).

**Trust rule 4 is enforced by the types.** `FixRunner.ApplyAsync` and `UndoService.UndoAsync` only accept a `FixPreview` the person has confirmed, and they apply exactly the changes listed in it. An unconfirmed preview throws before anything is touched. In the preview, Cancel is the default button.

**Trust rule 6 is enforced by the guard, not the screens.** `ProtectionGuard` blocks firewall off, Defender real-time off, System Restore off, disabling the update and security services, and the registry values that switch off UAC, Defender, automatic updates or the firewall. Undo goes through the same guard. A test runs every fix from a worst-case PC and checks that nothing planned would weaken protection, that every change marked "undo in Windows Settings" is one the guard would refuse to reverse, and that every change promised an in-app undo is one it allows.

**Fixes in this phase:** firewall on, permission prompts back on, Defender real-time protection on, update Defender's virus information, Defender quick scan, Windows Update service back to Manual, Remote Desktop off, SMB1 off, System Restore on. Fixes plan only what's needed: "Fix this" on something already fixed says there's nothing to change.

**Left as "How to fix" steps for now:** resuming paused updates and removing the "updates turned off" policy (no in-app undo would be allowed under rule 6, and a policy may be deliberate on a work PC), resuming paused encryption, and the Windows 11 version upgrade. Uninstalling remote control apps comes with Scam help in phase 5.

**After a fix,** that one check runs again and its result is replaced in the same place, so nothing jumps around under the mouse.

**Wording** for fixes, the preview, results and the undo list is in `data/fixes.json`, checked by the same wording tests as `checks.json`.

## Phase 4: the Browser check

**Read only, apart from one fix.** The Browser check reads Microsoft Edge, Google Chrome and Firefox for the Windows account the app runs as: every profile, its search engine and start pages, its add-ons, and which websites may send notifications. Chrome and Edge profile files (`Local State`, `Preferences`, `Secure Preferences`, add-on manifests) are only ever read, never written. Firefox's settings are read from `prefs.js`, `extensions.json`, `extension-settings.json`, `search.json.mozlz4` (decoded by a small LZ4 reader in Core) and `permissions.sqlite`, which is copied to a temporary file first so a running Firefox never sees it opened. A test checks that reading a profile leaves its files byte for byte and timestamp for timestamp as they were.

**What gets flagged, and how.** A search engine not on `data/search-engines.json`, or a well-known one reached through another company's link (`/yhs/`, `hspart=`), is amber. A search engine or start page set by an add-on names that add-on. Add-ons from outside the official store, or forced on by a policy, are amber with "Do you recognise this?", never called harmful. Only an add-on on `data/extensions-known-bad.json` is red. Notification permissions are blue, with a note that a real virus warning never comes from a website.

**Fixes are either "Remove these settings" or the browser's own page.** "Managed by your organisation" policies (the registry keys under `SOFTWARE\Policies` for the whole PC and for this account, and Firefox's `distribution\policies.json`) can be removed in the app. Everything else opens the right page in that browser (`edge://`, `chrome://` or `about:` addresses in `data/browser-check.json`) with short steps. The app never edits a profile, so it can't break one.

**Removing policies, trust rules 5 and 6.** A whole key or file is a new kind of change. `FixRunner` backs it up before removing it, as readable JSON for a registry key (every value with its exact type, and every key under it) or a straight copy for a file, into `%ProgramData%\AMSoftware\KeepSafe\backups`. If the copy can't be made, nothing is removed. The undo list records where the copy is, and Undo puts it back from there, refusing a copy made for a different key. `ProtectionGuard.Allows` only lets whole keys or files be removed or restored at the three browser policy keys and a `policies.json` inside a Firefox install folder, so this power can't reach anything else.

**Closing the browser.** Policies only take effect when the browser restarts. After removing them, if the browser is open, the app asks first ("Not now" is the default button), then closes it the polite way, as if each window's close button were clicked, so the browser can offer to restore tabs. It never forces a browser closed. If it's still open after 15 seconds, the person is asked to close it themselves.

**Work and school PCs.** If the PC is joined to a domain, to Microsoft Entra (Azure AD) or to a device management service, policies are probably deliberate. They show as blue information with no button, and the person is asked to check with whoever looks after the PC.

**Decided: the wrong account's browsers (the open question from phase 3).** The app now detects when it is running as a different account from the person signed in to Windows (someone approved the administrator prompt with another account). The Browser check then says whose browsers it checked and how to check the right ones. It doesn't try to read the other account's profile, because that would mean reading another person's files.

**The Safety check gets a "Browsers" row.** It runs the same browser check and shows one summary result, with a button that moves to the Browser check tab, where the full results are already waiting.

**Not yet confirmed: the known-bad add-on list.** The seven ids in `data/extensions-known-bad.json` came from search results quoting security reports, because the reports themselves couldn't be opened from the build environment. Each is marked `"confirmed": false`. They need checking against their source reports before release. Until then the list is short on purpose: a wrong id would call an honest add-on harmful.

## Open questions

- **OneDrive folders for the wrong person.** The Browser check now says when it's checking a different account (see phase 4). The Safety check's OneDrive backup result doesn't yet, so on a PC where a standard user approves the prompt with another account it reads the administrator's OneDrive. Decide in a later phase whether to add the same note there.
- **Browser page addresses.** The `edge://` addresses in `data/browser-check.json`, and whether Chrome and Edge open an internal page when it's given on the command line, need checking on a real PC. If one doesn't open, the app shows the address so the person can type it in.
- **Known-bad add-on ids** need confirming against their sources (see phase 4).
- **Windows 11 support dates** in `checks.json` come from Microsoft's notices as reported in October 2026, because learn.microsoft.com couldn't be reached from the build environment. Recheck them on Microsoft's release health page before release.
