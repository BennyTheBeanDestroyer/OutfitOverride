# Outfit Override (Skua plugin)

A switchable override for the CoreBots **Loadout** that uses your AQW **Wardrobe outfits**.

- **ON:** scripts you start use the outfit and Advanced Skills mode you picked for each role:
  **Solo, Farm, Dodge, Boss**.
- **OFF:** scripts use your normal CoreBots loadout, untouched.
- **Ultra Change:** when a script sets up its own class, gear or enhancements (a boss or hard
  enemy), the plugin equips the outfit you picked for this row. It then steps aside until the
  script is done with it, so your other outfits are never changed.

## Install

Download `OutfitOverride.dll` from the [Releases](../../releases) page, copy it into
`%APPDATA%\Skua\plugins` and restart Skua. It's a single file
because Harmony is built into it. Open it from **Plugins → Outfit Override**.

## Use

1. Log in, open the plugin and press **Refresh outfits**.
2. For each role, click the blue **Outfit** button to pick a Wardrobe outfit. Then click the
   green **Mode** button to pick a mode; the list comes from Advanced Skills for that outfit's
   class.
3. Under **Ultra Change**, pick the outfit the plugin may change freely (your *Ultra Change* outfit).
   **Auto / Off** turns this part on or off.
4. Turn the switch **ON** before starting a script. CoreBots reads the loadout when a script
   starts.

A button with a colour means the plugin overrides that role. A grey button means CoreBots
decides. If a role has no outfit, it keeps the CoreBots loadout.

## How it works

| What | How |
|---|---|
| Outfit list | `world.objInfo.customs.loadouts`: outfit name → item IDs per slot |
| Outfit's class | The item ID in the outfit's `"ar"` slot |
| Mode list | The Advanced Skills saved for that class |
| Equip an outfit | `%xt%zm%equipLoadout%{room}%cmd%{OutfitName}%0%` |

**Loadout:** when a running script reads `CBO_Storage(<you>).txt`, the plugin changes what
the script sees for each role:
- the class becomes the outfit's class;
- the mode becomes the one you picked;
- "Specify Equipment" is turned off, because the outfit brings its own gear;
- auto-enhance is turned off, so your role outfits keep their enhancements.

The file on disk is never changed. When the script equips a role class, the plugin equips the
outfit instead.

**Ultra Change starts** (all call sites found by scanning all ~1950 CoreBots scripts):

| The script calls | Meaning |
|---|---|
| `Adv.GearStore()` | Saves your gear before changing it |
| `Adv.SmartEnhance(class)` with a class that isn't a role class | Enhancing for its own class |
| `Adv.EnhanceEquipped(...)` | Enhancing whatever is worn |
| `Adv.EnhanceItem(...)` on a worn item | Enhancing worn gear |
| Equipping a class that isn't a role class | `Core.Equip`, Ultra engines, army class sync |

The Ultra Change outfit goes on first. Then the script's own class, gear and enhancements go
on top of it.

**Ultra Change ends** with whichever of these happens first:
- `Adv.GearStore(true)`: the script puts your gear back;
- the script equips a role class again;
- the script stops;
- you leave the boss map (not for GearStore pauses);
- you pick "Done - turn the override back on now" from the Outfit menu.

After that the override is back on, and your role outfit goes back on.

## Settings

Each AQW account has its own setup: its own switch, role outfits, modes and Ultra Change outfit.
The plugin switches to the right one automatically when an account logs in. An account that
has never opened the plugin starts empty, so it never shows another account's outfits.

- **Per account:** `%APPDATA%\Skua\plugins\options\OutfitOverride\<account>.json`
- **Shared by all accounts:** `%APPDATA%\Skua\plugins\options\OutfitOverride.json`. This holds
  what the **Settings** button shows: wait after equipping an outfit (default 2000 ms), resend
  cooldown, auto-enhance protection for role outfits, and logging.

A picked outfit that isn't in the logged-in account's Wardrobe is shown greyed out with a note,
and is never equipped. That role uses the CoreBots loadout instead.

## Build from source

Requirements:
- Windows
- the [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Skua 1.4.3 or newer installed. The plugin compiles against Skua's own DLLs, which are not
  included in this repository.

Build it either way:
- **Double-click `build.bat`** if Skua is in `C:\Program Files\Skua`.
- **Or run** `dotnet build -c Release -p:SkuaDir="D:\Path\To\Skua"` if it's somewhere else.

The build puts the DLL in `.\release` and installs it into Skua's plugins folder; close Skua
first so the file isn't locked. Harmony comes from NuGet and is built into the DLL.

| File | What it does |
|---|---|
| `OutfitOverridePlugin.cs` | Load / unload, accounts, shared state |
| `Wardrobe.cs` | Reading and equipping Wardrobe outfits |
| `LoadoutOverride.cs` | The CoreBots loadout and role classes |
| `UltraChange.cs` | Ultra Change pause and resume |
| `ScriptBridge.cs` | Talking to the running script's CoreBots |
| `Patcher.cs` | Harmony hooks |
| `OverrideWindow.cs` | The window |
| `Settings.cs` | Shared and per-account settings |
| `EmbeddedAssemblies.cs` | Loads the built-in Harmony |

## License

[MIT](LICENSE). The release DLL includes [Harmony](https://github.com/pardeike/Harmony) (MIT);
see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This is a fan-made plugin for [Skua](https://github.com/auqw/Skua). It is not affiliated with
Artix Entertainment or the Skua developers.
