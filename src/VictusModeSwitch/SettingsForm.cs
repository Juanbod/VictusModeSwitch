using System.Diagnostics;
using Microsoft.Win32;

namespace VictusModeSwitch;

internal enum SettingsPage
{
    General,
    Power,
    About
}

internal sealed class SettingsForm : Form
{
    private readonly AppSettingsStore _store;
    private readonly ModeController _controller;
    private readonly GlobalHotkeyController? _globalHotkey;
    private readonly StartupController _startupController = new();
    private readonly UpdateService _updateService = new();
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<Control, string> _localizedControls = new();
    private readonly Panel _navigation = new();
    private readonly Panel _contentHost = new();
    private readonly Panel _generalPage = new();
    private readonly Panel _powerPage = new();
    private readonly Panel _aboutPage = new();
    private readonly NavigationButton _generalNavigation = new("\uE80F");
    private readonly NavigationButton _powerNavigation = new("\uE945");
    private readonly NavigationButton _aboutNavigation = new("\uE946");
    private readonly Label _versionLabel = new();
    private readonly Button _ecoButton = new();
    private readonly Button _standardButton = new();
    private readonly Button _performanceButton = new();
    private readonly Label _modeStatus = new();
    private readonly FluentToggle _maxFanToggle = new();
    private readonly FluentToggle _notificationsToggle = new();
    private readonly FluentToggle _startupToggle = new();
    private readonly FluentToggle _hpServicesToggle = new();
    private readonly ComboBox _languageCombo = new();
    private readonly Button _hotkeyButton = new();
    private readonly Button _clearHotkeyButton = new();
    private readonly FluentToggle _ecoRefreshRateToggle = new();
    private readonly FluentToggle _ecoFrameRateToggle = new();
    private readonly FluentToggle _ecoTurboBoostToggle = new();
    private readonly FluentToggle _powerTuningToggle = new();
    private readonly NumericUpDown _ecoAcMaximum = CreatePercentInput();
    private readonly NumericUpDown _ecoBatteryMaximum = CreatePercentInput();
    private readonly Label _powerStatus = new();
    private readonly FluentToggle _updateToggle = new();
    private readonly Button _checkUpdateButton = new();
    private readonly Button _githubButton = new();
    private readonly Label _updateStatus = new();
    private readonly System.Windows.Forms.Timer _powerApplyTimer = new() { Interval = 550 };
    private Localizer _localizer;
    private bool _loading;
    private bool _hardwareBusy;
    private bool _capturingHotkey;

    public SettingsForm(
        AppSettingsStore store,
        ModeController controller,
        SettingsPage initialPage = SettingsPage.General,
        GlobalHotkeyController? globalHotkey = null)
    {
        _store = store;
        _controller = controller;
        _globalHotkey = globalHotkey;
        _store.Settings.Normalize();
        _localizer = new Localizer(store.Settings.Language);

        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(900, 828);
        MinimumSize = new Size(780, 580);
        Font = WindowsTheme.Font(9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Create(store.Settings.CurrentMode.AccentColor(), store.Settings.MaxFanEnabled);

        BuildLayout();
        BindEvents();
        LoadSettings();
        ApplyText();
        ApplyTheme();
        SelectPage(initialPage);

        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        FormClosed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            _powerApplyTimer.Stop();
            _powerApplyTimer.Dispose();
            _globalHotkey?.SuppressInvocations = false;
            _toolTip.Dispose();
            Icon?.Dispose();
        };
        Shown += (_, _) => WindowsTheme.ApplyWindow(this);
    }

    public event Action? PreferencesChanged;
    public event Action? HardwareStateChanged;
    public event Action? InstallerStarted;

    public void ShowAboutPage() => SelectPage(SettingsPage.About);

    public void RefreshHardwareState()
    {
        _loading = true;
        _maxFanToggle.Checked = _controller.MaxFanEnabled;
        _loading = false;
        _modeStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        _modeStatus.Text = $"{_localizer["CurrentMode"]}: {_localizer.ModeDescription(_controller.CurrentMode)}";
        UpdateModeUi();
        ReplaceWindowIcon();
        Invalidate(true);
    }

