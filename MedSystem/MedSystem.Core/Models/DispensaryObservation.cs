namespace MedSystem.Core.Models;

/// <summary>Названия сохраняются на дату заполнения, независимо от последующих правок справочников.</summary>
public sealed class DispensaryObservation
{
    public string Specialty { get; set; } = "";
    public string IcdCode { get; set; } = "";
    public string Diagnosis { get; set; } = "";

    public string Display => $"{Specialty} — {IcdCode} {Diagnosis}";
}
