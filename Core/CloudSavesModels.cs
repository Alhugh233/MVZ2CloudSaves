#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace MVZ2CloudSaves;

internal sealed class FileMeta
{
    public string Sha = string.Empty;
    public long Size;
    public long Mtime;

    public FileMeta Clone() => new FileMeta { Sha = Sha, Size = Size, Mtime = Mtime };

    public bool SameContent(FileMeta other) =>
        other != null && string.Equals(Sha, other.Sha, StringComparison.Ordinal);
}

internal sealed class Tombstone
{
    public string Sha = string.Empty;
    public long At;
    public string Device = string.Empty;
}

internal sealed class SnapshotManifest
{
    public int Format = 1;
    public long CreatedUtc;
    public string DeviceId = string.Empty;
    public string GameVersion = string.Empty;
    public string UnityVersion = string.Empty;
    public string Platform = string.Empty;
    public Dictionary<string, FileMeta> Files = new(StringComparer.Ordinal);
    public Dictionary<string, Tombstone> Deleted = new(StringComparer.Ordinal);

    public string ToJson()
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("format").Value(Format);
        w.Key("createdUtc").Value(CreatedUtc);
        w.Key("device").Value(DeviceId);
        w.Key("game").Value(GameVersion);
        w.Key("unity").Value(UnityVersion);
        w.Key("platform").Value(Platform);

        w.Key("files").BeginObject();
        foreach (var pair in Files)
        {
            w.Key(pair.Key).BeginObject();
            w.Key("sha").Value(pair.Value.Sha);
            w.Key("size").Value(pair.Value.Size);
            w.Key("mtime").Value(pair.Value.Mtime);
            w.EndObject();
        }
        w.EndObject();

        w.Key("deleted").BeginObject();
        foreach (var pair in Deleted)
        {
            w.Key(pair.Key).BeginObject();
            w.Key("sha").Value(pair.Value.Sha);
            w.Key("at").Value(pair.Value.At);
            w.Key("device").Value(pair.Value.Device);
            w.EndObject();
        }
        w.EndObject();

        w.EndObject();
        return w.ToString();
    }

    public static SnapshotManifest? Parse(string json)
    {
        Dictionary<string, object?>? root;
        try
        {
            root = MiniJson.Obj(MiniJson.Parse(json));
        }
        catch
        {
            return null;
        }
        if (root == null)
            return null;

        var m = new SnapshotManifest
        {
            Format = MiniJson.Int(Get(root, "format")),
            CreatedUtc = MiniJson.Long(Get(root, "createdUtc")),
            DeviceId = MiniJson.Str(Get(root, "device")),
            GameVersion = MiniJson.Str(Get(root, "game")),
            UnityVersion = MiniJson.Str(Get(root, "unity")),
            Platform = MiniJson.Str(Get(root, "platform")),
        };

        if (m.Format <= 0)
            m.Format = 1;

        var files = MiniJson.Obj(Get(root, "files"));
        if (files != null)
        {
            foreach (var pair in files)
            {
                var obj = MiniJson.Obj(pair.Value);
                if (obj == null)
                    continue;
                m.Files[pair.Key] = new FileMeta
                {
                    Sha = MiniJson.Str(Get(obj, "sha")),
                    Size = MiniJson.Long(Get(obj, "size")),
                    Mtime = MiniJson.Long(Get(obj, "mtime")),
                };
            }
        }

        var deleted = MiniJson.Obj(Get(root, "deleted"));
        if (deleted != null)
        {
            foreach (var pair in deleted)
            {
                var obj = MiniJson.Obj(pair.Value);
                if (obj == null)
                    continue;
                m.Deleted[pair.Key] = new Tombstone
                {
                    Sha = MiniJson.Str(Get(obj, "sha")),
                    At = MiniJson.Long(Get(obj, "at")),
                    Device = MiniJson.Str(Get(obj, "device")),
                };
            }
        }

        return m;
    }

    private static object? Get(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out var value) ? value : null;
}

