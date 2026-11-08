#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace MVZ2CloudSaves;

internal sealed class HttpResponse
{
    public long Status;
    public byte[] Body = Array.Empty<byte>();
    public string Error = string.Empty;
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);

    public bool Ok => Status >= 200 && Status < 300;
    public string Text => Encoding.UTF8.GetString(Body);

    public string Header(string name)
    {
        return Headers.TryGetValue(name, out var value) ? value : string.Empty;
    }
}

internal static class Http
{
    public static IEnumerator Send(string method, string url, Dictionary<string, string>? headers,
        byte[]? body, Action<HttpResponse> done, string? accept = null)
    {
        UnityWebRequest? request = null;
        var response = new HttpResponse();
        try
        {
            request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
                request.uploadHandler = new UploadHandlerRaw(body);
            request.timeout = Math.Max(5, CloudSavesConfig.RequestTimeoutSeconds.Value);

            if (headers != null)
            {
                foreach (var pair in headers)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                        continue;
                    try { request.SetRequestHeader(pair.Key, pair.Value); }
                    catch (Exception e) { Diag.Warn("设置请求头失败 " + pair.Key + "：" + e.Message); }
                }
            }
            if (!string.IsNullOrEmpty(accept))
                request.SetRequestHeader("Accept", accept);
        }
        catch (Exception e)
        {
            response.Error = e.Message;
            done?.Invoke(response);
            request?.Dispose();
            yield break;
        }

        yield return request.SendWebRequest();

        try
        {
            response.Status = request.responseCode;
            response.Error = request.error ?? string.Empty;
            try { response.Body = request.downloadHandler?.data ?? Array.Empty<byte>(); }
            catch { response.Body = Array.Empty<byte>(); }
            try
            {
                var hs = request.GetResponseHeaders();
                if (hs != null)
                {
                    foreach (var pair in hs)
                    {
                        if (!string.IsNullOrEmpty(pair.Key))
                            response.Headers[pair.Key] = pair.Value;
                    }
                }
            }
            catch
            {
                // 某些平台在连接失败时取响应头会抛
            }
        }
        finally
        {
            request.Dispose();
        }

        done?.Invoke(response);
    }
}
