using System.Globalization;

namespace MedSystem.Core.Models;

public class Appeal
{
    public long Id { get; set; }
    public long Number { get; set; }
    public string CreatedAt { get; set; } = "";
    /// <summary>Время обращения "чч:мм"; пусто у записей, созданных до версии 1.2.</summary>
    public string CreatedTime { get; set; } = "";
    /// <summary>Карточка студента, к которой относится обращение; null — не привязано.</summary>
    public long? StudentId { get; set; }
    public string Sender { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string ParentPhone { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Complaints { get; set; } = "";

    public int? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public double? Temperature { get; set; }
    public int? BloodPressureSystolic { get; set; }
    public int? BloodPressureDiastolic { get; set; }
    public int? Pulse { get; set; }
    public int? Saturation { get; set; }
    public bool AmbulanceCalled { get; set; }

    public string Diagnosis { get; set; } = "";
    public string ActionsRecommendations { get; set; } = "";

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>«18.11.2026 14:35» или только дата, если время не указано.</summary>
    public string DateTimeDisplay =>
        string.IsNullOrWhiteSpace(CreatedTime) ? CreatedAt : $"{CreatedAt} {CreatedTime}";

    public string TemperatureDisplay =>
        Temperature is { } t ? $"{t.ToString("0.0", Russian)} °C" : "";

    public string BloodPressureDisplay =>
        BloodPressureSystolic is null && BloodPressureDiastolic is null
            ? ""
            : $"{BloodPressureSystolic?.ToString() ?? "?"}/{BloodPressureDiastolic?.ToString() ?? "?"}";

    /// <summary>Краткая строка показателей для списков: «Рост 170 см · Т 36,6 °C · АД 120/80 …».</summary>
    public string VitalsSummary
    {
        get
        {
            var parts = new List<string>(6);
            if (HeightCm is { } h)
                parts.Add($"Рост {h} см");
            if (WeightKg is { } w)
                parts.Add($"Вес {w.ToString("0.#", Russian)} кг");
            if (Temperature is not null)
                parts.Add($"Т {TemperatureDisplay}");
            if (BloodPressureDisplay.Length > 0)
                parts.Add($"АД {BloodPressureDisplay}");
            if (Pulse is { } p)
                parts.Add($"Пульс {p}");
            if (Saturation is { } s)
                parts.Add($"SpO₂ {s}%");
            return string.Join(" · ", parts);
        }
    }
}
