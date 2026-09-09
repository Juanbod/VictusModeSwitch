using System.Diagnostics;
using Microsoft.Win32;

namespace VictusModeSwitch;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettingsStore _store = new();
    private readonly ModeController _controller;
    private readonly UpdateService _updateService = new();
    private readonly OmenKeyListener _keyListener = new();
    private readonly GlobalHotkeyController _globalHotkey = new();
    private readonly HpServiceSuppressor _hpServiceSuppressor = new();
    private readonly OmenPressSequence _pressSequence = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Control _dispatcher = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _ecoItem;
    private readonly ToolStripMenuItem _standardItem;
    private readonly ToolStripMenuItem _performanceItem;
    private readonly ToolStripMenuItem _maxFanItem = new();
    private readonly ToolStripMenuItem _settingsItem = new();
    private readonly ToolStripMenuItem _updatesItem = new();
    private readonly ToolStripMenuItem _openLogItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private readonly System.Windows.Forms.Timer _startupTimer = new() { Interval = 1200 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 9000 };
    private readonly System.Windows.Forms.Timer _settingsSignalTimer = new() { Interval = 100 };
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly EventWaitHandle _openSettingsEvent;
    private readonly CancellationTokenSource _shutdown = new();
    private Localizer _localizer;
    private Icon? _currentIcon;
    private ModeToastForm? _toast;
    private SettingsForm? _settingsForm;
    private UpdateDialog? _updateDialog;
    private volatile bool _exiting;
    private bool _updateBusy;
    private int _interactiveHardwareCommands;

    public TrayApplicationContext()
    {
        _controller = new ModeController(_store);
        _localizer = new Localizer(_store.Settings.Language);
        _cleanupTimer = new(
            _ => UpdateService.CleanupDownloads(),
            null,
            TimeSpan.FromSeconds(30),
            Timeout.InfiniteTimeSpan);
        _dispatcher.CreateControl();
        _openSettingsEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            Program.OpenSettingsEventName);
        _settingsSignalTimer.Tick += CheckSettingsSignal;
        _settingsSignalTimer.Start();

        _ecoItem = CreateModeItem(AppMode.Eco);
        _standardItem = CreateModeItem(AppMode.Standard);
        _performanceItem = CreateModeItem(AppMode.Performance);
        _maxFanItem.Click += async (_, _) => await ToggleMaxFanAsync(showToast: false);
        _settingsItem.Click += (_, _) => OpenSettings(SettingsPage.General);
        _updatesItem.Click += async (_, _) => await CheckForUpdatesAsync(showUpToDate: true);
        _openLogItem.Click += OpenLog;
        _exitItem.Click += (_, _) => ExitThread();

        _menu.Font = WindowsTheme.Font(9f);
        _menu.Opening += (_, _) =>
        {
            ApplyMenuTheme();
            WindowsTheme.ApplyNativeWindow(_menu.Handle);
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _ecoItem,
            _standardItem,
            _performanceItem,
            new ToolStripSeparator(),
            _maxFanItem,
            new ToolStripSeparator(),
            _settingsItem,
            _updatesItem,
            _openLogItem,
            new ToolStripSeparator(),
            _exitItem
        });

        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.Text = "Victus Mode Switch";
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => OpenSettings(SettingsPage.General);
        ApplyMenuTheme();
        UpdateLocalizedText();
        UpdateUi();

        _startupTimer.Tick += ReapplyAfterStartup;
        _updateTimer.Tick += CheckUpdatesAfterStartup;
        _keyListener.OmenKeyPressed += OnOmenKeyPressed;
        _globalHotkey.Pressed += OnGlobalHotkeyPressed;
        _pressSequence.GestureRecognized += OnGestureRecognized;
        var hotkeyResult = _globalHotkey.Apply(_store.Settings.KeyboardShortcut);
        if (!hotkeyResult.Success)
        {
            _notifyIcon.ShowBalloonTip(
                4500,
                "Victus Mode Switch",
                _localizer["ShortcutStartupFailed"],
                ToolTipIcon.Warning);
        }

        _keyListener.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        Log.Info(
            $"Victus Mode Switch {AppVersion.Display} запущен, режим: {_controller.CurrentMode}, " +
            $"Max Fan: {_controller.MaxFanEnabled}, Power tuning: {_store.Settings.PowerTuning.Enabled}, " +
            $"HP services suppressed: {_store.Settings.SuppressHpAppServices}, " +
            $"Windows uptime: {systemUptime:c}");
        _startupTimer.Start();
    }

    protected override void ExitThreadCore()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _shutdown.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _keyListener.OmenKeyPressed -= OnOmenKeyPressed;
        _globalHotkey.Pressed -= OnGlobalHotkeyPressed;
        _pressSequence.GestureRecognized -= OnGestureRecognized;
        _keyListener.Dispose();
        _globalHotkey.Dispose();
        _pressSequence.Dispose();
        _startupTimer.Stop();
        _startupTimer.Dispose();
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _settingsSignalTimer.Stop();
        _settingsSignalTimer.Tick -= CheckSettingsSignal;
        _settingsSignalTimer.Dispose();
        _cleanupTimer.Dispose();
        _hpServiceSuppressor.Dispose();
        _shutdown.Dispose();
        _openSettingsEvent.Dispose();
        _toast?.Close();
        _settingsForm?.Close();
        _updateDialog?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon?.Dispose();
        _dispatcher.Dispose();
        Log.Info("Victus Mode Switch остановлен");
        base.ExitThreadCore();
    }

    private ToolStripMenuItem CreateModeItem(AppMode mode)
    {
        var item = new ToolStripMenuItem();
        item.Click += async (_, _) => await ApplyModeAsync(mode, showToast: false);
        return item;
    }

    private void OnOmenKeyPressed()
    {
        QueueGesturePress();
    }

    private void OnGlobalHotkeyPressed()
    {
        QueueGesturePress();
    }

    private void QueueGesturePress()
    {
        if (!_dispatcher.IsDisposed)
        {
            _dispatcher.BeginInvoke(new Action(_pressSequence.RegisterPress));
        }
    }

    private void CheckSettingsSignal(object? sender, EventArgs eventArgs)
    {
        if (_exiting || !_openSettingsEvent.WaitOne(0))
        {
            return;
        }

        Log.Info("Получена команда открыть настройки");
        OpenSettings(SettingsPage.General);
    }

    private async void OnGestureRecognized(OmenGesture gesture)
    {
        Log.Info($"Распознан жест кнопки или хоткея: {gesture}");
        switch (gesture)
        {
            case OmenGesture.SinglePress:
                await ApplyModeAsync(_controller.CurrentMode.TogglePerformance());
                break;
            case OmenGesture.DoublePress:
                await ToggleMaxFanAsync();
                break;
            case OmenGesture.TriplePress:
                await ApplyModeAsync(AppMode.Eco);
                break;
        }
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode != PowerModes.Resume)
        {
            return;
        }

        await Task.Delay(2500);
        if (!_dispatcher.IsDisposed)
        {
            _dispatcher.BeginInvoke(new Action(async () =>
                await ApplyModeAsync(_controller.CurrentMode, reapply: true, showToast: false)));
        }
    }

    private async Task ApplyModeAsync(AppMode mode, bool reapply = false, bool showToast = true)
    {
        if (!reapply)
        {
            Interlocked.Increment(ref _interactiveHardwareCommands);
        }

        ModeToastForm? pendingToast = null;
        if (showToast && _store.Settings.ShowNotifications)
        {
            _toast?.Close();
            pendingToast = ModeToastForm.ForPendingMode(mode, _localizer);
            _toast = pendingToast;
            pendingToast.Show();
        }

        var result = await _controller.ApplyAsync(mode, reapply);
        if (!result.Success)
        {
            pendingToast?.Close();
            _notifyIcon.ShowBalloonTip(
                3500,
                "Victus Mode Switch",
                result.Warnings.FirstOrDefault() ?? _localizer["ModeError"],
                ToolTipIcon.Error);
            return;
        }

        UpdateUi();
        RefreshSettingsHardwareState();
        if (pendingToast is not null && !pendingToast.IsDisposed)
        {
            pendingToast.CompleteMode(result.Mode, result.Warnings, _localizer);
        }
    }

    private async Task ToggleMaxFanAsync(bool showToast = true)
    {
        Interlocked.Increment(ref _interactiveHardwareCommands);
        var requested = !_controller.MaxFanEnabled;
        ModeToastForm? pendingToast = null;
        if (showToast && _store.Settings.ShowNotifications)
        {
            _toast?.Close();
            pendingToast = ModeToastForm.ForPendingMaxFan(requested, _localizer);
            _toast = pendingToast;
            pendingToast.Show();
        }

        var result = await _controller.SetMaxFanAsync(requested);
        if (!result.Success)
        {
            pendingToast?.Close();
            _notifyIcon.ShowBalloonTip(
                3500,
                "Victus Mode Switch",
                result.Warnings.FirstOrDefault() ?? _localizer["FanError"],
                ToolTipIcon.Error);
            return;
        }

        UpdateUi();
        RefreshSettingsHardwareState();
        if (pendingToast is not null && !pendingToast.IsDisposed)
        {
            pendingToast.CompleteMaxFan(result.Enabled, _localizer);
        }
    }

    private void UpdateUi()
    {
        var mode = _controller.CurrentMode;
        _ecoItem.Checked = mode == AppMode.Eco;
        _standardItem.Checked = mode == AppMode.Standard;
        _performanceItem.Checked = mode == AppMode.Performance;
        _maxFanItem.Checked = _controller.MaxFanEnabled;
        var tooltip = $"Victus: {_localizer.ModeName(mode)}";
        if (_controller.MaxFanEnabled)
        {
            tooltip += " | Max Fan";
        }

        _notifyIcon.Text = tooltip[..Math.Min(tooltip.Length, 63)];
        var nextIcon = AppIcon.Create(mode.AccentColor(), _controller.MaxFanEnabled);
        _notifyIcon.Icon = nextIcon;
        _currentIcon?.Dispose();
        _currentIcon = nextIcon;
    }

    private void RefreshSettingsHardwareState()
    {
        if (_settingsForm is not { IsDisposed: false, IsHandleCreated: true } settings)
        {
            return;
        }

        settings.BeginInvoke(new Action(settings.RefreshHardwareState));
    }

    private void UpdateLocalizedText()
    {
        _ecoItem.Text = _localizer.ModeName(AppMode.Eco);
        _standardItem.Text = _localizer.ModeName(AppMode.Standard);
        _performanceItem.Text = _localizer.ModeName(AppMode.Performance);
        _maxFanItem.Text = _localizer["MaxFan"];
        _settingsItem.Text = _localizer["Settings"];
        _updatesItem.Text = _localizer["CheckUpdates"];
        _openLogItem.Text = _localizer["OpenLog"];
        _exitItem.Text = _localizer["Exit"];
    }

    private async void ReapplyAfterStartup(object? sender, EventArgs eventArgs)
    {
        _startupTimer.Stop();
        if (Volatile.Read(ref _interactiveHardwareCommands) == 0)
        {
            await ApplyModeAsync(_controller.CurrentMode, reapply: true, showToast: false);
        }
        else
        {
            Log.Info("Стартовое повторное применение пропущено: уже получена команда пользователя");
        }

        _ = StartHpServiceSuppressionAfterStartupAsync();
        _updateTimer.Start();
    }

    private async Task StartHpServiceSuppressionAfterStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_exiting)
        {
            await _controller.WaitForIdleAsync().ConfigureAwait(false);
        }

        if (!_exiting)
        {
            await SyncHpServiceSuppressionAsync(showError: false).ConfigureAwait(false);
        }
    }

    private async void CheckUpdatesAfterStartup(object? sender, EventArgs eventArgs)
    {
        _updateTimer.Stop();
        if (!_store.Settings.CheckForUpdates ||
            _store.Settings.LastUpdateCheckUtc is { } checkedAt &&
            DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromDays(1))
        {
            return;
        }

        await CheckForUpdatesAsync(showUpToDate: false);
    }

    private async Task CheckForUpdatesAsync(bool showUpToDate)
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        _updatesItem.Enabled = false;
        try
        {
            var result = await _updateService.CheckAsync();
            _store.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _store.Save();
            if (result.Status == UpdateCheckStatus.Available && result.Update is not null)
            {
                ShowUpdateDialog(result.Update);
            }
            else if (showUpToDate && result.Status == UpdateCheckStatus.UpToDate)
            {
                _notifyIcon.ShowBalloonTip(2500, "Victus Mode Switch", _localizer["UpToDate"], ToolTipIcon.Info);
            }
            else if (showUpToDate && result.Status == UpdateCheckStatus.Failed)
            {
                _notifyIcon.ShowBalloonTip(3500, "Victus Mode Switch", _localizer["UpdateError"], ToolTipIcon.Warning);
            }
        }
        finally
        {
            _updateBusy = false;
            _updatesItem.Enabled = true;
        }
    }

    private void ShowUpdateDialog(UpdateInfo update)
    {
        if (_updateDialog is not null)
        {
            _updateDialog.Activate();
            return;
        }

        _updateDialog = new UpdateDialog(update, _localizer);
        _updateDialog.InstallerStarted += ExitThread;
        _updateDialog.FormClosed += (_, _) => _updateDialog = null;
        _updateDialog.Show();
        _updateDialog.Activate();
    }

    private void OpenSettings(SettingsPage page)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (page == SettingsPage.About)
            {
                _settingsForm.ShowAboutPage();
            }

            WindowsTheme.ShowAndActivate(_settingsForm);
            Log.Info("Окно настроек активировано");
            return;
        }

        _settingsForm = new SettingsForm(_store, _controller, page, _globalHotkey);
        _settingsForm.PreferencesChanged += OnPreferencesChanged;
        _settingsForm.HardwareStateChanged += UpdateUi;
        _settingsForm.InstallerStarted += ExitThread;
        _settingsForm.FormClosed += (_, _) =>
        {
            if (_settingsForm is null)
            {
                return;
            }

            _settingsForm.PreferencesChanged -= OnPreferencesChanged;
            _settingsForm.HardwareStateChanged -= UpdateUi;
            _settingsForm.InstallerStarted -= ExitThread;
            _settingsForm = null;
        };
        WindowsTheme.ShowAndActivate(_settingsForm);
        Log.Info("Окно настроек открыто");
    }

    private void OnPreferencesChanged()
    {
        _localizer = new Localizer(_store.Settings.Language);
        UpdateLocalizedText();
        UpdateUi();
        _ = SyncHpServiceSuppressionAsync(showError: true);
    }

    private async Task SyncHpServiceSuppressionAsync(bool showError)
    {
        HpServiceSuppressionResult result;
        try
        {
            result = await _hpServiceSuppressor
                .SetEnabledAsync(_store.Settings.SuppressHpAppServices)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_exiting)
        {
            return;
        }

        if (result.Success || !showError || _exiting || _dispatcher.IsDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(new Action(() =>
            _notifyIcon.ShowBalloonTip(
                4500,
                "Victus Mode Switch",
                _localizer["HpServicesError"],
                ToolTipIcon.Warning)));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (_dispatcher.IsDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyMenuTheme();
            UpdateUi();
        }));
    }

    private void ApplyMenuTheme() => WindowsMenuTheme.Apply(_menu);

    private static void OpenLog(object? sender, EventArgs eventArgs)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.LogFile))
        {
            File.WriteAllText(AppPaths.LogFile, string.Empty);
        }

        Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
    }
}
