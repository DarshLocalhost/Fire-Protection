using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Central settings dialog: active design standard + units, an editable per-hazard
/// spacing/coverage table (your own design criteria, persisted as overrides), and
/// placement offsets. Backed by <see cref="SynthFireSettings"/> via <see cref="SettingsStore"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>An editable Design Standards row (values in the standard's units).</summary>
    public sealed class DesignRow
    {
        public string StandardId { get; set; } = "";
        public string HazardId { get; set; } = "";
        public string Hazard { get; set; } = "";
        public string MaxSpacing { get; set; } = "";
        public string MinSpacing { get; set; } = "";
        public string MaxToWall { get; set; } = "";
        public string Coverage { get; set; } = "";
    }

    private readonly StandardRegistry _registry;
    private readonly SynthFireSettings _settings;
    private readonly ProfileBook _profiles;
    private string _currentDesignStandardId = "";

    private SettingsWindow(StandardRegistry registry, SynthFireSettings settings)
    {
        InitializeComponent();
        _registry = registry;
        _settings = settings;
        _profiles = SettingsProfileStore.Load();

        foreach (var standard in registry.All)
        {
            var rb = new RadioButton
            {
                Content = standard.DisplayName,
                Tag = standard.Id,
                GroupName = "Standard",
                Margin = new Thickness(0, 4, 0, 4),
                IsChecked = string.Equals(standard.Id, settings.StandardId, StringComparison.OrdinalIgnoreCase)
            };
            rb.Checked += (_, _) => { CaptureEdits(); RebuildDesignTable(); };
            StandardOptions.Children.Add(rb);
        }

        UnitMetric.IsChecked = settings.Units == UnitSystem.Metric;
        UnitImperial.IsChecked = settings.Units == UnitSystem.Imperial;
        CeilingOffsetBox.Text = settings.CeilingOffsetInches.ToString(CultureInfo.InvariantCulture);
        MountingHeightBox.Text = settings.NotificationMountingHeightInches.ToString(CultureInfo.InvariantCulture);
        NotificationCandelaBox.Text = settings.NotificationCandela.ToString(CultureInfo.InvariantCulture);
        LoadTileControls();

        RebuildDesignTable();
        RefreshProfileList();
    }

    /// <summary>Creates the settings window (shown modelessly by the caller so Revit stays interactive).</summary>
    public static SettingsWindow Create(StandardRegistry registry, SynthFireSettings settings, IntPtr ownerHandle = default)
    {
        var window = new SettingsWindow(registry, settings);
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        return window;
    }

    private string SelectedStandardId()
    {
        foreach (var child in StandardOptions.Children)
            if (child is RadioButton { IsChecked: true, Tag: string id })
                return id;
        return _settings.StandardId;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void RebuildDesignTable()
    {
        if (DesignGrid is null) return;
        var standard = _registry.Get(SelectedStandardId());
        _currentDesignStandardId = standard.Id;

        DesignTitle.Text = standard.DisplayName +
            (standard.Units == UnitSystem.Metric ? "  (metres, m²)" : "  (feet, ft²)");
        DesignProvisional.Text = standard.HasApprovedRules
            ? string.Empty
            : "Provisional values — encoded from published tables, not FPE-verified. Review before issue.";

        var rows = new List<DesignRow>();
        foreach (var hz in standard.SupportedHazardClasses)
        {
            var r = EffectiveRules.For(_registry, _settings, standard.Id, hz.Id);
            rows.Add(new DesignRow
            {
                StandardId = standard.Id,
                HazardId = hz.Id,
                Hazard = hz.DisplayName,
                MaxSpacing = Num(r.MaxSpacing),
                MinSpacing = Num(r.MinSpacing),
                MaxToWall = Num(r.MaxDistanceToWall),
                Coverage = Num(r.MaxCoverageAreaPerSprinkler)
            });
        }
        DesignGrid.ItemsSource = rows;
    }

    /// <summary>Reads the grid, storing any value that differs from the published one as an override.</summary>
    private void CaptureEdits()
    {
        if (DesignGrid?.ItemsSource is not IEnumerable<DesignRow> rows) return;

        foreach (var row in rows)
        {
            var baseRules = _registry.Get(row.StandardId).GetSprinklerRules(row.HazardId);
            var o = new HazardRuleOverride
            {
                MaxSpacing = Diff(row.MaxSpacing, baseRules.MaxSpacing),
                MinSpacing = Diff(row.MinSpacing, baseRules.MinSpacing),
                MaxDistanceToWall = Diff(row.MaxToWall, baseRules.MaxDistanceToWall),
                MaxCoverageAreaPerSprinkler = Diff(row.Coverage, baseRules.MaxCoverageAreaPerSprinkler)
            };
            string key = HazardRuleOverride.Key(row.StandardId, row.HazardId);
            if (o.IsEmpty) _settings.Overrides.Remove(key);
            else _settings.Overrides[key] = o;
        }
    }

    // Returns the parsed value only if it meaningfully differs from the published one.
    private static double? Diff(string text, double published)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
            && Math.Abs(v - published) > 1e-6)
            return v;
        return null;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelStandards is null) return;
        PanelStandards.Visibility = NavStandards.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelDesign.Visibility = NavDesign.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelProfiles.Visibility = NavProfiles.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new SynthFireSettings();
        _settings.StandardId = defaults.StandardId;
        _settings.Units = defaults.Units;
        _settings.CeilingOffsetInches = defaults.CeilingOffsetInches;
        _settings.NotificationMountingHeightInches = defaults.NotificationMountingHeightInches;
        _settings.NotificationCandela = defaults.NotificationCandela;
        _settings.SnapToCeilingTile = defaults.SnapToCeilingTile;
        _settings.CeilingTileModuleXInches = defaults.CeilingTileModuleXInches;
        _settings.CeilingTileModuleYInches = defaults.CeilingTileModuleYInches;
        _settings.CeilingTileOriginXInches = defaults.CeilingTileOriginXInches;
        _settings.CeilingTileOriginYInches = defaults.CeilingTileOriginYInches;
        _settings.Overrides.Clear();
        _settings.FamilyMap.Clear();

        LoadSettingsIntoUi();
        SettingsStore.Save(_settings);
    }

    // Reflects the current tile settings into the General-panel tile controls.
    private void LoadTileControls()
    {
        SnapToTileBox.IsChecked = _settings.SnapToCeilingTile;
        TileModuleXBox.Text = _settings.CeilingTileModuleXInches.ToString(CultureInfo.InvariantCulture);
        TileModuleYBox.Text = _settings.CeilingTileModuleYInches.ToString(CultureInfo.InvariantCulture);
        TileOriginXBox.Text = _settings.CeilingTileOriginXInches.ToString(CultureInfo.InvariantCulture);
        TileOriginYBox.Text = _settings.CeilingTileOriginYInches.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reflects the current <see cref="_settings"/> back into every control.</summary>
    private void LoadSettingsIntoUi()
    {
        foreach (var child in StandardOptions.Children)
            if (child is RadioButton rb && rb.Tag is string id)
                rb.IsChecked = string.Equals(id, _settings.StandardId, StringComparison.OrdinalIgnoreCase);

        UnitMetric.IsChecked = _settings.Units == UnitSystem.Metric;
        UnitImperial.IsChecked = _settings.Units == UnitSystem.Imperial;
        CeilingOffsetBox.Text = _settings.CeilingOffsetInches.ToString(CultureInfo.InvariantCulture);
        MountingHeightBox.Text = _settings.NotificationMountingHeightInches.ToString(CultureInfo.InvariantCulture);
        NotificationCandelaBox.Text = _settings.NotificationCandela.ToString(CultureInfo.InvariantCulture);
        LoadTileControls();

        RebuildDesignTable();
    }

    /// <summary>Commits pending grid/overrides edits and the standard/units/offset controls into <see cref="_settings"/>.</summary>
    private void CommitUiToSettings()
    {
        DesignGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CaptureEdits();

        _settings.StandardId = SelectedStandardId();
        _settings.Units = UnitMetric.IsChecked == true ? UnitSystem.Metric : UnitSystem.Imperial;
        if (double.TryParse(CeilingOffsetBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double co))
            _settings.CeilingOffsetInches = co;
        if (double.TryParse(MountingHeightBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double mh))
            _settings.NotificationMountingHeightInches = mh;
        if (double.TryParse(NotificationCandelaBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double cd))
            _settings.NotificationCandela = cd;

        _settings.SnapToCeilingTile = SnapToTileBox.IsChecked == true;
        if (double.TryParse(TileModuleXBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double tmx))
            _settings.CeilingTileModuleXInches = tmx;
        if (double.TryParse(TileModuleYBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double tmy))
            _settings.CeilingTileModuleYInches = tmy;
        if (double.TryParse(TileOriginXBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double tox))
            _settings.CeilingTileOriginXInches = tox;
        if (double.TryParse(TileOriginYBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double toy))
            _settings.CeilingTileOriginYInches = toy;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        CommitUiToSettings();
        SettingsStore.Save(_settings);
        Close();
    }

    // ---- Profiles ----

    private void RefreshProfileList()
    {
        string? selected = ProfileList.SelectedItem as string;
        ProfileList.Items.Clear();
        foreach (var p in _profiles.Profiles)
            ProfileList.Items.Add(p.Name);

        if (selected is not null && ProfileList.Items.Contains(selected))
            ProfileList.SelectedItem = selected;

        ProfileStatus.Text = _profiles.ActiveProfileName is { } active
            ? $"Active profile: {active}"
            : "No profile applied.";
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool has = ProfileList.SelectedItem is string;
        ProfileLoadButton.IsEnabled = has;
        ProfileDeleteButton.IsEnabled = has;
        if (ProfileList.SelectedItem is string name)
            ProfileNameBox.Text = name;
    }

    private void ProfileSave_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text?.Trim() ?? "";
        if (name.Length == 0) { ProfileStatus.Text = "Enter a name to save the current settings as a profile."; return; }

        CommitUiToSettings();
        _profiles.Save(name, _settings);
        SettingsProfileStore.Save(_profiles);
        RefreshProfileList();
        ProfileList.SelectedItem = name;
        ProfileStatus.Text = $"Saved profile: {name}";
    }

    private void ProfileLoad_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name) return;
        var applied = _profiles.Apply(name);
        if (applied is null) return;

        _settings.StandardId = applied.StandardId;
        _settings.Units = applied.Units;
        _settings.CeilingOffsetInches = applied.CeilingOffsetInches;
        _settings.NotificationMountingHeightInches = applied.NotificationMountingHeightInches;
        _settings.NotificationCandela = applied.NotificationCandela;
        _settings.SnapToCeilingTile = applied.SnapToCeilingTile;
        _settings.CeilingTileModuleXInches = applied.CeilingTileModuleXInches;
        _settings.CeilingTileModuleYInches = applied.CeilingTileModuleYInches;
        _settings.CeilingTileOriginXInches = applied.CeilingTileOriginXInches;
        _settings.CeilingTileOriginYInches = applied.CeilingTileOriginYInches;
        _settings.Overrides.Clear();
        foreach (var kv in applied.Overrides) _settings.Overrides[kv.Key] = kv.Value;
        _settings.FamilyMap.Clear();
        foreach (var kv in applied.FamilyMap) _settings.FamilyMap[kv.Key] = kv.Value;

        LoadSettingsIntoUi();
        SettingsStore.Save(_settings);
        SettingsProfileStore.Save(_profiles);
        ProfileStatus.Text = $"Loaded profile: {name}";
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name) return;
        _profiles.Remove(name);
        SettingsProfileStore.Save(_profiles);
        RefreshProfileList();
        ProfileStatus.Text = $"Deleted profile: {name}";
    }
}
