#nullable enable

using System;
using UnityEngine;

namespace MVZ2CloudSaves;

/// <summary>
/// 剪贴板读写。Android 上 Unity 的 <c>GUIUtility.systemCopyBuffer</c> 偶尔
/// 只能拿到部分内容，因此额外走一次 Android ClipboardManager，并取较长的结果。
/// </summary>
internal static class Clipboard
{
    public static string Read()
    {
        var best = string.Empty;
        var unityLength = 0;
        try
        {
            var unity = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(unity))
            {
                best = unity;
                unityLength = unity.Length;
            }
        }
        catch (Exception e)
        {
            Diag.Warn("读取 Unity 剪贴板失败：" + e.Message);
        }

        if (IsAndroid())
        {
            var native = ReadAndroid();
            if (native.Length > best.Length)
                best = native;
            Diag.Info("剪贴板长度：Unity=" + unityLength + "，Android=" + native.Length
                + "，采用=" + best.Length);
        }
        return best;
    }

    private static bool IsAndroid()
    {
        try { return Application.platform == RuntimePlatform.Android; }
        catch { return false; }
    }

    private static string ReadAndroid()
    {
        try
        {
            var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null)
                return string.Empty;

            var manager = activity.Call<AndroidJavaObject>("getSystemService", "clipboard");
            if (manager == null)
                return string.Empty;

            var clip = manager.Call<AndroidJavaObject>("getPrimaryClip");
            if (clip == null)
                return string.Empty;

            var count = clip.Call<int>("getItemCount");
            if (count <= 0)
                return string.Empty;

            var item = clip.Call<AndroidJavaObject>("getItemAt", 0);
            if (item == null)
                return string.Empty;

            var text = item.Call<AndroidJavaObject>("getText");
            if (text == null)
                return string.Empty;

            return text.Call<string>("toString") ?? string.Empty;
        }
        catch (Exception e)
        {
            Diag.Warn("读取 Android 剪贴板失败：" + e.Message);
            return string.Empty;
        }
    }
}
