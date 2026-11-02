using Newtonsoft.Json;
using Skua.Core.Models;

namespace OutfitOverride;

public class RoleSetting
{
    /// <summary>Wardrobe outfit name, exactly as in AQW's Outfit Sets window.</summary>
    public string? Outfit { get; set; }

    /// <summary>ClassUseMode name (Base, Atk, Solo...). Null = keep the CoreBots mode.</summary>
    public string? Mode { get; set; }

    /// <summary>Class in the outfit's "ar" slot, remembered from the last Refresh.</summary>
    public string? ClassName { get; set; }
}

/// <summary>Everything that belongs to one AQW account: its switch, role outfits and Ultra Change outfit.</summary>
public class AccountProfile
{
    public bool Enabled { get; set; }

    public Dictionary<string, RoleSetting> Roles { get; set; } =
        OverrideSettings.RoleNames.ToDictionary(r => r, _ => new RoleSetting());

    /// <summary>"Ultra Change" role: Wardrobe outfit equipped when a script sets up its own class / gear / enhancements.</summary>
    public string? BossChangeOutfit { get; set; }

    /// <summary>Whether the Ultra Change role is armed (only acts while the override is on).</summary>
    public bool BossChangeArmed { get; set; } = true;
}

/// <summary>
/// Settings, split in two:
///   options\OutfitOverride.json              - shared by all accounts (timings, logging, enhance protection)
///   options\OutfitOverride\&lt;account&gt;.json    - one per AQW account (switch, outfits, modes, Ultra Change)
/// An account that has never opened the plugin starts empty, so it never sees another account's outfits.
/// </summary>
public class OverrideSettings
{
    public static readonly string[] RoleNames = { "Solo", "Farm", "Dodge", "Boss" };

    // ---------------------------------------------------------------- shared settings

    /// <summary>How long to wait after equipLoadout before the script continues.</summary>
    public int SetDelayMs { get; set; } = 2000;

    /// <summary>Don't resend the same outfit within this many seconds.</summary>
    public int CooldownSeconds { get; set; } = 10;

    public bool Verbose { get; set; } = true;

    /// <summary>Stop CoreBots auto-enhancing after a role class is equipped while the override is active.</summary>
    public bool SkipAutoEnhance { get; set; } = true;

    // ---------------------------------------------------------------- the logged-in account

    /// <summary>Account the profile below belongs to; null while nobody is logged in.</summary>
    [JsonIgnore]
    public string? Account { get; private set; }

    [JsonIgnore]
    public AccountProfile Profile { get; private set; } = new();

    /// <summary>True when this account had no saved profile yet (first time it opens the plugin).</summary>
    [JsonIgnore]
    public bool IsNew { get; private set; }

    [JsonIgnore] public bool Enabled { get => Profile.Enabled; set => Profile.Enabled = value; }
    [JsonIgnore] public string? BossChangeOutfit { get => Profile.BossChangeOutfit; set => Profile.BossChangeOutfit = value; }
    [JsonIgnore] public bool BossChangeArmed { get => Profile.BossChangeArmed; set => Profile.BossChangeArmed = value; }

    public RoleSetting Role(string role)
    {
        if (!Profile.Roles.TryGetValue(role, out RoleSetting? r))
            Profile.Roles[role] = r = new RoleSetting();
        return r;
    }

    // ---------------------------------------------------------------- files

    [JsonIgnore]
    public static string FilePath => Path.Combine(ClientFileSources.SkuaPluginsDIR, "options", "OutfitOverride.json");

    [JsonIgnore]
    public static string AccountsDir => Path.Combine(ClientFileSources.SkuaPluginsDIR, "options", "OutfitOverride");

    private static string AccountFile(string account)
    {
        string safe = string.Concat(account.Trim().ToLowerInvariant().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(AccountsDir, safe + ".json");
    }

    public static OverrideSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<OverrideSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    /// <summary>Switches to an account's profile (empty if it has none yet). Null = logged out.</summary>
    public void UseAccount(string? account)
    {
        Account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        Profile = new AccountProfile();
        IsNew = false;
        if (Account is null)
            return;
        try
        {
            string file = AccountFile(Account);
            if (File.Exists(file))
                Profile = JsonConvert.DeserializeObject<AccountProfile>(File.ReadAllText(file)) ?? new();
            else
                IsNew = true;
        }
        catch
        {
            IsNew = true;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (Account is not null)
            {
                Directory.CreateDirectory(AccountsDir);
                File.WriteAllText(AccountFile(Account), JsonConvert.SerializeObject(Profile, Formatting.Indented));
                IsNew = false;
            }
        }
        catch { }
    }
}
