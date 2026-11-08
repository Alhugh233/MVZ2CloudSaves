#nullable enable

using System;
using BepInEx;
using BepInEx.Configuration;

namespace MVZ2CloudSaves;

/// <summary>
/// BepInEx 配置（<c>BepInEx/config/com.mvz2.cloudsaves.cfg</c>）。
/// Token 不在这里，见 <see cref="TokenStore"/>。
/// </summary>
internal static class CloudSavesConfig
{
    public static ConfigEntry<string> Owner = null!;
    public static ConfigEntry<string> Repo = null!;
    public static ConfigEntry<string> Branch = null!;
    public static ConfigEntry<string> TokenSource = null!;
    public static ConfigEntry<string> ApiBase = null!;
    public static ConfigEntry<string> ApiVersion = null!;

    public static ConfigEntry<bool> AutoPush = null!;
    public static ConfigEntry<bool> AutoPullOnStartup = null!;
    public static ConfigEntry<bool> UseGraphQlFastPath = null!;
    public static ConfigEntry<int> DebounceSeconds = null!;
    public static ConfigEntry<int> LevelExitDebounceSeconds = null!;
    public static ConfigEntry<int> RequestTimeoutSeconds = null!;
    public static ConfigEntry<int> StartupPullTimeoutSeconds = null!;
    public static ConfigEntry<bool> IncludeReplays = null!;
    public static ConfigEntry<bool> IncludeBackups = null!;
    public static ConfigEntry<int> MaxFileMB = null!;
    public static ConfigEntry<int> MaxTotalMB = null!;
    public static ConfigEntry<bool> AllowPlaintextToken = null!;

    public static ConfigFile File = null!;
    public static int Generation { get; private set; }

    public static void Init(ConfigFile cfg)
    {
        File = cfg;

        Owner = cfg.Bind("GitHub", "Owner", "", "GitHub 仓库 owner。例：Alhugh233。");
        Repo = cfg.Bind("GitHub", "Repo", "", "GitHub 仓库名。例：mvz2-cloud-saves。");
        Branch = cfg.Bind("GitHub", "Branch", "", "分支名。留空 = 使用仓库默认分支。");
        TokenSource = cfg.Bind("GitHub", "TokenSource", "Auto", D("Token 来源：Auto = 优先加密存储、其次环境变量；Store = 只用加密存储；Environment = 只用环境变量。", new AcceptableValueList<string>("Auto", "Store", "Environment")));
        ApiBase = cfg.Bind("GitHub", "ApiBase", "https://api.github.com", "GitHub REST API 地址。");
        ApiVersion = cfg.Bind("GitHub", "ApiVersion", "2026-03-10", "X-GitHub-Api-Version。");

        AutoPush = cfg.Bind("Sync", "AutoPush", true, "游戏保存后自动上传。关卡内只标记，退出关卡后上传。");
        AutoPullOnStartup = cfg.Bind("Sync", "AutoPullOnStartup", true, "启动时检查云端存档；只在非关卡状态应用。");
        UseGraphQlFastPath = cfg.Bind("Sync", "UseGraphQlFastPath", true, "小变更优先使用 GraphQL createCommitOnBranch；失败时回退 REST。");
        DebounceSeconds = cfg.Bind("Sync", "DebounceSeconds", 90, D("保存后等待多少秒再上传（合并连续保存）。", new AcceptableValueRange<int>(5, 600)));
        LevelExitDebounceSeconds = cfg.Bind("Sync", "LevelExitDebounceSeconds", 8, D("退出关卡后等待多少秒再上传。", new AcceptableValueRange<int>(2, 120)));
        RequestTimeoutSeconds = cfg.Bind("Sync", "RequestTimeoutSeconds", 30, D("单个 GitHub 请求超时（秒）。", new AcceptableValueRange<int>(5, 120)));
        StartupPullTimeoutSeconds = cfg.Bind("Sync", "StartupPullTimeoutSeconds", 10, D("启动预取等待时间（秒）。超时则本次跳过启动拉取。", new AcceptableValueRange<int>(2, 60)));
        IncludeReplays = cfg.Bind("Sync", "IncludeReplays", false, "是否同步 userdata/userN/replays/（回放可能很大）。");
        IncludeBackups = cfg.Bind("Sync", "IncludeBackups", false, "是否同步 user.dat.bak1..bak8。");
        MaxFileMB = cfg.Bind("Sync", "MaxFileMB", 50, D("单个文件大小上限（MB），超过则跳过并警告。", new AcceptableValueRange<int>(1, 200)));
        MaxTotalMB = cfg.Bind("Sync", "MaxTotalMB", 300, D("单次快照总大小上限（MB），超过则拒绝同步。", new AcceptableValueRange<int>(1, 2000)));
        AllowPlaintextToken = cfg.Bind("Security", "AllowPlaintextToken", false, "允许在平台加密不可用时明文保存 token（不推荐）。");
    }

    public static void Touch() => Generation++;

    public static void SetRepo(string owner, string repo, string branch)
    {
        Owner.Value = owner ?? "";
        Repo.Value = repo ?? "";
        Branch.Value = branch ?? "";
        Touch();
        File.Save();
    }

    public static void SetTokenSource(string source)
    {
        TokenSource.Value = source;
        Touch();
        File.Save();
    }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Owner.Value) && !string.IsNullOrWhiteSpace(Repo.Value);

    public static string EffectiveBranch(string defaultBranch) =>
        string.IsNullOrWhiteSpace(Branch.Value) ? defaultBranch : Branch.Value.Trim();

    private static ConfigDescription D(string text, AcceptableValueBase values) =>
        values == null ? new ConfigDescription(text) : new ConfigDescription(text, values);
}
