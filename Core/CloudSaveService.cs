#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MVZ2.Managers;

namespace MVZ2CloudSaves;

internal sealed class PullResult
{
    public bool Ready;
    public bool Applied;
    public bool Conflict;
    public bool NoRemote;
    public bool IgnoreLocalDirty;
    public string Commit = string.Empty;
    public string Tree = string.Empty;
    public string StagingRoot = string.Empty;
    public string Error = string.Empty;
    public SnapshotManifest? Manifest;
}

/// <summary>
/// 同步会话：唯一锁、保存代次、三方比较、GitHub 发布。
/// 所有方法只允许在 Unity 主线程调用；HTTP 用协程，文件扫描也在主线程完成（存档很小）。
/// </summary>
internal static class CloudSaveService
{
    private const string ManifestPath = "manifest.json";

    private static bool ready;
    private static int lastTickFrame = -1;
    private static bool tickLogged;
    private static CloudSaveState state = new CloudSaveState();

    private static bool syncing;
    private static bool reloading;
    private static bool startupPullRunning;
    private static bool authFailed;
    private static bool pendingReload;
    private static bool saveManagerLoadCompleted;
    private static long deferredReloadUsersChanged;

    private static int saveGeneration;
    private static bool dirty;
    private static long lastDirtyUtc;
    private static long rateLimitUntil;

    private static PullResult? startupResult;
    private static string lastError = string.Empty;
    private static string conflict = string.Empty;
    private static string status = "空闲";
    private static bool manualOperation;
    private static bool forceNextPush;
    private static bool forceNextPull;

    public static string Status => status;
    public static string LastError => lastError;
    public static string Conflict => conflict;
    public static bool IsSyncing => syncing;
    public static bool IsConfigured => CloudSavesConfig.IsConfigured && TokenStore.HasToken;

    public static void Initialize()
    {
        ready = true;
        state = CloudSaveState.Load();
        TokenStore.Load();
        dirty = state.PendingPush;
        lastDirtyUtc = Now();
        if (state.RateLimitReset > Now())
            rateLimitUntil = state.RateLimitReset;
        authFailed = false;
        status = DescribeIdle();
        Diag.Info("service initialized: token=" + (TokenStore.HasToken ? "yes" : "no")
            + ", repo=" + (CloudSavesConfig.IsConfigured ? CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value : "(none)")
            + ", state=" + (state.LastCommit.Length > 0 ? state.LastCommit.Substring(0, Math.Min(8, state.LastCommit.Length)) : "(none)"));
    }

    /// <summary>
    /// 由 <c>MainSceneController.Update</c> 的 Harmony postfix 每帧调用。
    /// 多个 MainSceneController 实例同帧命中时只执行一次。
    /// </summary>
    public static void OnUnityTick()
    {
        int frame;
        try { frame = UnityEngine.Time.frameCount; }
        catch { frame = -1; }
        if (frame >= 0 && frame == lastTickFrame)
            return;
        lastTickFrame = frame;

        if (!tickLogged)
        {
            tickLogged = true;
            Diag.Info("每帧心跳已启动（MainSceneController.Update）");
        }

        try
        {
            CoroutinePump.Tick();
            Tick();
        }
        catch (Exception e)
        {
            Diag.Warn("每帧心跳异常：" + e);
        }
    }

    public static void BeginStartupPull()
    {
        if (!CloudSavesConfig.AutoPullOnStartup.Value || !IsConfigured)
        {
            RefreshStatus();
            return;
        }
        if (ready)
            StartStartupPull();
    }

    public static void OnSaveManagerLoadPrefix()
    {
        // Harmony 回调跨 native->managed 边界：绝不能把异常抛回游戏。
        try
        {
            saveManagerLoadCompleted = false;
            if (reloading)
                return;

            if (startupResult != null && startupResult.Ready && !startupResult.Applied)
            {
                ApplyPull(startupResult, true);
                return;
            }
        }
        catch (Exception e)
        {
            Diag.Warn("SaveManager.Load 前缀异常：" + e);
        }
    }

    public static void OnSaveManagerLoadPostfix()
    {
        try
        {
            saveManagerLoadCompleted = true;
            // 上次会话如果是强退/崩溃，pendingPush 可能没写进去；这里按内容再判一次。
            var scan = FileScanner.Scan(state, out _, out var error);
            if (string.IsNullOrEmpty(error))
            {
                dirty = FileScanner.IsLocalDirty(state, scan);
                if (dirty)
                {
                    lastDirtyUtc = Now();
                    state.PendingPush = true;
                }
            }
            RefreshStatus();
        }
        catch (Exception e)
        {
            Diag.Warn("SaveManager.Load 后缀异常：" + e);
        }
    }

    public static void OnSaveEvent(string reason)
    {
        try
        {
            if (reloading)
                return;
            saveGeneration++;
            dirty = true;
            lastDirtyUtc = Now();
            if (string.IsNullOrEmpty(conflict))
                status = Loc.T("本地有改动，等待同步");
            Diag.Info("已标记待同步：" + reason + "（generation=" + saveGeneration + "）");
        }
        catch (Exception e)
        {
            Diag.Warn("标记待同步失败：" + e);
        }
    }

    public static void OnQuit()
    {
        try
        {
            state.PendingPush = dirty || state.PendingPush;
            state.Save();
        }
        catch (Exception e)
        {
            Diag.Warn("退出保存状态失败：" + e);
        }
    }

    public static void OnBackground()
    {
        try
        {
            state.PendingPush = dirty || state.PendingPush;
            state.Save();
        }
        catch (Exception e)
        {
            Diag.Warn("后台保存状态失败：" + e);
        }
    }

    public static void OnApplicationFocusEvent(bool focus)
    {
        if (!focus)
            OnBackground();
    }

    public static void Tick()
    {
        SceneGate.Tick();
        Toast.Flush();

        if (reloading || syncing)
            return;
        if (!IsConfigured || authFailed || Now() < rateLimitUntil)
            return;

        if (startupResult != null && startupResult.Ready && !startupResult.Applied)
        {
            if (SceneGate.CanSync && saveManagerLoadCompleted)
                ApplyPull(startupResult, false);
            return;
        }

        if (pendingReload)
        {
            TryReloadAfterPull(deferredReloadUsersChanged != 0);
            return;
        }

        if (!dirty || !SceneGate.CanSync)
            return;
        if (!CloudSavesConfig.AutoPush.Value)
            return;

        var wait = SceneGate.NonLevelSeconds <= 30
            ? Math.Max(2, CloudSavesConfig.LevelExitDebounceSeconds.Value)
            : Math.Max(5, CloudSavesConfig.DebounceSeconds.Value);
        if (Now() - lastDirtyUtc < wait)
            return;

        StartPush(false);
    }

    public static void ManualSync()
    {
        if (!CheckManualAllowed(out var message))
        {
            Toast.Show(message);
            return;
        }
        if (dirty || state.LastCommit.Length == 0)
            StartPush(true);
        else
            StartPull(true);
    }

