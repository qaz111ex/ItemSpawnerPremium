# ItemSpawnerPremium

A standalone client-side item spawner for PEAK. Press **F5** to open the browser and spawn any item in hand. Only **BepInEx** is required — no other mods.

> A continuation of the original `quackandcheese-ItemSpawner`, rebuilt as a single-DLL standalone mod.

## Features

- **Localized item names** in all 15 game languages (resolved via `LocalizedText`).
- **Smart search + Pinyin** (full spelling and initials, e.g. `regouchang` / `rgc` finds 热狗肠).
- **Multi-tag categories**: All / Tools / Food / Mystical / Equipment / Consumables / Props.
- **Favorites**: right-click to toggle, heart marker, config persistence, favorites filter.
- **Hide-unused toggle** (default on): hides chess pieces, torn guidebook pages, and `_Prop`/`_TEMP`/`_UNUSED`/`_Hidden` prefabs.
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

1. Edit `Directory.Build.props` and set `<GameDir>` to your PEAK root (the folder containing `BepInEx` and `PEAK_Data`).
2. Run `dotnet build ItemSpawnerEnhancement.csproj`.
3. Output: `bin/ItemSpawnerPremium.dll`.

## Project structure

- `Plugin.cs` — BepInEx entry point, config, F5 polling, GUIManager patch.
- `ItemSpawnerPremiumWindow.cs` — the `MenuWindow` subclass (pure-code UI).
- `ItemListView.cs` — list view, search/pinyin scoring, object pool, favorites.
- `UiEnhancer.cs` — UI builder (two styles, SDF-baked sprites).
- `ItemCatalog.cs` / `ItemCatalogMethods.cs` — item catalog data + category logic.
- `FavoriteStore.cs` — favorites persistence.
- `Warmup.cs` — panel warmup + render priming.
- `TinyPinyin/` — bundled pinyin library (MIT).
- `Localization/` — 15 embedded language JSONs.

## Acknowledgements

- **Original mod author:** [quackandcheese](https://thunderstore.io/c/peak/p/quackandcheese/ItemSpawner/) — `quackandcheese-ItemSpawner` (0.1.4).
- **Code reference:** [ItemSpawnerEnhanced](https://thunderstore.io/c/peak/p/lllei/ItemSpawnerEnhanced/) by lllei.
- **TinyPinyin:** [TinyPinyin.Net](https://github.com/hstarorg/TinyPinyin.Net) (MIT, © 2017 Jay.M.Hu).

## License

MIT — see [LICENSE](LICENSE). Third-party notices in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
