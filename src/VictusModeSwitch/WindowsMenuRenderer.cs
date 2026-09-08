using System.Drawing.Drawing2D;

namespace VictusModeSwitch;

internal static class WindowsMenuTheme
{
    public static void Apply(ContextMenuStrip menu)
    {
        var palette = WindowsTheme.Current;
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Renderer = new WindowsMenuRenderer(palette);
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = item.Enabled ? palette.Text : palette.SecondaryText;
        }
    }
}

internal sealed class WindowsMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly ThemePalette _palette;
    private readonly Color _selection;

    public WindowsMenuRenderer(ThemePalette palette)
        : base(new WindowsMenuColorTable(palette))
    {
        _palette = palette;
        _selection = palette.Dark
            ? Color.FromArgb(58, 58, 58)
            : Color.FromArgb(232, 232, 232);
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(_palette.Surface);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs eventArgs)
    {
        using var brush = new SolidBrush(_palette.Surface);
        eventArgs.Graphics.FillRectangle(brush, eventArgs.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs eventArgs)
    {
        if (!eventArgs.Item.Selected)
        {
            return;
        }

        var bounds = new Rectangle(4, 2, eventArgs.Item.Width - 8, eventArgs.Item.Height - 4);
        using var path = RoundedRectangle(bounds, 4);
        using var brush = new SolidBrush(_selection);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs eventArgs)
    {
        var size = 16;
        var bounds = new Rectangle(
            eventArgs.ImageRectangle.Left,
            eventArgs.Item.ContentRectangle.Top + (eventArgs.Item.ContentRectangle.Height - size) / 2,
            size,
            size);
        using var path = RoundedRectangle(bounds, 3);
        using var brush = new SolidBrush(_palette.Accent);
        using var pen = new Pen(_palette.AccentText, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.FillPath(brush, path);
        eventArgs.Graphics.DrawLines(pen, new[]
        {
            new Point(bounds.Left + 4, bounds.Top + 8),
            new Point(bounds.Left + 7, bounds.Top + 11),
            new Point(bounds.Left + 12, bounds.Top + 5)
        });
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs eventArgs)
    {
        using var pen = new Pen(_palette.Border);
        var y = eventArgs.Item.Height / 2;
        eventArgs.Graphics.DrawLine(pen, 30, y, eventArgs.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs eventArgs)
    {
        using var pen = new Pen(_palette.Border);
        var bounds = new Rectangle(0, 0, eventArgs.ToolStrip.Width - 1, eventArgs.ToolStrip.Height - 1);
        eventArgs.Graphics.DrawRectangle(pen, bounds);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class WindowsMenuColorTable : ProfessionalColorTable
{
    private readonly ThemePalette _palette;
    private readonly Color _selection;

    public WindowsMenuColorTable(ThemePalette palette)
    {
        _palette = palette;
        _selection = palette.Dark
            ? Color.FromArgb(58, 58, 58)
            : Color.FromArgb(232, 232, 232);
        UseSystemColors = false;
    }

    public override Color ToolStripDropDownBackground => _palette.Surface;
    public override Color ImageMarginGradientBegin => _palette.Surface;
    public override Color ImageMarginGradientMiddle => _palette.Surface;
    public override Color ImageMarginGradientEnd => _palette.Surface;
    public override Color MenuBorder => _palette.Border;
    public override Color MenuItemBorder => _selection;
    public override Color MenuItemSelected => _selection;
    public override Color MenuItemSelectedGradientBegin => _selection;
    public override Color MenuItemSelectedGradientEnd => _selection;
    public override Color MenuItemPressedGradientBegin => _selection;
    public override Color MenuItemPressedGradientMiddle => _selection;
    public override Color MenuItemPressedGradientEnd => _selection;
    public override Color CheckBackground => _palette.Accent;
    public override Color CheckSelectedBackground => _palette.Accent;
    public override Color CheckPressedBackground => _palette.Accent;
    public override Color SeparatorDark => _palette.Border;
    public override Color SeparatorLight => _palette.Border;
}