    public static void ManualUpload()
    {
        if (!CheckManualAllowed(out var message))
        {
            Toast.Show(message);
            return;
        }
        StartPush(true);
    }

    public static void ManualDownload()
    {
        if (!CheckManualAllowed(out var message))
        {
            Toast.Show(message);
            return;
        }
        StartPull(true);
    }

    public static void ForceUpload()
    {
        if (!CheckManualAllowed(out var message))
        {
            Toast.Show(message);
            return;
        }
        StartPush(true, true);
    }

    public static void ForceDownload()
    {
        if (!CheckManualAllowed(out var message))
        {
            Toast.Show(message);
            return;
        }
        StartPull(true, true);
    }

    private static bool CheckManualAllowed(out string message)
    {
        if (!IsConfigured) { message = Loc.T("云存档：请先配置 token 和仓库"); return false; }
        if (authFailed) { message = Loc.T("云存档：GitHub 授权已失效，请重新输入 token"); return false; }
        if (SceneGate.InLevel) { message = Loc.T("云存档：请先退出关卡再同步"); return false; }
        if (syncing) { message = Loc.T("云存档：正在同步中"); return false; }
        message = string.Empty;
        return true;
    }

    public static void SaveToken(string token, Action<bool, string> done)
    {
        if (syncing || startupPullRunning)
        {
            done?.Invoke(false, Loc.T("云存档：正在同步中"));
            return;
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            done?.Invoke(false, Loc.T("云存档：token 不能为空"));
            return;
        }
        if (!ready)
        {
            done?.Invoke(false, Loc.T("云存档：游戏尚未就绪"));
            return;
        }
        CoroutinePump.Start(SaveTokenCoroutine(token.Trim(), done));
    }

    private static IEnumerator SaveTokenCoroutine(string token, Action<bool, string> done)
    {
        // 先落盘，再验证。验证要走网络；如果先验证再保存，断网/被墙时
        // 用户每次都会被判“保存失败”，token 永远存不进去。
        if (!TokenStore.Save(token))
        {
            done?.Invoke(false, Loc.T("云存档：无法保存 token") + "：" + TokenStore.LastError);
            yield break;
        }
        authFailed = false;
        CloudSavesConfig.Touch();
        Diag.Info("token 已写入本地：长度=" + token.Length + "，保护方式=" + TokenStore.CurrentProtector);

        var response = default(HttpResponse);
        yield return GitHubApi.GetUserUnobserved(token, r => response = r);

        if (response != null && response.Status == 401)
        {
            // 明确无效：回滚刚写入的 token，避免之后一直拿着坏 token 同步。
            TokenStore.Clear();
            authFailed = false;
            Diag.Warn("token 校验返回 401，已回滚本地保存。");
            done?.Invoke(false, Loc.T("云存档：token 无效或已过期"));
            RefreshStatus();
            yield break;
        }

        if (response == null || !response.Ok)
        {
            var reason = response == null ? Loc.T("网络失败") : GitHubApi.ErrorMessage(response);
            Diag.Warn("token 已保存，但验证失败（HTTP "
                + (response != null ? response.Status.ToString() : "0") + "）：" + reason);
            done?.Invoke(true, Loc.T("云存档：token 已保存，但验证失败：{0}", reason));
            RefreshStatus();
            yield break;
        }

        Diag.Info("token 校验通过。");
        done?.Invoke(true, Loc.T("云存档：token 已保存"));
        RefreshStatus();
    }

    public static void ClearToken()
    {
        if (syncing || startupPullRunning)
        {
            Toast.Show(Loc.T("云存档：正在同步中"));
            return;
        }
        TokenStore.Clear();
        authFailed = false;
        dirty = false;
        conflict = string.Empty;
        Toast.Show(Loc.T("云存档：token 已清除"));
        RefreshStatus();
    }

