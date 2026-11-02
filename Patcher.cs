using System.Reflection;
using HarmonyLib;
using Skua.Core.Scripts;

namespace OutfitOverride;

/// <summary>
/// Two hooks:
///  - File.ReadAllLines: a running script reading CBO_Storage(user).txt gets the outfit loadout (file on disk untouched).
///  - ScriptInventory.EquipItem: when that class is equipped, the Outfit Set is equipped instead.
/// </summary>
internal static class Patcher
{
    private const string HarmonyId = "outfitoverride.skua";
    private static Harmony? _harmony;

    public static void Apply()
    {
        _harmony ??= new Harmony(HarmonyId);

        MethodInfo equip = typeof(ScriptInventory).GetMethod(nameof(ScriptInventory.EquipItem), new[] { typeof(int) })
            ?? throw new MissingMethodException("ScriptInventory.EquipItem(int) not found - Skua version changed?");
        _harmony.Patch(equip, prefix: new HarmonyMethod(typeof(Patcher), nameof(EquipPrefix)));

        MethodInfo readLines = typeof(File).GetMethod(nameof(File.ReadAllLines), new[] { typeof(string) })!;
        _harmony.Patch(readLines, postfix: new HarmonyMethod(typeof(Patcher), nameof(ReadAllLinesPostfix)));
    }

    public static void Remove() => _harmony?.UnpatchAll(HarmonyId);

    private static readonly HashSet<Assembly> _patchedScripts = new();

    // How deep we are inside hooked CoreAdvanced methods on this thread. Only the outermost call counts, so e.g.
    // the EnhanceEquipped that GearStore(true) runs internally is not mistaken for a new boss setup.
    [ThreadStatic] private static int _depth;

    /// <summary>
    /// Each script run compiles its own copy of CoreAdvanced. Hook the calls scripts use to set up their own
    /// class, gear and enhancements (found by scanning all ~1950 scripts):
    ///   GearStore()        - "save my gear, I'm about to change it"   -> start Ultra Change
    ///   GearStore(true)    - "put the saved gear back"                 -> Ultra Change done
    ///   SmartEnhance(cls)  - enhance for a class                       -> start, if cls isn't one of your roles
    ///   EnhanceEquipped()  - enhance whatever is worn right now        -> start
    ///   EnhanceItem(name)  - enhance a specific item                   -> start, if that item is worn
    /// Class equips (Core.Equip, Ultra engines, army class sync) are caught by the ScriptInventory.EquipItem hook.
    /// </summary>
    public static void PatchScript(Assembly scriptAssembly)
    {
        if (_harmony is null || !_patchedScripts.Add(scriptAssembly))
            return;
        Type? adv = scriptAssembly.GetType("CoreAdvanced");
        if (adv is null)
            return;

        var finalizer = new HarmonyMethod(typeof(Patcher), nameof(LeaveFinalizer));
        foreach (MethodInfo m in adv.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            string? prefix = m.Name switch
            {
                "GearStore" => nameof(GearStorePrefix),
                "SmartEnhance" => nameof(SmartEnhancePrefix),
                "EnhanceEquipped" => nameof(EnhanceEquippedPrefix),
                "EnhanceItem" => nameof(EnhanceItemPrefix),
                _ => null,
            };
            if (prefix is null)
                continue;
            try
            {
                _harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(Patcher), prefix),
                    postfix: m.Name == "GearStore" ? new HarmonyMethod(typeof(Patcher), nameof(GearStorePostfix)) : null,
                    finalizer: finalizer);
            }
            catch { /* a changed CoreAdvanced: the class-equip trigger still works */ }
        }
    }

    private static void LeaveFinalizer() => _depth = Math.Max(0, _depth - 1);

    private static void GearStorePrefix(object[] __args)
    {
        bool outer = _depth++ == 0;
        try
        {
            if (outer && __args.Length > 0 && __args[0] is true)
                OutfitOverridePlugin.Instance?.OnGearStoreRestore();
        }
        catch { }
    }

    private static void GearStorePostfix(object[] __args)
    {
        try
        {
            // _depth is still counting this call here (the finalizer runs after the postfix).
            if (_depth == 1 && (__args.Length == 0 || __args[0] is not true))
                OutfitOverridePlugin.Instance?.OnGearStoreSave();
        }
        catch { }
    }

    private static void SmartEnhancePrefix(object[] __args)
    {
        bool outer = _depth++ == 0;
        try
        {
            if (outer)
                OutfitOverridePlugin.Instance?.BeforeSmartEnhance(__args.Length > 0 ? __args[0] as string : null);
        }
        catch { }
    }

    private static void EnhanceEquippedPrefix()
    {
        bool outer = _depth++ == 0;
        try
        {
            if (outer)
                OutfitOverridePlugin.Instance?.BeforeEnhanceEquipped();
        }
        catch { }
    }

    private static void EnhanceItemPrefix(object[] __args)
    {
        bool outer = _depth++ == 0;
        try
        {
            if (!outer || __args.Length == 0)
                return;
            string[] names = __args[0] switch
            {
                string one => new[] { one },
                string[] many => many,
                _ => Array.Empty<string>(),
            };
            OutfitOverridePlugin.Instance?.BeforeEnhanceItems(names);
        }
        catch { }
    }

    private static bool EquipPrefix(int id) =>
        OutfitOverridePlugin.Instance?.BeforeEquip(id) ?? true;

    private static void ReadAllLinesPostfix(string path, ref string[] __result)
    {
        try
        {
            if (OutfitOverridePlugin.Instance is { } plugin && __result is not null)
                __result = plugin.OverrideCboLines(path, __result);
        }
        catch { /* never break file reads */ }
    }
}
