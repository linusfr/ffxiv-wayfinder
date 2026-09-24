# Wayfinder

[![latest](https://img.shields.io/github/v/release/linusfr/ffxiv-wayfinder?sort=semver&display_name=tag&label=latest&color=blue&cacheSeconds=300)](https://github.com/linusfr/ffxiv-wayfinder/releases/latest)
[![ci](https://img.shields.io/github/actions/workflow/status/linusfr/ffxiv-wayfinder/ci.yml?branch=main&label=ci&cacheSeconds=300)](https://github.com/linusfr/ffxiv-wayfinder/actions/workflows/ci.yml)
[![licence](https://img.shields.io/github/license/linusfr/ffxiv-wayfinder?color=blue)](LICENSE)

> Type the name instead of hunting the category.

The Duty Finder knows about several hundred duties and asks you which of ten
tabs they are in. The Teleport window knows every aetheryte you have attuned and
asks you which region it was. Wayfinder adds the one thing both are missing —
a search box — and then gets out of the way:

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

Picking a result hands it to the game. The native window switches category and
ticks the duty itself; queueing, unlock checks, level and role requirements,
gil costs and confirmations are all still the game's, untouched. This plugin
finds things. It does not do things.

## Integrations

| Window | Addon | What you search | What a pick does |
|---|---|---|---|
| Duty Finder | `ContentsFinder` | Every duty and roulette the game lists in the Duty Finder | Calls the game's own duty-link handler, which opens the right category and selects the entry |
| Teleport | `Teleport` | Every aetheryte this character has attuned, with its gil cost | Calls the game's teleport function — the same one a click on the row reaches |

## Examples

Matching is case-insensitive, fuzzy, and works on whatever language your client
is set to.

| You type | You get |
|---|---|
| `aurum`, `AURUM`, `arum` | The Aurum Vale |
| `vale` | The Aurum Vale |
| `alex` | every Alexander wing |
| `toto` | The Thousand Maws of Toto-Rak |
| `longstop` | Brayflox's Longstop |
| `raid` | the duties the game files under raids |
| `limsa` | Limsa Lominsa Lower Decks, Limsa Lominsa Upper Decks |
| `radz` | Radz-at-Han |
| `tuliy` | Tuliyollal |
| `thanalan` | the aetherytes standing in Thanalan |

Results are ranked by how the query matched, strongest first: exact name, then
prefix, then start-of-word, then substring, then loose character match. A
literal hit always outranks a fuzzy one, so typing more never pushes what you
wanted further down.

## Install

`/xlsettings` → **Experimental** → Custom Plugin Repositories → paste, `+`, save:

```
https://raw.githubusercontent.com/linusfr/ffxiv-wayfinder/main/pluginmaster.json
```

Then `/xlplugins` → search **Wayfinder** → Install. Or grab
[`Wayfinder.zip`](https://github.com/linusfr/ffxiv-wayfinder/releases/latest/download/Wayfinder.zip)
and extract into `~/.xlcore/devPlugins/Wayfinder/` (Linux) or
`%AppData%\XIVLauncher\devPlugins\Wayfinder\` (Windows) — a manual zip never
self-updates, so prefer the repo.

## Use

Open the Duty Finder or the Teleport window as usual. The search box appears
underneath it and follows it around if you drag the window.

| | |
|---|---|
| Click the box | Focus it |
| Type | Filters as you go |
| ↑ / ↓ | Move through results |
| Enter | Pick the highlighted result |
| Click a result | Pick it |
| Escape | Clear the search |
| Escape again | Leave the search box |
| Escape once more | Closes the game's window, as always |

Keyboard input is only taken while the box is focused. With the box unfocused
the plugin reads nothing, so every game hotkey behaves as if it were not
installed.

## Configuration

`/wayfindercfg`, or the cog in `/xlplugins`.

| Setting | Default | |
|---|---|---|
| Duty Finder | on | |
| Teleport | on | |
| Matching | Balanced | `Strict` drops the fuzzy tier entirely; `Loose` accepts any letters in order. |
| Focus the search box when the window opens | off | The game's own windows don't grab the keyboard on open; one that does eats your first keystroke. |
| Escape clears the search | on | Off leaves Escape to the game entirely. |

## Limitations

- **The search box is an overlay, not a native widget.** It is drawn flush under
  the game window rather than inside it, and it will not pick up a custom game
  UI theme. See [How it works](#how-it-works) for why injecting a real
  `AtkComponentTextInput` was the wrong trade.
- **Duties you have not unlocked are shown greyed out, not hidden.** The unlock
  check is reliable for `ContentLinkType 1` — dungeons, trials, raids, nearly
  everything — and everything else is reported as available rather than guessed
  at. Clicking a locked duty does nothing, because the game's own selection
  function declines it.
- **Housing destinations are labelled by district**, e.g. `Mist (3-12)` for ward
  3, plot 12. The native list uses estate wording this plugin has no localised
  source for; searching the district name still finds them.
- **The aethernet window (`TelepotTown`) is not covered** — only the main
  Teleport window is.
- **Struct offsets come from FFXIVClientStructs**, so a game patch may need a
  Dalamud update first. If an integration throws five times it turns itself off
  for the session and says so in `/xllog` rather than repeating the failure.

## How it works

**Nothing is hooked.** Both integrations use published Dalamud services:
`IAddonLifecycle` to know when a window opens, refreshes and closes,
`AtkUnitBasePtr` to read where it is on screen, `IDataManager` and
`IAetheryteList` for the data, and `IUnlockState` for unlock state. There is no
detour on any game function.

**Selection is one call into the game.** For duties that is
`AgentContentsFinder.OpenRegularDuty` / `OpenRouletteDuty` — the functions
behind a duty link in chat and the Duty Finder button in the Timers window. They
switch category and tick the entry, which is why there is no
category-navigation, list-filtering or queueing code here at all. For teleports
it is `Telepo.Teleport`, the client function a click on a native row reaches,
called with an id taken straight from the game's own list of destinations.

**Why an overlay and not a native text field.** This was the first thing
investigated, and it does not hold up:

- Dalamud ships no node-construction API. `IAddonEventManager` attaches events
  to nodes that already exist; nothing creates one. Building an
  `AtkComponentTextInput` means hand-rolled `IMemorySpace` allocation, ULD asset
  wiring, IME plumbing and manual teardown, against offsets that move on patch
  day — where a leak or a double free is a game crash, not a missing search box.
- Filtering the native list is worse. Both windows draw from an
  `AtkComponentTreeList` that the agent repopulates on every update, so a
  plugin-side filter would be overwritten continuously and would have to fight
  the game for the list every refresh.
- Both windows fill their top edge with controls — the Duty Finder's category
  radio buttons, the Teleport window's region tabs. A search box drawn inside
  the frame would cover one of them, and covering category navigation to add
  search defeats the point.

Docked underneath, the overlay covers nothing, needs no hooks, and degrades to
*not drawn* when anything is null. It borrows the game's own Axis font and
window colouring so it reads as part of the window it is attached to.

**Names are never hardcoded.** Duties come from `ContentFinderCondition` and
`ContentRoulette`, filtered by the game's own `IsInDutyFinder` flag; teleport
destinations from `Telepo`'s live list via `IAetheryteList`, named by their
`PlaceName` row. All of it is whatever language the client is running in.
Matching folds case and combining marks, so a German client finds `Höhle` by
typing `hohle`, but nothing is transliterated and nothing is translated here.

**Cost.** Candidates are folded into their searchable form once, when a window
opens or the game refreshes it. Results are recomputed only when the query text
actually changes. Drawing a frame reads the last result list and allocates
nothing. There is no per-frame scan for the game windows — the addon pointer
comes from the lifecycle events, and `IGameGui.GetAddonByName` is called exactly
once per integration, at load, to catch a window that was already open.

## Adding another window

The core is not duty- or teleport-specific. A new integration is one interface
and one line:

```csharp
internal sealed class EmoteIntegration : SearchIntegration
{
    public EmoteIntegration() : base(new EmoteProvider()) { }

    protected override bool IsEnabled(Configuration config) => config.EmoteSearch;

    private sealed class EmoteProvider : ISearchProvider
    {
        public string AddonName   => "Emote";
        public string Placeholder => "Search emotes...";

        public IReadOnlyList<SearchCandidate> BuildCandidates() => /* game data */;
        public void Activate(SearchCandidate candidate)         => /* the game's own call */;
    }
}
```

Then add it to `_integrations` in `Plugin.cs`. Lifecycle tracking, anchoring,
the overlay, ranking, keyboard handling and the failure budget are all shared.

The one rule: `Activate` makes a single call into the game and does nothing
else. Anything that reimplements what the native window already does — a queue,
a teleport, a restriction check — belongs in the game, not here.

## Safety

This plugin does not queue for duties, teleport on its own, skip confirmation
dialogs, bypass unlock or level requirements, alter gil costs, or automate any
part of play. It filters lists and forwards one click.

## Development

```bash
just build      # debug build
just install    # build and drop into devPlugins
just check      # pre-commit hooks
just fmt        # dotnet format (style + analyzers)
just hooks      # install git hooks, once
```

`src/Search/` is the game-agnostic search engine, `src/Integrations/` one class
per native window, `src/UI/` the overlay and settings.

Hermit pins prek/gitleaks/jq; the .NET SDK comes from nixpkgs via the justfile.
CI builds on `windows-latest` because Dalamud needs the Windows targeting pack.
`Wayfinder.json` is generated by DalamudPackager from the csproj properties —
edit it there, not as a file.

`go-semantic-release` reads conventional commits on `main`: `fix:` → patch,
`feat:` → minor, `feat!:` → major. The first release is always `1.0.0` whatever
the message says. CI tags, attaches the zip, and updates `pluginmaster.json`.

## Licence

MIT — see [`LICENSE`](LICENSE).
