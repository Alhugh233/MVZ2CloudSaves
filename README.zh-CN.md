语言：[English](README.md) | 简体中文

# MVZ2 CloudSaves

![Vibe Coding](https://img.shields.io/badge/Vibe%20Coding-100%25-blueviolet)

面向 **MVZ2 (Minecraft VS Zombies 2) 0.7.x** 的 BepInEx 6 (IL2CPP) GitHub 云存档 mod。

- 插件 GUID：`com.mvz2.cloudsaves`
- 当前版本：**0.1.0**
- 前置：**DSHCore**（`qoznos.mvz2.core`）、**LanguagePackEnhance**（`com.mvz2.languagepackenhance`）
- 适配：MVZ2 **0.7.x**（Windows / Android / IL2CPP）

> ⚠️ 本项目为社区制作的**非官方**内容，与游戏原作者无关。

> 🧪 **早期测试阶段**：本 Mod 仍在早期测试与验证中，同步、冲突处理和跨平台行为都可能继续调整。**使用前请务必备份 `userdata/` 目录**（至少保留 `userdata/users.dat` 和 `userdata/userN/` 的副本）。

## 目录

- [概述](#概述)
- [Vibe Coding](#vibe-coding)
- [功能](#功能)
- [安装](#安装)
- [使用](#使用)
- [配置](#配置)
- [同步逻辑](#同步逻辑)
- [本地化](#本地化)
- [数据格式](#数据格式)
- [从源码构建](#从源码构建)
- [兼容性](#兼容性)
- [已知限制](#已知限制)
- [开发说明](#开发说明)
- [致谢](#致谢)
- [许可与合规](#许可与合规)

## 概述

MVZ2 的存档分散在两个位置：

- `userdata/users.dat` + `userdata/userN/<命名空间>/user.dat`：全局存档（金币、解锁、统计、难度记录……）；
- `userdata/userN/<命名空间>/level/<关卡>.lvl`：关卡进度。

MVZ2CloudSaves 把整个 `userdata/` 作为快照同步到你的 **私有 GitHub 仓库**，使用 **逐文件 Git tree**，而不是每次上传整包 zip。

它解决的问题：

- 换机器 / 重装游戏后恢复存档；
- 多设备之间同步；
- 不想依赖网盘客户端，直接用自己的 GitHub 仓库；
- 云端和本地都有改动时**不静默覆盖**。

本 Mod 的灵感来自 **[NeriPlayer](https://github.com/cwuom/NeriPlayer)**——一个**能够**把歌单、收藏、最近播放和播放统计等用户数据同步到自己的 GitHub 私有仓库（也支持 WebDAV）的 Android 音乐播放器。MVZ2CloudSaves 把同样的思路用在了 MVZ2 的 `userdata/` 上；仅借鉴设计思路，未复用其源码、素材或文档。

## Vibe Coding

本项目采用 **Vibe Coding** 开发：代码与文档主要由向 AI 助手提问生成，部分代码为 AI 生成。请在使用前自行审查与测试。

## 功能

- **自动上传**：游戏保存后合并连续保存，延迟上传；不会每次 `SaveToFile` 都提交。
- **启动拉取**：启动时检查云端；只在非关卡状态应用，**不需要重启游戏**。
- **非关卡同步关卡进度**：关卡内只记录改动；退出关卡、回到主菜单/地图后再同步 `.lvl`。
- **创建 / 使用已有仓库**：可直接创建私有仓库，或填 `owner/repo` 使用已有私有仓库。
- **逐文件差异**：只上传变化的文件；删除用 Git tree 的 `sha:null` 表达。
- **两条发布路径**：小变更走 GraphQL `createCommitOnBranch` + `expectedHeadOid`；大变更走 REST blobs → tree → commit → ref。
- **Tombstone**：记录删除，避免另一台设备把已删的关卡/用户带回来。
- **三方比较**：以「上次同步的 manifest」为 base，逐文件比较 local / remote / base。
- **冲突保护**：本地和云端都改动时不静默覆盖；可手动选择上传本地或恢复云端。
- **原生 toast**：同步结果用 MVZ2 自带的 `MainSceneController.ShowPopup` 提示。
- **LPE 本地化**：内嵌 `MVZ2CloudSaves.lang.json`，并镜像到 DSHCore `UiLocale`。
- **Token 不落明文**：Windows DPAPI；Android 应用私有目录 + AES-GCM；支持环境变量。

## 安装

1. **先备份 `userdata/` 目录** —— 本 Mod 仍处于早期测试阶段，同步出错时可以直接还原。
2. 确认游戏已安装 **BepInEx 6 (IL2CPP)**。
3. 安装 **[DSHCore](https://github.com/QoZnoS/MVZ2Mod)** 与 **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)**（硬依赖）。
4. 把 `MVZ2CloudSaves.dll` 复制到 `BepInEx/plugins/`。
5. 启动游戏 → 主菜单 → 更多选项 → 插件 → **云存档**。

日志出现 `Plugin com.mvz2.cloudsaves is loaded!` 即加载成功。

## 使用

### 1. 配置 token

在「云存档」页输入 GitHub token，点击「保存并验证」。Android/桌面都可以用「从剪贴板粘贴」直接填入完整 token；「创建 Token」可直接打开 GitHub 的 fine-grained token 创建页，「打开仓库」打开当前仓库。

- token 会先写入本地加密存储，再联网验证；
- 验证返回 401 才会回滚并提示「token 无效」；
- 仅网络失败（断网、超时、被墙）时仍会保留 token，并提示「已保存，但验证失败」，之后网络恢复即可直接同步。

Token 权限：

| 操作 | 推荐权限 |
| --- | --- |
| 使用已有私有仓库 | fine-grained PAT：`Contents: Read and write` |
| 在游戏内创建新仓库 | fine-grained PAT：`Administration: Read and write` + `Contents: Read and write`，仓库访问选 **All repositories**；或 classic PAT 的 `repo` |

> 创建新仓库后如果 token 只授权了 “Only select repositories”，新仓库可能不在 token 范围内；本 mod 会验证并提示。

### 2. 创建或选择仓库

- **创建新仓库**：填仓库名，强制私有，自动初始化 README；
- **使用已有仓库**：填 `owner/repo`，校验私有 / 写入权限 / 默认分支；
- 仓库已有 `userdata/` 但没有本 mod 的 `manifest.json` 时不会静默接管。

### 3. 同步

- **自动**：保存后 debounce；退出关卡后约 8 秒、普通保存约 90 秒后上传；
- **启动**：后台预取云端，在 `SaveManager.Load` 前缀应用；
- **手动**：页面上的「立即同步 / 上传本地 / 从云端恢复」；
- **拉取后自动重读**：应用云端存档后会重新读取 `users.dat` 和用户存档，主菜单用户名/用户列表立即刷新，不需要重启游戏；
- **指令**：`/cloudsave status|sync|push|pull|conflicts|help`，支持 Tab 补全；
- **冲突**：页面会出现「上传本地覆盖云端 / 从云端覆盖本地」，覆盖前会确认并备份。

### 4. 退出游戏

退出/切后台时写入 `PendingPush`，下次启动补传；不依赖退出前的异步请求一定完成。

## 配置

配置文件：`BepInEx/config/com.mvz2.cloudsaves.cfg`

```ini
[GitHub]
Owner =
Repo =
Branch =            # 留空 = 仓库默认分支
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

Token 不写在 cfg 里。它保存在：

```
BepInEx/CloudSaves/secrets/token.bin
```

- Windows：DPAPI（当前用户）；
- Android：应用私有目录 + AES-GCM（随机密钥在应用沙盒内；Android Keystore 硬件封装留待后续版本）；
- 环境变量：`MVZ2_GITHUB_TOKEN`，完全不落盘；
- 明文只作为显式降级选项（`AllowPlaintextToken = true`），默认关闭。

## 同步逻辑

```
启动
  → 后台预取云端 ref / commit / tree / manifest
  → SaveManager.Load 前缀：本地干净且云端更新 → 备份并应用
  → 本地有改动且云端也变 → 冲突，不覆盖

游戏中
  → SaveToFile / SaveUserList / SaveLevel / RemoveLevelState 后置：只标记 dirty
  → 关卡内：不同步
  → 退出关卡 / 主菜单 / 地图：
       等待场景稳定 + debounce
       → 上传变化文件（逐文件 tree）
       → 或拉取云端（本地干净时）

退出游戏
  → 写入 pendingPush 标记
  → 下次启动补传
```

### 为什么不需要重启

- `user.dat` / `users.dat` 变化：非关卡状态调用 `SaveManager.LoadUserData` 或 `SaveManager.Load` 重读；
- `.lvl` 变化：非关卡状态只写文件，下次进关时游戏自己 `LoadLevel`；
- 当前关卡实例不会被热替换。

## 本地化

本 mod 自身文本通过 **LanguagePackEnhance** 提供：

- 内嵌 `Resources/MVZ2CloudSaves.lang.json`（`en-US`）；
- 所有界面文案走 `Loc.T`（`LanguagePackEnhanceApi.Translate`）；
- 同一份表镜像到 DSHCore `UiLocale`，让「模组设置」页也能翻译；
- 第三方语言包（优先级高于 Mod 自带）可以翻译 / 覆盖。

查找优先级：**导入的第三方语言包 > Mod 自带语言包 > 游戏内置 > 原文**。

> 不熟悉语言包制作？参考官方模板：**Cuerzor/MVZ2LanguagePack** — <https://github.com/Cuerzor/MVZ2LanguagePack>。

## 数据格式

云端仓库结构：

```
<私有仓库>/<分支>/
├─ manifest.json
├─ userdata/users.dat
├─ userdata/user0/mvz2/user.dat
├─ userdata/user0/mvz2/level/day.lvl
└─ ...
```

`manifest.json` 记录：

- `format`：协议版本（当前 1）；
- `device`：设备 ID；
- `game` / `unity` / `platform`：录制环境；
- `files`：路径 → Git blob SHA / size / mtime；
- `deleted`：删除记录（tombstone）。

`.dat` / `.lvl` 本身是 gzip + BSON，本 mod **不解析、不重写**，按不透明字节同步。

## 从源码构建

需要 `.NET SDK`（目标 `net6.0`）。

把游戏 interop 放到 `interop/`，或使用 `-p:InteropDir=`：

```bash
dotnet build \
  -p:InteropDir=/path/to/interop_mvz2 \
  -p:DshCoreDir=/path/to/DSHCore \
  -p:LpeDir=/path/to/LanguagePackEnhance.API
```

也可以在 Windows 上：

```powershell
.\build.ps1 -InteropDir "path\to\interop" `
            -DshCoreDir "path\to\DSHCore" `
            -LpeDir "path\to\LanguagePackEnhance.API"
```

产物：

```
bin/Debug/net6.0/MVZ2CloudSaves.dll
```

`-Pack` 会把 DLL、README、LICENSE、`THIRD-PARTY-NOTICES.md`、`LICENSES/`、`SOURCE.txt`，以及完整的对应源码 `src/` 复制到 `dist/`（GPL-3.0 §6）。

### 依赖

- `BepInEx.Unity.IL2CPP`、`BepInEx.PluginInfoProps`
- **[DSHCore](https://github.com/QoZnoS/MVZ2Mod)**（运行时硬依赖）
- **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)**（运行时硬依赖；编译期引用瘦 API `LanguagePackEnhance.API.dll`）

## 兼容性

- 目标框架：`net6.0`（BepInEx 6 IL2CPP / CoreCLR）。
- 插件 GUID：`com.mvz2.cloudsaves`。
- 面向 **MVZ2 0.7.x** 适配。
- Windows / Android 使用同一份源码；HTTP 用 `UnityWebRequest`，不依赖 `System.Net.Http` / `System.Text.Json` / `System.IO.Compression`。
- 只同步 `userdata/`，不碰 Addressables、语言包资源、回放资源（默认）。

## 已知限制

- 只同步游戏实际写出的文件；普通关卡中途不会自动生成 `.lvl`。本 mod 不会改变游戏存档频率。
- 冲突默认保留本地并提示；需要手动选择覆盖方向。
- Android 的 token 目前是应用私有 AES-GCM，不是 Android Keystore 硬件密钥。
- GitHub 仓库需要是私有仓库；公开仓库会拒绝。
- 不处理字段级 BSON 合并；只做文件级三方比较。
- 默认不同步 `replays/`、`user.dat.bak*`；可在 cfg 打开。
- GraphQL 快速路径未在真机上做完整压力测试；失败时会回退或提示。

## 开发说明

### 目录结构

```
MVZ2CloudSaves/
├─ Plugin.cs                     Mod 入口：配置、LPE/DSHCore 注册、Harmony、服务初始化
├─ Core/
│  ├─ CoroutinePump.cs           由 MainSceneController.Update 驱动的手写协程泵
│  ├─ CloudSaveService.cs        同步会话：锁、generation、预取、发布、冲突
│  ├─ CloudSavesConfig.cs        BepInEx 配置
│  ├─ CloudSavesModels.cs        manifest / tombstone / state
│  ├─ FileScanner.cs             userdata 扫描、排除项、Git blob 哈希
│  ├─ GitBlobHash.cs             纯 C# SHA-1（Git blob）
│  ├─ Http.cs                    UnityWebRequest 封装
│  ├─ MiniJson.cs / JsonWriter.cs  JSON 解析 / 序列化
│  ├─ TokenStore.cs              DPAPI / Android AES-GCM / 环境变量
│  ├─ SceneGate.cs               关卡/场景同步门禁
│  ├─ Toast.cs                   MVZ2 原生 toast
│  └─ Loc.cs                     LPE + UiLocale 本地化
├─ GitHub/GitHubApi.cs           GitHub REST + GraphQL 请求
├─ Patches/PatchManager.cs       Harmony 补丁
├─ UI/CloudSavesPage.cs          DSHCore「云存档」页面
├─ Commands/CloudSaveCommand.cs  /cloudsave 指令 + Tab 补全
├─ Resources/MVZ2CloudSaves.lang.json
├─ libs/LanguagePackEnhance.API.dll
└─ build.ps1 / README / LICENSE / THIRD-PARTY-NOTICES.md
```

### 主要逻辑

- **保存检测**：挂 `SaveManager.SaveToFile`、`SaveUserList`、`LevelManager.SaveLevel`、`RemoveLevelState` 后置；只标记 dirty 和 `saveGeneration`。
- **同步门禁**：`SceneGate` 在关卡内、进入/退出关卡、场景稳定前禁止同步。
- **推送**：扫描 `userdata` → 计算 Git blob SHA → 拉远端 tree/manifest → 三方比较 → GraphQL 小变更 / REST 大变更。
- **拉取**：后台下载到 staging → 校验 blob SHA → 在 `SaveManager.Load` 前缀或非关卡状态应用 → `LoadUserData` / `Load` 重读。
- **退出**：`MainManager.OnApplicationQuit` 后置写 `PendingPush`；Android 切后台同理。
- **Token**：首次输入 → 平台加密 → 后续自动解密；不写 config。

### Harmony 补丁列表

| 目标 | 类型 | 作用 |
| --- | --- | --- |
| `SaveManager.Load` | Prefix / Postfix | 启动前应用云端；加载后扫描本地脏数据 |
| `SaveManager.SaveToFile` | Postfix | 标记 dirty |
| `SaveManager.SaveUserList` | Postfix | `users.dat` 变化 |
| `LevelManager.SaveLevel` | Postfix | `.lvl` 变化 |
| `LevelManager.RemoveLevelState` | Postfix | `.lvl` 删除 / tombstone |
| `LevelManager.GotoLevelSceneAsync` | Prefix | 进入关卡期间禁止同步 |
| `LevelManager.ExitLevelSceneAsync` | Prefix | 退出关卡期间禁止同步 |
| `LevelManager.InitLevel` | Postfix | 关卡初始化后解除门禁 |
| `MainManager.OnApplicationQuit` | Postfix | 退出前写 pending |
| `MainManager.OnApplicationFocus` | Postfix | Android 切后台写 pending |
| `DebugManager.ExecuteCommand` | Prefix | `/cloudsave` |
| `DebugManager.FillSuggestions` | Prefix | Tab 补全（只追加，void） |

## 致谢

- **Cuerzor** — MVZ2 原作者，以及 0.7 的官方 Mod 扩展点
- **[NeriPlayer](https://github.com/cwuom/NeriPlayer)** — 本 Mod 的灵感来源（用 GitHub 私有仓库做跨设备数据同步）
- **QoZnoS** — DSHCore 作者（`MVZ2Mod` 仓库，非本项目作者）
- **Alhugh233** — LanguagePackEnhance、MVZ2-MusicBox（MiniJson 来源）
- [BepInEx](https://github.com/BepInEx/BepInEx) / [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) — 注入与互操作框架

## 许可与合规

- 本项目：**GPL-3.0**，Copyright (C) 2026 Alhugh233，见 [LICENSE](LICENSE)。
- 第三方组件、版权与许可证：见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- 所有协议原文随包附带在 [LICENSES/](LICENSES/)：
  - `GPL-3.0.txt` — 本项目、改编的 MiniJson、MVZ2 本体
  - `Apache-2.0.txt` + `LanguagePackEnhance.API-NOTICE.txt` — `libs/LanguagePackEnhance.API.dll`
  - `LGPL-2.1.txt` — BepInEx（运行时前置，未打包）
  - `MIT-HarmonyX.txt` / `MIT-MonoMod.txt` — HarmonyX / MonoMod（由 BepInEx 提供，未打包）
- `libs/LanguagePackEnhance.API.dll`：**Apache-2.0**（Copyright (C) 2026 Alhugh233），许可证与 NOTICE 同包分发。
- `LanguagePackEnhance.dll` 是 **GPL-3.0 的实现插件**，只作为运行时硬依赖，不引用、不打包；本项目只编译引用 Apache-2.0 的瘦 API。
- DSHCore、LanguagePackEnhance、BepInEx、MVZ2 本体是**独立分发**的运行时前置，本仓库不打包它们；各自遵循原许可证。
- 本项目源代码即本仓库；分发二进制时请一并附带 `LICENSE`、`THIRD-PARTY-NOTICES.md`、`LICENSES/`、`SOURCE.txt` 与完整源码 `src/`。
