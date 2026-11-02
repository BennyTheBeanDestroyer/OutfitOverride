using Skua.Core.Models.Items;

namespace OutfitOverride;

public partial class OutfitOverridePlugin
{
    // ---------------------------------------------------------------- 3. Ultra Change role

    // A pause starts when a script sets up its own class, gear or enhancements (see Patcher.cs for the full list):
    // the Ultra Change outfit goes on and the override steps aside until the script is done with it.

    /// <summary>Called before the script's CoreAdvanced.SmartEnhance runs.</summary>
    internal void BeforeSmartEnhance(string? className)
    {
        if (string.IsNullOrWhiteSpace(className) || !Active || IsRoleClass(className!))
            return;
        PauseForBoss(className!);
    }

    /// <summary>Adv.GearStore(): the script saved what you wear because it is about to change it.</summary>
    internal void OnGearStoreSave()
    {
        if (!Active)
            return;
        PauseForBoss("its own gear setup", fromGearStore: true);
    }

    /// <summary>Adv.GearStore(true): the script is putting your saved gear back - the special part is over.</summary>
    internal void OnGearStoreRestore()
    {
        if (_paused)
            Resume("script restored its saved gear");
    }

    /// <summary>Adv.EnhanceEquipped(...): the script is about to enhance whatever is worn right now.</summary>
    internal void BeforeEnhanceEquipped()
    {
        if (!Active)
            return;
        PauseForBoss("its own enhancements");
    }

    /// <summary>Adv.EnhanceItem(...): only matters when one of the items is worn (otherwise nothing you wear changes).</summary>
    internal void BeforeEnhanceItems(string[] names)
    {
        if (!Active || names.Length == 0)
            return;
        InventoryItem? worn = null;
        try
        {
            worn = Bot.Inventory.Items.FirstOrDefault(i => i.Equipped && names.Any(n => SameName(n, i.Name)));
        }
        catch { }
        if (worn is null || (worn.Category == ItemCategory.Class && IsRoleClass(worn.Name)))
            return;
        PauseForBoss($"enhancing {worn.Name}");
    }

    /// <summary>Equip the Ultra Change outfit, then pause the override so the script's own setup goes through.</summary>
    private void PauseForBoss(string className, bool fromGearStore = false)
    {
        if (!Settings.BossChangeArmed || _paused)
            return;

        string? bossOutfit = Settings.BossChangeOutfit;
        if (!string.IsNullOrWhiteSpace(bossOutfit))
            SendOutfit(bossOutfit!, $"Ultra Change ({className})", ignoreCooldown: true);

        // Let the script enhance the Ultra Change outfit however it wants: restore your own CBO auto-enhance setting.
        if (_enhanceOverridden)
            SetScriptCbo("DisableAutoEnhance", OwnCboValue("DisableAutoEnhance") ?? "False");

        lock (_sync)
        {
            _paused = true;
            _pausedByGearStore = fromGearStore;
            _pausedFor = className;
            _pausedAt = DateTime.UtcNow;
            _pausedMap = SafeMap();
            _lastOutfit = null; // the next role equip must resend its outfit
        }
        Log($"Script is setting up {(className.StartsWith("its ") || className.StartsWith("enhancing") ? className : $"its own class ({className})")}. " +
            (string.IsNullOrWhiteSpace(bossOutfit) ? "" : $"Equipped \"{bossOutfit}\" and ") +
            "paused the override until the script is done with it.");
        StateChanged?.Invoke();
    }

    /// <summary>Turns the override back on after a boss pause (only ever set by PauseForBoss, so it was on before).</summary>
    internal void Resume(string why)
    {
        lock (_sync)
        {
            if (!_paused)
                return;
            _paused = false;
            _pausedFor = null;
            _pausedMap = null;
            _lastOutfit = null;
        }
        // Back to your role outfits: protect them from auto-enhancing again.
        if (_enhanceOverridden && ScriptRunning)
            SetScriptCbo("DisableAutoEnhance", "True");
        Log($"Ultra Change done ({why}) - override back on.");
        StateChanged?.Invoke();
    }

    /// <summary>Drops a boss pause without logging (used when you switch the override off yourself).</summary>
    internal void ClearPause()
    {
        lock (_sync)
        {
            _paused = false;
            _pausedFor = null;
            _pausedMap = null;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Every 2 s while paused: resume when the script stops, or when the character leaves the boss map.</summary>
    private void WatchPause()
    {
        if (!_paused)
            return;
        try
        {
            if (!ScriptRunning)
            {
                Resume("script stopped");
                return;
            }
            // A GearStore() pause ends at GearStore(true), however many maps the script visits in between.
            if (_pausedByGearStore)
                return;
            string? map = SafeMap();
            if (string.IsNullOrEmpty(map) || SameName(map, _pausedMap))
                return;
            // Scripts often equip the boss class first and then travel to the boss: follow that first move.
            if ((DateTime.UtcNow - _pausedAt).TotalSeconds < 60)
            {
                lock (_sync)
                    _pausedMap = map;
                return;
            }
            Resume($"left {_pausedMap}");
        }
        catch { }
    }
}
