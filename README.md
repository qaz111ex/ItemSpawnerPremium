# ItemSpawnerPremium

A standalone item spawner for PEAK. Press **F5** to open the browser and spawn any item in hand. Only **BepInEx** is required — no other mods.

> **Multiplayer note:** items are network-instantiated by the room host, so anything you spawn is visible to everyone in the room. Use it in your own lobby, or check with your teammates first.

> A continuation of the original `quackandcheese-ItemSpawner`, rebuilt as a single-DLL standalone mod.

Verified on PEAK `2.3.a` (Steam build 24961053, 2026-08-28).

## Features

- **Localized item names** — the UI is translated into all 15 game languages; item names follow the game's own localization table, and if a language column is missing there the English name is shown instead.
- **Smart search + Pinyin** (full spelling and initials, e.g. `regouchang` / `rgc` finds 热狗肠). Pinyin matching is enabled when the game language is Simplified or Traditional Chinese.
- **Multi-tag categories**: All / Tools / Food / Mystical / Equipment / Consumables / Misc.
- **Favorites**: right-click to toggle, heart marker, config persistence, favorites filter.
- **Hide-unused toggle** (default on): hides chess pieces, torn guidebook pages, `_Prop`/`_TEMP`/`_UNUSED`/`_Hidden` prefabs, and a short list of template/placeholder prefabs. Favorites are preserved when toggling.
- **Two UI styles**: HandDrawn (warm kraft paper, default) / Transparent.
- **Single DLL**: TinyPinyin and all 15 localization JSONs are bundled; only BepInEx is required.

## Installation

1. Install [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/).
2. Put `plugins/ItemSpawnerPremium.dll` into `BepInEx/plugins/`.
3. Launch the game and press **F5**.

## Configuration

`BepInEx/config/com.itemspawnerpremium.ItemSpawnerPremium.cfg`:

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| General | ToggleKey | F5 | Key to open/close the spawn menu (UnityEngine.KeyCode). |
| General | Style | HandDrawn | UI style: HandDrawn / Transparent. |
| General | HideUnused | true | Hide decorative/unused items; set `false` to show everything. |
| Favorites | ItemNames | `[]` | Favorite prefab names (JSON array). |

## Building

Prerequisites: .NET SDK (targets `net472`), and a PEAK install.

1. Point `GameDir` at your PEAK root (the folder containing `BepInEx` and `PEAK_Data`) — either pass
   `-p:GameDir="X:\path\to\PEAK"`, set a `GameDir` environment variable, or edit the default in
   `Directory.Build.props`. The build fails fast with a clear message if the path is wrong.
2. Run `dotnet build ItemSpawnerEnhancement.csproj -c Release`.
3. Output: `bin/Release/ItemSpawnerPremium.dll` (Debug goes to `bin/Debug/`, so the two never overwrite each other).

## Releasing

`pack.ps1` assembles `dist/ItemSpawnerPremium/` from a clean Release build. It refuses to run unless the
git working tree is clean and all four version strings agree (csproj, `Plugin.cs`, `package/manifest.json`,
`CHANGELOG.md`), it runs the unit tests, and it verifies the `ItemCatalog.cs` invariants via
`verify_catalog.py`. Zipping is opt-in (`-Pack`). See the comment header in `pack.ps1` for the full
release order.

## Tests

`tests/ItemSpawnerPremium.Tests` is a `net8.0` NUnit project with **no Unity or BepInEx dependency**: it
source-links only the pure-logic files (`SearchText.cs`, `SearchRanking.cs`, `ItemCatalog*.cs`,
`FavoriteCodec.cs`, `Localization/*`, `TinyPinyin/*`) and embeds the 15 language JSONs under the same
resource names the shipped DLL uses, so the real marker parsing and English fallback chain are exercised.

```
dotnet test tests/ItemSpawnerPremium.Tests/ItemSpawnerPremium.Tests.csproj
```

Covered: search normalisation and pinyin conversion (including the full BMP Han sweep for the decode
bound guard), ranking tiers and culture-independence, the catalog sort's total-order properties, hidden
rule vs. category table complementarity, favorite JSON round-trips and corrupt-input recovery, and
localization key completeness across all 15 languages.

## Project structure

- `Plugin.cs` — BepInEx entry point, config, F5 polling, GUIManager patch.
- `ItemSpawnerPremiumWindow.cs` — the `MenuWindow` subclass (pure-code UI).
- `ItemListView.cs` — list view, object pool, favorites, Unity lifecycle.
- `SearchText.cs` / `SearchRanking.cs` — pure-logic search normalisation, ranking and catalog ordering.
- `UiEnhancer.cs` — UI builder (two styles, SDF-baked sprites).
- `ItemCatalog.cs` / `ItemCatalogMethods.cs` — item catalog data + category logic.
- `FavoriteStore.cs` / `FavoriteCodec.cs` — favorites persistence and its pure-logic codec.
- `Warmup.cs` — panel warmup + render priming.
- `TinyPinyin/` — bundled pinyin library (MIT, locally modified).
- `Localization/` — 15 embedded language JSONs.
- `package/` — canonical `manifest.json` and `icon.png` used to build the release folder.
- `tests/` — unit tests (excluded from the mod DLL via `Compile Remove`).
- `pack.ps1` / `verify_catalog.py` — release assembly and catalog invariant checks.

## Acknowledgements

- **Original mod author:** [quackandcheese](https://thunderstore.io/c/peak/p/quackandcheese/ItemSpawner/) — `quackandcheese-ItemSpawner` (0.1.4).
- **Code reference:** [ItemSpawnerEnhanced](https://thunderstore.io/c/peak/p/lllei/ItemSpawnerEnhanced/) by lllei.
- **TinyPinyin:** [TinyPinyin.Net](https://github.com/hstarorg/TinyPinyin.Net) (MIT, © 2017 Jay.M.Hu).

## License

MIT — see [LICENSE](LICENSE). Third-party notices in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
