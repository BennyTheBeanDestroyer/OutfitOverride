using System.Windows;
using Newtonsoft.Json.Linq;
using Skua.Core.Models;
using Skua.Core.Models.Items;

namespace OutfitOverride;

public partial class OutfitOverridePlugin
{
    // ---------------------------------------------------------------- 1. override the CBO loadout for running scripts

    /// <summary>
    /// Postfix on File.ReadAllLines: when a running script reads CBO_Storage(user).txt, it gets the outfit's class,
    /// the chosen mode, and "equipment off" for that role. The file on disk is never changed.
    /// </summary>
    internal string[] OverrideCboLines(string path, string[] lines)
    {
        if (!IsCboFile(path) || OnUiThread())
            return lines;
        EnsureAccount();
        if (_paused && !ScriptRunning)
            Resume("a new script started");
        if (!Active)
            return lines;

        var list = lines.ToList();
        Dictionary<string, JObject>? outfits = null;
        bool overridden = false;
        foreach (string role in OverrideSettings.RoleNames)
        {
            RoleSetting rs = Settings.Role(role);
            if (string.IsNullOrWhiteSpace(rs.Outfit))
                continue;

            outfits ??= ReadOutfits();
            if (outfits.Count > 0 && !outfits.ContainsKey(rs.Outfit!))
            {
                Log($"{role}: outfit \"{rs.Outfit}\" isn't in this account's Wardrobe - using the CoreBots loadout for {role}.");
                continue;
            }
            string? cls = OutfitClass(rs.Outfit!, outfits) ?? rs.ClassName;
            if (cls is not null)
                rs.ClassName = cls;

            overridden = true;
            // The outfit brings its own gear, so CoreBots must not put the CBO gear on top of it.
            SetLine(list, $"{role}EquipCheck", "False");
            if (!string.IsNullOrWhiteSpace(cls))
                SetLine(list, $"{role}ClassSelect", cls!);
            if (!string.IsNullOrWhiteSpace(rs.Mode))
                SetLine(list, $"{role}ModeSelect", rs.Mode!);
        }
        if (!overridden)
            return lines;

        // Remember which script is reading, so a boss setup can be detected at its first SmartEnhance.
        _scriptAsm = FindCallingScript();
        if (_scriptAsm is not null)
        {
            try { Patcher.PatchScript(_scriptAsm); } catch (Exception ex) { Log($"Could not hook the script's enhancing: {ex.Message}"); }
        }

        // Your role outfits are hand-picked: no auto-enhancing while they're in use.
        // (Turned back on for the script during an Ultra Change pause.)
        _enhanceOverridden = Settings.SkipAutoEnhance;
        if (_enhanceOverridden)
            SetLine(list, "DisableAutoEnhance", "True");
        if (Settings.Verbose)
            Log("Script is using the outfit loadout.");
        return list.ToArray();
    }

    private static void SetLine(List<string> lines, string key, string value)
    {
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(key + ":", StringComparison.Ordinal))
            {
                lines[i] = $"{key}: {value}"; // every copy: CoreBots reads the first match
                found = true;
            }
        }
        if (!found)
            lines.Add($"{key}: {value}");
    }

    private static bool IsCboFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("CBO_Storage(", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    // Skua's own CoreBots Options window reads the same file on the UI thread - leave that alone, so saving the
    // CBO window never writes the override into your real loadout.
    private static bool OnUiThread() => Application.Current?.Dispatcher.CheckAccess() == true;

    // ---------------------------------------------------------------- 2. equip the outfit when the script equips that class

    /// <summary>Prefix on ScriptInventory.EquipItem(int). Returns false to skip the script's own equip.</summary>
    internal bool BeforeEquip(int id)
    {
        try
        {
            EnsureAccount();
            if (!Settings.Enabled || !ScriptRunning)
                return true;
            if (!Bot.Inventory.TryGetItem(id, out InventoryItem? item) || item is null || item.Category != ItemCategory.Class)
                return true;

            string? outfit = null, role = null;
            foreach (string r in OverrideSettings.RoleNames)
            {
                RoleSetting rs = Settings.Role(r);
                if (!string.IsNullOrWhiteSpace(rs.Outfit) && SameName(rs.ClassName, item.Name))
                {
                    outfit = rs.Outfit;
                    role = r;
                    break;
                }
            }

            if (_paused)
            {
                // Script went back to one of your roles: the boss part is over.
                if (outfit is not null || IsCboRoleClass(item.Name))
                    Resume($"script went back to {item.Name}");
                else
                    return true;
                if (outfit is null)
                    return true;
            }
            else if (outfit is null)
            {
                // Not one of your roles: the script wants its own setup (boss / hard enemy).
                if (!IsCboRoleClass(item.Name))
                    PauseForBoss(item.Name);
                return true;
            }

            SendOutfit(outfit, role!);

            // Skip the script's equip if the outfit already put the class on; otherwise let it through so the bot
            // never fights with the wrong skills.
            return !SameName(Bot.Player.CurrentClass?.Name, item.Name);
        }
        catch (Exception ex)
        {
            Log($"Error, equipping normally: {ex.Message}");
            return true;
        }
    }

    private bool IsRoleClass(string className) =>
        OverrideSettings.RoleNames.Any(r => !string.IsNullOrWhiteSpace(Settings.Role(r).Outfit) && SameName(Settings.Role(r).ClassName, className))
        || IsCboRoleClass(className);

    private bool IsCboRoleClass(string className)
    {
        Dictionary<string, string>? cbo = ReadCboRaw();
        return cbo is not null && OverrideSettings.RoleNames.Any(r =>
            cbo.TryGetValue($"{r}ClassSelect", out string? c) && SameName(c, className));
    }

    internal Dictionary<string, string>? ReadCboRaw()
    {
        try
        {
            string dir = ClientFileSources.SkuaOptionsDIR;
            if (!Directory.Exists(dir))
                return null;
            string user = string.Empty;
            try { user = Bot.Flash.GetGameObject("sfc.myUserName")?.Trim('"') ?? string.Empty; } catch { }
            if (string.IsNullOrEmpty(user))
                try { user = Bot.Player.Username ?? string.Empty; } catch { }

            string[] all = Directory.GetFiles(dir, "CBO_Storage(*).txt");
            string? file = all.FirstOrDefault(f => Path.GetFileName(f).Equals($"CBO_Storage({user}).txt", StringComparison.OrdinalIgnoreCase))
                ?? (all.Length == 1 ? all[0] : null);
            if (file is null)
                return null;

            // ReadAllText on purpose: ReadAllLines is the hooked method.
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllText(file).Split('\n'))
            {
                int sep = line.IndexOf(": ", StringComparison.Ordinal);
                if (sep > 0)
                    values[line[..sep].Trim()] = line[(sep + 2)..].Trim();
            }
            return values;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRealValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Equals("None", StringComparison.OrdinalIgnoreCase);
}
