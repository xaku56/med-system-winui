using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

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

    public static List<Appeal> GetAll()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM appeals WHERE deleted_at IS NULL ORDER BY number DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<Appeal>();
        while (reader.Read())
            result.Add(Map(reader));
        return result;
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

    /// <summary>Сотрудники и студенты одним списком — для выбора
    /// отправителя обращения (перенос fetch_persons_for_combobox).</summary>
    public static List<PersonOption> GetPersonsForPicker()
    {
        var persons = new List<PersonOption>();
        using var conn = Db.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT last_name, first_name, middle_name, birth_date, affiliation
                FROM employees
                WHERE deleted_at IS NULL AND archived_at IS NULL
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fio = string.Join(' ', new[]
                {
                    reader.GetString(0), reader.GetString(1), reader.GetString(2)
                }.Where(p => !string.IsNullOrWhiteSpace(p)));
                var affiliation = reader.IsDBNull(4) ? "" : reader.GetString(4);
                persons.Add(new PersonOption
                {
                    Display = string.IsNullOrWhiteSpace(affiliation)
                        ? $"{fio} (Сотрудник)"
                        : $"{fio} (Сотрудник, {affiliation})",
                    BirthDate = reader.GetString(3),
                    GroupName = affiliation,
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.last_name, s.first_name, s.middle_name, s.birth_date, g.name
                FROM students s LEFT JOIN groups g ON s.group_id = g.id
                WHERE s.deleted_at IS NULL AND s.archived_at IS NULL
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fio = string.Join(' ', new[]
                {
                    reader.GetString(0), reader.GetString(1), reader.GetString(2)
                }.Where(p => !string.IsNullOrWhiteSpace(p)));
                var groupName = reader.IsDBNull(4) ? "" : reader.GetString(4);
                persons.Add(new PersonOption
                {
                    Display = string.IsNullOrWhiteSpace(groupName)
                        ? $"{fio} (Студент)"
                        : $"{fio} (Группа {groupName})",
                    BirthDate = reader.GetString(3),
                    GroupName = groupName,
                });
            }
        }

        return persons.OrderBy(p => p.Display).ToList();
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
