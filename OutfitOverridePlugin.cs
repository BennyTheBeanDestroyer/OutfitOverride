using System.Reflection;
using System.Windows;
using Skua.Core.Interfaces;

namespace OutfitOverride;

/// <summary>
/// Outfit Override - a Skua plugin.
///
/// While switched on, a running script's CoreBots loadout (Solo / Farm / Dodge / Boss) is replaced by the Wardrobe
/// outfits and Advanced Skills modes picked in the plugin window. When a script sets up its own class, gear or
/// enhancements (a boss or hard enemy), the "Ultra Change" outfit is equipped and the override pauses until the
/// script is done with it. Switched off, scripts use the normal CoreBots loadout untouched.
///
/// The plugin is split over several files:
///   OutfitOverridePlugin.cs  - load / unload, window, shared state and helpers (this file)
///   Wardrobe.cs              - reading and equipping in-game Wardrobe outfits
///   LoadoutOverride.cs       - replacing the CoreBots loadout and the four role classes
///   UltraChange.cs           - the Ultra Change pause and resume
///   ScriptBridge.cs          - reaching into the running script's CoreBots
///   Patcher.cs               - the Harmony hooks
///   OverrideWindow.cs        - the plugin window
/// </summary>
public partial class OutfitOverridePlugin : ISkuaPlugin
{
    internal static OutfitOverridePlugin? Instance { get; private set; }

    public string Name => "Outfit Override";
    public string Author => "OutfitOverride";
    public string Description =>
        "Toggleable override for the CoreBots loadout: scripts equip your Wardrobe outfits (with the class mode you pick) instead of the CBO class + gear.";

    public List<IOption>? Options => null;

    internal OverrideSettings Settings { get; private set; } = new();
    internal IScriptInterface Bot => _bot!;

    private IScriptInterface? _bot;
    private IAdvancedSkillContainer? _advancedSkills;
    private IPluginHelper? _helper;
    private OverrideWindow? _window;

    private readonly object _sync = new();
    private string? _lastOutfit;
    private DateTime _lastSent = DateTime.MinValue;

    // Ultra Change pause: set when a script asks for its own class setup while the override is on.
    private bool _paused;
    private string? _pausedMap;
    private DateTime _pausedAt;
    private string? _pausedFor;
    private Timer? _watch;

    // The running script's compiled assembly (holds its own CoreBots / CoreAdvanced).
    private Assembly? _scriptAsm;
    private bool _enhanceOverridden;
    private bool _pausedByGearStore;

    /// <summary>Raised (any thread) when the pause state changes, so the window can update.</summary>
    internal event Action? StateChanged;

    /// <summary>Raised (any thread) when a different account logs in (or everyone logs out).</summary>
    internal event Action? AccountChanged;

    internal bool Paused => _paused;
    internal string? PausedFor => _pausedFor;

    /// <summary>True while the override should act: switch on, a script running, and not paused for Ultra Change.</summary>
    private bool Active => Settings.Enabled && !_paused && ScriptRunning;

    // ---------------------------------------------------------------- load / unload

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        Instance = this;
        _helper = helper;
        _bot = (IScriptInterface)provider.GetService(typeof(IScriptInterface))!;
        _advancedSkills = provider.GetService(typeof(IAdvancedSkillContainer)) as IAdvancedSkillContainer;
        Settings = OverrideSettings.Load();
        EnsureAccount();

        // Harmony ships embedded in this DLL; resolve it before any Harmony type is touched.
        EmbeddedAssemblies.Register();
        try
        {
            Patcher.Apply();
        }
        catch (Exception ex)
        {
            Log($"Could not hook Skua: {ex.Message}");
        }

        _watch = new Timer(_ =>
        {
            EnsureAccount();
            WatchPause();
        }, null, 2000, 2000);
        helper.AddMenuButton(Name, ShowWindow);
        Log("Loaded. Open it from the Plugins menu.");
    }

    public void Unload()
    {
        try { Patcher.Remove(); } catch { }
        _watch?.Dispose();
        _helper?.RemoveMenuButton(Name);
        try { Application.Current?.Dispatcher.Invoke(() => _window?.ForceClose()); } catch { }
        Instance = null;
    }

    private void ShowWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverrideWindow(this);
            _window.Show();
            _window.Activate();
        });
    }

    internal void SaveSettings() => Settings.Save();

    // ---------------------------------------------------------------- accounts

    /// <summary>
    /// Every account has its own switch and outfits. Called every 2 s and before the plugin acts, so the settings
    /// always belong to whoever is logged in. A new account starts empty.
    /// </summary>
    internal void EnsureAccount()
    {
        string? account = CurrentAccount();
        if (account is null ? Settings.Account is null : SameName(account, Settings.Account))
            return;

        lock (_sync)
        {
            if (account is null ? Settings.Account is null : SameName(account, Settings.Account))
                return;
            _paused = false;
            _pausedFor = null;
            _lastOutfit = null;
            Settings.UseAccount(account);
        }
        if (account is not null)
        {
            if (Settings.IsNew)
                ImportFromCbo(); // only this account's own CoreBots Outfit Mode picks, if it has any
            Log($"Using {account}'s outfits ({(Settings.Enabled ? "ON" : "off")}).");
        }
        AccountChanged?.Invoke();
    }

    private string? CurrentAccount()
    {
        try
        {
            if (!Bot.Player.LoggedIn)
                return null;
            string? name = Bot.Player.Username;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    private bool ScriptRunning
    {
        get { try { return Bot.Manager.ScriptRunning; } catch { return false; } }
    }

    private string? SafeMap()
    {
        try { return Bot.Map.Name; } catch { return null; }
    }

    private static bool SameName(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    internal void Log(string message)
    {
        try { _bot?.Log($"[Outfit Override] {message}"); } catch { }
    }
}
