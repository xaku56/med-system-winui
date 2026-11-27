using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

public static class AppealFeatureChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool value, string scenario)
        {
            if (!value)
                throw new InvalidOperationException(scenario);
            count++;
        }

        Check(ComplaintTemplateRepository.GetAll().Count > 0, "Default complaint templates are available");
        ComplaintTemplateRepository.Save(null, "  Пример жалобы  ");
        Check(ComplaintTemplateRepository.GetAll().Contains("Пример жалобы"), "Template is trimmed and persisted");
        ComplaintTemplateRepository.Save("Пример жалобы", "Новый текст шаблона");
        var templates = ComplaintTemplateRepository.GetAll();
        Check(!templates.Contains("Пример жалобы") && templates.Contains("Новый текст шаблона"), "Template editing");
        Check(ComplaintTemplates.Append("Текст врача", "Новый текст шаблона") == "Текст врача\nНовый текст шаблона"
            && ComplaintTemplates.Append("", "Новый текст шаблона") == "Новый текст шаблона", "Template insertion preserves free text");
        try
        {
            ComplaintTemplates.Append(new string('а', ComplaintTemplates.MaxLength), "Жалоба");
            throw new Exception("Complaint text limit was ignored");
        }
        catch (ArgumentException) { count++; }
        ComplaintTemplateRepository.Delete("Новый текст шаблона");
        Check(!ComplaintTemplateRepository.GetAll().Contains("Новый текст шаблона"), "Template deletion");

        var day = new DateOnly(2002, 1, 17);
        var dateText = day.ToString(ExpirationRules.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
        var firstNumber = AppealRepository.GetNextNumber();
        void Insert(int offset, string time, string date, long? studentId = null) => AppealRepository.Insert(new Appeal
        {
            Number = firstNumber + offset, CreatedAt = date, Sender = "Проверка журнала",
            CreatedTime = time, Complaints = "Жалоба", StudentId = studentId,
        });
        Insert(0, "", dateText);
        Insert(1, "14:30", dateText);
        Insert(2, "08:10", dateText);
        Insert(3, "08:10", dateText);
        Insert(4, "08:20", dateText);
        Insert(5, "12:00", "18.01.2002");
        AppealRepository.MoveToTrash(AppealRepository.GetByDay(day).Single(a => a.Number == firstNumber + 4).Id);
        var rows = AppealRepository.GetByDay(day);
        Check(rows.Select(a => a.Number - firstNumber).SequenceEqual(new long[] { 2, 3, 1, 0 }),
            "Journal date, trash filter, time order, deterministic ties and legacy blank times");
        Check(rows.All(a => a.StudentId == null) && rows[^1].CreatedTime == "", "Unlinked journal records preserved");
        Check(AppealRepository.GetByDay(day.AddDays(-1)).Count == 0, "Empty journal day");
        var appeal = rows[0];
        appeal.Diagnosis = new string('A', 10) + " - " + new string('Б', 255);
        Check(Validators.ValidateAppeal(appeal).Count == 0, "Complete ICD code and name fit the appeal diagnosis field");

        StudentRepository.Insert(new Student { LastName = "Журнальный", FirstName = "Студент" });
        var studentId = StudentRepository.GetPage(new() { SearchText = "Журнальный" }).Items.Single().Id;
        Insert(6, "15:00", dateText, studentId);
        var linked = AppealRepository.GetByDay(day).Single(a => a.Number == firstNumber + 6);
        Check(linked.StudentId == studentId, "Linked journal entry opens the student by id");
        StudentRepository.MoveToTrash(studentId);
        var unavailable = AppealRepository.GetByDay(day).Single(a => a.Id == linked.Id);
        Check(unavailable.StudentId == null && unavailable.Sender == linked.Sender
            && AppealRepository.GetById(linked.Id)?.StudentId == studentId, "Journal hides unavailable link and retains original association");
        return count;
    }
}
