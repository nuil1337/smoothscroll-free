using System.Drawing;
using System.Windows.Forms;
using SmoothScroll.Core;

namespace SmoothScroll.UI;

/// <summary>
/// Окно настроек. Все изменения применяются сразу и сохраняются автоматически.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly Settings _s;

    public SettingsForm(Settings settings)
    {
        _s = settings;

        Text = "Настройки SmoothScroll";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(450, 540);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14)
        };

        // --- Прокрутка -------------------------------------------------------
        var gScroll = new GroupBox
        {
            Text = "Прокрутка",
            Width = 415,
            Height = 248,
            Margin = new Padding(0, 0, 0, 10)
        };
        var scrollFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 6, 6, 6)
        };

        scrollFlow.Controls.Add(Slider("Шаг за один щелчок", 25, 400,
            (int)(_s.StepMultiplier * 100),
            v => { _s.StepMultiplier = v / 100.0; Save(); return $"{_s.StepMultiplier:0.0#}×"; }));
        scrollFlow.Controls.Add(Slider("Длительность анимации, мс", 100, 1200,
            _s.AnimationMs,
            v => { _s.AnimationMs = v; Save(); return $"{v} мс"; }));
        scrollFlow.Controls.Add(Slider("Ускорение при серии щелчков", 100, 300,
            (int)(_s.Acceleration * 100),
            v => { _s.Acceleration = v / 100.0; Save(); return $"{_s.Acceleration:0.0}×"; }));

        var easingLabel = new Label
        {
            Text = "Характер движения",
            Width = 385,
            Margin = new Padding(0, 8, 0, 2)
        };
        var easingBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 385
        };
        easingBox.Items.AddRange(new object[]
        {
            "Плавная инерция",   // Exponential
            "Равномерная",       // Linear
            "Мягкое торможение"  // EaseOut
        });
        easingBox.SelectedIndex = (int)_s.Easing;
        easingBox.SelectedIndexChanged += (_, _) =>
        {
            _s.Easing = (EasingMode)easingBox.SelectedIndex;
            Save();
        };
        scrollFlow.Controls.Add(easingLabel);
        scrollFlow.Controls.Add(easingBox);
        gScroll.Controls.Add(scrollFlow);

        // --- Прочее ----------------------------------------------------------
        var gOther = new GroupBox
        {
            Text = "Прочее",
            Width = 415,
            Height = 92,
            Margin = new Padding(0, 0, 0, 10)
        };
        var otherFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 6, 6, 6)
        };
        var chkHorizontal = new CheckBox
        {
            Text = "Плавная горизонтальная прокрутка (наклон колеса)",
            Width = 385,
            Checked = _s.SmoothHorizontal
        };
        chkHorizontal.CheckedChanged += (_, _) =>
        {
            _s.SmoothHorizontal = chkHorizontal.Checked;
            Save();
        };
        var chkAutoStart = new CheckBox
        {
            Text = "Запускать при входе в Windows",
            Width = 385,
            Checked = AutoStart.IsOn()
        };
        chkAutoStart.CheckedChanged += (_, _) =>
        {
            _s.AutoStart = chkAutoStart.Checked;
            AutoStart.Set(chkAutoStart.Checked);
            Save();
        };
        otherFlow.Controls.Add(chkHorizontal);
        otherFlow.Controls.Add(chkAutoStart);
        gOther.Controls.Add(otherFlow);

        // --- Исключения ------------------------------------------------------
        var gExclude = new GroupBox
        {
            Text = "Исключения",
            Width = 415,
            Height = 140
        };
        var excludeFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 6, 6, 6)
        };
        excludeFlow.Controls.Add(new Label
        {
            Text = "Процессы с обычной прокруткой (по имени в строке, например игры):",
            Width = 385,
            Margin = new Padding(0)
        });
        var txtBlacklist = new TextBox
        {
            Multiline = true,
            Width = 385,
            Height = 72,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _s.Blacklist)
        };
        txtBlacklist.Leave += (_, _) =>
        {
            _s.Blacklist = txtBlacklist.Lines
                .Select(Settings.NormalizeProcessName)
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();
            Save();
        };
        excludeFlow.Controls.Add(txtBlacklist);
        gExclude.Controls.Add(excludeFlow);

        root.Controls.Add(new Label
        {
            Text = "Изменения применяются сразу и сохраняются автоматически.",
            ForeColor = Color.DimGray,
            Width = 410,
            Margin = new Padding(2, 0, 0, 8)
        });
        root.Controls.Add(gScroll);
        root.Controls.Add(gOther);
        root.Controls.Add(gExclude);
        Controls.Add(root);
    }

    /// <summary>Строка «подпись + слайдер»: возвращает панель и обновляет текст значения.</summary>
    private Control Slider(string title, int min, int max, int value, Func<int, string> apply)
    {
        var panel = new Panel { Width = 385, Height = 58 };

        var lbl = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var val = new Label
        {
            Text = apply(value), // начальное значение без события
            Dock = DockStyle.None,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(300, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(85, 16)
        };
        var track = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickStyle = TickStyle.None,
            LargeChange = Math.Max(1, (max - min) / 10),
            Width = 385,
            Location = new Point(0, 20)
        };
        track.ValueChanged += (_, _) =>
        {
            val.Text = apply(track.Value);
            val.Location = new Point(385 - val.Width, 0);
        };

        panel.Controls.Add(lbl);
        panel.Controls.Add(val);
        panel.Controls.Add(track);
        return panel;
    }

    private void Save() => _s.Save();
}
