using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public sealed class AppealPageRequest
{
    public string SearchText { get; init; } = "";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}

public sealed class AppealPageResult
{
    public required List<Appeal> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}

public static class AppealRepository
{
    private const string Columns = """
        id, number, created_at, sender, birth_date, parent_phone,
        group_name, complaints, diagnosis, actions_recommendations
        """;

    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM appeals WHERE deleted_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static AppealPageResult GetPage(AppealPageRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var requestedPage = Math.Max(request.Page, 1);
        var where = new List<string> { "deleted_at IS NULL" };

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();

        var search = request.SearchText.Trim();
        if (search.Length > 0)
        {
            where.Add("(contains_ci(CAST(number AS TEXT), $search) OR contains_ci(sender, $search))");
            cmd.Parameters.AddWithValue("$search", search);
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT COUNT(*) FROM appeals WHERE {whereSql}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, totalPages);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT {Columns} FROM appeals
            WHERE {whereSql}
            ORDER BY number DESC, id DESC
            LIMIT $limit OFFSET $offset
            """;
        using var reader = cmd.ExecuteReader();
        var items = new List<Appeal>(pageSize);
        while (reader.Read())
            items.Add(Map(reader));

        return new AppealPageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    public static Appeal? GetById(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM appeals WHERE id = $id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public static long GetNextNumber()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        // Номер остаётся монотонным, даже если последнее обращение находится в корзине.
        cmd.CommandText = "SELECT MAX(number) FROM appeals";
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? 1 : Convert.ToInt64(result) + 1;
    }

    public static void Insert(Appeal a)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO appeals (
                number, created_at, sender, birth_date, parent_phone,
                group_name, complaints, diagnosis, actions_recommendations
            ) VALUES (
                $number, $createdAt, $sender, $birthDate, $parentPhone,
                $groupName, $complaints, $diagnosis, $actions
            )
            """;
        AddParameters(cmd, a);
        cmd.ExecuteNonQuery();
    }

    public static void Update(Appeal a)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE appeals SET
                number = $number, created_at = $createdAt, sender = $sender,
                birth_date = $birthDate, parent_phone = $parentPhone,
                group_name = $groupName, complaints = $complaints,
                diagnosis = $diagnosis, actions_recommendations = $actions
            WHERE id = $id AND deleted_at IS NULL
            """;
        AddParameters(cmd, a);
        cmd.Parameters.AddWithValue("$id", a.Id);
        cmd.ExecuteNonQuery();
    }

    public static void MoveToTrash(long id) => TrashRepository.MoveToTrash("appeal", id);

    public static List<PersonOption> SearchPersons(string query, int limit = 20)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new List<PersonOption>();

        limit = Math.Clamp(limit, 1, 100);
        var persons = new List<PersonOption>();
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT last_name, first_name, middle_name, birth_date, context, person_type
            FROM (
                SELECT last_name, first_name, middle_name, birth_date,
                       affiliation AS context, 0 AS person_type
                FROM employees
                WHERE deleted_at IS NULL AND archived_at IS NULL
                  AND (contains_ci(last_name || ' ' || first_name || ' ' || middle_name, $query)
                       OR contains_ci(affiliation, $query))
                UNION ALL
                SELECT s.last_name, s.first_name, s.middle_name, s.birth_date,
                       g.name AS context, 1 AS person_type
                FROM students s
                LEFT JOIN groups g ON s.group_id = g.id
                WHERE s.deleted_at IS NULL AND s.archived_at IS NULL
                  AND (contains_ci(s.last_name || ' ' || s.first_name || ' ' || s.middle_name, $query)
                       OR contains_ci(g.name, $query))
            )
            ORDER BY last_name, first_name, middle_name
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", query);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var fio = string.Join(' ', new[]
            {
                reader.GetString(0), reader.GetString(1), reader.GetString(2)
            }.Where(p => !string.IsNullOrWhiteSpace(p)));
            var context = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var isStudent = reader.GetInt64(5) == 1;
            persons.Add(new PersonOption
            {
                Display = isStudent
                    ? string.IsNullOrWhiteSpace(context) ? $"{fio} (Студент)" : $"{fio} (Группа {context})"
                    : string.IsNullOrWhiteSpace(context) ? $"{fio} (Сотрудник)" : $"{fio} (Сотрудник, {context})",
                BirthDate = reader.GetString(3),
                GroupName = context,
            });
        }
        return persons;
    }

    private static Appeal Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Number = r.GetInt64(1),
        CreatedAt = r.GetString(2),
        Sender = r.GetString(3),
        BirthDate = r.GetString(4),
        ParentPhone = r.GetString(5),
        GroupName = r.GetString(6),
        Complaints = r.GetString(7),
        Diagnosis = r.GetString(8),
        ActionsRecommendations = r.GetString(9),
    };

    private static void AddParameters(Microsoft.Data.Sqlite.SqliteCommand cmd, Appeal a)
    {
        cmd.Parameters.AddWithValue("$number", a.Number);
        cmd.Parameters.AddWithValue("$createdAt", a.CreatedAt);
        cmd.Parameters.AddWithValue("$sender", a.Sender);
        cmd.Parameters.AddWithValue("$birthDate", a.BirthDate);
        cmd.Parameters.AddWithValue("$parentPhone", a.ParentPhone);
        cmd.Parameters.AddWithValue("$groupName", a.GroupName);
        cmd.Parameters.AddWithValue("$complaints", a.Complaints);
        cmd.Parameters.AddWithValue("$diagnosis", a.Diagnosis);
        cmd.Parameters.AddWithValue("$actions", a.ActionsRecommendations);
    }
}
