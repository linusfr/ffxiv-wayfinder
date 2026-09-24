# Wayfinder

[![latest](https://img.shields.io/github/v/release/linusfr/ffxiv-wayfinder?sort=semver&display_name=tag&label=latest&color=blue&cacheSeconds=300)](https://github.com/linusfr/ffxiv-wayfinder/releases/latest)
[![ci](https://img.shields.io/github/actions/workflow/status/linusfr/ffxiv-wayfinder/ci.yml?branch=main&label=ci&cacheSeconds=300)](https://github.com/linusfr/ffxiv-wayfinder/actions/workflows/ci.yml)
[![licence](https://img.shields.io/github/license/linusfr/ffxiv-wayfinder?color=blue)](LICENSE)

> Type the name instead of hunting the category.

The Duty Finder asks which of ten tabs your duty is in. The Teleport window asks
which region. Wayfinder adds a search box to both and then gets out of the way:

```text
┌─ Duty Finder ─────────────────────────────┐
│  (the game's window, untouched)           │
└───────────────────────────────────────────┘
┌───────────────────────────────────────────┐
│ 🔍 aurum                                  │
├───────────────────────────────────────────┤
│ The Aurum Vale                    Lv. 47  │
└───────────────────────────────────────────┘
```

Picking a result hands it to the game — one call into the same function the
native row-click reaches. Queueing, unlock and level checks, gil costs and
confirmations all stay where the game put them. It finds things; it does not do
things.

| Window | Addon | Searches | A pick calls |
|---|---|---|---|
| Duty Finder | `ContentsFinder` | every duty and roulette the game lists there | `AgentContentsFinder.OpenRegularDuty` / `OpenRouletteDuty` — the game's own duty-link handler, which switches category and selects |
| Teleport | `Teleport` | every aetheryte you've attuned, with gil cost | `Telepo.Teleport` |

## Search

Case-insensitive, fuzzy, and in whatever language your client runs.

| Type | Get |
|---|---|
| `aurum`, `AURUM`, `arum` | The Aurum Vale |
| `vale` | The Aurum Vale |
| `alex` | every Alexander wing |
| `raid` | what the game files under raids |
| `limsa` | both Limsa decks |
| `tuliy` | Tuliyollal |

Ranked exact → prefix → word-start → substring → loose character match, so a
literal hit always outranks a fuzzy one and typing more never pushes what you
wanted down the list.

## Install

`/xlsettings` → **Experimental** → Custom Plugin Repositories → paste, `+`, save:

```
https://raw.githubusercontent.com/linusfr/ffxiv-wayfinder/main/pluginmaster.json
```

Then `/xlplugins` → **Wayfinder** → Install.

## Use

The box appears under the game window and follows it if you drag it.

| | |
|---|---|
| Click the box | Focus it |
| ↑ / ↓ | Move through results |
| Enter, or click | Pick |
| Escape | Clear the search |
| Escape again | Leave the box; the next one closes the game window |

Keyboard input is read only while the box is focused, so every game hotkey
behaves as if the plugin were not installed.

## Configuration

`/wayfindercfg`, or the cog in `/xlplugins`.

| Setting | Default | |
|---|---|---|
| Duty Finder / Teleport | on | Per-window switches. |
| Matching | Balanced | `Strict` drops the fuzzy tier; `Loose` takes any letters in order. |
| Focus the box on open | off | The game's own windows don't grab the keyboard either. |
| Escape clears the search | on | Off leaves Escape to the game entirely. |

## Limitations

- **It's an overlay, not a native widget** — docked under the window, so it won't
  pick up a custom UI theme. Dalamud ships no node-construction API, and both
  windows' lists are repopulated by the agent on every update, so a real text
  field *and* a filtered native list would each mean fighting the game every
  frame.
- **Locked duties are dimmed, not hidden.** The unlock check is reliable for
  `ContentLinkType 1` (dungeons, trials, raids); everything else is reported
  available rather than guessed at. Clicking one does nothing — the game's own
  selection function declines it.
- **Housing shows as district plus numbers**, e.g. `Mist (3-12)`. The native
  estate wording has no localised source here; the district name still finds it.
- **The aethernet window (`TelepotTown`) isn't covered.**
- A game patch may need a Dalamud update first. An integration that throws five
  times disables itself for the session and says so in `/xllog`.

## How it works

Nothing is hooked. `IAddonLifecycle` for open and close, `AtkUnitBasePtr` for
on-screen position, `IDataManager` / `IAetheryteList` / `IUnlockState` for data.
Names come from the game's sheets and from `Telepo`'s live list, never a
hardcoded list; matching folds case and combining marks, so a German client finds
`Höhle` by typing `hohle`.

Candidates are folded into searchable form once per window opening, results
recompute only when the query changes, and drawing a frame allocates nothing.

Another searchable window is one `ISearchProvider` — addon name, candidates, and
an `Activate` that makes a single call into the game — plus a line in
`_integrations`. Everything else is shared. `Activate` doing more than that one
call is the thing the design forbids.

## Development

```bash
just build      # debug build
just install    # build and drop into devPlugins
just check      # pre-commit hooks
just fmt        # dotnet format
```

`src/Search/` is the game-agnostic engine, `src/Integrations/` one class per
window, `src/UI/` the overlay and settings.

CI builds on `windows-latest` because Dalamud needs the Windows targeting pack.
`Wayfinder.json` is generated by DalamudPackager from the csproj — edit it there,
not as a file. `go-semantic-release` reads conventional commits on `main` and
updates `pluginmaster.json`.

## Licence

MIT — see [`LICENSE`](LICENSE).
