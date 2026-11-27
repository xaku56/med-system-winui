using Microsoft.Data.Sqlite;
using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public static class GroupRepository
{
    /// <summary>Дата перевода групп на следующий курс (месяц, день).</summary>
    public static (int Month, int Day) AcademicYearRollover { get; set; } = (8, 15);

    public static List<Group> GetAll(bool archived = false)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, name, archive_reason FROM groups
            WHERE deleted_at IS NULL
              AND archived_at IS {(archived ? "NOT NULL" : "NULL")}
            ORDER BY name
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<Group>();
        while (reader.Read())
            result.Add(new Group
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                ArchiveReason = reader.IsDBNull(2) ? "" : reader.GetString(2),
            });
        return result;
    }

    /// <summary>Группы со счётчиком студентов одним запросом (без N+1).</summary>
    public static List<(Group Group, long StudentCount)> GetAllWithCounts(bool archived = false)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        var lifecycleCondition = archived ? "IS NOT NULL" : "IS NULL";
        cmd.CommandText = $"""
            SELECT g.id, g.name, g.archive_reason, COUNT(s.id)
            FROM groups g
            LEFT JOIN students s ON s.group_id = g.id
                AND s.deleted_at IS NULL
                AND s.archived_at {lifecycleCondition}
            WHERE g.deleted_at IS NULL AND g.archived_at {lifecycleCondition}
            GROUP BY g.id, g.name, g.archive_reason
            ORDER BY g.name
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<(Group, long)>();
        while (reader.Read())
            result.Add((new Group
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                ArchiveReason = reader.IsDBNull(2) ? "" : reader.GetString(2),
            }, reader.GetInt64(3)));
        return result;
    }

    public static long GetStudentCount(long groupId)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM students WHERE group_id = $id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$id", groupId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Возвращает false, если группа с таким названием уже существует.</summary>
    public static bool Insert(string name)
    {
        try
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO groups (name) VALUES ($name)";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    /// <summary>Возвращает false, если новое название конфликтует с существующим.</summary>
    public static bool Update(long id, string name)
    {
        try
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE groups SET name = $name WHERE id = $id AND deleted_at IS NULL";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public static void MoveToTrash(long id) => TrashRepository.MoveToTrash("group", id);

    public static int ArchiveWithStudents(long id, string reason)
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        var count = ArchiveWithStudents(conn, id, reason);
        tx.Commit();
        return count;
    }

    private static int ArchiveWithStudents(SqliteConnection conn, long id, string reason)
    {
        var archivedAt = DateTime.UtcNow.ToString("O");

        using (var group = conn.CreateCommand())
        {
            group.CommandText = """
                UPDATE groups
                SET archived_at = $archivedAt, archive_reason = $reason
                WHERE id = $id AND deleted_at IS NULL AND archived_at IS NULL
                """;
            group.Parameters.AddWithValue("$archivedAt", archivedAt);
            group.Parameters.AddWithValue("$reason", reason);
            group.Parameters.AddWithValue("$id", id);
            if (group.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("Группа не найдена или уже находится в архиве.");
        }

        int studentCount;
        using (var students = conn.CreateCommand())
        {
            students.CommandText = """
                UPDATE students
                SET archived_at = $archivedAt, archive_reason = $reason
                WHERE group_id = $id AND deleted_at IS NULL AND archived_at IS NULL
                """;
            students.Parameters.AddWithValue("$archivedAt", archivedAt);
            students.Parameters.AddWithValue("$reason", reason);
            students.Parameters.AddWithValue("$id", id);
            studentCount = students.ExecuteNonQuery();
        }

        return studentCount;
    }

    public static int RestoreFromArchive(long id)
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        string? archivedAt;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT archived_at FROM groups WHERE id = $id AND deleted_at IS NULL";
            read.Parameters.AddWithValue("$id", id);
            archivedAt = read.ExecuteScalar() as string;
        }
        if (archivedAt == null)
            throw new InvalidOperationException("Группа не найдена или не находится в архиве.");

        int studentCount;
        using (var students = conn.CreateCommand())
        {
            students.CommandText = """
                UPDATE students
                SET archived_at = NULL, archive_reason = NULL
                WHERE group_id = $id AND deleted_at IS NULL AND archived_at = $archivedAt
                """;
            students.Parameters.AddWithValue("$archivedAt", archivedAt);
            students.Parameters.AddWithValue("$id", id);
            studentCount = students.ExecuteNonQuery();
        }

        using (var group = conn.CreateCommand())
        {
            group.CommandText = """
                UPDATE groups SET archived_at = NULL, archive_reason = NULL
                WHERE id = $id AND deleted_at IS NULL
                """;
            group.Parameters.AddWithValue("$id", id);
            group.ExecuteNonQuery();
        }

        tx.Commit();
        return studentCount;
    }

    public static int IncrementFirstDigitInAllGroups() =>
        AdvanceAcademicYear(DateTime.Today, manual: true);

    public static int CheckAndAutoIncrementGroups() => AdvanceAcademicYear(DateTime.Today);

    internal static int AdvanceAcademicYear(DateTime date, bool manual = false)
    {
        var (month, day) = AcademicYearRollover;
        var beforeRollover = date.Date < new DateTime(date.Year, month, day);
        var year = beforeRollover && !manual ? date.Year - 1 : date.Year;
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT value FROM system_info WHERE key = 'last_group_increment_year'";
        var hasPrevious = int.TryParse(check.ExecuteScalar() as string, out var previous);
        if (hasPrevious ? previous >= year : beforeRollover && !manual)
            return 0;

        int count = 0;
        // За четыре пропущенных перевода выпускаются даже первокурсники.
        var steps = hasPrevious ? Math.Min(4L, (long)year - previous) : 1;
        for (var i = 0; i < steps; i++)
            count += IncrementGroups(conn);

        using var save = conn.CreateCommand();
        save.CommandText = "INSERT OR REPLACE INTO system_info (key, value) VALUES ('last_group_increment_year', $year)";
        save.Parameters.AddWithValue("$year", year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        save.ExecuteNonQuery();
        tx.Commit();
        return count;
    }

    private static int IncrementGroups(SqliteConnection conn)
    {
        var groups = new List<(long Id, string Name)>();
        using (var cmd = conn.CreateCommand())
        {
            // Сначала освобождаем названия старших курсов.
            cmd.CommandText = """
                SELECT id, name FROM groups
                WHERE deleted_at IS NULL AND archived_at IS NULL AND substr(name, 1, 1) IN ('1','2','3','4')
                ORDER BY name DESC
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                groups.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        foreach (var (id, name) in groups)
        {
            if (name[0] == '4')
            {
                ArchiveWithStudents(conn, id, "Выпуск");
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE groups SET name = $name WHERE id = $id";
            cmd.Parameters.AddWithValue("$name", (char)(name[0] + 1) + name[1..]);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        return groups.Count;
    }
}
