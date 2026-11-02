using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Skua.WPF;

namespace OutfitOverride;

/// <summary>
/// Plugin window in the style of Butler V5: one row per CoreBots role with an Outfit button and a Mode button.
/// A coloured button = that role is overridden; a grey button = CoreBots decides.
/// </summary>
internal sealed class OverrideWindow : CustomWindow
{
    // Outfit = blue (what you wear), Mode = green (how the class fights). Grey = CoreBots default.
    private static readonly Brush OutfitBrush = Frozen(Color.FromRgb(0x7E, 0x9B, 0xAE));
    private static readonly Brush ModeBrush = Frozen(Color.FromRgb(0x2E, 0xBD, 0x6B));
    private static readonly Brush DarkText = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Brush PanelBrush = Frozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LineBrush = Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly Brush OffBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    private readonly OutfitOverridePlugin _plugin;
    private OverrideSettings S => _plugin.Settings;

    private readonly TextBlock _stateLine = new() { Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _accountLine = new() { FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 1, 0, 0) };
    private ToggleButton _toggle = null!;
    private FrameworkElement _list = null!;
    private bool _syncing;
    private readonly TextBlock _status = new() { Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<string, Row> _rows = new();
    private readonly FrameworkElement _settingsPanel;
    private List<string> _outfitNames = new();
    private Dictionary<string, string>? _cbo;

    private const string BossRole = "Ultra Change";

    private sealed class Row
    {
        public required TextBlock Detail { get; init; }
        public required Button Outfit { get; init; }
        public required Button Mode { get; init; }
    }

    public OverrideWindow(OutfitOverridePlugin plugin)
    {
        _plugin = plugin;
        if (TryFindResource("CustomWindow") is Style style)
            Style = style;
        Title = TitleText = "Outfit Override";
        HideWindow = true;
        Width = 560;
        Height = 540;
        MinWidth = 480;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "MaterialDesignPaper");
        SetResourceReference(ForegroundProperty, "MaterialDesignBody");

        _settingsPanel = BuildSettings();
        Content = BuildContent();

        _plugin.StateChanged += () => Dispatcher.BeginInvoke(() => SetEnabled(S.Enabled, save: false));
        _plugin.AccountChanged += () => Dispatcher.BeginInvoke(OnAccountChanged);
        OnAccountChanged();
    }

    public void ForceClose()
    {
        HideWindow = false;
        Close();
    }

