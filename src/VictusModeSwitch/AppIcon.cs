using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VictusModeSwitch;

internal static class AppIcon
{
    public static Icon Create(Color color, bool maxFanEnabled = false)
    {
        using var bitmap = CreateBitmap(color, maxFanEnabled, 32);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap CreateBitmap(Color color, bool maxFanEnabled = false, int size = 32)
    {
        var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.White, Math.Max(1f, size / 16f));
            var inset = Math.Max(2, size / 10);
            var center = size / 2;
            var points = new[]
            {
                new Point(center, inset),
                new Point(size - inset, center),
                new Point(center, size - inset),
                new Point(inset, center)
            };
            graphics.FillPolygon(brush, points);
            graphics.DrawPolygon(pen, points);
            if (maxFanEnabled)
            {
                var dot = Math.Max(4, size / 4);
                graphics.FillEllipse(Brushes.White, center - dot / 2, center - dot / 2, dot, dot);
            }
        }

        return bitmap;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
