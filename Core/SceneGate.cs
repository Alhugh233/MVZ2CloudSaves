#nullable enable

using System;
using MVZ2.Managers;

namespace MVZ2CloudSaves;

/// <summary>关卡/场景门禁：关卡内和切场景期间不允许同步。</summary>
internal static class SceneGate
{
    public static bool Busy { get; private set; }
    public static bool InLevel
    {
        get
        {
            try { return MainManager.Instance != null && MainManager.Instance.LevelManager.IsInLevel(); }
            catch { return false; }
        }
    }

    private static long nonLevelSince;
    private static bool lastInLevel;
    private static bool forceBusy;
    private const int SettleSeconds = 3;

    public static void OnGotoLevel()
    {
        forceBusy = true;
        Busy = true;
    }

    public static void OnExitLevel()
    {
        forceBusy = true;
        Busy = true;
    }

    public static void OnInitLevel()
    {
        forceBusy = false;
        Busy = false;
    }

    public static void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var inLevel = InLevel;
        if (inLevel)
        {
            lastInLevel = true;
            forceBusy = false;
            nonLevelSince = 0;
            Busy = false;
            return;
        }

        if (lastInLevel)
        {
            lastInLevel = false;
            forceBusy = false;
            nonLevelSince = now;
            Busy = true;
            return;
        }

        if (nonLevelSince == 0)
            nonLevelSince = now;
        if (forceBusy || now - nonLevelSince < SettleSeconds)
            Busy = true;
        else
            Busy = false;
    }

    public static bool CanSync => !InLevel && !Busy;

    public static long NonLevelSeconds
    {
        get
        {
            if (nonLevelSince == 0)
                return 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Math.Max(0, now - nonLevelSince);
        }
    }
}
