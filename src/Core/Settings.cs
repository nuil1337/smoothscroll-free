using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmoothScroll.Core;

public enum EasingMode
{
    Exponential = 0, // плавная инерция (экспоненциальное затухание)
    Linear = 1,      // равномерная прокрутка
    EaseOut = 2      // мягкое торможение (кубическая кривая)
}

/// <summary>
/// Настройки приложения. Хранятся в %APPDATA%\SmoothScroll\settings.json.
/// </summary>
public sealed class Settings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Во сколько раз больше/меньше прокручивать за один щелчок (1.0 — как в системе).</summary>
    public double StepMultiplier { get; set; } = 1.0;

    /// <summary>Характерная длительность анимации одного щелчка, мс.</summary>
    public int AnimationMs { get; set; } = 350;

    public EasingMode Easing { get; set; } = EasingMode.Exponential;

    /// <summary>Максимальное ускорение при серии быстрых щелчков (1.0 — без ускорения).</summary>
    public double Acceleration { get; set; } = 1.5;

    public bool SmoothHorizontal { get; set; } = true;
    public bool AutoStart { get; set; }

    /// <summary>Имена процессов (без .exe, в нижнем регистре), которым оставляем обычную прокрутку.</summary>
    public List<string> Blacklist { get; set; } = new();

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmoothScroll", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Clamp();
                    return s;
                }
            }
        }
        catch
        {
            // повреждённый файл — просто используем значения по умолчанию
        }
        return new Settings();
    }

    public void Save()
    {
        Clamp();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // невозможность сохранить настройки не должна ронять приложение
        }
    }

    /// <summary>Приводит значения к допустимым диапазонам (защита от правки файла вручную).</summary>
    public void Clamp()
    {
        StepMultiplier = Math.Clamp(StepMultiplier, 0.25, 4.0);
        AnimationMs = Math.Clamp(AnimationMs, 100, 1200);
        Acceleration = Math.Clamp(Acceleration, 1.0, 3.0);
        if (!Enum.IsDefined(Easing)) Easing = EasingMode.Exponential;
        Blacklist = Blacklist
            .Select(NormalizeProcessName)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
    }

    public static string NormalizeProcessName(string? name)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
}
