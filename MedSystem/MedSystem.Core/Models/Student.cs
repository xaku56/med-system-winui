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
    public string SanminimumDate { get; set; } = "";
    public string MedicalExamDate { get; set; } = "";
    public string FluorographyDate { get; set; } = "";
    public string HealthGroup { get; set; } = "";
    public string ArchiveReason { get; set; } = "";

    public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();
}
