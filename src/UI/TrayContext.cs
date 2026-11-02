using System.Windows.Forms;
using SmoothScroll.Core;

namespace SmoothScroll.UI;

/// <summary>
/// Контекст приложения: значок в области уведомлений с меню.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly Settings _settings;
    private readonly SmoothScroller _engine;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private SettingsForm? _settingsForm;

    public TrayContext(Settings settings, SmoothScroller engine)
    {
        _settings = settings;
        _engine = engine;

        _enabledItem = new ToolStripMenuItem("Включено") { Checked = settings.Enabled };
        _enabledItem.Click += (_, _) =>
        {
            settings.Enabled = !settings.Enabled;
            _enabledItem.Checked = settings.Enabled;
            settings.Save();
        };

        var settingsItem = new ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => ShowSettings();

        _autoStartItem = new ToolStripMenuItem("Запускать с Windows") { Checked = AutoStart.IsOn() };
        _autoStartItem.Click += (_, _) =>
        {
            bool on = !_autoStartItem.Checked;
            _autoStartItem.Checked = on;
            settings.AutoStart = on;
            AutoStart.Set(on);
            settings.Save();
        };

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _enabledItem,
            settingsItem,
            new ToolStripSeparator(),
            _autoStartItem,
            new ToolStripSeparator(),
            exitItem
        });

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "SmoothScroll — плавная прокрутка",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsForm == null)
        {
            _settingsForm = new SettingsForm(_settings);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.Activate();
        }
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose();
        Application.Exit();
    }
}
