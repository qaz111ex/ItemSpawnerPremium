# Third-Party Notices

This project bundles the following third-party components. Their copyright notices and licenses are reproduced below.

## TinyPinyin (TinyPinyin.Net)

Bundled into `ItemSpawnerPremium.dll` for pinyin search. Source files were vendored into
`TinyPinyin/` (no NuGet reference); the upstream project publishes no version tags, so the
snapshot is identified by its vendoring date: **2026-08-18, from the `master` branch of
https://github.com/hstarorg/TinyPinyin.Net**.

**Modified.** Six files were vendored: `Engine.cs`, `PinyinHelper.cs` and the four data tables
(`Data/PinyinData.cs`, `Data/PinyinCode1.cs`, `Data/PinyinCode2.cs`, `Data/PinyinCode3.cs`). The four
data tables are character-for-character identical to upstream once line endings are normalised. The
two code files differ as follows:

- `TinyPinyin/Engine.cs` — `ToPinyin` was reduced from the upstream
  `(inputStr, trie, pinyinDictList, separator)` signature to `(inputStr, separator)`. The trie and
  dictionary branches, together with the whole `PinyinFromDict` method, were deleted: this mod only
  needs per-character conversion, so word-level disambiguation is dead weight. Consequently
  upstream's `IPinyinDict.cs` and `PinyinDict.cs` are **not** vendored at all.
- `TinyPinyin/Engine.cs` — `GetPinyinByChar` now bounds-checks the decoded index against
  `PinyinData.PINYIN_TABLE.Length` and falls back to the original character instead of throwing
  `IndexOutOfRangeException`.
- `TinyPinyin/PinyinHelper.cs` — `GetPinyinInitials` returns early for `null`/empty input, and
  `GetPinyin(string, string)` calls the two-argument `Engine.ToPinyin` overload above.

Not a local change: the guard in `GetPinyinInitials` against empty segments produced by the `|`
separator (upstream issue
[hstarorg/TinyPinyin.Net#5](https://github.com/hstarorg/TinyPinyin.Net/issues/5)) is **upstream's
own fix**, retained here unmodified — earlier revisions of this file misattributed it.

Source: https://github.com/hstarorg/TinyPinyin.Net

MIT License

Copyright (c) 2017 Jay.M.Hu

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## ItemSpawner (quackandcheese-ItemSpawner)

ItemSpawnerPremium is based on the behavior of the original `quackandcheese-ItemSpawner` (0.1.4). The original mod's DLL and AssetBundle are not redistributed.

Source: https://thunderstore.io/c/peak/p/quackandcheese/ItemSpawner/

MIT License

Copyright (c) 2025, 'QuackAndCheese'

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## ItemSpawnerEnhanced

**Portions derived.** Two files in this project started from the corresponding files in
`lllei-ItemSpawnerEnhanced` and still share their structure line for line, including non-trivial
details that would not coincide independently:

- `Localization/LocalizationCatalog.cs` — same nested
  `Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)` shape, same
  `const string marker = ".Localization."` resource-name probe, same
  `IndexOf(marker, StringComparison.Ordinal)` + `EndsWith(".json", …)` filter, the same substring
  arithmetic that extracts the language code, and the same two-level lookup (requested language when
  the value is non-whitespace, then `"en"`, then the key itself). Added here: a known-language-code
  whitelist, per-file `try`/`catch` so one corrupt JSON cannot break the rest, an injected warning
  callback (which is what keeps the type unit-testable), and a C# 7.3 rewrite.
- `Localization/GameLanguage.cs` — the same `LocalizedText.Language` → language-code mapping, with the
  same 15 codes in the same order, rewritten from a switch expression to a C# 7.3 switch statement
  (English is listed explicitly here rather than relying only on the default arm).

Separately, this project was **informed by the design** of that mod's right-click favorite trigger
and favorite-store pattern; those are idiomatic Unity/BepInEx shapes rather than derived code, and
the implementations here differ substantially (write-ahead persistence ordering, report-only pruning,
`OrdinalIgnoreCase` comparison throughout).

No files or binaries from that project are shipped verbatim; the derived code above is covered by the
MIT license reproduced below, whose conditions this notice satisfies.

Source: https://github.com/llleixx/ItemSpawnerEnhanced

MIT License

Copyright (c) 2026 lllei

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