internal sealed class CloudSaveState
{
    public string DeviceId = string.Empty;
    public string LastCommit = string.Empty;
    public string LastTree = string.Empty;
    public long LastSyncUtc;
    public bool PendingPush;
    public long RateLimitReset;
    public Dictionary<string, FileMeta> LastFiles = new(StringComparer.Ordinal);
    public Dictionary<string, Tombstone> Deleted = new(StringComparer.Ordinal);

    public static string DirectoryPath =>
        Path.Combine(Paths.GameRootPath, "BepInEx", "CloudSaves");

    public static string FilePath => Path.Combine(DirectoryPath, "state.json");

    public static CloudSaveState Load()
    {
        var state = new CloudSaveState();
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var root = MiniJson.Obj(MiniJson.Parse(text));
                if (root != null)
                {
                    state.DeviceId = MiniJson.Str(Get(root, "device"));
                    state.LastCommit = MiniJson.Str(Get(root, "lastCommit"));
                    state.LastTree = MiniJson.Str(Get(root, "lastTree"));
                    state.LastSyncUtc = MiniJson.Long(Get(root, "lastSyncUtc"));
                    state.PendingPush = MiniJson.Bool(Get(root, "pendingPush"));
                    state.RateLimitReset = MiniJson.Long(Get(root, "rateLimitReset"));

                    var files = MiniJson.Obj(Get(root, "lastFiles"));
                    if (files != null)
                    {
                        foreach (var pair in files)
                        {
                            var obj = MiniJson.Obj(pair.Value);
                            if (obj == null)
                                continue;
                            state.LastFiles[pair.Key] = new FileMeta
                            {
                                Sha = MiniJson.Str(Get(obj, "sha")),
                                Size = MiniJson.Long(Get(obj, "size")),
                                Mtime = MiniJson.Long(Get(obj, "mtime")),
                            };
                        }
                    }

                    var deleted = MiniJson.Obj(Get(root, "deleted"));
                    if (deleted != null)
                    {
                        foreach (var pair in deleted)
                        {
                            var obj = MiniJson.Obj(pair.Value);
                            if (obj == null)
                                continue;
                            state.Deleted[pair.Key] = new Tombstone
                            {
                                Sha = MiniJson.Str(Get(obj, "sha")),
                                At = MiniJson.Long(Get(obj, "at")),
                                Device = MiniJson.Str(Get(obj, "device")),
                            };
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Diag.Warn("读取状态文件失败：" + e.Message);
        }

        if (string.IsNullOrEmpty(state.DeviceId))
            state.DeviceId = Guid.NewGuid().ToString("N");
        return state;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var w = new JsonWriter();
            w.BeginObject();
            w.Key("device").Value(DeviceId);
            w.Key("lastCommit").Value(LastCommit);
            w.Key("lastTree").Value(LastTree);
            w.Key("lastSyncUtc").Value(LastSyncUtc);
            w.Key("pendingPush").Value(PendingPush);
            w.Key("rateLimitReset").Value(RateLimitReset);

            w.Key("lastFiles").BeginObject();
            foreach (var pair in LastFiles)
            {
                w.Key(pair.Key).BeginObject();
                w.Key("sha").Value(pair.Value.Sha);
                w.Key("size").Value(pair.Value.Size);
                w.Key("mtime").Value(pair.Value.Mtime);
                w.EndObject();
            }
            w.EndObject();

            w.Key("deleted").BeginObject();
            foreach (var pair in Deleted)
            {
                w.Key(pair.Key).BeginObject();
                w.Key("sha").Value(pair.Value.Sha);
                w.Key("at").Value(pair.Value.At);
                w.Key("device").Value(pair.Value.Device);
                w.EndObject();
            }
            w.EndObject();

            w.EndObject();

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, w.ToString());
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
        catch (Exception e)
        {
            Diag.Warn("写入状态文件失败：" + e.Message);
        }
    }

    public void ResetForNewRepo()
    {
        LastCommit = string.Empty;
        LastTree = string.Empty;
        LastSyncUtc = 0;
        PendingPush = false;
        RateLimitReset = 0;
        LastFiles.Clear();
        Deleted.Clear();
        Save();
    }

    private static object? Get(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out var value) ? value : null;
}
