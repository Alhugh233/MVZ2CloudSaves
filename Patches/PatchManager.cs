#nullable enable

using System;
using HarmonyLib;
using MVZ2.Debugs;
using MVZ2.Level;
using MVZ2.Managers;
using MVZ2.Saves;
using MVZ2.Scenes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MVZ2CloudSaves;

internal static class PatchManager
{
    private const string HarmonyId = "com.mvz2.cloudsaves";

    public static void Apply()
    {
        try
        {
            var harmony = new Harmony(HarmonyId);

            Patch(harmony, typeof(SaveManager), "Load", null,
                typeof(CloudSaveService), nameof(CloudSaveService.OnSaveManagerLoadPrefix),
                typeof(CloudSaveService), nameof(CloudSaveService.OnSaveManagerLoadPostfix));

            Patch(harmony, typeof(SaveManager), "SaveToFile", null,
                null, null,
                typeof(PatchManager), nameof(OnSaveToFile));
            Patch(harmony, typeof(SaveManager), "SaveUserList", null,
                null, null,
                typeof(PatchManager), nameof(OnSaveUserList));

            Patch(harmony, typeof(LevelManager), "SaveLevel", null,
                null, null,
                typeof(PatchManager), nameof(OnSaveLevel));
            Patch(harmony, typeof(LevelManager), "RemoveLevelState", null,
                null, null,
                typeof(PatchManager), nameof(OnRemoveLevelState));

            Patch(harmony, typeof(LevelManager), "GotoLevelSceneAsync", null,
                typeof(SceneGate), nameof(SceneGate.OnGotoLevel), null, null);
            Patch(harmony, typeof(LevelManager), "ExitLevelSceneAsync", null,
                typeof(SceneGate), nameof(SceneGate.OnExitLevel), null, null);
            Patch(harmony, typeof(LevelManager), "InitLevel", null,
                null, null,
                typeof(SceneGate), nameof(SceneGate.OnInitLevel));

            Patch(harmony, typeof(MainManager), "OnApplicationQuit", null,
                null, null,
                typeof(CloudSaveService), nameof(CloudSaveService.OnQuit));
            Patch(harmony, typeof(MainManager), "OnApplicationFocus", new[] { typeof(bool) },
                null, null,
                typeof(CloudSaveService), nameof(CloudSaveService.OnApplicationFocusEvent));

            // 每帧心跳：官方 MainSceneController 常驻且每帧 Update。
            // 不自行注入 MonoBehaviour —— 实测注入的 Start/Update 在这套
            // Android/BepInEx 运行时里不会触发，整个 mod 会静默失效。
            Patch(harmony, typeof(MainSceneController), "Update", null,
                null, null,
                typeof(CloudSaveService), nameof(CloudSaveService.OnUnityTick));

            // 备用心跳：同帧重复命中由 OnUnityTick 里的 frameCount 去重。
            Patch(harmony, typeof(PerformanceManager), "Update", null,
                null, null,
                typeof(CloudSaveService), nameof(CloudSaveService.OnUnityTick));

            Patch(harmony, typeof(DebugManager), "ExecuteCommand", new[] { typeof(string), typeof(int) },
                typeof(CloudSaveCommand), nameof(CloudSaveCommand.Prefix), null, null);
            Patch(harmony, typeof(DebugManager), "FillSuggestions",
                new[] { typeof(Il2CppStringArray), typeof(Il2CppSystem.Collections.Generic.List<string>) },
                typeof(CloudSaveCommand), nameof(CloudSaveCommand.SuggestPrefix), null, null);

            Diag.Info("Harmony 补丁已应用");
        }
        catch (Exception e)
        {
            Diag.Error("应用 Harmony 补丁失败：" + e);
        }
    }

    private static void Patch(Harmony harmony, Type type, string method, Type[]? args,
        Type? prefixType, string? prefix, Type? postfixType, string? postfix)
    {
        try
        {
            var target = args == null
                ? AccessTools.Method(type, method)
                : AccessTools.Method(type, method, args);
            if (target == null)
            {
                Diag.Warn("找不到补丁目标：" + type.FullName + "." + method + "，跳过");
                return;
            }

            HarmonyMethod? pre = null;
            HarmonyMethod? post = null;
            if (!string.IsNullOrEmpty(prefix) && prefixType != null)
            {
                var m = AccessTools.Method(prefixType, prefix);
                if (m == null)
                    Diag.Warn("找不到 prefix 方法：" + prefixType.Name + "." + prefix);
                else
                    pre = new HarmonyMethod(m);
            }
            if (!string.IsNullOrEmpty(postfix) && postfixType != null)
            {
                var m = AccessTools.Method(postfixType, postfix);
                if (m == null)
                    Diag.Warn("找不到 postfix 方法：" + postfixType.Name + "." + postfix);
                else
                    post = new HarmonyMethod(m);
            }
            if (pre == null && post == null)
                return;
            harmony.Patch(target, prefix: pre, postfix: post);
        }
        catch (Exception e)
        {
            Diag.Warn("挂补丁失败 " + type.Name + "." + method + "：" + e);
        }
    }

    private static void OnSaveToFile() => CloudSaveService.OnSaveEvent("SaveToFile");
    private static void OnSaveUserList() => CloudSaveService.OnSaveEvent("SaveUserList");
    private static void OnSaveLevel() => CloudSaveService.OnSaveEvent("SaveLevel");
    private static void OnRemoveLevelState() => CloudSaveService.OnSaveEvent("RemoveLevelState");
}
