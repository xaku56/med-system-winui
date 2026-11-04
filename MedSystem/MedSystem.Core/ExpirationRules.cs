using System.Globalization;

namespace MedSystem.Core;

/// <summary>
/// Бизнес-правила по срокам (перенос logic из python validators.py).
/// Даты хранятся строками "дд.мм.гггг". Сравнение по дням:
/// документ или лекарство со сроком «сегодня» ещё действует.
/// </summary>
public static class ExpirationRules
{
    /// <summary>За сколько дней предупреждать об истечении.</summary>
    public const int WarningDays = 14;

    public const string DateFormat = "dd.MM.yyyy";

    public static bool TryParseDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value ?? "", DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Статус документов человека (санминимум/медосмотр/флюорография).
    /// Каждый документ действует 1 год с даты прохождения.
    /// </summary>
    public static (bool IsExpired, bool IsExpiring) GetPersonStatus(IEnumerable<string> checkupDates)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isExpired = false;
        var isExpiring = false;
        foreach (var value in checkupDates)
        {
            var status = GetSingleCheckupStatus(value, today);
            isExpired |= status.IsExpired;
            isExpiring |= status.IsExpiring;
        }
        return (isExpired, isExpiring);
    }

    /// <summary>Статус одного документа человека (действует 1 год).</summary>
    public static (bool IsExpired, bool IsExpiring) GetSingleCheckupStatus(string checkupDate) =>
        GetSingleCheckupStatus(checkupDate, DateOnly.FromDateTime(DateTime.Now));

    internal static (bool IsExpired, bool IsExpiring) GetSingleCheckupStatus(string checkupDate, DateOnly today) =>
        TryParseDate(checkupDate, out var passed)
            // DateOnly корректно переводит 29 февраля в 28 февраля следующего года.
            ? GetExpirationStatus(passed.AddYears(1), today)
            : (false, false);

    /// <summary>Статус срока годности лекарства.</summary>
    public static (bool IsExpired, bool IsExpiring) GetMedicineStatus(string expirationDate) =>
        TryParseDate(expirationDate, out var expires)
            ? GetExpirationStatus(expires, DateOnly.FromDateTime(DateTime.Now))
            : (false, false);

    internal static (bool IsExpired, bool IsExpiring) GetExpirationStatus(DateOnly expires, DateOnly today) =>
        (expires < today, expires >= today && expires <= today.AddDays(WarningDays));
}
