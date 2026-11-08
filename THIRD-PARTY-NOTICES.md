# Third-Party Notices and License Compliance

MVZ2CloudSaves itself is licensed under **GNU GPL-3.0** — see [LICENSE](LICENSE).
The project is Copyright (C) 2026 Alhugh233.

This file distinguishes **what is actually redistributed** from **what is only a
runtime prerequisite**. That distinction matters for GPL/LGPL/Apache obligations.

---

## 0. Full license texts bundled with this project

Every license referenced below is shipped verbatim under `LICENSES/`, so a
release package is self-contained and does not rely on external links:

| File | License | Applies to |
| --- | --- | --- |
| [LICENSE](LICENSE) / [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt) | GPL-3.0 | this project; adapted MiniJson; MVZ2 (runtime) |
| [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt) | Apache-2.0 | `libs/LanguagePackEnhance.API.dll` |
| [LICENSES/LanguagePackEnhance.API-NOTICE.txt](LICENSES/LanguagePackEnhance.API-NOTICE.txt) | Apache-2.0 NOTICE | `libs/LanguagePackEnhance.API.dll` |
| [LICENSES/LGPL-2.1.txt](LICENSES/LGPL-2.1.txt) | LGPL-2.1-only | BepInEx (runtime prerequisite, not bundled) |
| [LICENSES/MIT-HarmonyX.txt](LICENSES/MIT-HarmonyX.txt) | MIT | HarmonyX (runtime prerequisite, not bundled) |
| [LICENSES/MIT-MonoMod.txt](LICENSES/MIT-MonoMod.txt) | MIT | MonoMod (runtime prerequisite, not bundled) |

The LGPL/MIT texts are included for transparency and to make the licensing
picture auditable. They are **not** an admission that those binaries are
redistributed: this project ships none of them.

---

## 1. Redistributed in this repository

### LanguagePackEnhance.API

- File: `libs/LanguagePackEnhance.API.dll`
- License: **Apache-2.0**
- Copyright (C) 2026 Alhugh233
- Project: <https://github.com/Alhugh233/MVZ2-LPEAPI>
- License text: [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt)
- NOTICE: [LICENSES/LanguagePackEnhance.API-NOTICE.txt](LICENSES/LanguagePackEnhance.API-NOTICE.txt)

This is the **thin API assembly** only. It contains the public API surface
(`LanguagePackEnhanceApi`, `TranslationEntry`, `ILanguagePackEnhanceService`)
and is intentionally Apache-2.0 so mods can reference it without taking the
GPL-3.0 implementation.

This project references this API only (`Private=false`). It does **not**
reference or redistribute `LanguagePackEnhance.dll`.

Apache-2.0 §4 obligations satisfied here:

- recipients receive a copy of the Apache-2.0 license;
- the NOTICE file is preserved;
- the copyright/attribution notice is retained;
- no modified Apache-2.0 files are distributed (the binary is unmodified).

---

## 2. Runtime prerequisites — NOT redistributed

These are separate programs/plugins. MVZ2CloudSaves declares dependencies on
some of them, but does not copy their binaries or source into this repository
or its release output. Users install them separately.

| Component | License | Relationship | License text |
| --- | --- | --- | --- |
| DSHCore | GPL-3.0 | hard runtime dependency; separate plugin | [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt) |
| LanguagePackEnhance (plugin) | GPL-3.0 | hard runtime dependency; separate plugin | [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt) |
| BepInEx | LGPL-2.1-only | runtime prerequisite; not bundled | [LICENSES/LGPL-2.1.txt](LICENSES/LGPL-2.1.txt) |
| HarmonyX | MIT | provided by BepInEx; not bundled | [LICENSES/MIT-HarmonyX.txt](LICENSES/MIT-HarmonyX.txt) |
| MonoMod | MIT | provided by BepInEx; not bundled | [LICENSES/MIT-MonoMod.txt](LICENSES/MIT-MonoMod.txt) |
| MVZ2 (game) | GPL-3.0 | game itself; not bundled | [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt) |

### Why LanguagePackEnhance is listed separately from its API

LPE is deliberately split into:

- `LanguagePackEnhance.API.dll` — Apache-2.0 thin API, referenced at compile time;
- `LanguagePackEnhance.dll` — GPL-3.0 implementation, installed by the user.

MVZ2CloudSaves uses only the API assembly at compile time. The implementation
plugin is a separate runtime service. It is listed here because it is a GPL-3.0
runtime prerequisite, not because it is linked into or redistributed with this
mod.

