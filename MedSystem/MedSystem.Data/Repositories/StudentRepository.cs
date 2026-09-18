using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public sealed class StudentPageRequest
{
    public string SearchText { get; init; } = "";
    public long GroupId { get; init; }
    public string GroupSearchText { get; init; } = "";
    public int StatusFilter { get; init; }
    public bool Archived { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}

public sealed class StudentPageResult
{
    public required List<Student> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}

public static class StudentRepository
{
    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM students WHERE deleted_at IS NULL AND archived_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static StudentPageResult GetPage(StudentPageRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var requestedPage = Math.Max(request.Page, 1);
        var where = new List<string>
        {
            "s.deleted_at IS NULL",
            request.Archived ? "s.archived_at IS NOT NULL" : "s.archived_at IS NULL",
        };

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();

        var search = request.SearchText.Trim();
        if (search.Length > 0)
        {
            where.Add("(contains_ci(s.last_name || ' ' || s.first_name || ' ' || s.middle_name, $search) OR contains_ci(s.oms, $search))");
            cmd.Parameters.AddWithValue("$search", search);
        }

        if (request.GroupId > 0)
        {
            where.Add("s.group_id = $groupId");
            cmd.Parameters.AddWithValue("$groupId", request.GroupId);
        }
        else if (request.GroupId == -1)
        {
            where.Add("s.group_id IS NULL");
        }
        else
        {
            var groupSearch = request.GroupSearchText.Trim();
            if (groupSearch.Length > 0)
            {
                where.Add("contains_ci(g.name, $groupSearch)");
                cmd.Parameters.AddWithValue("$groupSearch", groupSearch);
            }
        }

        if (request.StatusFilter is 1 or 2)
        {
            var statusFlag = request.StatusFilter == 1 ? 1 : 2;
            where.Add("(person_status(s.sanminimum_date, s.medical_exam_date, s.fluorography_date) & $statusFlag) <> 0");
            cmd.Parameters.AddWithValue("$statusFlag", statusFlag);
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT COUNT(*) FROM students s LEFT JOIN groups g ON s.group_id = g.id WHERE {whereSql}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, totalPages);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT s.id, s.group_id, g.name, s.last_name, s.first_name, s.middle_name,
                   s.birth_date, s.oms, s.address,
                   s.sanminimum_date, s.medical_exam_date, s.fluorography_date,
                   s.health_group, s.archive_reason
            FROM students s
            LEFT JOIN groups g ON s.group_id = g.id
            WHERE {whereSql}
            ORDER BY s.last_name, s.first_name, s.middle_name, s.id
            LIMIT $limit OFFSET $offset
            """;

        using var reader = cmd.ExecuteReader();
        var items = new List<Student>(pageSize);
        while (reader.Read())
            items.Add(ReadStudent(reader));

        return new StudentPageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    public static Student? GetById(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.group_id, g.name, s.last_name, s.first_name, s.middle_name,
                   s.birth_date, s.oms, s.address,
                   s.sanminimum_date, s.medical_exam_date, s.fluorography_date,
                   s.health_group, s.archive_reason
            FROM students s
            LEFT JOIN groups g ON s.group_id = g.id
            WHERE s.id = $id AND s.deleted_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return ReadStudent(reader);
    }

    public static List<string> FindDuplicates(Student student)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT last_name, first_name, middle_name, birth_date, oms,
                   archived_at, deleted_at
            FROM students
            WHERE id <> $id
              AND (
                  ($oms <> '' AND equals_ci(oms, $oms))
                  OR ($birthDate <> '' AND birth_date = $birthDate
                      AND equals_ci(last_name, $lastName)
                      AND equals_ci(first_name, $firstName)
                      AND equals_ci(middle_name, $middleName))
              )
            ORDER BY deleted_at IS NOT NULL, archived_at IS NOT NULL, last_name, first_name
            LIMIT 10
            """;
        cmd.Parameters.AddWithValue("$id", student.Id);
        cmd.Parameters.AddWithValue("$oms", student.Oms);
        cmd.Parameters.AddWithValue("$birthDate", student.BirthDate);
        cmd.Parameters.AddWithValue("$lastName", student.LastName);
        cmd.Parameters.AddWithValue("$firstName", student.FirstName);
        cmd.Parameters.AddWithValue("$middleName", student.MiddleName);

        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            var reasons = new List<string>(2);
            if (!string.IsNullOrEmpty(student.Oms)
                && string.Equals(reader.GetString(4), student.Oms, StringComparison.OrdinalIgnoreCase))
                reasons.Add("совпадает ОМС");
            if (!string.IsNullOrEmpty(student.BirthDate)
                && reader.GetString(3) == student.BirthDate
                && string.Equals(reader.GetString(0), student.LastName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(1), student.FirstName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(2), student.MiddleName, StringComparison.OrdinalIgnoreCase))
                reasons.Add("совпадают ФИО и дата рождения");

            var fullName = string.Join(' ', new[]
            {
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var status = !reader.IsDBNull(6)
                ? "в корзине"
                : !reader.IsDBNull(5) ? "в архиве" : "активная запись";
            result.Add($"{fullName}, {reader.GetString(3)} — {string.Join(", ", reasons)} ({status})");
        }
        return result;
    }

    public static void Insert(Student s)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO students (
                group_id, last_name, first_name, middle_name, birth_date,
                oms, address, sanminimum_date, medical_exam_date, fluorography_date,
                health_group
            ) VALUES (
                $groupId, $lastName, $firstName, $middleName, $birthDate,
                $oms, $address, $sanminimumDate, $medicalExamDate, $fluorographyDate,
                $healthGroup
            )
            """;
        AddParameters(cmd, s);
        cmd.ExecuteNonQuery();
    }

    public static void Update(Student s)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE students SET
                group_id = $groupId, last_name = $lastName, first_name = $firstName,
                middle_name = $middleName, birth_date = $birthDate, oms = $oms,
                address = $address, sanminimum_date = $sanminimumDate,
                medical_exam_date = $medicalExamDate, fluorography_date = $fluorographyDate,
                health_group = $healthGroup
            WHERE id = $id AND deleted_at IS NULL
            """;
        AddParameters(cmd, s);
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.ExecuteNonQuery();
    }

    public static void MoveToTrash(long id) => TrashRepository.MoveToTrash("student", id);

    public static void Archive(long id, string reason)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE students
            SET archived_at = $archivedAt, archive_reason = $reason
            WHERE id = $id AND deleted_at IS NULL AND archived_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$archivedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestoreFromArchive(long id)
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        using (var group = conn.CreateCommand())
        {
            group.CommandText = """
                UPDATE groups SET archived_at = NULL, archive_reason = NULL
                WHERE id = (SELECT group_id FROM students WHERE id = $id)
                  AND deleted_at IS NULL AND archived_at IS NOT NULL
                """;
            group.Parameters.AddWithValue("$id", id);
            group.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE students
            SET archived_at = NULL, archive_reason = NULL
            WHERE id = $id AND deleted_at IS NULL AND archived_at IS NOT NULL
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static void AddParameters(Microsoft.Data.Sqlite.SqliteCommand cmd, Student s)
    {
        cmd.Parameters.AddWithValue("$groupId", s.GroupId > 0 ? s.GroupId : DBNull.Value);
        cmd.Parameters.AddWithValue("$lastName", s.LastName);
        cmd.Parameters.AddWithValue("$firstName", s.FirstName);
        cmd.Parameters.AddWithValue("$middleName", s.MiddleName);
        cmd.Parameters.AddWithValue("$birthDate", s.BirthDate);
        cmd.Parameters.AddWithValue("$oms", s.Oms);
        cmd.Parameters.AddWithValue("$address", s.Address);
        cmd.Parameters.AddWithValue("$sanminimumDate", s.SanminimumDate);
        cmd.Parameters.AddWithValue("$medicalExamDate", s.MedicalExamDate);
        cmd.Parameters.AddWithValue("$fluorographyDate", s.FluorographyDate);
        cmd.Parameters.AddWithValue("$healthGroup", s.HealthGroup);
    }

    private static Student ReadStudent(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        GroupId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
        GroupName = reader.IsDBNull(2) ? "" : reader.GetString(2),
        LastName = reader.GetString(3),
        FirstName = reader.GetString(4),
        MiddleName = reader.GetString(5),
        BirthDate = reader.GetString(6),
        Oms = reader.GetString(7),
        Address = reader.GetString(8),
        SanminimumDate = reader.GetString(9),
        MedicalExamDate = reader.GetString(10),
        FluorographyDate = reader.GetString(11),
        HealthGroup = reader.GetString(12),
        ArchiveReason = reader.IsDBNull(13) ? "" : reader.GetString(13),
    };
}
