#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MVZ2CloudSaves;

internal static class FileScanner
{
    public const string UserDataDirName = "userdata";

    private static string? cachedSaveRoot;

    public static string SaveRoot
    {
        get
        {
            if (cachedSaveRoot != null)
                return cachedSaveRoot;
            try
            {
                cachedSaveRoot = Path.Combine(Application.persistentDataPath, UserDataDirName);
            }
            catch (Exception e)
            {
                // 与 Application.unityVersion 一样，个别 ICall 在这套运行时里没有映射。
                Diag.Warn("读取 persistentDataPath 失败：" + e.Message);
                cachedSaveRoot = Path.Combine(BepInEx.Paths.GameRootPath, UserDataDirName);
            }
            return cachedSaveRoot;
        }
    }

    // 这台 Android/BepInEx 运行时里 UnityEngine.Application::get_unityVersion
    // 的 ICall 没有映射，直接读会抛 "ICall ... was not resolved"，并让整个
    // Harmony postfix 在 native->managed 边界炸掉。所以所有版本信息都做成
    // “读不到就空”的安全访问。
    private static string SafeVersion()
    {
        try { return Application.version ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeUnityVersion()
    {
        try { return Application.unityVersion ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafePlatform()
    {
        try { return Application.platform.ToString(); }
        catch { return "Unknown"; }
    }

    public static SnapshotManifest Scan(CloudSaveState state, out long totalBytes, out string error)
    {
        totalBytes = 0;
        error = string.Empty;

        var manifest = new SnapshotManifest
        {
            Format = 1,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DeviceId = state.DeviceId,
            GameVersion = SafeVersion(),
            UnityVersion = SafeUnityVersion(),
            Platform = SafePlatform(),
        };

        foreach (var pair in state.Deleted)
            manifest.Deleted[pair.Key] = new Tombstone { Sha = pair.Value.Sha, At = pair.Value.At, Device = pair.Value.Device };

        if (!Directory.Exists(SaveRoot))
            return manifest;

        string[] files;
        try
        {
            files = Directory.GetFiles(SaveRoot, "*", SearchOption.AllDirectories);
        }
        catch (Exception e)
        {
            error = "扫描存档目录失败：" + e.Message;
            return manifest;
        }

        long maxFile = (long)Math.Max(1, CloudSavesConfig.MaxFileMB.Value) * 1024 * 1024;
        long maxTotal = (long)Math.Max(1, CloudSavesConfig.MaxTotalMB.Value) * 1024 * 1024;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(SaveRoot, file).Replace('\\', '/');
            if (!Include(relative))
                continue;

            if (IsTempFile(relative))
                continue;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch { continue; }
            if (!info.Exists)
                continue;
            if (info.Length > maxFile)
            {
                error = "文件超过大小上限：" + relative + "（" + info.Length + " 字节）";
                return manifest;
            }

            var repoPath = UserDataDirName + "/" + relative;
            var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            var meta = new FileMeta { Size = info.Length, Mtime = mtime };

            if (state.LastFiles.TryGetValue(repoPath, out var old) && old.Size == meta.Size && old.Mtime == meta.Mtime && !string.IsNullOrEmpty(old.Sha))
            {
                meta.Sha = old.Sha;
            }
            else
            {
                try
                {
                    meta.Sha = GitBlobHash.ComputeFile(file);
                }
                catch (Exception e)
                {
                    error = "计算文件哈希失败：" + relative + "：" + e.Message;
                    return manifest;
                }
            }

            totalBytes += meta.Size;
            if (totalBytes > maxTotal)
            {
                error = "快照总大小超过上限（" + totalBytes + " 字节）";
                return manifest;
            }

            manifest.Files[repoPath] = meta;
            manifest.Deleted.Remove(repoPath);
        }

        return manifest;
    }

    public static bool IsLocalDirty(CloudSaveState state, SnapshotManifest local)
    {
        if (string.IsNullOrEmpty(state.LastCommit))
            return HasMeaningfulContent(local);

        foreach (var pair in local.Files)
        {
            if (!state.LastFiles.TryGetValue(pair.Key, out var old))
                return true;
            if (!string.Equals(old.Sha, pair.Value.Sha, StringComparison.Ordinal))
                return true;
        }
        foreach (var pair in state.LastFiles)
        {
            if (!local.Files.ContainsKey(pair.Key))
                return true;
        }
        return false;
    }

    public static bool HasLocalFiles()
    {
        try
        {
            if (!Directory.Exists(SaveRoot))
                return false;
            var files = Directory.GetFiles(SaveRoot, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(SaveRoot, file).Replace('\\', '/');
                if (Include(relative) && !IsTempFile(relative))
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// 只有 users.dat、没有 userN/ 内容的本地目录视为「新安装」，不阻止云端拉取。
    /// </summary>
    public static bool HasMeaningfulContent(SnapshotManifest manifest)
    {
        foreach (var key in manifest.Files.Keys)
        {
            if (IsUserContent(key))
                return true;
        }
        return false;
    }

    private static bool IsUserContent(string repoPath)
    {
        const string prefix = UserDataDirName + "/user";
        if (!repoPath.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        int slash = repoPath.IndexOf('/', prefix.Length);
        return slash > prefix.Length;
    }

    public static string LocalPath(string repoPath)
    {
        if (repoPath.StartsWith(UserDataDirName + "/", StringComparison.Ordinal))
            repoPath = repoPath.Substring(UserDataDirName.Length + 1);
        return Path.Combine(SaveRoot, repoPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string RepoPath(string localRelative)
    {
        return UserDataDirName + "/" + localRelative.Replace('\\', '/');
    }

    private static bool Include(string relative)
    {
        var p = relative.Replace('\\', '/');
        if (!CloudSavesConfig.IncludeReplays.Value && (p == "replays" || p.StartsWith("replays/", StringComparison.Ordinal) || p.Contains("/replays/", StringComparison.Ordinal)))
            return false;
        if (!CloudSavesConfig.IncludeBackups.Value)
        {
            var name = Path.GetFileName(p);
            if (name.StartsWith("user.dat.bak", StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool IsTempFile(string relative)
    {
        var name = Path.GetFileName(relative);
        return name.IndexOf(".temp", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
