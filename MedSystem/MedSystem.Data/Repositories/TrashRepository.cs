using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MedSystem.Data.Repositories;

public sealed class TrashItem
{
    public required string EntityType { get; init; }
    public required long Id { get; init; }
    public required string TypeName { get; init; }
    public required string DisplayName { get; init; }
    public required DateTime DeletedAtUtc { get; init; }
    public string Key => $"{EntityType}:{Id}";
}

public static class TrashRepository
{
    public const int DefaultRetentionDays = 60;

    private const string TrashUnionSql = """
        SELECT 'student' AS entity_type, id, 'Студент' AS type_name,
               trim(last_name || ' ' || first_name || ' ' || middle_name) AS display_name,
               deleted_at
        FROM students WHERE deleted_at IS NOT NULL
        UNION ALL
        SELECT 'employee', id, 'Сотрудник',
               trim(last_name || ' ' || first_name || ' ' || middle_name), deleted_at
        FROM employees WHERE deleted_at IS NOT NULL
        UNION ALL
        SELECT 'medicine', id, 'Лекарство', name, deleted_at
        FROM medicines WHERE deleted_at IS NOT NULL
        UNION ALL
        SELECT 'appeal', id, 'Обращение', 'Обращение №' || number, deleted_at
        FROM appeals WHERE deleted_at IS NOT NULL
        UNION ALL
        SELECT 'group', id, 'Группа', name, deleted_at
        FROM groups WHERE deleted_at IS NOT NULL
        """;

    public static (List<TrashItem> Items, long TotalCount) GetPage(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        using var conn = Db.Open();
        using var count = conn.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM ({TrashUnionSql})";
        var totalCount = Convert.ToInt64(count.ExecuteScalar());

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT entity_type, id, type_name, display_name, deleted_at
            FROM ({TrashUnionSql})
            ORDER BY deleted_at DESC, entity_type, id
            LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1L) * pageSize);

        var items = new List<TrashItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!DateTime.TryParse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var deletedAt))
            {
                continue;
            }

            items.Add(new TrashItem
            {
                EntityType = reader.GetString(0),
                Id = reader.GetInt64(1),
                TypeName = reader.GetString(2),
                DisplayName = reader.GetString(3),
                DeletedAtUtc = deletedAt.ToUniversalTime(),
            });
        }

        return (items, totalCount);
    }

    public static void MoveToTrash(string entityType, long id)
    {
        var table = ResolveTable(entityType);
        using var conn = Db.Open();

        if (entityType == "group")
        {
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM students WHERE group_id = $id AND deleted_at IS NULL";
            count.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt64(count.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Нельзя переместить в корзину группу, в которой есть студенты.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET deleted_at = $deletedAt WHERE id = $id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void Restore(string entityType, long id)
    {
        var table = ResolveTable(entityType);
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        // Если студент и его группа были удалены по отдельности, сначала
        // возвращаем группу, чтобы активная запись не ссылалась на скрытую.
        if (entityType == "student")
        {
            using var restoreGroup = conn.CreateCommand();
            restoreGroup.CommandText = """
                UPDATE groups SET deleted_at = NULL
                WHERE id = (SELECT group_id FROM students WHERE id = $id)
                  AND deleted_at IS NOT NULL
                """;
            restoreGroup.Parameters.AddWithValue("$id", id);
            restoreGroup.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET deleted_at = NULL WHERE id = $id AND deleted_at IS NOT NULL";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public static void DeletePermanently(string entityType, long id)
    {
        var table = ResolveTable(entityType);
        using var conn = Db.Open();

        if (entityType == "group")
        {
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM students WHERE group_id = $id";
            count.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt64(count.ExecuteScalar()) > 0)
                throw new InvalidOperationException(
                    "Сначала восстановите группу или окончательно удалите связанных с ней студентов.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {table} WHERE id = $id AND deleted_at IS NOT NULL";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static int EmptyTrash()
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        var count = DeleteMatching(conn, "students", "deleted_at IS NOT NULL")
                  + DeleteMatching(conn, "employees", "deleted_at IS NOT NULL")
                  + DeleteMatching(conn, "appeals", "deleted_at IS NOT NULL")
                  + DeleteMatching(conn, "medicines", "deleted_at IS NOT NULL")
                  + DeleteMatching(conn, "groups", "deleted_at IS NOT NULL");
        tx.Commit();
        return count;
    }

    public static int PurgeExpired()
    {
        var retentionDays = GetRetentionDays();
        if (retentionDays == 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O", CultureInfo.InvariantCulture);
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        var condition = "deleted_at IS NOT NULL AND deleted_at < $cutoff";
        var count = DeleteMatching(conn, "students", condition, cutoff)
                  + DeleteMatching(conn, "employees", condition, cutoff)
                  + DeleteMatching(conn, "appeals", condition, cutoff)
                  + DeleteMatching(conn, "medicines", condition, cutoff)
                  + DeleteMatching(conn, "groups", condition, cutoff);
        tx.Commit();
        return count;
    }

    public static int GetRetentionDays()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM system_info WHERE key = 'trash_retention_days'";
        return int.TryParse(cmd.ExecuteScalar()?.ToString(), out var days) && days is >= 0 and <= 365
            ? days
            : DefaultRetentionDays;
    }

    public static void SetRetentionDays(int days)
    {
        if (days is < 0 or > 365)
            throw new ArgumentOutOfRangeException(nameof(days), "Срок хранения должен быть от 1 до 365 дней или 0 без автоочистки.");

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO system_info (key, value) VALUES ('trash_retention_days', $days)";
        cmd.Parameters.AddWithValue("$days", days.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public static (string EntityType, long Id) ParseKey(string key)
    {
        var separator = key.IndexOf(':');
        if (separator <= 0 || !long.TryParse(key[(separator + 1)..], out var id))
            throw new ArgumentException("Некорректный идентификатор записи корзины.", nameof(key));

        var entityType = key[..separator];
        ResolveTable(entityType);
        return (entityType, id);
    }

    private static string ResolveTable(string entityType) => entityType switch
    {
        "student" => "students",
        "employee" => "employees",
        "medicine" => "medicines",
        "appeal" => "appeals",
        "group" => "groups",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType), "Неизвестный тип записи корзины."),
    };

    private static int DeleteMatching(
        SqliteConnection conn,
        string table,
        string condition,
        string? cutoff = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {table} WHERE {condition}";
        if (cutoff != null)
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
        return cmd.ExecuteNonQuery();
    }
}