    // ---------------------------------------------------------------- layout

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(14, 10, 14, 12) };

        // Header: title + state on the left, switch on the right.
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 12) };
        var toggle = _toggle = new ToggleButton { IsChecked = S.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Turn the override on or off for this account" };
        if (TryFindResource("MaterialDesignSwitchToggleButton") is Style sw)
            toggle.Style = sw;
        toggle.Checked += (_, _) => { if (!_syncing) SetEnabled(true); };
        toggle.Unchecked += (_, _) => { if (!_syncing) SetEnabled(false); };
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "CoreBots loadout override", FontSize = 17, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(_stateLine);
        titles.Children.Add(_accountLine);
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Footer: status on the left, buttons on the right.
        var footer = new DockPanel { Margin = new Thickness(2, 10, 0, 0) };
        var settingsBtn = FooterButton("Settings");
        settingsBtn.Click += (_, _) => _settingsPanel.Visibility = _settingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var refreshBtn = FooterButton("Refresh outfits");
        refreshBtn.ToolTip = "Fetch your current Wardrobe outfits from the game";
        refreshBtn.Click += (_, _) => Refresh();
        DockPanel.SetDock(settingsBtn, Dock.Right);
        DockPanel.SetDock(refreshBtn, Dock.Right);
        footer.Children.Add(settingsBtn);
        footer.Children.Add(refreshBtn);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        DockPanel.SetDock(_settingsPanel, Dock.Bottom);
        root.Children.Add(_settingsPanel);

        // The role list, in a rounded panel like Butler's account list.
        var list = new StackPanel();
        string[] roles = OverrideSettings.RoleNames;
        foreach (string role in roles)
            list.Children.Add(BuildRow(role, last: false));
        list.Children.Add(BuildRow(BossRole, last: true));
        root.Children.Add(_list = new Border
        {
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 4, 14, 4),
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        });

        SetEnabled(S.Enabled, save: false);
        return root;
    }

    private UIElement BuildRow(string role, bool last)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };

        var mode = PillButton(110);
        mode.Margin = new Thickness(8, 0, 0, 0);
        mode.Click += (_, _) =>
        {
            if (role == BossRole)
                ToggleBossArmed();
            else
                OpenModeMenu(role, mode);
        };
        DockPanel.SetDock(mode, Dock.Right);
        row.Children.Add(mode);

        var outfit = PillButton(130);
        outfit.Click += (_, _) => OpenOutfitMenu(role, outfit);
        DockPanel.SetDock(outfit, Dock.Right);
        row.Children.Add(outfit);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = role, FontSize = 15, FontWeight = FontWeights.SemiBold });
        var detail = new TextBlock { FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(detail);
        row.Children.Add(text);

        _rows[role] = new Row { Detail = detail, Outfit = outfit, Mode = mode };

        if (last)
            return row;
        var wrap = new StackPanel();
        wrap.Children.Add(row);
        wrap.Children.Add(new Border { Height = 1, Background = LineBrush });
        return wrap;
    }

    private FrameworkElement BuildSettings()
    {
        var panel = new Border
        {
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var p = new StackPanel();

        var delayRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var delay = new TextBox { Text = S.SetDelayMs.ToString(), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        delay.LostFocus += (_, _) => { if (int.TryParse(delay.Text, out int v)) { S.SetDelayMs = v; _plugin.SaveSettings(); } };
        DockPanel.SetDock(delay, Dock.Right);
        delayRow.Children.Add(delay);
        delayRow.Children.Add(new TextBlock { Text = "Wait after equipping an outfit (ms)", VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(delayRow);

        var cdRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var cd = new TextBox { Text = S.CooldownSeconds.ToString(), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        cd.LostFocus += (_, _) => { if (int.TryParse(cd.Text, out int v)) { S.CooldownSeconds = v; _plugin.SaveSettings(); } };
        DockPanel.SetDock(cd, Dock.Right);
        cdRow.Children.Add(cd);
        cdRow.Children.Add(new TextBlock { Text = "Don't resend the same outfit within (s)", VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(cdRow);

        var enh = new CheckBox { Content = "Don't auto-enhance my role outfits (the Ultra Change outfit can be enhanced)", IsChecked = S.SkipAutoEnhance, Margin = new Thickness(0, 4, 0, 4) };
        enh.Click += (_, _) => { S.SkipAutoEnhance = enh.IsChecked == true; _plugin.SaveSettings(); };
        p.Children.Add(enh);

        var verbose = new CheckBox { Content = "Log each outfit swap", IsChecked = S.Verbose };
        verbose.Click += (_, _) => { S.Verbose = verbose.IsChecked == true; _plugin.SaveSettings(); };
        p.Children.Add(verbose);

        var version = typeof(OverrideWindow).Assembly.GetName().Version;
        p.Children.Add(new TextBlock
        {
            Text = $"Outfit Override v{version?.ToString(3)}",
            FontSize = 11,
            Opacity = 0.5,
            Margin = new Thickness(0, 8, 0, 0),
        });

        panel.Child = p;
        return panel;
    }

    private Button PillButton(double width)
    {
        var b = new Button { Width = width, Height = 32, FontWeight = FontWeights.SemiBold, Padding = new Thickness(10, 0, 10, 0) };
        if (TryFindResource("MaterialDesignFlatButton") is Style s)
            b.Style = s;
        b.BorderThickness = new Thickness(0);
        MaterialDesignThemes.Wpf.ButtonAssist.SetCornerRadius(b, new CornerRadius(3));
        return b;
    }

    private Button FooterButton(string text)
    {
        var b = new Button { Content = text, Height = 32, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0), FontWeight = FontWeights.SemiBold };
        if (TryFindResource("MaterialDesignFlatButton") is Style s)
            b.Style = s;
        b.Background = OffBrush;
        b.SetResourceReference(ForegroundProperty, "MaterialDesignBody");
        MaterialDesignThemes.Wpf.ButtonAssist.SetCornerRadius(b, new CornerRadius(3));
        return b;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------------------------------------------------------------- menus

    private void OpenOutfitMenu(string role, Button anchor)
    {
        if (role == BossRole)
        {
            OpenBossOutfitMenu(anchor);
            return;
        }
        RoleSetting rs = S.Role(role);
        var menu = new ContextMenu();

        menu.Items.Add(MenuEntry("Use CoreBots loadout", string.IsNullOrWhiteSpace(rs.Outfit), () => SetOutfit(role, null)));
        menu.Items.Add(new Separator());
        if (_outfitNames.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No outfits - press Refresh outfits", IsEnabled = false });
        foreach (string name in _outfitNames)
        {
            string n = name;
            menu.Items.Add(MenuEntry(n, string.Equals(rs.Outfit, n, StringComparison.OrdinalIgnoreCase), () => SetOutfit(role, n)));
        }

        if (!string.IsNullOrWhiteSpace(rs.Outfit))
        {
            menu.Items.Add(new Separator());
            string current = rs.Outfit!;
            menu.Items.Add(MenuEntry($"Equip \"{current}\" now", false, () =>
                Task.Run(() => _plugin.SendOutfit(current, $"Equip now ({role})", ignoreCooldown: true))));
        }
        Show(menu, anchor);
    }

    private void OpenBossOutfitMenu(Button anchor)
    {
        string? current = S.BossChangeOutfit;
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("No outfit (just pause the override)", string.IsNullOrWhiteSpace(current), () => SetBossOutfit(null)));
        menu.Items.Add(new Separator());
        if (_outfitNames.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No outfits - press Refresh outfits", IsEnabled = false });
        foreach (string name in _outfitNames)
        {
            string n = name;
            menu.Items.Add(MenuEntry(n, string.Equals(current, n, StringComparison.OrdinalIgnoreCase), () => SetBossOutfit(n)));
        }
        if (!string.IsNullOrWhiteSpace(current))
        {
            menu.Items.Add(new Separator());
            string c = current!;
            menu.Items.Add(MenuEntry($"Equip \"{c}\" now", false, () =>
                Task.Run(() => _plugin.SendOutfit(c, "Equip now (Ultra Change)", ignoreCooldown: true))));
        }
        if (_plugin.Paused)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Done - turn the override back on now", false, () => _plugin.Resume("you said so")));
        }
        Show(menu, anchor);
    }

    private void SetBossOutfit(string? outfit)
    {
        S.BossChangeOutfit = outfit;
        _plugin.SaveSettings();
        UpdateRow(BossRole);
    }

    private void ToggleBossArmed()
    {
        S.BossChangeArmed = !S.BossChangeArmed;
        _plugin.SaveSettings();
        UpdateRow(BossRole);
    }

    private void OpenModeMenu(string role, Button anchor)
    {
        RoleSetting rs = S.Role(role);
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("Use CoreBots mode", rs.Mode is null, () => SetMode(role, null)));
        menu.Items.Add(new Separator());
        foreach (string mode in _plugin.ModesFor(rs.ClassName))
        {
            string m = mode;
            menu.Items.Add(MenuEntry(m, rs.Mode == m, () => SetMode(role, m)));
        }
        Show(menu, anchor);
    }

    private static MenuItem MenuEntry(string text, bool isChecked, Action onClick)
    {
        var item = new MenuItem { Header = text, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void Show(ContextMenu menu, Button anchor)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.MinWidth = anchor.ActualWidth;
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- state

    /// <summary>A different account logged in (or logged out): show that account's own outfits.</summary>
    private void OnAccountChanged()
    {
        bool loggedIn = S.Account is not null;
        _syncing = true;
        _toggle.IsChecked = S.Enabled;
        _syncing = false;
        _toggle.IsEnabled = loggedIn;
        _list.IsEnabled = loggedIn;
        _accountLine.Text = loggedIn ? $"Account: {S.Account}" : "Log in to set up this account";
        _outfitNames = new List<string>();
        Refresh();
        SetEnabled(S.Enabled, save: false);
    }

    private void SetEnabled(bool on, bool save = true)
    {
        _stateLine.Text = !on
            ? "OFF - scripts use the normal CoreBots loadout"
            : _plugin.Paused
                ? $"PAUSED for Ultra Change ({_plugin.PausedFor}) - turns back on when the script is done"
                : "ON - scripts you start use these outfits";
        if (save && S.Account is null)
            save = false; // nothing to save to until someone logs in
        if (save)
        {
            if (!on && _plugin.Paused)
                _plugin.ClearPause();
            S.Enabled = on;
            _plugin.SaveSettings();
            _status.Text = on ? "Applies to scripts started from now on." : "Scripts use the CoreBots loadout.";
        }
        foreach (string role in _rows.Keys)
            UpdateRow(role);
    }

    private void SetOutfit(string role, string? outfit)
    {
        RoleSetting rs = S.Role(role);
        rs.Outfit = outfit;
        rs.ClassName = outfit is null ? null : _plugin.OutfitClass(outfit);
        // Keep the mode only if the new class actually has it in Advanced Skills.
        if (rs.Mode is not null && (outfit is null || !_plugin.ModesFor(rs.ClassName).Contains(rs.Mode)))
            rs.Mode = null;
        _plugin.SaveSettings();
        UpdateRow(role);
    }

    private void SetMode(string role, string? mode)
    {
        S.Role(role).Mode = mode;
        _plugin.SaveSettings();
        UpdateRow(role);
    }

    private void Refresh()
    {
        string status = _plugin.RefreshOutfits(out List<string> names);
        if (names.Count > 0)
            _outfitNames = names;
        _cbo = _plugin.ReadCboRaw();
        _status.Text = status;
        foreach (string role in _rows.Keys)
            UpdateRow(role);
    }

    private void UpdateRow(string role)
    {
        if (!_rows.TryGetValue(role, out Row? row))
            return;
        if (role == BossRole)
        {
            UpdateBossRow(row);
            return;
        }
        RoleSetting rs = S.Role(role);
        bool hasOutfit = !string.IsNullOrWhiteSpace(rs.Outfit);
        bool missing = hasOutfit && !Owned(rs.Outfit);

        Paint(row.Outfit, hasOutfit ? rs.Outfit! : "Outfit", hasOutfit && !missing ? OutfitBrush : null);
        Paint(row.Mode, rs.Mode ?? "Mode", hasOutfit && rs.Mode is not null ? ModeBrush : null);
        row.Mode.IsEnabled = hasOutfit;
        row.Outfit.ToolTip = hasOutfit ? $"Outfit: {rs.Outfit} (click to change)" : "Pick a Wardrobe outfit for this role";
        row.Mode.ToolTip = hasOutfit ? "Advanced Skills mode for this outfit's class" : "Pick an outfit first";

        string cboClass = Cbo($"{role}ClassSelect");
        string cboMode = Cbo($"{role}ModeSelect");
        string cbo = string.IsNullOrEmpty(cboClass)
            ? "CoreBots loadout"
            : $"CoreBots: {cboClass}{(string.IsNullOrEmpty(cboMode) ? "" : $" · {cboMode}")}";

        if (!hasOutfit)
            row.Detail.Text = cbo;
        else if (missing)
            row.Detail.Text = "Outfit not in this account's Wardrobe - uses CoreBots";
        else if (!S.Enabled)
            row.Detail.Text = $"{cbo}   (override off)";
        else
            row.Detail.Text = $"{rs.ClassName ?? "class unknown - refresh"} · {rs.Mode ?? (string.IsNullOrEmpty(cboMode) ? "CoreBots mode" : cboMode)}";
        row.Detail.Opacity = hasOutfit && S.Enabled ? 0.85 : 0.6;
    }

    private void UpdateBossRow(Row row)
    {
        string? outfit = S.BossChangeOutfit;
        bool hasOutfit = !string.IsNullOrWhiteSpace(outfit);
        bool missing = hasOutfit && !Owned(outfit);
        bool armed = S.BossChangeArmed;

        Paint(row.Outfit, hasOutfit ? outfit! : "Outfit", hasOutfit && !missing ? OutfitBrush : null);
        Paint(row.Mode, armed ? "Auto" : "Off", armed ? ModeBrush : null);
        row.Mode.IsEnabled = true;
        row.Outfit.ToolTip = "Outfit to equip when a script sets up its own class, gear or enhancements (boss / hard enemy)";
        row.Mode.ToolTip = armed
            ? "Auto: pause the override when a script needs its own setup, turn it back on afterwards. Click to turn off."
            : "Off: scripts with their own class get the outfits above anyway. Click to turn on.";

        if (_plugin.Paused)
            row.Detail.Text = $"Active now: {_plugin.PausedFor} - override paused";
        else if (missing)
            row.Detail.Text = "Outfit not in this account's Wardrobe";
        else if (!armed)
            row.Detail.Text = "Off - scripts' own setups are not detected";
        else
            row.Detail.Text = hasOutfit
                ? "Takes over when a script sets up its own gear"
                : "Pauses when a script sets up its own gear";
        row.Detail.Opacity = _plugin.Paused ? 0.95 : (armed && S.Enabled ? 0.8 : 0.6);
    }

    /// <summary>True if the outfit is in this account's Wardrobe (or the list isn't loaded yet).</summary>
    private bool Owned(string? outfit) =>
        _outfitNames.Count == 0 || _outfitNames.Contains(outfit ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private string Cbo(string key) =>
        _cbo is not null && _cbo.TryGetValue(key, out string? v) && !v.Equals("None", StringComparison.OrdinalIgnoreCase) ? v : string.Empty;

    private static void Paint(Button b, string text, Brush? fill)
    {
        b.Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        if (fill is not null)
        {
            b.Background = fill;
            b.Foreground = DarkText;
        }
        else
        {
            b.Background = OffBrush;
            b.SetResourceReference(ForegroundProperty, "MaterialDesignBody");
        }
    }
}
