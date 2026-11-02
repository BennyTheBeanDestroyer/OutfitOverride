using Newtonsoft.Json.Linq;
using Skua.Core.Models.Skills;

namespace OutfitOverride;

public partial class OutfitOverridePlugin
{
    // ---------------------------------------------------------------- Wardrobe (same protocol as Skua's built-in Outfit Mode)

    /// <summary>All saved Wardrobe outfits: name -> slot data ("ar" = class, "co" = armor, "he", "ba", "pe", "Weapon").</summary>
    internal Dictionary<string, JObject> ReadOutfits()
    {
        var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // skua.swf exports getLoadouts (1.4.3+); older swfs: read the game object directly.
            string? raw = null;
            try { raw = Bot.Flash.Call("getLoadouts"); } catch { }
            if (string.IsNullOrWhiteSpace(raw) || raw is "null" or "undefined")
                raw = Bot.Flash.GetGameObject("world.objInfo.customs.loadouts");
            if (string.IsNullOrWhiteSpace(raw) || raw is "null" or "undefined")
                return result;
            foreach (JProperty p in JObject.Parse(raw).Properties())
                if (p.Value is JObject o)
                    result[p.Name] = o;
        }
        catch { }
        return result;
    }

    /// <summary>Class name of an outfit, from the item ID in its "ar" slot.</summary>
    internal string? OutfitClass(string outfit, Dictionary<string, JObject>? outfits = null)
    {
        outfits ??= ReadOutfits();
        if (!outfits.TryGetValue(outfit, out JObject? data) || !int.TryParse(data["ar"]?.ToString(), out int id))
            return null;
        try
        {
            return Bot.Inventory.Items?.Find(i => i.ID == id)?.Name
                ?? Bot.Bank.Items?.Find(i => i.ID == id)?.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Modes your Advanced Skills have for this class (like the CBO Mode list). Unknown class = all modes.</summary>
    internal List<string> ModesFor(string? className)
    {
        if (string.IsNullOrWhiteSpace(className) || _advancedSkills is null)
            return Enum.GetNames<ClassUseMode>().ToList();
        var modes = _advancedSkills.LoadedSkills
            .Where(s => string.Equals(s.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ClassUseMode)
            .Distinct()
            .OrderBy(m => m)
            .Select(m => m.ToString())
            .ToList();
        return modes.Count > 0 ? modes : new List<string> { nameof(ClassUseMode.Base) };
    }

    /// <summary>Equips a Wardrobe outfit: %xt%zm%equipLoadout%{room}%cmd%{name}%0%</summary>
    internal void EquipOutfit(string outfit)
    {
        Bot.Send.Packet($"%xt%zm%equipLoadout%{Bot.Map.RoomID}%cmd%{outfit}%0%", "String");
    }

    /// <summary>Re-reads the outfit list, refreshes each role's class, and returns a status line.</summary>
    internal string RefreshOutfits(out List<string> names)
    {
        names = new List<string>();
        if (!Bot.Player.Playing)
            return "Log in first, then Refresh Outfits.";

        var outfits = ReadOutfits();
        names = outfits.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string role in OverrideSettings.RoleNames)
        {
            RoleSetting rs = Settings.Role(role);
            if (!string.IsNullOrWhiteSpace(rs.Outfit))
                rs.ClassName = OutfitClass(rs.Outfit!, outfits) ?? rs.ClassName;
        }
        SaveSettings();
        return names.Count == 0
            ? "No Wardrobe outfits found. Save some in AQW (Your Hero > Outfits) and press Refresh again."
            : $"{names.Count} outfit(s) found.";
    }

    /// <summary>First run: pick up the Outfit / Mode choices already saved by Skua's CBO Outfit Mode, if any.</summary>
    internal void ImportFromCbo()
    {
        Dictionary<string, string>? cbo = ReadCboRaw();
        if (cbo is null)
            return;
        bool any = false;
        foreach (string role in OverrideSettings.RoleNames)
        {
            RoleSetting rs = Settings.Role(role);
            if (string.IsNullOrWhiteSpace(rs.Outfit) && cbo.TryGetValue($"{role}Outfit", out string? o) && IsRealValue(o))
            {
                rs.Outfit = o;
                any = true;
                if (cbo.TryGetValue($"{role}OutfitMode", out string? m) && Enum.TryParse(m, true, out ClassUseMode _))
                    rs.Mode = m;
            }
        }
        if (any)
            SaveSettings();
    }

    /// <summary>Equips an outfit, unless it was just equipped or this account doesn't have it.</summary>
    internal void SendOutfit(string outfit, string label, bool ignoreCooldown = false)
    {
        var owned = ReadOutfits();
        if (owned.Count > 0 && !owned.ContainsKey(outfit))
        {
            Log($"{label}: outfit \"{outfit}\" isn't in this account's Wardrobe - not equipped.");
            return;
        }

        lock (_sync)
        {
            if (!ignoreCooldown &&
                string.Equals(_lastOutfit, outfit, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - _lastSent).TotalSeconds < Math.Max(0, Settings.CooldownSeconds))
                return;
            _lastOutfit = outfit;
            _lastSent = DateTime.UtcNow;
        }

        EquipOutfit(outfit);
        if (Settings.Verbose)
            Log($"{label}: equipped outfit \"{outfit}\".");
        Thread.Sleep(Math.Max(0, Settings.SetDelayMs));
    }
}