The project is GPL-3.0 itself, so even the runtime interaction is
license-compatible.

### LGPL note

BepInEx is LGPL-2.1-only and is **not** redistributed here. If you produce a
self-contained package that includes BepInEx binaries, you must additionally
comply with LGPL-2.1: include the license, copyright notices, and the means to
replace/relink the LGPL component. This project's `-Pack` output does not
include BepInEx. The full LGPL-2.1 text is shipped at
[LICENSES/LGPL-2.1.txt](LICENSES/LGPL-2.1.txt) for reference.

### MIT note

HarmonyX (Copyright (C) 2020 BepInEx) and MonoMod (Copyright (C) 2015-2020
0x0ade) are MIT-licensed and are provided by BepInEx at runtime. Their full
texts are shipped at [LICENSES/MIT-HarmonyX.txt](LICENSES/MIT-HarmonyX.txt) and
[LICENSES/MIT-MonoMod.txt](LICENSES/MIT-MonoMod.txt). MIT requires the notice to
accompany copies of the software; because we do not copy these binaries, the
obligation falls to whoever distributes BepInEx.

---

## 3. Source code adapted from other GPL projects

### MiniJson

- Adapted from: **MVZ2-MusicBox**, `Core/MusicCore.cs`
- Copyright (C) 2026 Alhugh233
- License: **GPL-3.0**
- Project: <https://github.com/Alhugh233/MVZ2-MusicBox>
- Adapted file: `Core/MiniJson.cs`
- Change notice: parser retained and adapted for MVZ2CloudSaves;
  the JSON writer (`JsonWriter.cs`) is original to this project.

The source file carries a prominent attribution/change header. Both the origin
and this project are GPL-3.0, so the adaptation is license-compatible. The
complete GPL-3.0 text is in [LICENSE](LICENSE) and
[LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt).

---

## 4. Source code availability (GPL-3.0 §6)

MVZ2CloudSaves is GPL-3.0. The complete corresponding source is:

1. the public MVZ2CloudSaves source repository at the release tag/commit, and
2. the "Source code (zip)" / "Source code (tar.gz)" archives that GitHub
   generates automatically for that Release.

The optional `src/` directory produced by `build.ps1 -Pack` may also be
included; the release zip omits it to keep the download small. In that case the
repository tag and GitHub's source archive are the corresponding source.

`SOURCE.txt` points to this. If a binary is distributed without access to that
source, the distributor must provide the complete source or a valid GPL-3.0
written offer.

---

## 5. Game-derived artifacts

The following are **not** part of this repository or its release output:

- `interop/` (Il2CppInterop-generated assemblies);
- `runtime/`;
- game assemblies / decompiled game code;
- Addressables / game assets.

They are generated locally from the user's game installation and excluded by
`.gitignore`. Do not redistribute them with this mod.

---

## 6. Inspiration, not a derivative work

The idea for MVZ2CloudSaves comes from **[NeriPlayer](https://github.com/cwuom/NeriPlayer)**,
an Android music player that can sync user data — playlists, favorites, recent
plays, deletion records and playback statistics (metadata only; no audio files)
— to the user's own private GitHub repository (or a WebDAV remote).

Only the general approach was taken as inspiration. No NeriPlayer source code,
assets, icons, UI text or documentation text is copied, ported or adapted in
this project. NeriPlayer is not a dependency, is not linked, and is not
redistributed with MVZ2CloudSaves, so its GPL-3.0 license does not apply to
this project. The name is used only for attribution.

---

## 7. Verification checklist for a release

- [ ] `LICENSE` present (GPL-3.0)
- [ ] `THIRD-PARTY-NOTICES.md` present
- [ ] `LICENSES/GPL-3.0.txt` present
- [ ] `LICENSES/Apache-2.0.txt` present
- [ ] `LICENSES/LanguagePackEnhance.API-NOTICE.txt` present
- [ ] `LICENSES/LGPL-2.1.txt` present
- [ ] `LICENSES/MIT-HarmonyX.txt` present
- [ ] `LICENSES/MIT-MonoMod.txt` present
- [ ] `SOURCE.txt` present and points to the release tag source (or `src/` when included)
- [ ] `libs/LanguagePackEnhance.API.dll` accompanied by its Apache license/notice
- [ ] no `interop/`, `runtime/`, game assemblies, or BepInEx/DSHCore/LPE plugin DLLs
      accidentally included
- [ ] if bundling BepInEx/DSHCore/LPE later, add their licenses, notices and
      source/offer as required
