using MedSystem.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedSystem.Data.Repositories;

public sealed class IcdPageResult
{
    public required List<IcdCode> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}

public static class IcdRepository
{
    public static List<IcdCode> Search(string query, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<IcdCode>();

        limit = Math.Clamp(limit, 1, 100);
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT code, name FROM icd_codes
            WHERE contains_ci(code, $query) OR contains_ci(name, $query)
            ORDER BY code
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", query.Trim());
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<IcdCode>(limit);
        while (reader.Read())
            result.Add(Map(reader));
        return result;
    }

    public static IcdPageResult GetPage(string searchText, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();

        var query = searchText.Trim();
        var where = "";
        if (query.Length > 0)
        {
            where = "WHERE contains_ci(code, $query) OR contains_ci(name, $query)";
            cmd.Parameters.AddWithValue("$query", query);
        }

        cmd.CommandText = $"SELECT COUNT(*) FROM icd_codes {where}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT code, name FROM icd_codes
            {where}
            ORDER BY code
            LIMIT $limit OFFSET $offset
            """;
        using var reader = cmd.ExecuteReader();
        var items = new List<IcdCode>(pageSize);
        while (reader.Read())
            items.Add(Map(reader));
        return new IcdPageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    /// <summary>Возвращает false, если код уже существует.</summary>
    public static bool Insert(string code, string name)
    {
        try
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO icd_codes (code, name) VALUES ($code, $name)";
            cmd.Parameters.AddWithValue("$code", code);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    /// <summary>Возвращает false, если новый код конфликтует с существующим.</summary>
    public static bool Update(string oldCode, string newCode, string newName)
    {
        try
        {
            using var conn = Db.Open();
            if (oldCode != newCode)
            {
                using var check = conn.CreateCommand();
                check.CommandText = "SELECT 1 FROM icd_codes WHERE code = $code";
                check.Parameters.AddWithValue("$code", newCode);
                if (check.ExecuteScalar() != null)
                    return false;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE icd_codes SET code = $newCode, name = $name WHERE code = $oldCode";
            cmd.Parameters.AddWithValue("$newCode", newCode);
            cmd.Parameters.AddWithValue("$name", newName);
            cmd.Parameters.AddWithValue("$oldCode", oldCode);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public static void Delete(string code)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM icd_codes WHERE code = $code";
        cmd.Parameters.AddWithValue("$code", code);
        cmd.ExecuteNonQuery();
    }

    private static IcdCode Map(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Code = reader.GetString(0),
        Name = reader.GetString(1),
    };
}
