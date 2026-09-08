using System.Diagnostics;

namespace VictusModeSwitch;

internal sealed class ModeToastForm : Form
{
    private const int SlideDistance = 34;
    private const int EnterDurationMilliseconds = 220;
    private const int HoldDurationMilliseconds = 1550;
    private const int ExitDurationMilliseconds = 180;

    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly Stopwatch _phaseClock = new();
    private readonly bool _animationsEnabled = WindowsTheme.AnimationsEnabled;
    private Point _targetLocation;
    private AnimationPhase _phase;

    protected override bool ShowWithoutActivation => true;

    private ModeToastForm(string titleText, string descriptionText, Color accentColor, bool warning)
    {
        var palette = WindowsTheme.Current;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = palette.Surface;
        ClientSize = new Size(330, 94);
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        var accent = new Panel
        {
            BackColor = accentColor,
            Dock = DockStyle.Left,
            Width = 6
        };

        var title = new Label
        {
            AutoSize = false,
            Font = WindowsTheme.Font(15f, FontStyle.Regular),
            ForeColor = palette.Text,
            Location = new Point(23, 15),
            Size = new Size(290, 31),
            Text = titleText
        };

        var description = new Label
        {
            AutoSize = false,
            Font = WindowsTheme.Font(9f),
            ForeColor = warning ? Color.FromArgb(196, 117, 0) : palette.SecondaryText,
            Location = new Point(24, 51),
            Size = new Size(290, 28),
            Text = descriptionText
        };

        Controls.Add(description);
        Controls.Add(title);
        Controls.Add(accent);

        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        _targetLocation = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        Location = new Point(_targetLocation.X + SlideDistance, _targetLocation.Y);

        _animationTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animationTimer.Tick += Animate;
        Shown += (_, _) =>
        {
            WindowsTheme.RoundControl(this, 8);
            WindowsTheme.ApplyWindow(this, mica: false);
            _phase = _animationsEnabled ? AnimationPhase.Entering : AnimationPhase.Holding;
            if (!_animationsEnabled)
            {
                Opacity = 1d;
                Location = _targetLocation;
            }

            _phaseClock.Restart();
            _animationTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _phaseClock.Stop();
        };
    }

    public static ModeToastForm ForMode(
        AppMode mode,
        IReadOnlyCollection<string> warnings,
        Localizer localizer) => new(
            localizer.ModeName(mode),
            warnings.Count == 0 ? localizer.ModeDescription(mode) : localizer["PartialMode"],
            mode.AccentColor(),
            warnings.Count != 0);

    public static ModeToastForm ForMaxFan(bool enabled, Localizer localizer) => new(
        localizer[enabled ? "MaxFanOn" : "MaxFanOff"],
        localizer[enabled ? "MaxFanOnDescription" : "MaxFanOffDescription"],
        enabled ? Color.FromArgb(0, 120, 212) : Color.FromArgb(105, 105, 105),
        false);

    private void Animate(object? sender, EventArgs eventArgs)
    {
        switch (_phase)
        {
            case AnimationPhase.Entering:
                {
                    var progress = Progress(EnterDurationMilliseconds);
                    var eased = 1d - Math.Pow(1d - progress, 3d);
                    Opacity = eased;
                    Location = new Point(
                        _targetLocation.X + (int)Math.Round(SlideDistance * (1d - eased)),
                        _targetLocation.Y);
                    if (progress >= 1d)
                    {
                        BeginPhase(AnimationPhase.Holding);
                    }

                    break;
                }
            case AnimationPhase.Holding:
                if (_phaseClock.ElapsedMilliseconds >= HoldDurationMilliseconds)
                {
                    if (_animationsEnabled)
                    {
                        BeginPhase(AnimationPhase.Exiting);
                    }
                    else
                    {
                        Close();
                    }
                }

                break;
            case AnimationPhase.Exiting:
                {
                    var progress = Progress(ExitDurationMilliseconds);
                    var eased = Math.Pow(progress, 3d);
                    Opacity = 1d - eased;
                    Location = new Point(
                        _targetLocation.X + (int)Math.Round(SlideDistance * eased),
                        _targetLocation.Y);
                    if (progress >= 1d)
                    {
                        Close();
                    }

                    break;
                }
        }
    }

    private double Progress(int durationMilliseconds) =>
        Math.Clamp((double)_phaseClock.ElapsedMilliseconds / durationMilliseconds, 0d, 1d);

    private void BeginPhase(AnimationPhase phase)
    {
        _phase = phase;
        _phaseClock.Restart();
        if (phase == AnimationPhase.Holding)
        {
            Opacity = 1d;
            Location = _targetLocation;
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExNoActivate = 0x08000000;
            const int WsExToolWindow = 0x00000080;
            const int CsDropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    private enum AnimationPhase
    {
        Entering,
        Holding,
        Exiting
    }
}
