using System.Diagnostics;

namespace VictusModeSwitch;

internal sealed class UpdateDialog : Form
{
    private readonly UpdateService _service = new();
    private readonly UpdateInfo _update;
    private readonly Localizer _localizer;
    private readonly Label _title = new();
    private readonly Label _description = new();
    private readonly Label _notesLabel = new();
    private readonly TextBox _notes = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _installButton = new();
    private readonly Button _laterButton = new();
    private readonly CancellationTokenSource _cancellation = new();

    public UpdateDialog(UpdateInfo update, Localizer localizer)
    {
        _update = update;
        _localizer = localizer;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 430);
        Font = WindowsTheme.Font(9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = _localizer["UpdateReadyTitle"];
        Icon = AppIcon.Create(Color.FromArgb(0, 120, 212), false);

        BuildLayout();
        ApplyText();
        ApplyTheme();

        _installButton.Click += Install;
        _laterButton.Click += (_, _) => Close();
        FormClosed += (_, _) =>
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            Icon?.Dispose();
        };
        Shown += (_, _) => WindowsTheme.ApplyWindow(this);
        AcceptButton = _installButton;
        CancelButton = _laterButton;
    }

    public event Action? InstallerStarted;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 20),
            ColumnCount = 1,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _title.Dock = DockStyle.Fill;
        _title.Font = WindowsTheme.Font(20f, FontStyle.Regular);
        _description.Dock = DockStyle.Fill;
        _description.Font = WindowsTheme.Font(9.5f);
        _notesLabel.Dock = DockStyle.Fill;
        _notesLabel.Font = WindowsTheme.Font(9f, FontStyle.Bold);
        _notes.Dock = DockStyle.Fill;
        _notes.Multiline = true;
        _notes.ReadOnly = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        _notes.BorderStyle = BorderStyle.FixedSingle;
        _notes.TabStop = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _progress.Dock = DockStyle.Fill;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0)
        };
        _installButton.Width = 170;
        _laterButton.Width = 92;
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_laterButton);

        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_description, 0, 1);
        root.Controls.Add(_notesLabel, 0, 2);
        root.Controls.Add(_notes, 0, 3);
        root.Controls.Add(_status, 0, 4);
        root.Controls.Add(_progress, 0, 5);
        root.Controls.Add(buttons, 0, 6);
        Controls.Add(root);
    }

    private void ApplyText()
    {
        _title.Text = _localizer["UpdateReadyTitle"];
        _description.Text = _localizer.Format(
            "UpdateReadyDescription",
            $"{_update.Version.Major}.{_update.Version.Minor}.{_update.Version.Build}");
        _notesLabel.Text = _localizer["ReleaseNotes"];
        _notes.Text = string.IsNullOrWhiteSpace(_update.ReleaseNotes)
            ? _update.ReleaseUrl
            : _update.ReleaseNotes.Trim();
        _installButton.Text = _localizer["InstallUpdate"];
        _laterButton.Text = _localizer["Later"];
    }

    private void ApplyTheme()
    {
        var palette = WindowsTheme.Current;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        _title.ForeColor = palette.Text;
        _description.ForeColor = palette.SecondaryText;
        _notesLabel.ForeColor = palette.Text;
        _notes.BackColor = palette.Surface;
        _notes.ForeColor = palette.Text;
        _status.ForeColor = palette.SecondaryText;
        FluentUi.StyleButton(_installButton, palette, primary: true);
        FluentUi.StyleButton(_laterButton, palette);
    }

    private async void Install(object? sender, EventArgs eventArgs)
    {
        _installButton.Enabled = false;
        _laterButton.Enabled = false;
        _progress.Visible = true;
        try
        {
            var progress = new Progress<int>(value =>
            {
                _progress.Value = Math.Clamp(value, 0, 100);
                _status.Text = _localizer.Format("DownloadingUpdate", value);
            });
            var installer = await _service.DownloadVerifiedInstallerAsync(
                _update,
                progress,
                _cancellation.Token);
            _status.Text = _localizer["VerifyingUpdate"];
            UpdateService.QueueInstallerAfterExit(installer);
            InstallerStarted?.Invoke();
            Close();
        }
        catch (OperationCanceledException)
        {
            // Closing the dialog cancels the download.
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось установить обновление", exception);
            _status.Text = _localizer.Format("UpdateFailed", exception.Message);
            _status.ForeColor = Color.FromArgb(196, 43, 28);
            _installButton.Enabled = true;
            _laterButton.Enabled = true;
            _progress.Visible = false;
        }
    }
}
