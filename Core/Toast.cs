#nullable enable

using System;
using System.Collections.Generic;
using MVZ2.Managers;

namespace MVZ2CloudSaves;

/// <summary>MVZ2 原生 toast（PopupController：animator.SetTrigger("Show") + TMP 文本）。</summary>
internal static class Toast
{
    private static readonly Queue<string> queue = new Queue<string>();
    private static string last = string.Empty;
    private static long lastAt;
    private static long nextAt;
    private static readonly object gate = new object();

    public static void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        text = text.Trim();
        if (text.Length > 160)
            text = text.Substring(0, 157) + "…";

        lock (gate)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (string.Equals(text, last, StringComparison.Ordinal) && now - lastAt < 5)
                return;
            queue.Enqueue(text);
        }
    }

    public static void Flush()
    {
        lock (gate)
        {
            if (queue.Count == 0)
                return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now < nextAt)
                return;

            var main = MainManager.Instance;
            if (main == null || main.Scene == null)
                return;

            var text = queue.Dequeue();
            try
            {
                main.Scene.ShowPopup(text);
                // DSHCore AddonPage 的页面壳位于 GlobalUICanvas/Dialogs，且是在官方
                // Popup 之后创建的兄弟节点；把它提到最后，避免 toast 被页面挡住。
                var popup = DSHCore.Ui.UiFinder.FindOne<MVZ2.Scenes.PopupController>();
                if (popup != null && popup.transform != null)
                    popup.transform.SetAsLastSibling();
                last = text;
                lastAt = now;
                nextAt = now + 1;
            }
            catch (Exception e)
            {
                diagWarn(e);
            }
        }
    }

    private static void diagWarn(Exception e)
    {
        Diag.Warn("显示 toast 失败：" + e.Message);
    }
}