    private void BuildLayout()
    {
        _navigation.Dock = DockStyle.Left;
        _navigation.Width = 218;
        _navigation.Padding = new Padding(12, 18, 12, 12);

        var brandIcon = new PictureBox
        {
            Image = AppIcon.CreateBitmap(_store.Settings.CurrentMode.AccentColor(), _store.Settings.MaxFanEnabled, 30),
            Location = new Point(16, 18),
            Size = new Size(30, 30),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        var brand = new Label
        {
            AutoSize = true,
            Font = WindowsTheme.Font(10f, FontStyle.Bold),
            Location = new Point(56, 17),
            Text = "Victus Mode Switch"
        };
        _versionLabel.AutoSize = false;
        _versionLabel.Font = WindowsTheme.Font(8.5f);
        _versionLabel.Location = new Point(57, 40);
        _versionLabel.Size = new Size(140, 20);

        _generalNavigation.Location = new Point(12, 82);
        _generalNavigation.Width = 194;
        _powerNavigation.Location = new Point(12, 130);
        _powerNavigation.Width = 194;
        _aboutNavigation.Location = new Point(12, 178);
        _aboutNavigation.Width = 194;
        Register(_generalNavigation, "General");
        Register(_powerNavigation, "Power");
        Register(_aboutNavigation, "About");

        _navigation.Controls.AddRange(new Control[]
        {
            brandIcon,
            brand,
            _versionLabel,
            _generalNavigation,
            _powerNavigation,
            _aboutNavigation
        });

        var divider = new Panel { Dock = DockStyle.Left, Width = 1 };
        _contentHost.Dock = DockStyle.Fill;
        _contentHost.Padding = new Padding(32, 22, 28, 24);
        BuildGeneralPage();
        BuildPowerPage();
        BuildAboutPage();
        _contentHost.Controls.AddRange(new Control[] { _generalPage, _powerPage, _aboutPage });

        Controls.Add(_contentHost);
        Controls.Add(divider);
        Controls.Add(_navigation);
        divider.BringToFront();
    }

    private void BuildGeneralPage()
    {
        ConfigurePage(_generalPage);
        var layout = NewPageLayout(761);
        AddHeader(layout, 0, "GeneralTitle", "GeneralSubtitle");

        var quickTitle = NewLabel(11f, FontStyle.Bold);
        quickTitle.Dock = DockStyle.Fill;
        Register(quickTitle, "QuickControls");
        layout.Controls.Add(quickTitle, 0, 1);

        var modeSelector = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(1),
            Margin = new Padding(0, 3, 0, 3)
        };
        modeSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        modeSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        modeSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        ConfigureModeButton(_ecoButton, AppMode.Eco);
        ConfigureModeButton(_standardButton, AppMode.Standard);
        ConfigureModeButton(_performanceButton, AppMode.Performance);
        modeSelector.Controls.Add(_ecoButton, 0, 0);
        modeSelector.Controls.Add(_standardButton, 1, 0);
        modeSelector.Controls.Add(_performanceButton, 2, 0);
        modeSelector.Tag = "mode-selector";
        layout.Controls.Add(modeSelector, 0, 2);

        _modeStatus.Dock = DockStyle.Fill;
        _modeStatus.Font = WindowsTheme.Font(8.5f);
        _modeStatus.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_modeStatus, 0, 3);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill, Height = 1, Tag = "separator" }, 0, 4);
        layout.Controls.Add(CreateSettingRow("MaxFan", "FanDescription", _maxFanToggle), 0, 5);
        layout.Controls.Add(CreateSettingRow("Notifications", "NotificationsDescription", _notificationsToggle), 0, 6);
        layout.Controls.Add(CreateSettingRow("StartWithWindows", "StartWithWindowsDescription", _startupToggle), 0, 7);
        layout.Controls.Add(CreateSettingRow("HpServices", "HpServicesDescription", _hpServicesToggle), 0, 8);

        _languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageCombo.FlatStyle = FlatStyle.Flat;
        _languageCombo.Width = 192;
        layout.Controls.Add(CreateSettingRow("Language", "LanguageDescription", _languageCombo), 0, 9);
        layout.Controls.Add(CreateSettingRow(
            "KeyboardShortcut",
            "KeyboardShortcutDescription",
            CreateHotkeyControl()), 0, 10);

        var buttonTitle = NewLabel(11f, FontStyle.Bold);
        buttonTitle.Dock = DockStyle.Fill;
        Register(buttonTitle, "DiamondButton");
        layout.Controls.Add(buttonTitle, 0, 11);
        layout.Controls.Add(CreateMappingRow("SinglePress", "ToggleModes"), 0, 12);
        layout.Controls.Add(CreateMappingRow("DoublePress", "ToggleMaxFan"), 0, 13);
        layout.Controls.Add(CreateMappingRow("TriplePress", "EnableEco"), 0, 14);

        SetRows(layout, 80, 32, 48, 24, 1, 68, 68, 68, 68, 68, 72, 38, 42, 42, 42);
        _generalPage.Controls.Add(layout);
    }

    private void BuildPowerPage()
    {
        ConfigurePage(_powerPage);
        var layout = NewPageLayout(625);
        AddHeader(layout, 0, "PowerTitle", "PowerSubtitle");
        layout.Controls.Add(CreateSectionTitle("EcoBehaviorTitle"), 0, 1);
        layout.Controls.Add(CreateSettingRow(
            "EcoRefreshRate",
            "EcoRefreshRateDescription",
            _ecoRefreshRateToggle), 0, 2);
        layout.Controls.Add(CreateSettingRow(
            "EcoFrameRate",
            "EcoFrameRateDescription",
            _ecoFrameRateToggle), 0, 3);
        layout.Controls.Add(CreateSettingRow(
            "EcoTurboBoost",
            "EcoTurboBoostDescription",
            _ecoTurboBoostToggle), 0, 4);
        layout.Controls.Add(CreateSectionTitle("WindowsPowerTitle"), 0, 5);
        layout.Controls.Add(CreateSettingRow("PowerTuning", "PowerTuningDescription", _powerTuningToggle), 0, 6);
        layout.Controls.Add(CreateSettingRow("EcoAcLimit", "EcoAcDescription", PercentControl(_ecoAcMaximum)), 0, 7);
        layout.Controls.Add(CreateSettingRow("EcoBatteryLimit", "EcoBatteryDescription", PercentControl(_ecoBatteryMaximum)), 0, 8);

        _powerStatus.Dock = DockStyle.Fill;
        _powerStatus.Font = WindowsTheme.Font(9f);
        _powerStatus.Padding = new Padding(0, 14, 0, 0);
        _powerStatus.Tag = "secondary";
        Register(_powerStatus, "PowerRestoreNote");
        layout.Controls.Add(_powerStatus, 0, 9);
        SetRows(layout, 80, 30, 68, 68, 68, 30, 82, 72, 72, 55);
        _powerPage.Controls.Add(layout);
    }

    private void BuildAboutPage()
    {
        ConfigurePage(_aboutPage);
        var layout = NewPageLayout(556);
        AddHeader(layout, 0, "AboutTitle", "AboutSubtitle");
        layout.Controls.Add(CreateStaticRow("SupportedHardware", "SupportedHardwareDescription"), 0, 1);
        layout.Controls.Add(CreateSettingRow("CheckForUpdates", "CheckForUpdatesDescription", _updateToggle), 0, 2);

        var updateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        updateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        updateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _updateStatus.Dock = DockStyle.Fill;
        _updateStatus.Font = WindowsTheme.Font(9f);
        _updateStatus.TextAlign = ContentAlignment.MiddleLeft;
        _checkUpdateButton.Anchor = AnchorStyles.Right;
        _checkUpdateButton.Width = 138;
        Register(_checkUpdateButton, "CheckNow");
        updateRow.Controls.Add(_updateStatus, 0, 0);
        updateRow.Controls.Add(_checkUpdateButton, 1, 0);
        updateRow.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, Tag = "separator" }, 0, 1);
        layout.Controls.Add(updateRow, 0, 3);

        _githubButton.Anchor = AnchorStyles.Left;
        _githubButton.Width = 196;
        Register(_githubButton, "OpenGitHub");
        layout.Controls.Add(_githubButton, 0, 4);

        var ai = NewLabel(9f);
        ai.Dock = DockStyle.Fill;
        ai.Tag = "secondary";
        Register(ai, "BuiltWithAi");
        layout.Controls.Add(ai, 0, 5);

        var warning = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 10), Tag = "warning" };
        var warningTitle = NewLabel(9.5f, FontStyle.Bold);
        warningTitle.Dock = DockStyle.Top;
        warningTitle.Height = 25;
        Register(warningTitle, "RiskNoticeTitle");
        var warningText = NewLabel(8.8f);
        warningText.Dock = DockStyle.Fill;
        Register(warningText, "RiskNotice");
        warning.Controls.Add(warningText);
        warning.Controls.Add(warningTitle);
        layout.Controls.Add(warning, 0, 6);

        SetRows(layout, 80, 82, 82, 68, 55, 65, 124);
        _aboutPage.Controls.Add(layout);
    }

    private void BindEvents()
    {
        _generalNavigation.Click += (_, _) => SelectPage(SettingsPage.General);
        _powerNavigation.Click += (_, _) => SelectPage(SettingsPage.Power);
        _aboutNavigation.Click += (_, _) => SelectPage(SettingsPage.About);
        _ecoButton.Click += async (_, _) => await ApplyModeAsync(AppMode.Eco);
        _standardButton.Click += async (_, _) => await ApplyModeAsync(AppMode.Standard);
        _performanceButton.Click += async (_, _) => await ApplyModeAsync(AppMode.Performance);
        _maxFanToggle.CheckedChanged += async (_, _) => await ChangeMaxFanAsync();
        _notificationsToggle.CheckedChanged += (_, _) => SavePreference(() =>
            _store.Settings.ShowNotifications = _notificationsToggle.Checked);
        _startupToggle.CheckedChanged += (_, _) => ChangeStartup();
        _hpServicesToggle.CheckedChanged += (_, _) => SavePreference(() =>
            _store.Settings.SuppressHpAppServices = _hpServicesToggle.Checked);
        _languageCombo.SelectedIndexChanged += ChangeLanguage;
        _hotkeyButton.Click += (_, _) => ToggleHotkeyCapture();
        _clearHotkeyButton.Click += (_, _) => SaveHotkey(new HotkeyBinding());
        Deactivate += (_, _) => CancelHotkeyCapture();
        _ecoRefreshRateToggle.CheckedChanged += async (_, _) => await ChangeEcoBehaviorAsync();
        _ecoFrameRateToggle.CheckedChanged += async (_, _) => await ChangeEcoBehaviorAsync();
        _ecoTurboBoostToggle.CheckedChanged += async (_, _) => await ChangeEcoBehaviorAsync();
        _powerTuningToggle.CheckedChanged += async (_, _) => await ChangePowerTuningAsync();
        _ecoAcMaximum.ValueChanged += QueuePowerSettingsSave;
        _ecoBatteryMaximum.ValueChanged += QueuePowerSettingsSave;
        _powerApplyTimer.Tick += ApplyQueuedPowerSettings;
        _updateToggle.CheckedChanged += (_, _) => SavePreference(() =>
            _store.Settings.CheckForUpdates = _updateToggle.Checked);
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _githubButton.Click += (_, _) => Process.Start(new ProcessStartInfo(UpdateService.RepositoryUrl)
        {
            UseShellExecute = true
        });
    }

    private void LoadSettings()
    {
        _loading = true;
        ReloadLanguageOptions();
        _notificationsToggle.Checked = _store.Settings.ShowNotifications;
        _startupToggle.Checked = ReadStartupState();
        _hpServicesToggle.Checked = _store.Settings.SuppressHpAppServices;
        _maxFanToggle.Checked = _controller.MaxFanEnabled;
        _ecoRefreshRateToggle.Checked = _store.Settings.EcoBehavior.LimitDisplayRefreshRate;
        _ecoFrameRateToggle.Checked = _store.Settings.EcoBehavior.LimitNvidiaFrameRate;
        _ecoTurboBoostToggle.Checked = _store.Settings.EcoBehavior.DisableTurboBoost;
        _powerTuningToggle.Checked = _store.Settings.PowerTuning.Enabled;
        _ecoAcMaximum.Value = _store.Settings.PowerTuning.EcoAcMaximumProcessor;
        _ecoBatteryMaximum.Value = _store.Settings.PowerTuning.EcoBatteryMaximumProcessor;
        _updateToggle.Checked = _store.Settings.CheckForUpdates;
        _loading = false;
        UpdateHotkeyUi();
        UpdateModeUi();
        UpdatePowerControls();
    }

    private void ReloadLanguageOptions()
    {
        var selected = _store.Settings.Language;
        _languageCombo.DisplayMember = nameof(LanguageOption.Name);
        _languageCombo.ValueMember = nameof(LanguageOption.Code);
        _languageCombo.DataSource = _localizer.GetLanguageOptions().ToList();
        _languageCombo.SelectedValue = selected;
    }

    private void ApplyText()
    {
        Text = $"Victus Mode Switch - {_localizer["Settings"]}";
        _versionLabel.Text = _localizer.Format("Version", AppVersion.Display);
        foreach (var (control, key) in _localizedControls)
        {
            control.Text = _localizer[key];
        }

        _ecoButton.Text = _localizer.ModeName(AppMode.Eco);
        _standardButton.Text = _localizer.ModeName(AppMode.Standard);
        _performanceButton.Text = _localizer.ModeName(AppMode.Performance);
        _updateStatus.Text = _localizer.Format("Version", AppVersion.Display);
        _toolTip.SetToolTip(_maxFanToggle, _localizer["MaxFan"]);
        _toolTip.SetToolTip(_notificationsToggle, _localizer["Notifications"]);
        _toolTip.SetToolTip(_startupToggle, _localizer["StartWithWindows"]);
        _toolTip.SetToolTip(_hpServicesToggle, _localizer["HpServices"]);
        _toolTip.SetToolTip(_hotkeyButton, _localizer["RecordShortcut"]);
        _toolTip.SetToolTip(_clearHotkeyButton, _localizer["ClearShortcut"]);
        _hotkeyButton.AccessibleName = _localizer["RecordShortcut"];
        _clearHotkeyButton.AccessibleName = _localizer["ClearShortcut"];
        _toolTip.SetToolTip(_ecoRefreshRateToggle, _localizer["EcoRefreshRate"]);
        _toolTip.SetToolTip(_ecoFrameRateToggle, _localizer["EcoFrameRate"]);
        _toolTip.SetToolTip(_ecoTurboBoostToggle, _localizer["EcoTurboBoost"]);
        _toolTip.SetToolTip(_powerTuningToggle, _localizer["PowerTuning"]);
        _toolTip.SetToolTip(_updateToggle, _localizer["CheckForUpdates"]);
        UpdateHotkeyUi();
        UpdateModeUi();
    }

    private void ApplyTheme()
    {
        var palette = WindowsTheme.Current;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        _navigation.BackColor = palette.Background;
        _contentHost.BackColor = palette.Background;
        foreach (var page in new[] { _generalPage, _powerPage, _aboutPage })
        {
            page.BackColor = palette.Background;
            foreach (var control in Descendants(page))
            {
                control.ForeColor = control.Tag as string == "secondary" ? palette.SecondaryText : palette.Text;
                if (control.Tag as string == "separator")
                {
                    control.BackColor = palette.Border;
                }
                else if (control.Tag as string == "warning")
                {
                    control.BackColor = palette.Dark ? Color.FromArgb(58, 45, 28) : Color.FromArgb(255, 247, 224);
                }
                else if (control is TableLayoutPanel or Panel)
                {
                    control.BackColor = palette.Background;
                }
            }
        }

        foreach (var navigation in new[] { _generalNavigation, _powerNavigation, _aboutNavigation })
        {
            navigation.ApplyTheme(palette);
        }

        foreach (var toggle in new[]
                 {
                     _maxFanToggle,
                     _notificationsToggle,
                     _startupToggle,
                     _hpServicesToggle,
                     _ecoRefreshRateToggle,
                     _ecoFrameRateToggle,
                     _ecoTurboBoostToggle,
                     _powerTuningToggle,
                     _updateToggle
                 })
        {
            toggle.ApplyTheme(palette);
        }

        _languageCombo.BackColor = palette.Surface;
        _languageCombo.ForeColor = palette.Text;
        foreach (var input in new[] { _ecoAcMaximum, _ecoBatteryMaximum })
        {
            input.BackColor = palette.Surface;
            input.ForeColor = palette.Text;
        }

        FluentUi.StyleButton(_checkUpdateButton, palette);
        FluentUi.StyleButton(_githubButton, palette);
        FluentUi.StyleButton(_hotkeyButton, palette);
        FluentUi.StyleButton(_clearHotkeyButton, palette);
        _clearHotkeyButton.Font = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular, GraphicsUnit.Point);
        StyleModeButtons(palette);
        Invalidate(true);
    }

    private void SelectPage(SettingsPage page)
    {
        _generalPage.Visible = page == SettingsPage.General;
        _powerPage.Visible = page == SettingsPage.Power;
        _aboutPage.Visible = page == SettingsPage.About;
        _generalNavigation.Selected = page == SettingsPage.General;
        _powerNavigation.Selected = page == SettingsPage.Power;
        _aboutNavigation.Selected = page == SettingsPage.About;
        var selected = page switch
        {
            SettingsPage.Power => _powerPage,
            SettingsPage.About => _aboutPage,
            _ => _generalPage
        };
        selected.BringToFront();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!_capturingHotkey)
        {
            return base.ProcessCmdKey(ref message, keyData);
        }

        CaptureHotkey(keyData);
        return true;
    }

    private void ToggleHotkeyCapture()
    {
        if (_capturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _capturingHotkey = true;
        if (_globalHotkey is not null)
        {
            _globalHotkey.SuppressInvocations = true;
        }

        _hotkeyButton.Text = _localizer["PressShortcut"];
        _hotkeyButton.Focus();
    }

    private void CaptureHotkey(Keys keyData)
    {
        var keyCode = (int)(keyData & Keys.KeyCode);
        var windowsKeyDown = GlobalHotkeyController.IsWindowsKeyDown();
        var hasStandardModifier = (keyData & Keys.Modifiers) != Keys.None;
        if (keyCode == (int)Keys.Escape && !hasStandardModifier && !windowsKeyDown)
        {
            CancelHotkeyCapture();
            return;
        }

        if ((keyCode == (int)Keys.Back || keyCode == (int)Keys.Delete) &&
            !hasStandardModifier &&
            !windowsKeyDown)
        {
            SaveHotkey(new HotkeyBinding());
            return;
        }

        if (HotkeyBinding.IsModifierKey(keyCode))
        {
            return;
        }

        var binding = HotkeyBinding.FromKeyData(keyData, windowsKeyDown);
        if (!binding.IsConfigured)
        {
            _hotkeyButton.Text = _localizer["ShortcutNeedsModifier"];
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        SaveHotkey(binding);
    }

    private void SaveHotkey(HotkeyBinding binding)
    {
        var previous = _store.Settings.KeyboardShortcut;
        var result = _globalHotkey?.Apply(binding) ?? HotkeyRegistrationResult.Registered();
        _capturingHotkey = false;
        if (_globalHotkey is not null)
        {
            _globalHotkey.SuppressInvocations = false;
        }

        if (!result.Success)
        {
            UpdateHotkeyUi();
            var message = result.ErrorCode == HotkeyRegistrationResult.AlreadyRegisteredError
                ? _localizer["ShortcutConflict"]
                : _localizer.Format("ShortcutRegistrationFailed", result.ErrorCode);
            MessageBox.Show(this, message, "Victus Mode Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _store.Settings.KeyboardShortcut = binding.Normalize();
            _store.Save();
        }
        catch
        {
            _store.Settings.KeyboardShortcut = previous;
            _globalHotkey?.Apply(previous);
            throw;
        }

        UpdateHotkeyUi();
        PreferencesChanged?.Invoke();
    }

    private void CancelHotkeyCapture()
    {
        if (!_capturingHotkey)
        {
            return;
        }

        _capturingHotkey = false;
        if (_globalHotkey is not null)
        {
            _globalHotkey.SuppressInvocations = false;
        }

        UpdateHotkeyUi();
    }

    private void UpdateHotkeyUi()
    {
        _hotkeyButton.Text = _capturingHotkey
            ? _localizer["PressShortcut"]
            : _store.Settings.KeyboardShortcut.IsConfigured
                ? _store.Settings.KeyboardShortcut.ToDisplayString()
                : _localizer["ShortcutNotAssigned"];
        _clearHotkeyButton.Enabled = _store.Settings.KeyboardShortcut.IsConfigured;
    }

    private async Task ApplyModeAsync(AppMode mode)
    {
        if (_hardwareBusy)
        {
            return;
        }

        SetHardwareBusy(true);
        _modeStatus.Text = _localizer["ChangingMode"];
        var result = await _controller.ApplyAsync(mode);
        SetHardwareBusy(false);
        if (!result.Success)
        {
            _modeStatus.Text = result.Warnings.FirstOrDefault() ?? _localizer["ModeError"];
            _modeStatus.ForeColor = Color.FromArgb(196, 43, 28);
            return;
        }

        _modeStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        _modeStatus.Text = result.Warnings.Count == 0
            ? _localizer["ModeApplied"]
            : _localizer["PartialMode"];
        _loading = true;
        _maxFanToggle.Checked = _controller.MaxFanEnabled;
        _loading = false;
        UpdateModeUi();
        ReplaceWindowIcon();
        HardwareStateChanged?.Invoke();
    }

    private async Task ChangeMaxFanAsync()
    {
        if (_loading || _hardwareBusy)
        {
            return;
        }

        var requested = _maxFanToggle.Checked;
        SetHardwareBusy(true);
        var result = await _controller.SetMaxFanAsync(requested);
        SetHardwareBusy(false);
        if (!result.Success)
        {
            _loading = true;
            _maxFanToggle.Checked = _controller.MaxFanEnabled;
            _loading = false;
            _modeStatus.Text = result.Warnings.FirstOrDefault() ?? _localizer["FanError"];
            _modeStatus.ForeColor = Color.FromArgb(196, 43, 28);
            return;
        }

        _modeStatus.Text = _localizer[result.Enabled ? "MaxFanOn" : "MaxFanOff"];
        _modeStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        ReplaceWindowIcon();
        HardwareStateChanged?.Invoke();
    }

    private void ChangeLanguage(object? sender, EventArgs eventArgs)
    {
        if (_loading || _languageCombo.SelectedItem is not LanguageOption selected)
        {
            return;
        }

        _store.Settings.Language = selected.Code;
        _store.Save();
        _localizer = new Localizer(selected.Code);
        _loading = true;
        ReloadLanguageOptions();
        _loading = false;
        ApplyText();
        PreferencesChanged?.Invoke();
    }

    private async Task ChangePowerTuningAsync()
    {
        if (_loading)
        {
            return;
        }

        _store.Settings.PowerTuning.Enabled = _powerTuningToggle.Checked;
        _store.Save();
        UpdatePowerControls();
        PreferencesChanged?.Invoke();
        var result = await _controller.ApplyAsync(_controller.CurrentMode, reapply: true);
        if (!result.Success || result.Warnings.Count > 0)
        {
            _powerStatus.Text = result.Warnings.FirstOrDefault() ?? _localizer["ModeError"];
            _powerStatus.ForeColor = Color.FromArgb(196, 43, 28);
            return;
        }

        _powerStatus.Text = _localizer["PowerRestoreNote"];
        _powerStatus.ForeColor = WindowsTheme.Current.SecondaryText;
    }

    private async Task ChangeEcoBehaviorAsync()
    {
        if (_loading || _hardwareBusy)
        {
            return;
        }

        _store.Settings.EcoBehavior.LimitDisplayRefreshRate = _ecoRefreshRateToggle.Checked;
        _store.Settings.EcoBehavior.LimitNvidiaFrameRate = _ecoFrameRateToggle.Checked;
        _store.Settings.EcoBehavior.DisableTurboBoost = _ecoTurboBoostToggle.Checked;
        _store.Save();
        PreferencesChanged?.Invoke();

        SetHardwareBusy(true);
        _powerStatus.Text = _localizer["ApplyingEcoBehavior"];
        _powerStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        try
        {
            var result = await _controller.ApplyAsync(_controller.CurrentMode, reapply: true);
            if (!result.Success || result.Warnings.Count > 0)
            {
                _powerStatus.Text = result.Warnings.FirstOrDefault() ?? _localizer["ModeError"];
                _powerStatus.ForeColor = Color.FromArgb(196, 43, 28);
                return;
            }

            _powerStatus.Text = _localizer["EcoBehaviorApplied"];
            _powerStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        }
        finally
        {
            SetHardwareBusy(false);
        }
    }

    private void QueuePowerSettingsSave(object? sender, EventArgs eventArgs)
    {
        if (_loading)
        {
            return;
        }

        _store.Settings.PowerTuning.EcoAcMaximumProcessor = (int)_ecoAcMaximum.Value;
        _store.Settings.PowerTuning.EcoBatteryMaximumProcessor = (int)_ecoBatteryMaximum.Value;
        _store.Save();
        PreferencesChanged?.Invoke();
        _powerApplyTimer.Stop();
        if (_store.Settings.PowerTuning.Enabled && _controller.CurrentMode == AppMode.Eco)
        {
            _powerApplyTimer.Start();
        }
    }

    private async void ApplyQueuedPowerSettings(object? sender, EventArgs eventArgs)
    {
        _powerApplyTimer.Stop();
        await _controller.ApplyAsync(AppMode.Eco, reapply: true);
    }

    private void SavePreference(Action change)
    {
        if (_loading)
        {
            return;
        }

        change();
        _store.Save();
        PreferencesChanged?.Invoke();
    }

    private bool ReadStartupState()
    {
        try
        {
            var enabled = _startupController.IsEnabled();
            if (_store.Settings.StartWithWindows != enabled)
            {
                _store.Settings.StartWithWindows = enabled;
                _store.Save();
            }

            return enabled;
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось прочитать состояние автозапуска", exception);
            return _store.Settings.StartWithWindows;
        }
    }

    private void ChangeStartup()
    {
        if (_loading)
        {
            return;
        }

        var requested = _startupToggle.Checked;
        var previousPreference = _store.Settings.StartWithWindows;
        var previousRegistration = previousPreference;
        try
        {
            previousRegistration = _startupController.IsEnabled();
            _startupController.SetEnabled(requested);
            _store.Settings.StartWithWindows = requested;
            _store.Save();
            PreferencesChanged?.Invoke();
        }
        catch (Exception exception)
        {
            _store.Settings.StartWithWindows = previousPreference;
            try
            {
                _startupController.SetEnabled(previousRegistration);
            }
            catch (Exception rollbackException)
            {
                Log.Error("Не удалось откатить изменение автозапуска", rollbackException);
            }

            Log.Error("Не удалось изменить автозапуск", exception);
            _loading = true;
            _startupToggle.Checked = ReadStartupState();
            _loading = false;
            MessageBox.Show(
                this,
                _localizer["StartupChangeError"],
                "Victus Mode Switch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        _checkUpdateButton.Enabled = false;
        _updateStatus.ForeColor = WindowsTheme.Current.SecondaryText;
        _updateStatus.Text = _localizer["CheckingUpdate"];
        var result = await _updateService.CheckAsync();
        _store.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _store.Save();
        _checkUpdateButton.Enabled = true;

        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                _updateStatus.Text = _localizer["UpToDate"];
                break;
            case UpdateCheckStatus.Available when result.Update is not null:
                _updateStatus.Text = _localizer.Format(
                    "UpdateAvailable",
                    $"{result.Update.Version.Major}.{result.Update.Version.Minor}.{result.Update.Version.Build}");
                ShowUpdateDialog(result.Update);
                break;
            default:
                _updateStatus.Text = _localizer["UpdateError"];
                _updateStatus.ForeColor = Color.FromArgb(196, 43, 28);
                break;
        }
    }

    private void ShowUpdateDialog(UpdateInfo update)
    {
        var dialog = new UpdateDialog(update, _localizer);
        dialog.InstallerStarted += () =>
        {
            InstallerStarted?.Invoke();
            Application.Exit();
        };
        dialog.Show(this);
    }

    private void SetHardwareBusy(bool busy)
    {
        _hardwareBusy = busy;
        _ecoButton.Enabled = !busy;
        _standardButton.Enabled = !busy;
        _performanceButton.Enabled = !busy;
        _maxFanToggle.Enabled = !busy;
        _ecoRefreshRateToggle.Enabled = !busy;
        _ecoFrameRateToggle.Enabled = !busy;
        _ecoTurboBoostToggle.Enabled = !busy;
    }

    private void UpdateModeUi()
    {
        var palette = WindowsTheme.Current;
        StyleModeButton(_ecoButton, AppMode.Eco, palette);
        StyleModeButton(_standardButton, AppMode.Standard, palette);
        StyleModeButton(_performanceButton, AppMode.Performance, palette);
        if (string.IsNullOrEmpty(_modeStatus.Text))
        {
            _modeStatus.Text = $"{_localizer["CurrentMode"]}: {_localizer.ModeDescription(_controller.CurrentMode)}";
        }
    }

    private void StyleModeButtons(ThemePalette palette)
    {
        StyleModeButton(_ecoButton, AppMode.Eco, palette);
        StyleModeButton(_standardButton, AppMode.Standard, palette);
        StyleModeButton(_performanceButton, AppMode.Performance, palette);
        var selector = _ecoButton.Parent;
        if (selector is not null)
        {
            selector.BackColor = palette.Border;
        }
    }

    private void StyleModeButton(Button button, AppMode mode, ThemePalette palette)
    {
        var selected = _controller.CurrentMode == mode;
        button.BackColor = selected
            ? palette.Dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(232, 232, 232)
            : palette.Surface;
        button.ForeColor = palette.Text;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = palette.Dark
            ? Color.FromArgb(64, 64, 64)
            : Color.FromArgb(225, 225, 225);
        var previousImage = button.Image;
        button.Image = AppIcon.CreateBitmap(mode.AccentColor(), selected && _controller.MaxFanEnabled, 16);
        previousImage?.Dispose();
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
    }

    private void ReplaceWindowIcon()
    {
        var previous = Icon;
        Icon = AppIcon.Create(_controller.CurrentMode.AccentColor(), _controller.MaxFanEnabled);
        previous?.Dispose();
    }

    private void UpdatePowerControls()
    {
        var enabled = _powerTuningToggle.Checked;
        _ecoAcMaximum.Enabled = enabled;
        _ecoBatteryMaximum.Enabled = enabled;
    }

    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            ApplyTheme();
            WindowsTheme.ApplyWindow(this);
        }));
    }

    private T Register<T>(T control, string key) where T : Control
    {
        _localizedControls[control] = key;
        return control;
    }

    private static void ConfigurePage(Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.AutoScroll = true;
        page.Visible = false;
    }

    private static TableLayoutPanel NewPageLayout(int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        ColumnCount = 1,
        RowCount = 12,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private void AddHeader(TableLayoutPanel layout, int row, string titleKey, string subtitleKey)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
        var title = NewLabel(22f);
        title.Height = 50;
        title.TextAlign = ContentAlignment.MiddleLeft;
        Register(title, titleKey);
        var subtitle = NewLabel(9.5f);
        subtitle.TextAlign = ContentAlignment.TopLeft;
        subtitle.Tag = "secondary";
        Register(subtitle, subtitleKey);
        void ArrangeHeader()
        {
            title.Bounds = new Rectangle(-4, 0, Math.Max(1, panel.ClientSize.Width + 4), 50);
            subtitle.Bounds = new Rectangle(0, 50, Math.Max(1, panel.ClientSize.Width), 30);
        }

        panel.SizeChanged += (_, _) => ArrangeHeader();
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        ArrangeHeader();
        layout.Controls.Add(panel, 0, row);
    }

    private Label CreateSectionTitle(string titleKey)
    {
        var title = NewLabel(11f, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Padding = new Padding(0, 5, 0, 0);
        return Register(title, titleKey);
    }

    private TableLayoutPanel CreateSettingRow(string titleKey, string descriptionKey, Control action)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(64, action.Width + 20)));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));

        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 7, 12, 4)
        };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = NewLabel(9.5f, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.AutoEllipsis = true;
        Register(title, titleKey);
        var description = NewLabel(8.5f);
        description.Dock = DockStyle.Fill;
        description.TextAlign = ContentAlignment.TopLeft;
        description.AutoEllipsis = true;
        description.Tag = "secondary";
        Register(description, descriptionKey);
        text.Controls.Add(title, 0, 0);
        text.Controls.Add(description, 0, 1);
        action.Anchor = AnchorStyles.Right;
        action.Margin = new Padding(8, 0, 0, 0);
        row.Controls.Add(text, 0, 0);
        row.Controls.Add(action, 1, 0);
        row.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, Tag = "separator" }, 0, 1);
        row.SetColumnSpan(row.GetControlFromPosition(0, 1)!, 2);
        return row;
    }

    private Panel CreateStaticRow(string titleKey, string descriptionKey)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = new Padding(0, 7, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        var title = NewLabel(9.5f, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.AutoEllipsis = true;
        Register(title, titleKey);
        var description = NewLabel(8.8f);
        description.Dock = DockStyle.Fill;
        description.TextAlign = ContentAlignment.TopLeft;
        description.AutoEllipsis = true;
        description.Tag = "secondary";
        Register(description, descriptionKey);
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(description, 0, 1);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill, Tag = "separator" }, 0, 2);
        return panel;
    }

    private TableLayoutPanel CreateMappingRow(string gestureKey, string actionKey)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        var gesture = NewLabel(9f);
        gesture.Dock = DockStyle.Fill;
        Register(gesture, gestureKey);
        var action = NewLabel(9f, FontStyle.Bold);
        action.Dock = DockStyle.Fill;
        action.TextAlign = ContentAlignment.MiddleRight;
        Register(action, actionKey);
        row.Controls.Add(gesture, 0, 0);
        row.Controls.Add(action, 1, 0);
        var separator = new Panel { Dock = DockStyle.Fill, Tag = "separator" };
        row.Controls.Add(separator, 0, 1);
        row.SetColumnSpan(separator, 2);
        return row;
    }

    private FlowLayoutPanel CreateHotkeyControl()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Size = new Size(244, 35)
        };
        _hotkeyButton.Size = new Size(204, 34);
        _hotkeyButton.Margin = Padding.Empty;
        _clearHotkeyButton.Size = new Size(34, 34);
        _clearHotkeyButton.Margin = new Padding(6, 0, 0, 0);
        _clearHotkeyButton.Text = "\uE74D";
        panel.Controls.Add(_hotkeyButton);
        panel.Controls.Add(_clearHotkeyButton);
        return panel;
    }

    private static FlowLayoutPanel PercentControl(NumericUpDown input)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = false,
            Size = new Size(118, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        input.Margin = new Padding(0, 1, 7, 0);
        var percent = new Label
        {
            AutoSize = false,
            Font = WindowsTheme.Font(9.5f),
            Size = new Size(18, 30),
            Text = "%",
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(input);
        panel.Controls.Add(percent);
        return panel;
    }

    private static Label NewLabel(float size, FontStyle style = FontStyle.Regular) => new()
    {
        AutoSize = false,
        Font = WindowsTheme.Font(size, style),
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static NumericUpDown CreatePercentInput() => new()
    {
        Minimum = 5,
        Maximum = 100,
        Increment = 5,
        Width = 82,
        Height = 32,
        TextAlign = HorizontalAlignment.Right,
        BorderStyle = BorderStyle.FixedSingle,
        Font = WindowsTheme.Font(9.5f)
    };

    private static void ConfigureModeButton(Button button, AppMode mode)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(mode == AppMode.Eco ? 0 : 1, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.Cursor = Cursors.Hand;
        button.Font = WindowsTheme.Font(9.5f, FontStyle.Bold);
        button.Padding = new Padding(12, 0, 8, 0);
        button.UseVisualStyleBackColor = false;
    }

    private static void SetRows(TableLayoutPanel layout, params int[] heights)
    {
        layout.RowCount = heights.Length;
        layout.RowStyles.Clear();
        foreach (var height in heights)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
