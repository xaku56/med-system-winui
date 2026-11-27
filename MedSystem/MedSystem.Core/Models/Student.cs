namespace MedSystem.Core.Models;

/// <summary>Студент. Даты хранятся строками "дд.мм.гггг" — как в python-версии.</summary>
public class Student
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    /// <summary>Название группы (заполняется при выборке с JOIN).</summary>
    public string GroupName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string Oms { get; set; } = "";
    public string Address { get; set; } = "";
    public string MedicalExamDate { get; set; } = "";
    public string FluorographyDate { get; set; } = "";
    public string HealthGroup { get; set; } = "";
    /// <summary>Состоит ли студент на диспансерном учёте.</summary>
    public bool IsOnDispensaryRegister { get; set; }
    /// <summary>У кого состоит на учёте (свободный текст, необязательно).</summary>
    public string DispensaryDetails { get; set; } = "";
    public List<DispensaryObservation> Observations { get; set; } = new();
    public string ArchiveReason { get; set; } = "";
    public bool IsArchived { get; set; }

    public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();

    public string DispensaryDisplay
    {
        get
        {
            if (!IsOnDispensaryRegister) return "Нет";
            var entries = Observations.Select(item => item.Display);
            if (!string.IsNullOrWhiteSpace(DispensaryDetails))
                entries = entries.Prepend(DispensaryDetails);
            var text = string.Join(Environment.NewLine, entries);
            return text.Length == 0 ? "Да" : $"Да — {text}";
        }
    }
}
