#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DSHCore.Ui;
using LanguagePackEnhance;

namespace MVZ2CloudSaves;

internal static class Loc
{
    private static bool registered;

    public static void Register()
    {
        if (registered)
            return;
        registered = true;

        try
        {
            LanguagePackEnhanceApi.RegisterMod("MVZ2CloudSaves", "MVZ2CloudSaves.lang.json");
        }
        catch (Exception e)
        {
            Diag.Warn("注册 LPE 语言包失败：" + e.Message);
        }

        // 同一份 en-US 表镜像进 DSHCore UiLocale，让「模组设置」页/日志也能翻译。
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("MVZ2CloudSaves.lang.json");
            if (stream == null)
                return;
            using var reader = new StreamReader(stream);
            var root = MiniJson.Obj(MiniJson.Parse(reader.ReadToEnd()));
            if (root == null)
                return;

            // 与 LPE 的 DshCoreIntegration.MirrorLanguagePackJson 保持同样的结构支持：
            //   普通条目：原文 -> 译文
            //   上下文条目：context -> { 原文 -> 译文 }
            //   复数条目：取第一项（UiLocale 不支持复数形式）
            foreach (var languagePair in root)
            {
                var language = languagePair.Key;
                var entries = MiniJson.Obj(languagePair.Value);
                if (entries == null)
                    continue;

                foreach (var entry in entries)
                {
                    if (entry.Value is string plain && !string.IsNullOrEmpty(plain))
                    {
                        UiLocale.Add(entry.Key, plain, language);
                        continue;
                    }

                    var contexts = MiniJson.Obj(entry.Value);
                    if (contexts == null)
                        continue;

                    foreach (var contextEntry in contexts)
                    {
                        var translation = FirstForm(contextEntry.Value);
                        if (!string.IsNullOrEmpty(translation))
                            UiLocale.Add(contextEntry.Key, translation, language, entry.Key);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Diag.Warn("镜像 UiLocale 失败：" + e.Message);
        }
    }

    private static string FirstForm(object? value)
    {
        if (value is string s)
            return s;
        var list = MiniJson.Arr(value);
        if (list != null && list.Count > 0)
            return list[0] as string ?? string.Empty;
        return string.Empty;
    }

    public static string T(string text, params object?[] args)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        try
        {
            if (args == null || args.Length == 0)
                return LanguagePackEnhanceApi.Translate(text);
            return LanguagePackEnhanceApi.Translate(text, args);
        }
        catch
        {
            return text;
        }
    }
}
