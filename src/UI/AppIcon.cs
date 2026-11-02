using System.Drawing;
using System.Drawing.Drawing2D;

namespace SmoothScroll.UI;

/// <summary>
/// Рисует иконку приложения (колесо мыши со стрелками прокрутки) программно — без .ico-файла.
/// </summary>
internal static class AppIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // тёмный круглый фон
        using (var bg = new SolidBrush(Color.FromArgb(32, 40, 56)))
            g.FillEllipse(bg, 1, 1, 30, 30);

        // обод «колеса»
        using (var ring = new Pen(Color.White, 2.4F))
            g.DrawEllipse(ring, 10, 8, 12, 16);

        // спица колеса
        using (var spoke = new Pen(Color.White, 2.0F))
            g.DrawLine(spoke, 16, 11, 16, 21);

        // стрелки прокрутки по бокам: слева вниз, справа вверх
        using var arrow = new Pen(Color.FromArgb(110, 190, 255), 2.4F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };
        g.DrawLine(arrow, 5.5F, 11, 5.5F, 20);
        g.DrawLine(arrow, 26.5F, 21, 26.5F, 12);

        return Icon.FromHandle(bmp.GetHicon());
    }
}