    public static void UseExistingRepository(string fullName, Action<bool, string> done)
    {
        if (syncing || startupPullRunning)
        {
            done?.Invoke(false, Loc.T("云存档：正在同步中"));
            return;
        }
        if (!ready)
        {
            done?.Invoke(false, Loc.T("云存档：游戏尚未就绪"));
            return;
        }
        var parts = (fullName ?? string.Empty).Trim().Split('/');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            done?.Invoke(false, Loc.T("云存档：格式应为 owner/repo"));
            return;
        }
        CoroutinePump.Start(UseRepoCoroutine(parts[0].Trim(), parts[1].Trim(), done));
    }

    private static IEnumerator UseRepoCoroutine(string owner, string repo, Action<bool, string> done)
    {
        var response = default(HttpResponse);
        yield return GitHubApi.GetRepo(owner, repo, r => response = r);
        if (response == null || !response.Ok)
        {
            var msg = response == null ? "网络失败" : GitHubApi.ErrorMessage(response);
            done?.Invoke(false, Loc.T("云存档：无法访问仓库") + "：" + msg);
            yield break;
        }

        var root = MiniJson.Obj(MiniJson.Parse(response.Text));
        var isPrivate = root != null && MiniJson.Bool(root.TryGetValue("private", out var p) ? p : null);
        var defaultBranch = root != null ? MiniJson.Str(root.TryGetValue("default_branch", out var d) ? d : null) : "";
        var permissions = root != null ? MiniJson.Obj(root.TryGetValue("permissions", out var perm) ? perm : null) : null;
        // 有些 token 的 /repos 响应不带 permissions；不要因此拒绝，真正写的时候再报错。
        var canPush = permissions == null || MiniJson.Bool(permissions.TryGetValue("push", out var push) ? push : null);

        if (!isPrivate)
        {
            done?.Invoke(false, Loc.T("云存档：仓库必须是私有的"));
            yield break;
        }
        if (!canPush)
        {
            done?.Invoke(false, Loc.T("云存档：token 没有该仓库的写入权限"));
            yield break;
        }

        var sameTarget = string.Equals(owner, CloudSavesConfig.Owner.Value, StringComparison.OrdinalIgnoreCase)
            && string.Equals(repo, CloudSavesConfig.Repo.Value, StringComparison.OrdinalIgnoreCase);
        CloudSavesConfig.SetRepo(owner, repo, defaultBranch);
        if (!sameTarget)
        {
            state.ResetForNewRepo();
            conflict = string.Empty;
            MarkDirtyFromLocal();
        }
        Toast.Show(Loc.T("云存档：仓库已配置") + " " + owner + "/" + repo);
        done?.Invoke(true, owner + "/" + repo);
        RefreshStatus();
    }

    public static void CreateRepository(string name, Action<bool, string> done)
    {
        if (syncing || startupPullRunning)
        {
            done?.Invoke(false, Loc.T("云存档：正在同步中"));
            return;
        }
        if (!ready)
        {
            done?.Invoke(false, Loc.T("云存档：游戏尚未就绪"));
            return;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            done?.Invoke(false, Loc.T("云存档：仓库名不能为空"));
            return;
        }
        CoroutinePump.Start(CreateRepoCoroutine(name.Trim(), done));
    }

    private static IEnumerator CreateRepoCoroutine(string name, Action<bool, string> done)
    {
        var response = default(HttpResponse);
        yield return GitHubApi.GetUser(TokenStore.CurrentToken, r => response = r);
        if (response == null || !response.Ok)
        {
            done?.Invoke(false, response == null ? "网络失败" : Loc.T("云存档：token 无效或已过期"));
            yield break;
        }
        var user = MiniJson.Obj(MiniJson.Parse(response.Text));
        var login = user != null ? MiniJson.Str(user.TryGetValue("login", out var l) ? l : null) : "";

        response = null;
        yield return GitHubApi.CreateRepository(name, r => response = r);
        if (response == null || !response.Ok)
        {
            var msg = response == null ? "网络失败" : GitHubApi.ErrorMessage(response);
            done?.Invoke(false, Loc.T("云存档：创建仓库失败") + "：" + msg);
            yield break;
        }

        var repo = MiniJson.Obj(MiniJson.Parse(response.Text));
        var fullName = repo != null ? MiniJson.Str(repo.TryGetValue("full_name", out var f) ? f : null) : "";
        var defaultBranch = repo != null ? MiniJson.Str(repo.TryGetValue("default_branch", out var d) ? d : null) : "";
        if (string.IsNullOrEmpty(fullName))
        {
            fullName = login + "/" + name;
            if (string.IsNullOrEmpty(defaultBranch))
                defaultBranch = "main";
        }

        var owner = fullName.Contains("/") ? fullName.Substring(0, fullName.IndexOf('/')) : login;
        CloudSavesConfig.SetRepo(owner, name, defaultBranch);
        state.ResetForNewRepo();
        conflict = string.Empty;
        MarkDirtyFromLocal();
        Toast.Show(Loc.T("云存档：仓库已创建") + " " + fullName);
        done?.Invoke(true, fullName);
        RefreshStatus();
    }

    public static string DescribeIdle()
    {
        if (!CloudSavesConfig.IsConfigured)
            return Loc.T("未配置仓库");
        if (!TokenStore.HasToken)
            return Loc.T("未配置 token");
        if (!string.IsNullOrEmpty(conflict))
            return Loc.T("存在冲突");
        if (state.LastSyncUtc > 0)
            return Loc.T("上次同步：") + FormatTime(state.LastSyncUtc);
        return Loc.T("空闲");
    }

    public static void OnCoroutineFault(Exception e)
    {
        lastError = e?.Message ?? "协程异常";
        Diag.Warn("同步协程异常：" + e);
        syncing = false;
        startupPullRunning = false;
        manualOperation = false;
        status = Loc.T("同步失败");
        try { CloudSavesPage.NotifyChanged(); } catch { }
    }

    public static void Observe(HttpResponse? response)
    {
        if (response == null)
            return;
        if (response.Status == 401)
        {
            if (!authFailed)
            {
                authFailed = true;
                status = Loc.T("GitHub 授权失效");
                Toast.Show(Loc.T("云存档：GitHub 授权已失效"));
            }
            return;
        }

        if (response.Status == 403 || response.Status == 429)
        {
            var remaining = response.Header("x-ratelimit-remaining");
            var retry = response.Header("retry-after");
            var reset = response.Header("x-ratelimit-reset");
            long until = 0;
            if (long.TryParse(retry, out var retrySec) && retrySec > 0)
                until = Now() + retrySec;
            else if (string.Equals(remaining, "0", StringComparison.Ordinal) && long.TryParse(reset, out var resetSec))
                until = resetSec;
            else if (response.Status == 429)
                until = Now() + 60;

            if (until > Now())
            {
                var first = rateLimitUntil <= Now();
                rateLimitUntil = until;
                state.RateLimitReset = until;
                state.Save();
                status = Loc.T("GitHub 限流，稍后重试");
                if (first)
                    Toast.Show(Loc.T("云存档：GitHub 限流，稍后重试"));
            }
        }
    }

    public static void RefreshStatus()
    {
        if (syncing)
            status = Loc.T("同步中…");
        else if (authFailed)
            status = Loc.T("GitHub 授权失效");
        else if (Now() < rateLimitUntil)
            status = Loc.T("GitHub 限流，稍后重试");
        else if (!string.IsNullOrEmpty(conflict))
            status = Loc.T("存在冲突");
        else if (!string.IsNullOrEmpty(lastError))
            status = Loc.T("同步失败") + "：" + lastError;
        else if (dirty)
            status = Loc.T("本地有改动，等待同步");
        else
            status = DescribeIdle();

        try { CloudSavesPage.NotifyChanged(); } catch { }
    }

    private static void StartStartupPull()
    {
        if (!ready || startupPullRunning)
            return;
        Diag.Info("startup pull starting");
        CoroutinePump.Start(StartupPullCoroutine());
    }

    private static void StartPull(bool manual, bool force = false)
    {
        if (!ready)
            return;
        manualOperation = manual;
        forceNextPull = force;
        syncing = true;
        status = Loc.T("同步中…");
        CoroutinePump.Start(PullCoroutine(manual));
    }

    private static void StartPush(bool manual, bool force = false)
    {
        if (!ready)
            return;
        manualOperation = manual;
        forceNextPush = force;
        syncing = true;
        status = Loc.T("同步中…");
        CoroutinePump.Start(PushCoroutine(manual));
    }

    private static IEnumerator StartupPullCoroutine()
    {
        startupPullRunning = true;
        syncing = true;
        status = Loc.T("检查云端…");
        Diag.Info("startup pull running");
        var result = new PullResult();
        lastError = string.Empty;

        var repo = default(HttpResponse);
        yield return GitHubApi.GetRepo(r => repo = r);
        if (repo == null || !repo.Ok)
        {
            result.Error = repo == null ? "网络失败" : GitHubApi.ErrorMessage(repo);
            startupResult = result;
            FinishSync(false);
            yield break;
        }
        var repoRoot = MiniJson.Obj(MiniJson.Parse(repo.Text));
        var defaultBranch = repoRoot != null ? MiniJson.Str(repoRoot.TryGetValue("default_branch", out var d) ? d : null) : "main";
        var branch = CloudSavesConfig.EffectiveBranch(defaultBranch);

        var refResponse = default(HttpResponse);
        yield return GitHubApi.GetRef(branch, r => refResponse = r);
        if (refResponse == null || !refResponse.Ok)
        {
            result.NoRemote = true;
            result.Ready = true;
            result.Applied = true;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var head = ReadRefSha(refResponse);
        if (string.IsNullOrEmpty(head))
        {
            result.NoRemote = true;
            result.Ready = true;
            result.Applied = true;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var commitResponse = default(HttpResponse);
        yield return GitHubApi.GetCommit(head, r => commitResponse = r);
        var tree = commitResponse != null && commitResponse.Ok ? ReadCommitTree(commitResponse) : "";
        if (string.IsNullOrEmpty(tree))
        {
            result.Error = commitResponse == null ? "网络失败" : "读取 commit 失败";
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var treeResponse = default(HttpResponse);
        yield return GitHubApi.GetTree(tree, r => treeResponse = r);
        var truncated = false;
        Dictionary<string, string>? entries = null;
        if (treeResponse != null && treeResponse.Ok)
            entries = ReadTree(treeResponse, out truncated);
        if (entries == null || truncated)
        {
            result.Error = treeResponse == null ? "网络失败" : "读取 tree 失败或被截断";
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        SnapshotManifest? manifest = null;
        if (entries.TryGetValue(ManifestPath, out var manifestSha))
        {
            var manifestResponse = default(HttpResponse);
            yield return GitHubApi.GetBlob(manifestSha, r => manifestResponse = r);
            if (manifestResponse != null && manifestResponse.Ok)
                manifest = SnapshotManifest.Parse(manifestResponse.Text);
        }

        if (manifest == null)
        {
            result.NoRemote = true;
            result.Ready = true;
            result.Applied = true;
            startupResult = result;
            FinishSync(false);
            yield break;
        }
        if (manifest.Format > 1)
        {
            result.Error = "云端存档协议版本过新：" + manifest.Format;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var local = FileScanner.Scan(state, out _, out var scanError);
        if (!string.IsNullOrEmpty(scanError))
        {
            result.Error = scanError;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var localDirty = FileScanner.IsLocalDirty(state, local);
        var remoteChanged = !string.Equals(head, state.LastCommit, StringComparison.Ordinal);
        if (remoteChanged && localDirty)
        {
            result.Conflict = true;
            result.Ready = true;
            result.Applied = true;
            result.Manifest = manifest;
            result.Commit = head;
            result.Tree = tree;
            startupResult = result;
            lastError = Loc.T("本地和云端都有改动，请处理冲突");
            conflict = lastError;
            Toast.Show(Loc.T("云存档：") + lastError);
            FinishSync(false);
            yield break;
        }
        if (!remoteChanged)
        {
            result.Ready = true;
            result.Applied = true;
            result.Manifest = manifest;
            result.Commit = head;
            result.Tree = tree;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        var staging = Path.Combine(CloudSaveState.DirectoryPath, "staging", head);
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
        }
        catch (Exception e)
        {
            result.Error = "准备 staging 失败：" + e.Message;
            startupResult = result;
            FinishSync(false);
            yield break;
        }

        foreach (var pair in manifest.Files)
        {
            var remoteSha = pair.Value.Sha;
            if (!entries.TryGetValue(pair.Key, out var treeSha) || !string.Equals(treeSha, remoteSha, StringComparison.Ordinal))
            {
                result.Error = "manifest 与 tree 不一致：" + pair.Key;
                startupResult = result;
                FinishSync(false);
                yield break;
            }

            var localPath = FileScanner.LocalPath(pair.Key);
            if (File.Exists(localPath))
            {
                try
                {
                    if (string.Equals(GitBlobHash.ComputeFile(localPath), remoteSha, StringComparison.Ordinal))
                        continue;
                }
                catch
                {
                }
            }

            var blobResponse = default(HttpResponse);
            yield return GitHubApi.GetBlob(remoteSha, r => blobResponse = r);
            if (blobResponse == null || !blobResponse.Ok)
            {
                result.Error = "下载 blob 失败：" + pair.Key;
                startupResult = result;
                FinishSync(false);
                yield break;
            }
            if (!string.Equals(GitBlobHash.Compute(blobResponse.Body), remoteSha, StringComparison.Ordinal))
            {
                result.Error = "blob 校验失败：" + pair.Key;
                startupResult = result;
                FinishSync(false);
                yield break;
            }

            var dest = Path.Combine(staging, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? staging);
            File.WriteAllBytes(dest, blobResponse.Body);
        }

        result.Ready = true;
        result.Manifest = manifest;
        result.Commit = head;
        result.Tree = tree;
        result.StagingRoot = staging;
        startupResult = result;
        FinishSync(false);
    }

    private static IEnumerator PullCoroutine(bool manual)
    {
        // 手动拉取复用同一套下载逻辑，但结果直接应用。
        var generation = saveGeneration;
        var configGeneration = CloudSavesConfig.Generation;
        var force = forceNextPull;
        forceNextPull = false;
        var fake = new PullResult { IgnoreLocalDirty = force };
        lastError = string.Empty;
        var repo = default(HttpResponse);
        yield return GitHubApi.GetRepo(r => repo = r);
        if (repo == null || !repo.Ok)
        {
            Fail(manual, repo == null ? "网络失败" : GitHubApi.ErrorMessage(repo));
            yield break;
        }
        var repoRoot = MiniJson.Obj(MiniJson.Parse(repo.Text));
        var defaultBranch = repoRoot != null ? MiniJson.Str(repoRoot.TryGetValue("default_branch", out var d) ? d : null) : "main";
        var branch = CloudSavesConfig.EffectiveBranch(defaultBranch);

        var refResponse = default(HttpResponse);
        yield return GitHubApi.GetRef(branch, r => refResponse = r);
        if (refResponse == null || !refResponse.Ok)
        {
            FinishSync(false);
            Toast.Show(Loc.T("云存档：云端还没有存档"));
            yield break;
        }
        var head = ReadRefSha(refResponse);
        var commitResponse = default(HttpResponse);
        yield return GitHubApi.GetCommit(head, r => commitResponse = r);
        var tree = commitResponse != null && commitResponse.Ok ? ReadCommitTree(commitResponse) : "";
        var treeResponse = default(HttpResponse);
        yield return GitHubApi.GetTree(tree, r => treeResponse = r);
        var truncated = false;
        Dictionary<string, string>? entries = null;
        if (treeResponse != null && treeResponse.Ok)
            entries = ReadTree(treeResponse, out truncated);
        if (entries == null || truncated)
        {
            Fail(manual, "读取云端 tree 失败");
            yield break;
        }
        SnapshotManifest? manifest = null;
        if (entries.TryGetValue(ManifestPath, out var manifestSha))
        {
            var manifestResponse = default(HttpResponse);
            yield return GitHubApi.GetBlob(manifestSha, r => manifestResponse = r);
            if (manifestResponse != null && manifestResponse.Ok)
                manifest = SnapshotManifest.Parse(manifestResponse.Text);
        }
        if (manifest == null)
        {
            FinishSync(false);
            Toast.Show(Loc.T("云存档：云端还没有存档"));
            yield break;
        }
        if (manifest.Format > 1)
        {
            Fail(manual, "云端存档协议版本过新：" + manifest.Format);
            yield break;
        }

        var local = FileScanner.Scan(state, out _, out var scanError);
        if (!string.IsNullOrEmpty(scanError))
        {
            Fail(manual, scanError);
            yield break;
        }
        if (!force && FileScanner.IsLocalDirty(state, local))
        {
            SetConflict(Loc.T("本地有未同步的改动，不能直接覆盖"));
            yield break;
        }

        var staging = Path.Combine(CloudSaveState.DirectoryPath, "staging", head);
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
        }
        catch (Exception e)
        {
            Fail(manual, "准备 staging 失败：" + e.Message);
            yield break;
        }

        foreach (var pair in manifest.Files)
        {
            var remoteSha = pair.Value.Sha;
            var blobResponse = default(HttpResponse);
            yield return GitHubApi.GetBlob(remoteSha, r => blobResponse = r);
            if (blobResponse == null || !blobResponse.Ok || !string.Equals(GitBlobHash.Compute(blobResponse.Body), remoteSha, StringComparison.Ordinal))
            {
                Fail(manual, "下载或校验失败：" + pair.Key);
                yield break;
            }
            var dest = Path.Combine(staging, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? staging);
            File.WriteAllBytes(dest, blobResponse.Body);
        }

        if (saveGeneration != generation)
        {
            dirty = true;
            lastDirtyUtc = Now();
            FinishSync(false);
            if (manual)
                Toast.Show(Loc.T("云存档：本地又有保存，稍后重试"));
            yield break;
        }

        if (CloudSavesConfig.Generation != configGeneration)
        {
            FinishSync(false);
            Toast.Show(Loc.T("云存档：配置已更改，本次拉取已取消"));
            yield break;
        }

        fake.Ready = true;
        fake.Manifest = manifest;
        fake.Commit = head;
        fake.Tree = tree;
        fake.StagingRoot = staging;
        FinishSync(false);
        if (SceneGate.InLevel)
        {
            startupResult = fake;
            pendingReload = true;
            Toast.Show(Loc.T("云存档：已下载，退出关卡后应用"));
            yield break;
        }
        ApplyPull(fake, false);
    }

    private static IEnumerator PushCoroutine(bool manual)
    {
        var generation = saveGeneration;
        var configGeneration = CloudSavesConfig.Generation;
        var force = forceNextPush;
        forceNextPush = false;
        lastError = string.Empty;
        var scan = FileScanner.Scan(state, out _, out var scanError);
        if (!string.IsNullOrEmpty(scanError))
        {
            Fail(manual, scanError);
            yield break;
        }

        var repo = default(HttpResponse);
        yield return GitHubApi.GetRepo(r => repo = r);
        if (repo == null || !repo.Ok)
        {
            Fail(manual, repo == null ? "网络失败" : GitHubApi.ErrorMessage(repo));
            yield break;
        }
        var repoRoot = MiniJson.Obj(MiniJson.Parse(repo.Text));
        var defaultBranch = repoRoot != null ? MiniJson.Str(repoRoot.TryGetValue("default_branch", out var d) ? d : null) : "main";
        var permissions = repoRoot != null ? MiniJson.Obj(repoRoot.TryGetValue("permissions", out var perm) ? perm : null) : null;
        if (permissions != null && !MiniJson.Bool(permissions.TryGetValue("push", out var push) ? push : null))
        {
            Fail(manual, Loc.T("token 没有该仓库的写入权限"));
            yield break;
        }
        var branch = CloudSavesConfig.EffectiveBranch(defaultBranch);

        var refResponse = default(HttpResponse);
        yield return GitHubApi.GetRef(branch, r => refResponse = r);
        var head = refResponse != null && refResponse.Ok ? ReadRefSha(refResponse) : "";
        if (string.IsNullOrEmpty(head))
        {
            // 空仓库：用 Contents API 写一个 README，创建默认分支。
            var init = default(HttpResponse);
            yield return GitHubApi.PutReadme("", r => init = r);
            if (init == null || !init.Ok)
            {
                init = null;
                yield return GitHubApi.PutReadme(defaultBranch, r => init = r);
            }
            if (init == null || !init.Ok)
            {
                Fail(manual, Loc.T("无法初始化空仓库"));
                yield break;
            }
            refResponse = null;
            yield return GitHubApi.GetRef(branch, r => refResponse = r);
            head = refResponse != null && refResponse.Ok ? ReadRefSha(refResponse) : "";
            if (string.IsNullOrEmpty(head))
            {
                Fail(manual, Loc.T("无法读取仓库 HEAD"));
                yield break;
            }
        }

        var commitResponse = default(HttpResponse);
        yield return GitHubApi.GetCommit(head, r => commitResponse = r);
        var tree = commitResponse != null && commitResponse.Ok ? ReadCommitTree(commitResponse) : "";
        if (string.IsNullOrEmpty(tree))
        {
            Fail(manual, "读取 commit 失败");
            yield break;
        }
        var treeResponse = default(HttpResponse);
        yield return GitHubApi.GetTree(tree, r => treeResponse = r);
        var truncated = false;
        Dictionary<string, string>? entries = null;
        if (treeResponse != null && treeResponse.Ok)
            entries = ReadTree(treeResponse, out truncated);
        if (entries == null || truncated)
        {
            Fail(manual, "读取云端 tree 失败");
            yield break;
        }

        SnapshotManifest? remoteManifest = null;
        if (entries.TryGetValue(ManifestPath, out var remoteManifestSha))
        {
            var manifestResponse = default(HttpResponse);
            yield return GitHubApi.GetBlob(remoteManifestSha, r => manifestResponse = r);
            if (manifestResponse != null && manifestResponse.Ok)
                remoteManifest = SnapshotManifest.Parse(manifestResponse.Text);
        }

        if (remoteManifest != null && remoteManifest.Format > 1)
        {
            Fail(manual, "云端存档协议版本过新：" + remoteManifest.Format);
            yield break;
        }
        var localDirty = FileScanner.IsLocalDirty(state, scan);
        if (remoteManifest != null && state.LastCommit.Length == 0 && !FileScanner.HasMeaningfulContent(scan))
        {
            // 本地是空的、云端有存档：这是拉取场景，绝不能把远端删光。
            dirty = false;
            state.PendingPush = false;
            state.Save();
            FinishSync(false);
            Toast.Show(Loc.T("云存档：云端已有存档，请使用「从云端恢复」"));
            yield break;
        }
        if (!force && remoteManifest == null && state.LastCommit.Length > 0)
        {
            SetConflict(Loc.T("云端 manifest 丢失，请先检查云端仓库"));
            yield break;
        }
        if (!force && remoteManifest != null && state.LastCommit.Length == 0 && FileScanner.HasMeaningfulContent(scan))
        {
            SetConflict(Loc.T("云端已有存档，本地也有存档，请选择保留哪一边"));
            yield break;
        }
        if (!force && remoteManifest != null && !string.Equals(head, state.LastCommit, StringComparison.Ordinal) && !localDirty)
        {
            // 本地没改、云端变了：这是拉取场景，不能拿旧本地覆盖云端。
            dirty = false;
            FinishSync(false);
            Toast.Show(Loc.T("云存档：云端有更新，请使用「从云端恢复」"));
            yield break;
        }
        if (!force && remoteManifest != null && !string.Equals(head, state.LastCommit, StringComparison.Ordinal) && localDirty)
        {
            SetConflict(Loc.T("本地和云端都有改动，请先处理冲突"));
            yield break;
        }

        var deleted = new Dictionary<string, Tombstone>(remoteManifest?.Deleted ?? state.Deleted, StringComparer.Ordinal);
        if (remoteManifest != null)
        {
            foreach (var pair in remoteManifest.Files)
            {
                if (!scan.Files.ContainsKey(pair.Key))
                {
                    deleted[pair.Key] = new Tombstone
                    {
                        Sha = pair.Value.Sha,
                        At = Now(),
                        Device = state.DeviceId,
                    };
                }
            }
        }
        foreach (var pair in scan.Files)
            deleted.Remove(pair.Key);
        scan.Deleted = deleted;

        var manifestBytes = Encoding.UTF8.GetBytes(scan.ToJson());

        var changed = new List<string>();
        foreach (var pair in scan.Files)
        {
            if (!entries.TryGetValue(pair.Key, out var remoteSha) || !string.Equals(remoteSha, pair.Value.Sha, StringComparison.Ordinal))
                changed.Add(pair.Key);
        }
        var deletions = new List<string>();
        if (remoteManifest != null)
        {
            foreach (var pair in remoteManifest.Files)
            {
                if (!scan.Files.ContainsKey(pair.Key))
                    deletions.Add(pair.Key);
            }
        }
        // mtime 变化但内容没变时不值得单独提交 manifest；只有内容/删除变化才发布。
        var manifestChanged = changed.Count > 0 || deletions.Count > 0 ||
            (remoteManifest == null && (scan.Files.Count > 0 || scan.Deleted.Count > 0));

        if (changed.Count == 0 && deletions.Count == 0 && !manifestChanged)
        {
            dirty = false;
            state.PendingPush = false;
            state.Save();
            FinishSync(false);
            if (manual)
                Toast.Show(Loc.T("云存档：已是最新"));
            yield break;
        }

        if (saveGeneration != generation)
        {
            dirty = true;
            lastDirtyUtc = Now();
            FinishSync(false);
            if (manual)
                Toast.Show(Loc.T("云存档：本地又有保存，稍后重试"));
            yield break;
        }

        // 小变更走 GraphQL createCommitOnBranch（一次请求、expectedHeadOid 防并发）。
        var fastFiles = changed.Count + deletions.Count + (manifestChanged ? 1 : 0);
        long fastBytes = manifestChanged ? manifestBytes.LongLength : 0;
        foreach (var path in changed)
        {
            if (scan.Files.TryGetValue(path, out var meta))
                fastBytes += meta.Size;
        }
        if (CloudSavesConfig.UseGraphQlFastPath.Value && fastFiles > 0 && fastFiles <= 8 && fastBytes <= 2 * 1024 * 1024)
        {
            var additions = new List<GraphQlAddition>();
            foreach (var path in changed)
            {
                try
                {
                    additions.Add(new GraphQlAddition
                    {
                        Path = path,
                        Bytes = File.ReadAllBytes(FileScanner.LocalPath(path)),
                    });
                }
                catch (Exception e)
                {
                    Fail(manual, "读取文件失败：" + path + "：" + e.Message);
                    yield break;
                }
            }
            if (manifestChanged)
                additions.Add(new GraphQlAddition { Path = ManifestPath, Bytes = manifestBytes });

            var gql = default(HttpResponse);
            yield return GitHubApi.CreateCommitOnBranch(GitHubApi.Owner, GitHubApi.Repo, branch, head,
                BuildCommitMessage(manual), additions, deletions, r => gql = r);

            var gqlCommit = gql != null && gql.Ok ? ReadGraphQlCommit(gql) : "";
            if (!string.IsNullOrEmpty(gqlCommit))
            {
                if (CloudSavesConfig.Generation != configGeneration)
                {
                    dirty = true;
                    lastDirtyUtc = Now();
                    FinishSync(false);
                    Toast.Show(Loc.T("云存档：配置已更改，本次上传未记入检查点"));
                    yield break;
                }

                var changedDuringGql = saveGeneration != generation;
                state.LastCommit = gqlCommit;
                state.LastTree = string.Empty;
                state.LastFiles = CloneFiles(scan.Files);
                state.Deleted = CloneTombstones(scan.Deleted);
                state.LastSyncUtc = Now();
                state.PendingPush = changedDuringGql;
                state.RateLimitReset = 0;
                state.Save();
                dirty = changedDuringGql;
                if (dirty)
                    lastDirtyUtc = Now();
                conflict = string.Empty;
                lastError = string.Empty;
                FinishSync(true);
                if (changedDuringGql)
                    Toast.Show(Loc.T("云存档：已上传，但本地又有修改"));
                else if (manual || changed.Count > 0 || deletions.Count > 0)
                    Toast.Show(Loc.T("云存档：已上传") + " " + (changed.Count + deletions.Count) + " " + Loc.T("个文件"));
                yield break;
            }

            // 没有产生 commit 就回退 REST。GraphQL 的 200 + errors、权限不足、
            // 字段校验失败都可能在 REST 下正常工作；如果 REST 也失败，会给出真实错误。
            Diag.Info("GraphQL 快速路径未成功（HTTP " + (gql != null ? gql.Status : 0) + "），回退 REST");
            if (gql != null && (gql.Status == 401 || gql.Status == 429))
            {
                Fail(manual, GitHubApi.ErrorMessage(gql));
                yield break;
            }
        }

        // 上传变化文件
        var blobShas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in changed)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(FileScanner.LocalPath(path)); }
            catch (Exception e) { Fail(manual, "读取文件失败：" + path + "：" + e.Message); yield break; }

            var response = default(HttpResponse);
            yield return GitHubApi.CreateBlob(bytes, r => response = r);
            if (response == null || !response.Ok)
            {
                Fail(manual, "上传 blob 失败：" + path + "：" + (response == null ? "网络失败" : GitHubApi.ErrorMessage(response)));
                yield break;
            }
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            var sha = root != null ? MiniJson.Str(root.TryGetValue("sha", out var s) ? s : null) : "";
            if (string.IsNullOrEmpty(sha))
            {
                Fail(manual, "上传 blob 没有返回 sha：" + path);
                yield break;
            }
            blobShas[path] = sha;
        }

        if (manifestChanged)
        {
            var response = default(HttpResponse);
            yield return GitHubApi.CreateBlob(manifestBytes, r => response = r);
            if (response == null || !response.Ok)
            {
                Fail(manual, "上传 manifest 失败");
                yield break;
            }
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            var sha = root != null ? MiniJson.Str(root.TryGetValue("sha", out var s) ? s : null) : "";
            if (string.IsNullOrEmpty(sha))
            {
                Fail(manual, "上传 manifest 没有返回 sha");
                yield break;
            }
            blobShas[ManifestPath] = sha;
        }

        var treeEntries = new List<TreeEntry>();
        foreach (var pair in blobShas)
            treeEntries.Add(new TreeEntry { Path = pair.Key, Sha = pair.Value });
        foreach (var path in deletions)
            treeEntries.Add(new TreeEntry { Path = path, Sha = null });

        var treeCreate = default(HttpResponse);
        yield return GitHubApi.CreateTree(tree, treeEntries, r => treeCreate = r);
        if (treeCreate == null || !treeCreate.Ok)
        {
            Fail(manual, "创建 tree 失败：" + (treeCreate == null ? "网络失败" : GitHubApi.ErrorMessage(treeCreate)));
            yield break;
        }
        var treeRoot = MiniJson.Obj(MiniJson.Parse(treeCreate.Text));
        var newTree = treeRoot != null ? MiniJson.Str(treeRoot.TryGetValue("sha", out var ts) ? ts : null) : "";
        if (string.IsNullOrEmpty(newTree))
        {
            Fail(manual, "创建 tree 没有返回 sha");
            yield break;
        }

        if (saveGeneration != generation)
        {
            dirty = true;
            lastDirtyUtc = Now();
            FinishSync(false);
            if (manual)
                Toast.Show(Loc.T("云存档：本地又有保存，稍后重试"));
            yield break;
        }

        var commitCreate = default(HttpResponse);
        yield return GitHubApi.CreateCommit(BuildCommitMessage(manual), newTree, head, r => commitCreate = r);
        if (commitCreate == null || !commitCreate.Ok)
        {
            Fail(manual, "创建 commit 失败：" + (commitCreate == null ? "网络失败" : GitHubApi.ErrorMessage(commitCreate)));
            yield break;
        }
        var commitRoot = MiniJson.Obj(MiniJson.Parse(commitCreate.Text));
        var newCommit = commitRoot != null ? MiniJson.Str(commitRoot.TryGetValue("sha", out var cs) ? cs : null) : "";
        if (string.IsNullOrEmpty(newCommit))
        {
            Fail(manual, "创建 commit 没有返回 sha");
            yield break;
        }

        if (saveGeneration != generation)
        {
            dirty = true;
            lastDirtyUtc = Now();
            FinishSync(false);
            if (manual)
                Toast.Show(Loc.T("云存档：本地又有保存，稍后重试"));
            yield break;
        }

        var update = default(HttpResponse);
        yield return GitHubApi.UpdateRef(branch, newCommit, r => update = r);
        if (update == null || !update.Ok)
        {
            var msg = update == null ? "网络失败" : GitHubApi.ErrorMessage(update);
            if (update != null && (update.Status == 409 || update.Status == 422))
            {
                SetConflict(Loc.T("云端在同步期间更新了，请重试"));
            }
            else
            {
                Fail(manual, "更新分支失败：" + msg);
            }
            yield break;
        }

        if (CloudSavesConfig.Generation != configGeneration)
        {
            dirty = true;
            lastDirtyUtc = Now();
            FinishSync(false);
            Toast.Show(Loc.T("云存档：配置已更改，本次上传未记入检查点"));
            yield break;
        }

        var changedDuring = saveGeneration != generation;
        state.LastCommit = newCommit;
        state.LastTree = newTree;
        state.LastFiles = CloneFiles(scan.Files);
        state.Deleted = CloneTombstones(scan.Deleted);
        state.LastSyncUtc = Now();
        state.PendingPush = changedDuring;
        state.RateLimitReset = 0;
        state.Save();
        dirty = changedDuring;
        if (dirty)
            lastDirtyUtc = Now();
        conflict = string.Empty;
        lastError = string.Empty;
        FinishSync(true);
        if (changedDuring)
            Toast.Show(Loc.T("云存档：已上传，但本地又有修改"));
        else if (manual || changed.Count > 0 || deletions.Count > 0)
            Toast.Show(Loc.T("云存档：已上传") + " " + (changed.Count + deletions.Count) + " " + Loc.T("个文件"));
    }

    private static void ApplyPull(PullResult result, bool beforeLoad)
    {
        if (result == null || !result.Ready || result.Applied || result.Conflict || result.NoRemote || result.Manifest == null)
            return;
        if (SceneGate.InLevel)
        {
            return;
        }

        if (!beforeLoad && !result.IgnoreLocalDirty)
        {
            var current = FileScanner.Scan(state, out _, out var currentError);
            if (!string.IsNullOrEmpty(currentError) || FileScanner.IsLocalDirty(state, current))
            {
                result.Applied = true;
                SetConflict(Loc.T("本地在拉取期间又有改动，请重新同步"));
                return;
            }
        }

        var oldUsersDat = state.LastFiles.TryGetValue("userdata/users.dat", out var oldU) ? oldU.Sha : "";
        var newUsersDat = result.Manifest.Files.TryGetValue("userdata/users.dat", out var newU) ? newU.Sha : "";
        var usersChanged = !string.Equals(oldUsersDat, newUsersDat, StringComparison.Ordinal);

        var backupRoot = Path.Combine(CloudSaveState.DirectoryPath, "backups", Now().ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            // 先备份当前所有托管文件
            var union = new HashSet<string>(state.LastFiles.Keys, StringComparer.Ordinal);
            foreach (var key in result.Manifest.Files.Keys)
                union.Add(key);
            foreach (var key in union)
            {
                var src = FileScanner.LocalPath(key);
                if (!File.Exists(src))
                    continue;
                var dst = Path.Combine(backupRoot, key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? backupRoot);
                File.Copy(src, dst, true);
            }
        }
        catch (Exception e)
        {
            result.Applied = true;
            Fail(true, "备份本地存档失败：" + e.Message);
            return;
        }

        try
        {
            foreach (var pair in result.Manifest.Files)
            {
                var dst = FileScanner.LocalPath(pair.Key);
                if (File.Exists(dst))
                {
                    try
                    {
                        if (string.Equals(GitBlobHash.ComputeFile(dst), pair.Value.Sha, StringComparison.Ordinal))
                            continue;
                    }
                    catch
                    {
                    }
                }
                var src = Path.Combine(result.StagingRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src))
                    throw new IOException("staging 缺少文件：" + pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? FileScanner.SaveRoot);
                File.Copy(src, dst, true);
                try { File.SetLastWriteTimeUtc(dst, DateTimeOffset.FromUnixTimeSeconds(pair.Value.Mtime).UtcDateTime); } catch { }
            }

            foreach (var key in state.LastFiles.Keys)
            {
                if (!result.Manifest.Files.ContainsKey(key))
                {
                    var dst = FileScanner.LocalPath(key);
                    if (File.Exists(dst))
                        File.Delete(dst);
                }
            }

            foreach (var pair in result.Manifest.Deleted)
            {
                var dst = FileScanner.LocalPath(pair.Key);
                if (!File.Exists(dst))
                    continue;
                try
                {
                    if (!string.Equals(GitBlobHash.ComputeFile(dst), pair.Value.Sha, StringComparison.Ordinal))
                        continue;
                }
                catch
                {
                    continue;
                }
                File.Delete(dst);
            }
        }
        catch (Exception e)
        {
            result.Applied = true;
            Fail(true, "应用云端存档失败：" + e.Message);
            return;
        }

        state.LastCommit = result.Commit;
        state.LastTree = result.Tree;
        state.LastFiles = CloneFiles(result.Manifest.Files);
        state.Deleted = CloneTombstones(result.Manifest.Deleted);
        state.LastSyncUtc = Now();
        state.PendingPush = false;
        state.Save();
        dirty = false;
        conflict = string.Empty;
        result.Applied = true;
        try { if (Directory.Exists(result.StagingRoot)) Directory.Delete(result.StagingRoot, true); } catch { }

        if (beforeLoad)
        {
            Toast.Show(Loc.T("云存档：已从云端恢复"));
            RefreshStatus();
            return;
        }

        ReloadAfterPull(usersChanged);
        Toast.Show(Loc.T("云存档：已从云端恢复"));
        RefreshStatus();
    }

    private static void ReloadAfterPull(bool usersChanged)
    {
        var main = MainManager.Instance;
        var sm = main != null ? main.SaveManager : null;
        if (sm == null)
        {
            pendingReload = true;
            deferredReloadUsersChanged = usersChanged ? 1 : 0;
            return;
        }

        reloading = true;
        try
        {
            // 统一走 SaveManager.Load()：它会重新读取 users.dat 和所有用户存档，
            // 同时刷新 userDataList。只在 users.dat 变化时调用 LoadUserData(index)
            // 会漏掉“用户列表本身变了”的情况，表现为拉取完成后主菜单里看不到用户，
            // 必须重启游戏才出现。
            sm.Load();
            Diag.Info("已重读存档（usersChanged=" + usersChanged + "）");
        }
        catch (Exception e)
        {
            Diag.Warn("重读存档失败：" + e.Message);
            pendingReload = true;
            deferredReloadUsersChanged = usersChanged ? 1 : 0;
        }
        finally
        {
            reloading = false;
        }

        // 如果此刻正停在主菜单，它的用户名只在 Display() 里刷新；
        // 拉取是在后台完成的，不会自动再走一次 Display。
        RefreshMainmenuIfVisible();
    }

    private static void RefreshMainmenuIfVisible()
    {
        try
        {
            var menu = DSHCore.Ui.UiFinder.FindOne<MVZ2.Mainmenu.MainmenuController>();
            if (menu != null && menu.gameObject.activeInHierarchy)
                menu.Reload();
        }
        catch (Exception e)
        {
            Diag.Warn("刷新主菜单用户失败：" + e.Message);
        }
    }

    private static void TryReloadAfterPull(bool usersChanged)
    {
        if (!SceneGate.CanSync)
            return;
        pendingReload = false;
        ReloadAfterPull(usersChanged);
    }

    private static void FinishSync(bool success)
    {
        syncing = false;
        startupPullRunning = false;
        if (!success && string.IsNullOrEmpty(lastError) && startupResult != null && !string.IsNullOrEmpty(startupResult.Error))
            lastError = startupResult.Error;
        RefreshStatus();
        if (!success && !string.IsNullOrEmpty(lastError) && manualOperation)
            Toast.Show(Loc.T("云存档：同步失败") + "：" + lastError);
        manualOperation = false;
    }

    private static void Fail(bool manual, string message)
    {
        lastError = message ?? "未知错误";
        Diag.Warn("同步失败：" + lastError);
        status = Loc.T("同步失败");
        syncing = false;
        startupPullRunning = false;
        RefreshStatus();
        if (manual)
            Toast.Show(Loc.T("云存档：同步失败") + "：" + lastError);
        manualOperation = false;
    }

    private static void SetConflict(string message)
    {
        conflict = message ?? Loc.T("存在冲突");
        dirty = true;
        lastDirtyUtc = Now();
        syncing = false;
        startupPullRunning = false;
        status = Loc.T("存在冲突");
        Toast.Show(Loc.T("云存档：") + conflict);
        manualOperation = false;
        try { CloudSavesPage.NotifyChanged(); } catch { }
    }

    private static string ReadGraphQlCommit(HttpResponse response)
    {
        try
        {
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            var data = root != null ? MiniJson.Obj(root.TryGetValue("data", out var d) ? d : null) : null;
            var payload = data != null ? MiniJson.Obj(data.TryGetValue("createCommitOnBranch", out var p) ? p : null) : null;
            var commit = payload != null ? MiniJson.Obj(payload.TryGetValue("commit", out var c) ? c : null) : null;
            return commit != null ? MiniJson.Str(commit.TryGetValue("oid", out var o) ? o : null) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadRefSha(HttpResponse response)
    {
        try
        {
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            var obj = root != null ? MiniJson.Obj(root.TryGetValue("object", out var o) ? o : null) : null;
            return obj != null ? MiniJson.Str(obj.TryGetValue("sha", out var s) ? s : null) : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadCommitTree(HttpResponse response)
    {
        try
        {
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            var tree = root != null ? MiniJson.Obj(root.TryGetValue("tree", out var t) ? t : null) : null;
            return tree != null ? MiniJson.Str(tree.TryGetValue("sha", out var s) ? s : null) : "";
        }
        catch
        {
            return "";
        }
    }

    private static Dictionary<string, string>? ReadTree(HttpResponse response, out bool truncated)
    {
        truncated = false;
        try
        {
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            if (root == null)
                return null;
            truncated = MiniJson.Bool(root.TryGetValue("truncated", out var tr) ? tr : null);
            var arr = MiniJson.Arr(root.TryGetValue("tree", out var t) ? t : null);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var obj = MiniJson.Obj(item);
                    if (obj == null)
                        continue;
                    var type = MiniJson.Str(obj.TryGetValue("type", out var ty) ? ty : null);
                    if (!string.Equals(type, "blob", StringComparison.Ordinal))
                        continue;
                    var path = MiniJson.Str(obj.TryGetValue("path", out var p) ? p : null);
                    var sha = MiniJson.Str(obj.TryGetValue("sha", out var s) ? s : null);
                    if (!string.IsNullOrEmpty(path))
                        map[path] = sha;
                }
            }
            return map;
        }
        catch
        {
            return null;
        }
    }

    private static void MarkDirtyFromLocal()
    {
        var scan = FileScanner.Scan(state, out _, out var error);
        dirty = string.IsNullOrEmpty(error) && FileScanner.HasMeaningfulContent(scan);
        if (dirty)
            lastDirtyUtc = Now();
        state.PendingPush = dirty;
        state.Save();
    }

    private static Dictionary<string, FileMeta> CloneFiles(Dictionary<string, FileMeta> source)
    {
        var result = new Dictionary<string, FileMeta>(StringComparer.Ordinal);
        foreach (var pair in source)
            result[pair.Key] = pair.Value.Clone();
        return result;
    }

    private static Dictionary<string, Tombstone> CloneTombstones(Dictionary<string, Tombstone> source)
    {
        var result = new Dictionary<string, Tombstone>(StringComparer.Ordinal);
        foreach (var pair in source)
            result[pair.Key] = new Tombstone { Sha = pair.Value.Sha, At = pair.Value.At, Device = pair.Value.Device };
        return result;
    }

    private static string BuildCommitMessage(bool manual)
    {
        return "MVZ2 CloudSaves: " + (manual ? "manual" : "auto") + " " + FormatTime(Now());
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string FormatTime(long unix)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return unix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
