using System.Threading;
using System.Windows.Forms;
using SmoothScroll.Core;
using SmoothScroll.UI;

namespace SmoothScroll;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SmoothScroll.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show(
                "SmoothScroll уже запущен — найдите значок в области уведомлений (возле часов).",
                "SmoothScroll", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var settings = Settings.Load();

        SmoothScroller engine;
        try
        {
            engine = new SmoothScroller(settings);
            engine.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось перехватить прокрутку:\n" + ex.Message,
                "SmoothScroll", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayContext(settings, engine));
        GC.KeepAlive(mutex);
    }
}
