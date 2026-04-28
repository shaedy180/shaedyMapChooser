using System;
using System.Reflection;

namespace ShaedyHudManager;

public static class HudManagerProxy
{
    private static Type? _hudManagerType;
    private static Type? _hudPriorityType;
    private static MethodInfo? _showMethod;
    private static MethodInfo? _clearMethod;
    private static bool _initialized;
    private static string? _lastError;

    public static class Priority
    {
        public const int Critical = 100;
        public const int High = 75;
        public const int Medium = 50;
        public const int Low = 25;
        public const int Background = 10;
    }

    private static bool Initialize()
    {
        if (_initialized) return _hudManagerType != null;
        _initialized = true;

        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "shaedyHudManager")
                {
                    _hudManagerType = asm.GetType("ShaedyHudManager.HudManager");
                    _hudPriorityType = asm.GetType("ShaedyHudManager.HudPriority");
                    if (_hudManagerType != null)
                    {
                        _showMethod = _hudManagerType.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong), typeof(string), _hudPriorityType!, typeof(int) }, null);
                        _clearMethod = _hudManagerType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong) }, null);
                        Console.WriteLine("[HudManagerProxy] Connected to HudManager successfully.");
                        return true;
                    }
                }
            }

            _lastError = "shaedyHudManager plugin not found. Install it in plugins/shaedyHudManager/.";
            Console.WriteLine("[HudManagerProxy] ERROR: " + _lastError);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.WriteLine("[HudManagerProxy] ERROR: " + _lastError);
        }

        return false;
    }

    private static object ToHudPriority(int priority)
    {
        if (_hudPriorityType == null) return priority;
        return Enum.ToObject(_hudPriorityType, priority);
    }

    public static void Show(ulong steamId, string html, int priority, int displaySeconds)
    {
        if (!Initialize()) return;
        try
        {
            _showMethod!.Invoke(null, new object[] { steamId, html, ToHudPriority(priority), displaySeconds });
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HudManagerProxy] Show failed: " + ex.InnerException?.Message);
        }
    }

    public static void Clear(ulong steamId)
    {
        if (!Initialize()) return;
        try
        {
            _clearMethod!.Invoke(null, new object[] { steamId });
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HudManagerProxy] Clear failed: " + ex.InnerException?.Message);
        }
    }
}