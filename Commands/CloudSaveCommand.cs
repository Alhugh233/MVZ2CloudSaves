#nullable enable

using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MVZ2Logic;

namespace MVZ2CloudSaves;

internal static class CloudSaveCommand
{
    public const string Name = "cloudsave";

    public static bool Prefix(string input)
    {
        try
        {
            var args = Split(input);
            if (args.Length == 0 || !string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase))
                return true;

            var text = Execute(args);
            if (text == null)
                return true;
            PrintLine("> " + input.Trim());
            PrintLine(text);
            return false;
        }
        catch (Exception e)
        {
            Diag.Warn("指令执行失败：" + e);
            return true;
        }
    }

    public static void SuggestPrefix(Il2CppStringArray parts,
        Il2CppSystem.Collections.Generic.List<string> currentSuggestions)
    {
        try
        {
            if (parts == null || currentSuggestions == null || parts.Length == 0)
                return;
            var args = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                args[i] = parts[i];
            var extra = Suggest(args);
            for (int i = 0; i < extra.Length; i++)
            {
                bool exists = false;
                for (int j = 0; j < currentSuggestions.Count; j++)
                {
                    if (string.Equals(currentSuggestions[j], extra[i], StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    currentSuggestions.Add(extra[i]);
            }
        }
        catch
        {
        }
    }

    private static string[] Suggest(string[] args)
    {
        if (args.Length == 0)
            return new[] { Name };
        if (args.Length == 1 && Name.StartsWith(args[0], StringComparison.OrdinalIgnoreCase))
            return new[] { Name };
        if (args.Length >= 1 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase))
        {
            var sub = args.Length > 1 ? args[1] : string.Empty;
            var all = new[] { "status", "sync", "push", "pull", "conflicts", "help" };
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in all)
            {
                if (item.StartsWith(sub, StringComparison.OrdinalIgnoreCase))
                    list.Add(item);
            }
            return list.ToArray();
        }
        return Array.Empty<string>();
    }

    private static string? Execute(string[] args)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        switch (sub)
        {
            case "status":
                return StatusText();
            case "sync":
                CloudSaveService.ManualSync();
                return Loc.T("云存档：已开始同步（非关卡状态才会执行）");
            case "push":
                CloudSaveService.ManualUpload();
                return Loc.T("云存档：已请求上传");
            case "pull":
                CloudSaveService.ManualDownload();
                return Loc.T("云存档：已请求从云端恢复");
            case "conflicts":
                return string.IsNullOrEmpty(CloudSaveService.Conflict)
                    ? Loc.T("当前没有冲突")
                    : CloudSaveService.Conflict;
            case "help":
                return HelpText();
            default:
                return HelpText();
        }
    }

    private static string StatusText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MVZ2 CloudSaves");
        sb.AppendLine("  " + Loc.T("状态") + "：" + CloudSaveService.Status);
        sb.AppendLine("  " + Loc.T("仓库") + "：" + (CloudSavesConfig.IsConfigured
            ? CloudSavesConfig.Owner.Value + "/" + CloudSavesConfig.Repo.Value
            : Loc.T("未配置")));
        sb.AppendLine("  Token：" + Loc.T(TokenStore.Describe()));
        if (!string.IsNullOrEmpty(CloudSaveService.LastError))
            sb.AppendLine("  " + Loc.T("错误") + "：" + CloudSaveService.LastError);
        if (!string.IsNullOrEmpty(CloudSaveService.Conflict))
            sb.AppendLine("  " + Loc.T("冲突") + "：" + CloudSaveService.Conflict);
        return sb.ToString().TrimEnd();
    }

    private static string HelpText()
    {
        return "/cloudsave status | sync | push | pull | conflicts | help\n"
            + Loc.T("云存档只在非关卡状态同步关卡进度。");
    }

    private static string[] Split(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = trimmed.Substring(1);
        return trimmed.Length == 0
            ? Array.Empty<string>()
            : trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void PrintLine(string text)
    {
        try
        {
            var debug = Global.Debugs;
            if (debug != null)
            {
                debug.Print(text + "\n");
                return;
            }
        }
        catch
        {
        }
        Diag.Info(text);
    }
}
