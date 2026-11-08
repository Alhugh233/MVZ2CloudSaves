#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace MVZ2CloudSaves;

internal static class GitHubApi
{
    public static string ApiBase
    {
        get
        {
            var value = CloudSavesConfig.ApiBase.Value;
            if (string.IsNullOrWhiteSpace(value))
                value = "https://api.github.com";
            return value.TrimEnd('/');
        }
    }

    public static string Owner => CloudSavesConfig.Owner.Value.Trim();
    public static string Repo => CloudSavesConfig.Repo.Value.Trim();

    public static string RepoPath => "/repos/" + Encode(Owner) + "/" + Encode(Repo);

    public static string Url(string path) => ApiBase + path;

    public static Dictionary<string, string> Headers(string? token = null, bool raw = false)
    {
        var t = string.IsNullOrEmpty(token) ? TokenStore.CurrentToken : token;
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + t,
            ["User-Agent"] = "MVZ2CloudSaves/" + MyPluginInfo.PLUGIN_VERSION,
            ["X-GitHub-Api-Version"] = CloudSavesConfig.ApiVersion.Value,
        };
        h["Accept"] = raw ? "application/vnd.github.raw+json" : "application/vnd.github+json";
        return h;
    }

    public static IEnumerator Get(string path, Action<HttpResponse> done, string? token = null, bool raw = false)
        => Http.Send("GET", Url(path), Headers(token, raw), null, Observe(done), raw ? "application/vnd.github.raw+json" : null);

    public static IEnumerator SendJson(string method, string path, string json, Action<HttpResponse> done, string? token = null)
    {
        var body = Encoding.UTF8.GetBytes(json ?? "{}");
        var headers = Headers(token);
        headers["Content-Type"] = "application/json";
        return Http.Send(method, Url(path), headers, body, Observe(done));
    }

    private static Action<HttpResponse> Observe(Action<HttpResponse> done)
    {
        return response =>
        {
            try { CloudSaveService.Observe(response); }
            catch { }
            done?.Invoke(response);
        };
    }

    public static IEnumerator GetUser(string token, Action<HttpResponse> done)
        => Get("/user", done, token);

    /// <summary>
    /// 校验待保存的 token。刻意不走 <see cref="Observe"/>：
    /// 候选 token 的 401 不应该把全局状态改成「授权失效」。
    /// </summary>
    public static IEnumerator GetUserUnobserved(string token, Action<HttpResponse> done)
        => Http.Send("GET", Url("/user"), Headers(token), null, done);

    public static IEnumerator GetRepo(Action<HttpResponse> done)
        => Get(RepoPath, done);

    public static IEnumerator GetRepo(string owner, string repo, Action<HttpResponse> done)
        => Get("/repos/" + Encode(owner) + "/" + Encode(repo), done);

    public static IEnumerator GetRef(string branch, Action<HttpResponse> done)
        => Get(RepoPath + "/git/ref/heads/" + EncodeRef(branch), done);

    public static IEnumerator GetCommit(string sha, Action<HttpResponse> done)
        => Get(RepoPath + "/git/commits/" + Encode(sha), done);

    public static IEnumerator GetTree(string sha, Action<HttpResponse> done)
        => Get(RepoPath + "/git/trees/" + Encode(sha) + "?recursive=1", done);

    public static IEnumerator GetBlob(string sha, Action<HttpResponse> done)
        => Get(RepoPath + "/git/blobs/" + Encode(sha), done, null, true);

    public static IEnumerator CreateBlob(byte[] data, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("content").Value(Convert.ToBase64String(data));
        w.Key("encoding").Value("base64");
        w.EndObject();
        return SendJson("POST", RepoPath + "/git/blobs", w.ToString(), done);
    }

    public static IEnumerator CreateTree(string? baseTree, List<TreeEntry> entries, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        if (!string.IsNullOrEmpty(baseTree))
            w.Key("base_tree").Value(baseTree);
        w.Key("tree").BeginArray();
        foreach (var entry in entries)
        {
            w.BeginObject();
            w.Key("path").Value(entry.Path);
            w.Key("mode").Value(entry.Mode);
            w.Key("type").Value(entry.Type);
            if (entry.Sha == null)
                w.Key("sha").Null();
            else
                w.Key("sha").Value(entry.Sha);
            w.EndObject();
        }
        w.EndArray();
        w.EndObject();
        return SendJson("POST", RepoPath + "/git/trees", w.ToString(), done);
    }

    public static IEnumerator CreateCommit(string message, string tree, string? parent, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("message").Value(message);
        w.Key("tree").Value(tree);
        if (!string.IsNullOrEmpty(parent))
        {
            w.Key("parents").BeginArray().Value(parent).EndArray();
        }
        w.EndObject();
        return SendJson("POST", RepoPath + "/git/commits", w.ToString(), done);
    }

    public static IEnumerator UpdateRef(string branch, string sha, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("sha").Value(sha);
        w.Key("force").Value(false);
        w.EndObject();
        return SendJson("PATCH", RepoPath + "/git/refs/heads/" + EncodeRef(branch), w.ToString(), done);
    }

    public static IEnumerator CreateCommitOnBranch(string owner, string repo, string branch, string head,
        string headline, List<GraphQlAddition> additions, List<string> deletions, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("query").Value("mutation($input: CreateCommitOnBranchInput!){ createCommitOnBranch(input:$input){ commit { oid } } }");
        w.Key("variables").BeginObject();
        w.Key("input").BeginObject();
        w.Key("branch").BeginObject();
        w.Key("repositoryNameWithOwner").Value(owner + "/" + repo);
        w.Key("branchName").Value(branch);
        w.EndObject();
        w.Key("message").BeginObject();
        w.Key("headline").Value(headline);
        w.EndObject();
        w.Key("expectedHeadOid").Value(head);
        w.Key("fileChanges").BeginObject();
        w.Key("additions").BeginArray();
        foreach (var item in additions)
        {
            w.BeginObject();
            w.Key("path").Value(item.Path);
            w.Key("contents").Value(Convert.ToBase64String(item.Bytes));
            w.EndObject();
        }
        w.EndArray();
        w.Key("deletions").BeginArray();
        foreach (var path in deletions)
        {
            w.BeginObject();
            w.Key("path").Value(path);
            w.EndObject();
        }
        w.EndArray();
        w.EndObject();
        w.EndObject();
        w.EndObject();
        w.EndObject();
        return SendJson("POST", "/graphql", w.ToString(), done);
    }

    public static IEnumerator CreateRepository(string name, Action<HttpResponse> done)
    {
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("name").Value(name);
        w.Key("description").Value("MVZ2 CloudSaves data");
        w.Key("private").Value(true);
        w.Key("auto_init").Value(true);
        w.Key("has_issues").Value(false);
        w.Key("has_projects").Value(false);
        w.Key("has_wiki").Value(false);
        w.Key("has_discussions").Value(false);
        w.EndObject();
        return SendJson("POST", "/user/repos", w.ToString(), done);
    }

    public static IEnumerator PutReadme(string branch, Action<HttpResponse> done)
    {
        var content = "# MVZ2 CloudSaves\n\nThis repository stores MVZ2 user data via MVZ2CloudSaves.\n";
        var w = new JsonWriter();
        w.BeginObject();
        w.Key("message").Value("Initialize MVZ2 CloudSaves");
        w.Key("content").Value(Convert.ToBase64String(Encoding.UTF8.GetBytes(content)));
        if (!string.IsNullOrEmpty(branch))
            w.Key("branch").Value(branch);
        w.EndObject();
        return SendJson("PUT", RepoPath + "/contents/README.md", w.ToString(), done);
    }

    public static string Encode(string value) => PercentEncode(value ?? string.Empty);

    public static string EncodeRef(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        // 分支里的 / 保留；其它字符逐段转义。
        var parts = value.Split('/');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = PercentEncode(parts[i]);
        return string.Join("/", parts);
    }

    /// <summary>UTF-8 百分号编码。刻意不用 System.Uri（Android BepInEx 的 JIT 坑）。</summary>
    private static string PercentEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder(bytes.Length + 8);
        foreach (var b in bytes)
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    public static string ErrorMessage(HttpResponse response)
    {
        try
        {
            var root = MiniJson.Obj(MiniJson.Parse(response.Text));
            if (root != null)
            {
                var msg = MiniJson.Str(root.TryGetValue("message", out var m) ? m : null);
                if (!string.IsNullOrEmpty(msg))
                    return msg;
                var errors = MiniJson.Arr(root.TryGetValue("errors", out var e) ? e : null);
                if (errors != null && errors.Count > 0)
                {
                    var first = MiniJson.Obj(errors[0]);
                    var em = first != null ? MiniJson.Str(first.TryGetValue("message", out var emv) ? emv : null) : "";
                    if (!string.IsNullOrEmpty(em))
                        return em;
                }
            }
        }
        catch
        {
        }
        if (!string.IsNullOrEmpty(response.Error))
            return response.Error;
        return "HTTP " + response.Status;
    }
}

internal sealed class GraphQlAddition
{
    public string Path = string.Empty;
    public byte[] Bytes = Array.Empty<byte>();
}

internal sealed class TreeEntry
{
    public string Path = string.Empty;
    public string Mode = "100644";
    public string Type = "blob";
    public string? Sha;
}
