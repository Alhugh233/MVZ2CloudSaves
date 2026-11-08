#nullable enable

using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DSHCore.Ui;
using LanguagePackEnhance;

namespace MVZ2CloudSaves;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("qoznos.mvz2.core")]
[BepInDependency("com.mvz2.languagepackenhance")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;

        CloudSavesConfig.Init(Config);
        Loc.Register();

        UiOptions.DeclareMod(
            "com.mvz2.cloudsaves",
            "MVZ2 Cloud Saves",
            "MVZ2 用户数据的 GitHub 云存档。",
            "Alhugh233",
            "云存档");

        UiOptions.DeclareToggle("com.mvz2.cloudsaves", "AutoPush", "general",
            "自动上传", "保存后自动上传；关卡内只记录改动，退出关卡后上传。",
            getter: () => CloudSavesConfig.AutoPush.Value,
            setter: v => CloudSavesConfig.AutoPush.Value = v);
        UiOptions.DeclareToggle("com.mvz2.cloudsaves", "AutoPull", "general",
            "启动时检查云端", "只在非关卡状态应用云端存档。",
            getter: () => CloudSavesConfig.AutoPullOnStartup.Value,
            setter: v => CloudSavesConfig.AutoPullOnStartup.Value = v);
        UiOptions.DeclareSlider("com.mvz2.cloudsaves", "Debounce", "general",
            "保存后等待", 5, 600, true, "合并连续保存的等待秒数。",
            getter: () => CloudSavesConfig.DebounceSeconds.Value,
            setter: v => CloudSavesConfig.DebounceSeconds.Value = (int)v);
        try
        {
            CloudSaveService.Initialize();
            Diag.Info("service initialized");
        }
        catch (Exception e)
        {
            Log.LogError("[MVZ2CloudSaves] 初始化同步服务失败：" + e);
        }

        _ = new CloudSavesPage();
        PatchManager.Apply();
        CloudSaveService.BeginStartupPull();

        Log.LogInfo("Plugin " + MyPluginInfo.PLUGIN_GUID + " is loaded!");
    }
}

internal static class Diag
{
    public static void Info(string message) => Plugin.Log?.LogInfo("[MVZ2CloudSaves] " + message);
    public static void Warn(string message) => Plugin.Log?.LogWarning("[MVZ2CloudSaves] " + message);
    public static void Error(string message) => Plugin.Log?.LogError("[MVZ2CloudSaves] " + message);
}
