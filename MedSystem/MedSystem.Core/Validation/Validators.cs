using MedSystem.Core.Models;

namespace MedSystem.Core.Validation;

/// <summary>
/// Валидация форм (перенос правил из python validators.py).
/// Возвращает список сообщений об ошибках; пустой список — всё в порядке.
/// </summary>
public static class Validators
{
    /// <summary>Буквы, дефис и пробел — для двойных фамилий и составных имён.</summary>
    public static bool IsValidName(string value)
    {
        var stripped = value.Replace("-", "").Replace(" ", "");
        return stripped.Length > 0 && stripped.All(char.IsLetter);
    }

    public static bool IsValidDate(string value) =>
        ExpirationRules.TryParseDate(value, out _);

    /// <summary>
    /// Необязательное число из поля формы: пусто → null; принимает и запятую, и точку.
    /// </summary>
    public static bool TryParseOptionalNumber(string? text, out double? value)
    {
        value = null;
        var normalized = (text ?? "").Trim().Replace(',', '.');
        if (normalized.Length == 0)
            return true;
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;
        value = parsed;
        return true;
    }

    /// <summary>Необязательное целое из поля формы: пусто → null.</summary>
    public static bool TryParseOptionalInteger(string? text, out int? value)
    {
        value = null;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return true;
        if (!int.TryParse(trimmed, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;
        value = parsed;
        return true;
    }

    public const string TimeFormat = "HH:mm";

    /// <summary>Время "чч:мм" в 24-часовом формате.</summary>
    public static bool IsValidTime(string value) =>
        TimeOnly.TryParseExact(value, TimeFormat, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    public static List<string> ValidateEmployee(Employee e)
    {
        var errors = new List<string>();

        CheckName(errors, e.LastName, "Фамилия");
        CheckName(errors, e.FirstName, "Имя");
        CheckName(errors, e.MiddleName, "Отчество", required: false);
        CheckDate(errors, e.BirthDate, "Дата рождения", required: false);

        if (!string.IsNullOrWhiteSpace(e.Affiliation) && e.Affiliation is not ("основной" or "внешний"))
            errors.Add("Поле 'Принадлежность' должно быть 'основной' или 'внешний'.");

        CheckDigits(errors, e.PassportSeries, "Паспорт: серия", 4);
        CheckDigits(errors, e.PassportNumber, "Паспорт: номер", 6);
        CheckText(errors, e.PassportIssuedBy, "Паспорт: кем выдан", maxLength: 255, required: false);
        CheckDate(errors, e.PassportIssueDate, "Паспорт: дата выдачи", required: false);
        CheckDigits(errors, e.PassportDepartmentCode, "Паспорт: код подразделения", 6);

        CheckDigits(errors, e.Oms, "ОМС", 16);
        CheckText(errors, e.Address, "Адрес проживания", maxLength: 255, required: false);
        CheckDate(errors, e.SanminimumDate, "Дата санминимума", required: false);
        CheckDate(errors, e.MedicalExamDate, "Дата медосмотра", required: false);
        CheckDate(errors, e.FluorographyDate, "Дата флюорографии", required: false);

        return errors;
    }

    public static List<string> ValidateStudent(Student s)
    {
        var errors = new List<string>();

        CheckName(errors, s.LastName, "Фамилия");
        CheckName(errors, s.FirstName, "Имя");
        CheckName(errors, s.MiddleName, "Отчество", required: false);
        CheckDate(errors, s.BirthDate, "Дата рождения", required: false);

        CheckDigits(errors, s.Oms, "ОМС", 16);
        CheckText(errors, s.Address, "Адрес проживания", maxLength: 255, required: false);
        CheckDate(errors, s.MedicalExamDate, "Дата медосмотра", required: false);
        CheckDate(errors, s.FluorographyDate, "Дата флюорографии", required: false);
        CheckText(errors, s.HealthGroup, "Группа здоровья", maxLength: 100, required: false);
        CheckText(errors, s.DispensaryDetails, "У кого состоит на учёте", maxLength: 255, required: false);

        return errors;
    }

    public static List<string> ValidateMedicine(Medicine m)
    {
        var errors = new List<string>();
        CheckText(errors, m.Name, "Название", maxLength: 255);
        CheckText(errors, m.Dosage, "Дозировка", maxLength: 64);
        if (m.Quantity < 0)
            errors.Add("Поле 'Количество' не может быть отрицательным.");
        CheckDate(errors, m.ExpirationDate, "Срок годности");
        return errors;
    }

    public static List<string> ValidateAppeal(Appeal a)
    {
        var errors = new List<string>();
        if (a.Number <= 0)
            errors.Add("Номер обращения должен быть положительным числом.");
        CheckDate(errors, a.CreatedAt, "Дата обращения");
        if (!string.IsNullOrWhiteSpace(a.CreatedTime) && !IsValidTime(a.CreatedTime))
            errors.Add("Поле 'Время обращения' должно быть в формате ЧЧ:ММ.");
        CheckText(errors, a.Sender, "Отправитель", maxLength: 255);
        CheckDate(errors, a.BirthDate, "Дата рождения", required: false);
        CheckText(errors, a.ParentPhone, "Телефон родителя", maxLength: 20, required: false);
        CheckText(errors, a.GroupName, "Группа / принадлежность", maxLength: 64, required: false);
        CheckText(errors, a.Complaints, "Жалобы", maxLength: 2000);
        CheckRange(errors, a.HeightCm, "Рост, см", 50, 250);
        CheckRange(errors, a.WeightKg, "Вес, кг", 10, 300);
        CheckRange(errors, a.Temperature, "Температура, °C", 34, 43);
        CheckRange(errors, a.BloodPressureSystolic, "Давление: верхнее", 60, 260);
        CheckRange(errors, a.BloodPressureDiastolic, "Давление: нижнее", 30, 160);
        if (a.BloodPressureSystolic is null != a.BloodPressureDiastolic is null)
            errors.Add("Укажите оба значения давления: верхнее и нижнее.");
        else if (a.BloodPressureSystolic <= a.BloodPressureDiastolic)
            errors.Add("Верхнее давление должно быть больше нижнего.");
        CheckRange(errors, a.Pulse, "Пульс, уд/мин", 30, 220);
        CheckRange(errors, a.Saturation, "Сатурация, %", 50, 100);
        CheckText(errors, a.Diagnosis, "Диагноз", maxLength: 255, required: false);
        CheckText(errors, a.ActionsRecommendations, "Действия и рекомендации", maxLength: 2000, required: false);
        return errors;
    }

    // ── Вспомогательные проверки ─────────────────────────────────────

    private static void CheckText(List<string> errors, string value, string label, int maxLength = 0, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors.Add($"Поле '{label}' обязательно.");
        }
        else if (maxLength > 0 && value.Length > maxLength)
            errors.Add($"Поле '{label}' не должно превышать {maxLength} символов.");
    }

    private static void CheckName(List<string> errors, string value, string label, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors.Add($"Поле '{label}' обязательно.");
        }
        else if (value.Length > 50)
            errors.Add($"Поле '{label}' не должно превышать 50 символов.");
        else if (!IsValidName(value))
            errors.Add($"Поле '{label}' должно содержать только буквы (допускаются дефис и пробел).");
    }

    private static void CheckDate(List<string> errors, string value, string label, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors.Add($"Поле '{label}' обязательно.");
        }
        else if (!IsValidDate(value))
            errors.Add($"Поле '{label}' должно быть в формате ДД.ММ.ГГГГ и корректной датой.");
    }

    private static void CheckRange(List<string> errors, double? value, string label, double min, double max)
    {
        if (value is { } v && (double.IsNaN(v) || v < min || v > max))
            errors.Add($"Поле '{label}' должно быть от {min} до {max}.");
    }

    private static void CheckDigits(List<string> errors, string value, string label, int exactLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        else if (value.Length != exactLength || !value.All(char.IsDigit))
            errors.Add($"Поле '{label}' должно содержать ровно {exactLength} цифр.");
    }
}
