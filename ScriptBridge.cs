using System.Reflection;

namespace OutfitOverride;

public partial class OutfitOverridePlugin
{
    /// <summary>The script assembly whose CoreBots is calling File.ReadAllLines right now.</summary>
    private static Assembly? FindCallingScript()
    {
        foreach (var frame in new System.Diagnostics.StackTrace().GetFrames())
        {
            Type? t = frame.GetMethod()?.DeclaringType;
            while (t?.DeclaringType is not null)
                t = t.DeclaringType;
            if (t?.Name == "CoreBots")
                return t.Assembly;
        }
        return null;
    }

    /// <summary>Changes a value in the running script's in-memory CBO list (CoreBots reads it on every check).</summary>
    private void SetScriptCbo(string key, string value)
    {
        try
        {
            Type? core = _scriptAsm?.GetType("CoreBots");
            object? instance = core?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (core?.GetProperty("CBOList", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is List<string> list)
                SetLine(list, key, value);
        }
        catch (Exception ex)
        {
            Log($"Could not change {key} for the script: {ex.Message}");
        }
    }

    private string? OwnCboValue(string key) =>
        ReadCboRaw() is { } cbo && cbo.TryGetValue(key, out string? v) ? v : null;
}
