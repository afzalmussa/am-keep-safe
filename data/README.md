# Data files

Everything the app says, and the lists it checks against, lives in this folder as readable JSON so it can be reviewed and edited without touching code. The files are built into the app, so what's here is exactly what ships.

| File | What it holds | Added in |
| --- | --- | --- |
| `checks.json` | Every safety check's name, wording, colour for each outcome, thresholds, support dates and help steps | Phase 2 |
| `remote-tools.json` | Remote control tools to list on the Safety check | Phase 2 |
| `fixes.json` | What each fix, the preview and the undo list say, and which results each fix is offered for | Phase 3 |
| `browser-check.json` | Everything the Browser check says, each browser's own settings page addresses, and the note shown when a different account's browsers were checked | Phase 4 |
| `search-engines.json` | Well-known search engines, matched by address, and the signs of a search going through another company's link | Phase 4 |
| `extensions-known-bad.json` | Browser add-ons removed from the store after they were found to be harmful, each with the report that names it. Ids marked `"confirmed": false` still need checking against their source | Phase 4 |
| `scam-help.json` | The text for each Scam help situation | Phase 5 |

Every piece of text in these files is checked by the tests against the app's wording rules: no scare words, no "your PC is safe" promises, no exclamation marks and no jargon. If a test fails after an edit, its message says which file, which line of text and which rule.
