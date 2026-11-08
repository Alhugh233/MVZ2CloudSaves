Language: English | [简体中文](README.zh-CN.md)

# MVZ2 CloudSaves

![Vibe Coding](https://img.shields.io/badge/Vibe%20Coding-100%25-blueviolet)

GitHub cloud saves for **MVZ2 (Minecraft VS Zombies 2) 0.7.x**, built as a BepInEx 6 (IL2CPP) mod.

- Plugin GUID: `com.mvz2.cloudsaves`
- Current version: **0.1.0**
- Dependencies: **DSHCore** (`qoznos.mvz2.core`), **LanguagePackEnhance** (`com.mvz2.languagepackenhance`)
- Target: MVZ2 **0.7.x** (Windows / Android / IL2CPP)

> ⚠️ Unofficial community content. Not affiliated with the original author.

> 🧪 **Early testing**: this mod is still in early testing. Sync, conflict handling and cross-platform behaviour may keep changing. **Back up your `userdata/` directory before use** (at minimum keep copies of `userdata/users.dat` and `userdata/userN/`).

## Contents

- [Overview](#overview)
- [Vibe Coding](#vibe-coding)
- [Features](#features)
- [Install](#install)
- [Usage](#usage)
- [Configuration](#configuration)
- [Sync logic](#sync-logic)
- [Localization](#localization)
- [Data format](#data-format)
- [Building from source](#building-from-source)
- [Compatibility](#compatibility)
- [Known limitations](#known-limitations)
- [Development notes](#development-notes)
- [Credits](#credits)
- [License and compliance](#license-and-compliance)

## Overview

MVZ2 stores its data in two places:

- `userdata/users.dat` + `userdata/userN/<namespace>/user.dat`: global saves (money, unlocks, stats, difficulty records, …);
- `userdata/userN/<namespace>/level/<stage>.lvl`: level progress.

MVZ2CloudSaves syncs the whole `userdata/` directory to your **private GitHub repository** using a **per-file Git tree**, not a single zip snapshot.

It is for:

- restoring saves after moving machines or reinstalling;
- syncing between devices;
- using your own GitHub repository instead of a cloud-drive client;
- never silently overwriting when both local and cloud changed.

The idea is inspired by **[NeriPlayer](https://github.com/cwuom/NeriPlayer)**, an Android music player that can sync user data such as playlists, favorites, recent plays and playback statistics to the user's own private GitHub repository (WebDAV is also supported). MVZ2CloudSaves applies the same approach to MVZ2's `userdata/`; only the design idea was borrowed — none of its source code, assets or documentation text is reused.

## Vibe Coding

This project is developed with **Vibe Coding**: code and documentation are largely produced by asking AI assistants, and some code is AI-generated. Please review and test it before use.

## Features

- **Automatic upload** after game saves, with debounce.
- **Startup pull**; applied only outside a level, no game restart required.
- **Level progress syncs only outside a level**: inside a level changes are recorded, then synced after leaving.
- **Create or use an existing repository** from the in-game page.
- **Per-file diff**: only changed files are uploaded; deletions use Git tree `sha:null`.
- **Two publish paths**: small changes use GraphQL `createCommitOnBranch` + `expectedHeadOid`; large changes use REST blobs → tree → commit → ref.
- **Tombstones** prevent a deleted level/user from being resurrected by another device.
- **Three-way comparison** using the last synced manifest as base.
- **Conflict protection**: never silently overwrite; choose local or cloud manually.
- **Native toast** via MVZ2's `MainSceneController.ShowPopup`.
- **LPE localization**, mirrored into DSHCore `UiLocale`.
- **No plaintext token**: Windows DPAPI; Android app-private AES-GCM; environment variable supported.

## Install

1. **Back up your `userdata/` directory first** — this mod is in early testing; a backup lets you restore immediately if anything goes wrong.
2. Install **BepInEx 6 (IL2CPP)**.
3. Install **[DSHCore](https://github.com/QoZnoS/MVZ2Mod)** and **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)** (hard dependencies).
4. Copy `MVZ2CloudSaves.dll` into `BepInEx/plugins/`.
5. Launch the game → Main menu → More options → Addons → **Cloud Saves**.

The log should contain `Plugin com.mvz2.cloudsaves is loaded!`.

## Usage

### 1. Configure a token

Enter a GitHub token in the Cloud Saves page and press **Save and validate**. On Android and desktop you can use **Paste from clipboard** to fill in the complete token.

- The token is written to encrypted local storage first, then validated online.
- Only an HTTP 401 rolls the token back and reports “token invalid”.
- On network-only failures (offline, timeout, blocked host) the token is kept and reported as “saved, but validation failed”; sync can proceed once the network recovers.

| Operation | Recommended permission |
| --- | --- |
| Use an existing private repository | fine-grained PAT: `Contents: Read and write` |
| Create a repository in game | fine-grained PAT: `Administration: Read and write` + `Contents: Read and write`, repository access **All repositories**; or classic PAT `repo` |

> If a fine-grained token only grants “Only select repositories”, a newly created repository may not be inside its scope. MVZ2CloudSaves validates and reports this.

### 2. Create or select a repository

- **Create new**: enter a name; the repository is forced private and initialized with a README.
- **Use existing**: enter `owner/repo`; private visibility, push permission and default branch are validated.
- A repository containing `userdata/` without this mod's `manifest.json` is not taken over silently.

### 3. Sync

- **Automatic**: after saves; about 8 seconds after leaving a level, 90 seconds for ordinary saves.
- **Startup**: background prefetch, applied in the `SaveManager.Load` prefix.
- **Manual**: Sync now / Upload local / Restore from cloud.
- **Refresh after pull**: after applying a cloud snapshot the mod re-reads `users.dat` and user saves; the main-menu user name and the user list update without restarting the game.
- **Commands**: `/cloudsave status|sync|push|pull|conflicts|help` with tab completion.
- **Conflicts**: the page offers **Upload local over cloud** / **Overwrite local with cloud**; confirmation and a local backup are required.

### 4. Quitting

`PendingPush` is persisted on quit/background; the next launch uploads it. The mod does not rely on an async request completing during shutdown.

## Configuration

`BepInEx/config/com.mvz2.cloudsaves.cfg`:

```ini
[GitHub]
Owner =
Repo =
Branch =            # empty = repository default branch
TokenSource = Auto  # Auto / Store / Environment
ApiBase = https://api.github.com
ApiVersion = 2026-03-10

[Sync]
AutoPush = true
AutoPullOnStartup = true
UseGraphQlFastPath = true
DebounceSeconds = 90
LevelExitDebounceSeconds = 8
RequestTimeoutSeconds = 30
StartupPullTimeoutSeconds = 10
IncludeReplays = false
IncludeBackups = false
MaxFileMB = 50
MaxTotalMB = 300

[Security]
AllowPlaintextToken = false
```

The token is not stored in the config file. It lives at:

```
BepInEx/CloudSaves/secrets/token.bin
```

- Windows: DPAPI (current user).
- Android: app-private directory + AES-GCM (random key inside the app sandbox; Android Keystore integration is planned).
- Environment variable `MVZ2_GITHUB_TOKEN` (no disk write).
- Plaintext only with explicit `AllowPlaintextToken = true` (off by default).

## Sync logic

```
Startup
  → background prefetch cloud ref / commit / tree / manifest
  → SaveManager.Load prefix: local clean + cloud newer → backup and apply
  → local changed + cloud changed → conflict, no overwrite

In game
  → SaveToFile / SaveUserList / SaveLevel / RemoveLevelState postfix: mark dirty only
  → inside a level: no sync
  → after leaving level / main menu / map:
       wait for scene settle + debounce
       → upload changed files (per-file tree)
       → or pull (if local clean)

Quit
  → persist pendingPush
  → upload on next launch
```

### Why no restart

- `user.dat` / `users.dat`: reloaded with `SaveManager.LoadUserData` or `SaveManager.Load` outside a level.
- `.lvl`: written outside a level; the game loads it on the next level entry.
- The current level instance is never hot-swapped.

## Localization

UI text is provided through **LanguagePackEnhance**:

- embedded `Resources/MVZ2CloudSaves.lang.json` (`en-US`);
- all UI strings go through `Loc.T` (`LanguagePackEnhanceApi.Translate`);
- the same table is mirrored into DSHCore `UiLocale` for the mod settings page;
- third-party language packs can translate/override (higher priority).

Priority: **imported third-party packs > mod pack > game built-in > source text**.

## Data format

```
<private repository>/<branch>/
├─ manifest.json
├─ userdata/users.dat
├─ userdata/user0/mvz2/user.dat
├─ userdata/user0/mvz2/level/day.lvl
└─ ...
```

`manifest.json` contains:

- `format`: protocol version (currently 1);
- `device`: device ID;
- `game` / `unity` / `platform`: environment;
- `files`: path → Git blob SHA / size / mtime;
- `deleted`: tombstones.

`.dat` / `.lvl` are gzip + BSON and are treated as opaque bytes. The mod does not parse or rewrite them.

## Building from source

Requires `.NET SDK` (target `net6.0`).

Put the game interop in `interop/`, or pass `-p:InteropDir=`:

```bash
dotnet build \
  -p:InteropDir=/path/to/interop_mvz2 \
  -p:DshCoreDir=/path/to/DSHCore \
  -p:LpeDir=/path/to/LanguagePackEnhance.API
```

On Windows:

```powershell
.\build.ps1 -InteropDir "path\to\interop" `
            -DshCoreDir "path\to\DSHCore" `
            -LpeDir "path\to\LanguagePackEnhance.API"
```

Output: `bin/Debug/net6.0/MVZ2CloudSaves.dll`.

`-Pack` copies the DLL, READMEs, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `LICENSES/`, `SOURCE.txt`, and the complete corresponding source `src/` into `dist/` (GPL-3.0 §6).

## Compatibility

- Target framework: `net6.0` (BepInEx 6 IL2CPP / CoreCLR).
- Plugin GUID: `com.mvz2.cloudsaves`.
- Targets **MVZ2 0.7.x**.
- Windows and Android share the same source; HTTP uses `UnityWebRequest`, avoiding `System.Net.Http`, `System.Text.Json` and `System.IO.Compression`.
- Only `userdata/` is synced; Addressables, language-pack resources and replay resources are untouched by default.

## Known limitations

- Only files actually written by the game are synced; normal levels do not autosave `.lvl` mid-level. The mod does not change save frequency.
- Conflicts keep local by default and require a manual choice.
- Android token storage is app-private AES-GCM, not Android Keystore yet.
- Public repositories are refused.
- No field-level BSON merge; only file-level three-way comparison.
- `replays/` and `user.dat.bak*` are excluded by default; they can be enabled in the config.
- The GraphQL fast path has not been stress-tested on real devices yet; failures fall back or are reported.

## Development notes

### Directory structure

```
MVZ2CloudSaves/
├─ Plugin.cs                     Entry: config, LPE/DSHCore registration, Harmony, service init
├─ Core/
│  ├─ CoroutinePump.cs           Coroutine pump driven by MainSceneController.Update
│  ├─ CloudSaveService.cs        Sync session: lock, generations, prefetch, publish, conflict
│  ├─ CloudSavesConfig.cs        BepInEx config
│  ├─ CloudSavesModels.cs        manifest / tombstone / state
│  ├─ FileScanner.cs             userdata scan, exclusions, Git blob hashes
│  ├─ GitBlobHash.cs             pure C# SHA-1 (Git blob)
│  ├─ Http.cs                    UnityWebRequest wrapper
│  ├─ MiniJson.cs / JsonWriter.cs  JSON parse / write
│  ├─ TokenStore.cs              DPAPI / Android AES-GCM / environment
│  ├─ SceneGate.cs               level/scene sync gate
│  ├─ Toast.cs                   native MVZ2 toast
│  └─ Loc.cs                     LPE + UiLocale
├─ GitHub/GitHubApi.cs           GitHub REST + GraphQL requests
├─ Patches/PatchManager.cs       Harmony patches
├─ UI/CloudSavesPage.cs          DSHCore Cloud Saves page
├─ Commands/CloudSaveCommand.cs  /cloudsave + tab completion
├─ Resources/MVZ2CloudSaves.lang.json
├─ libs/LanguagePackEnhance.API.dll
└─ build.ps1 / README / LICENSE / THIRD-PARTY-NOTICES.md
```

### Main logic

- **Save detection**: postfixes on `SaveManager.SaveToFile`, `SaveUserList`, `LevelManager.SaveLevel`, `RemoveLevelState`; only mark dirty and bump `saveGeneration`.
- **Gate**: `SceneGate` blocks sync inside a level, during enter/exit transitions, and before the scene settles.
- **Push**: scan `userdata` → Git blob SHA → fetch remote tree/manifest → three-way diff → GraphQL for small changes / REST for large ones.
- **Pull**: download to staging → verify blob SHA → apply in the `SaveManager.Load` prefix or outside a level → reload with `LoadUserData` / `Load`.
- **Quit**: postfix on `MainManager.OnApplicationQuit` writes `PendingPush`; Android focus loss does the same.
- **Token**: first input → platform encryption → automatic decryption afterwards; never in config.

### Harmony patches

| Target | Type | Purpose |
| --- | --- | --- |
| `SaveManager.Load` | Prefix / Postfix | Apply cloud before load; scan local dirtiness after load |
| `SaveManager.SaveToFile` | Postfix | Mark dirty |
| `SaveManager.SaveUserList` | Postfix | `users.dat` changes |
| `LevelManager.SaveLevel` | Postfix | `.lvl` changes |
| `LevelManager.RemoveLevelState` | Postfix | `.lvl` deletion / tombstone |
| `LevelManager.GotoLevelSceneAsync` | Prefix | Block sync while entering |
| `LevelManager.ExitLevelSceneAsync` | Prefix | Block sync while leaving |
| `LevelManager.InitLevel` | Postfix | Release the gate |
| `MainManager.OnApplicationQuit` | Postfix | Persist pending on quit |
| `MainManager.OnApplicationFocus` | Postfix | Persist pending on Android background |
| `DebugManager.ExecuteCommand` | Prefix | `/cloudsave` |
| `DebugManager.FillSuggestions` | Prefix | Tab completion (append-only, void) |

## Credits

- **Cuerzor** — MVZ2 original author and the 0.7 mod extension points
- **[NeriPlayer](https://github.com/cwuom/NeriPlayer)** — inspiration for this mod (cross-device sync via a private GitHub repository)
- **QoZnoS** — DSHCore author (`MVZ2Mod` repository; not the author of this project)
- **Alhugh233** — LanguagePackEnhance and MVZ2-MusicBox (MiniJson origin)
- [BepInEx](https://github.com/BepInEx/BepInEx) / [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) — injection and interop

## License and compliance

- Project: **GPL-3.0**, Copyright (C) 2026 Alhugh233 — see [LICENSE](LICENSE).
- Third-party components, copyright and license notices: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Full license texts ship under [LICENSES/](LICENSES/):
  - `GPL-3.0.txt` — this project, the adapted MiniJson, and MVZ2 itself
  - `Apache-2.0.txt` + `LanguagePackEnhance.API-NOTICE.txt` — `libs/LanguagePackEnhance.API.dll`
  - `LGPL-2.1.txt` — BepInEx (runtime prerequisite, not bundled)
  - `MIT-HarmonyX.txt` / `MIT-MonoMod.txt` — HarmonyX / MonoMod (supplied by BepInEx, not bundled)
- `libs/LanguagePackEnhance.API.dll`: **Apache-2.0** (Copyright (C) 2026 Alhugh233); license and NOTICE are distributed alongside it.
- `LanguagePackEnhance.dll` is the **GPL-3.0 implementation plugin**; it is a hard runtime dependency only — never referenced, never bundled. This project compiles against the Apache-2.0 thin API only.
- DSHCore, LanguagePackEnhance, BepInEx and MVZ2 are independently distributed runtime prerequisites and are not bundled here; they remain under their own licenses.
- The complete corresponding source is this repository. When distributing binaries, include `LICENSE`, `THIRD-PARTY-NOTICES.md`, `LICENSES/`, `SOURCE.txt`, and the complete source `src/`.
