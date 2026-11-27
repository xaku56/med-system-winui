using MedSystem.Core;
using Microsoft.Data.Sqlite;

namespace MedSystem.Data.Repositories;

public static class ComplaintTemplateRepository
{
    public static List<string> GetAll()
    {
        using var connection = Db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT text FROM complaint_templates ORDER BY text";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    public static void Save(string? original, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > ComplaintTemplates.MaxLength)
            throw new ArgumentException($"Введите текст шаблона длиной до {ComplaintTemplates.MaxLength} символов.");

        using var connection = Db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = original == null
            ? "INSERT INTO complaint_templates (text) VALUES ($text)"
            : "UPDATE complaint_templates SET text = $text WHERE text = $original";
        command.Parameters.AddWithValue("$text", text);
        if (original != null)
            command.Parameters.AddWithValue("$original", original);
        try
        {
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("Этот шаблон уже изменён или удалён.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Такой шаблон уже существует.", ex);
        }
    }

    public static void Delete(string text)
    {
        using var connection = Db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM complaint_templates WHERE text = $text";
        command.Parameters.AddWithValue("$text", text);
        command.ExecuteNonQuery();
    }
}
