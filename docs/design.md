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

## Open questions

- **Undo against rule 6.** Undoing "turned the firewall on" means turning it off again, which rule 6 forbids. Raised with the owner at the end of phase 1. To decide before phase 3.
