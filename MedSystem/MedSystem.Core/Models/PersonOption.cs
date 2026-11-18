namespace MedSystem.Core.Models;

/// <summary>Вариант выбора отправителя обращения (сотрудник или студент).</summary>
public class PersonOption
{
    public string Display { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string GroupName { get; set; } = "";
    /// <summary>Id студента; 0 — сотрудник (обращения сотрудников не привязываются).</summary>
    public long StudentId { get; set; }

    public override string ToString() => Display;
}
