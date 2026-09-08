using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace VictusModeSwitch;

internal sealed class FluentToggle : CheckBox
{
    private ThemePalette _palette = WindowsTheme.Current;

    public FluentToggle()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        Size = new Size(44, 24);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(Parent?.BackColor ?? _palette.Background);
        var track = new Rectangle(1, 2, Width - 2, Height - 4);
        var trackColor = Checked
            ? _palette.Accent
            : _palette.Dark ? Color.FromArgb(77, 77, 77) : Color.FromArgb(137, 137, 137);
        using var path = RoundedRectangle(track, track.Height / 2);
        using var trackBrush = new SolidBrush(trackColor);
        eventArgs.Graphics.FillPath(trackBrush, path);

        var diameter = Height - 8;
        var x = Checked ? Width - diameter - 4 : 4;
        using var knobBrush = new SolidBrush(Checked ? _palette.AccentText : Color.White);
        eventArgs.Graphics.FillEllipse(knobBrush, x, 4, diameter, diameter);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}

internal sealed class NavigationButton : Button
{
    private bool _selected;
    private ThemePalette _palette = WindowsTheme.Current;

    public NavigationButton(string glyph)
    {
        Glyph = glyph;
        AutoSize = false;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 42;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(44, 0, 8, 0);
        Font = WindowsTheme.Font(9.5f);
        UseVisualStyleBackColor = false;
        Resize += (_, _) => WindowsTheme.RoundControl(this, 5);
        Paint += DrawGlyph;
    }

    public string Glyph { get; }

    [DefaultValue(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            ApplyTheme(_palette);
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        ForeColor = palette.Text;
        BackColor = _selected
            ? palette.Dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(229, 229, 229)
            : palette.Background;
        FlatAppearance.MouseOverBackColor = palette.Dark
            ? Color.FromArgb(52, 52, 52)
            : Color.FromArgb(235, 235, 235);
        FlatAppearance.MouseDownBackColor = palette.Dark
            ? Color.FromArgb(65, 65, 65)
            : Color.FromArgb(222, 222, 222);
        Invalidate();
    }

    private void DrawGlyph(object? sender, PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var iconFont = new Font("Segoe Fluent Icons", 12f, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(_palette.Text);
        eventArgs.Graphics.DrawString(Glyph, iconFont, brush, new PointF(15, 11));

        if (!_selected)
        {
            return;
        }

        using var accent = new SolidBrush(_palette.Accent);
        eventArgs.Graphics.FillRectangle(accent, 0, 11, 3, 20);
    }
}

internal static class FluentUi
{
    public static void StyleButton(Button button, ThemePalette palette, bool primary = false)
    {
        button.AutoSize = false;
        button.Height = 34;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Font = WindowsTheme.Font(9f);
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? palette.Accent : palette.Surface;
        button.ForeColor = primary ? palette.AccentText : palette.Text;
        button.FlatAppearance.BorderColor = primary ? palette.Accent : palette.Border;
        button.FlatAppearance.MouseOverBackColor = primary
            ? ControlPaint.Light(palette.Accent, 0.08f)
            : palette.Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(238, 238, 238);
        button.FlatAppearance.MouseDownBackColor = primary
            ? ControlPaint.Dark(palette.Accent, 0.08f)
            : palette.Dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(226, 226, 226);
        WindowsTheme.RoundControl(button, 5);
    }

    public static Panel Separator(Color color) => new()
    {
        BackColor = color,
        Dock = DockStyle.Bottom,
        Height = 1
    };
}
