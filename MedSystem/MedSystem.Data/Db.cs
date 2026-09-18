using Microsoft.Data.Sqlite;
using MedSystem.Core;

namespace MedSystem.Data;

/// <summary>
/// Единая точка подключения к базе данных.
/// Использует файл med_system.db и актуальную схему приложения.
/// </summary>
public static class Db
{
    /// <summary>
    /// Путь к файлу базы. По умолчанию — рядом с исполняемым файлом.
    /// Упакованное (MSIX) приложение должно переопределить путь на папку
    /// LocalFolder при старте — см. App.xaml.cs.
    /// </summary>
    public static string DbPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "med_system.db");

    /// <summary>Открывает соединение с включёнными внешними ключами.
    /// Foreign Keys=True в строке подключения надёжнее ручного PRAGMA:
    /// применяется и к соединениям из пула.</summary>
    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath};Foreign Keys=True");
        conn.CreateFunction<string?, string?, bool>(
            "contains_ci",
            (value, query) => !string.IsNullOrEmpty(value)
                && !string.IsNullOrEmpty(query)
                && value.Contains(query, StringComparison.OrdinalIgnoreCase));
        conn.CreateFunction<string?, string?, string?, int>(
            "person_status",
            (sanminimum, medicalExam, fluorography) =>
            {
                var (isExpired, isExpiring) = ExpirationRules.GetPersonStatus(
                    new[] { sanminimum ?? "", medicalExam ?? "", fluorography ?? "" });
                return (isExpired ? 1 : 0) | (isExpiring ? 2 : 0);
            });
        conn.CreateFunction<string?, int>(
            "medicine_status",
            expirationDate =>
            {
                var (isExpired, isExpiring) = ExpirationRules.GetMedicineStatus(
                    expirationDate ?? "");
                return (isExpired ? 1 : 0) | (isExpiring ? 2 : 0);
            });
        conn.Open();
        return conn;
    }
}
