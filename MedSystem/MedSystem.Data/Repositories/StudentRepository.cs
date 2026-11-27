using Microsoft.Data.Sqlite;
using MedSystem.Core;
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
    public int PageSize { get; init; } = Pagination.DefaultPageSize;
}

public static class StudentRepository
{
    private const string SelectColumns = """
        s.id, s.group_id, g.name, s.last_name, s.first_name, s.middle_name,
        s.birth_date, s.oms, s.address,
        s.medical_exam_date, s.fluorography_date,
        s.health_group, s.archive_reason,
        s.dispensary_registered, s.dispensary_details, s.archived_at
        """;

    public static PageResult<Student> GetPage(StudentPageRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, Pagination.MaximumPageSize);
        var where = new List<string>
        {
            "s.deleted_at IS NULL",
            request.Archived ? "s.archived_at IS NOT NULL" : "s.archived_at IS NULL",
        };

        using var conn = Db.Open();
        using var read = conn.BeginTransaction(deferred: true);
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
            where.Add("(person_status(s.medical_exam_date, s.fluorography_date) & $statusFlag) <> 0");
            cmd.Parameters.AddWithValue("$statusFlag", statusFlag);
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT COUNT(*) FROM students s LEFT JOIN groups g ON s.group_id = g.id WHERE {whereSql}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var page = Pagination.ClampPage(request.Page, totalCount, pageSize);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM students s
            LEFT JOIN groups g ON s.group_id = g.id
            WHERE {whereSql}
            ORDER BY s.last_name, s.first_name, s.middle_name, s.id
            LIMIT $limit OFFSET $offset
            """;

        var items = new List<Student>(pageSize);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                items.Add(ReadStudent(reader));
        }
        LoadObservations(conn, items);
        read.Commit();

        return new PageResult<Student>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    public static Student? GetById(long id)
    {
        using var conn = Db.Open();
        using var read = conn.BeginTransaction(deferred: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM students s
            LEFT JOIN groups g ON s.group_id = g.id
            WHERE s.id = $id AND s.deleted_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$id", id);
        Student student;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;
            student = ReadStudent(reader);
        }
        LoadObservations(conn, [student]);
        read.Commit();
        return student;
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
        using var tx = conn.BeginTransaction();
        Insert(conn, s);
        tx.Commit();
    }

    // Групповой импорт использует общую транзакцию для всего файла.
    internal static void Insert(SqliteConnection conn, Student s)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO students (
                group_id, last_name, first_name, middle_name, birth_date,
                oms, address, medical_exam_date, fluorography_date,
                health_group, dispensary_registered, dispensary_details
            ) VALUES (
                $groupId, $lastName, $firstName, $middleName, $birthDate,
                $oms, $address, $medicalExamDate, $fluorographyDate,
                $healthGroup, $dispensaryRegistered, $dispensaryDetails
            )
            """;
        AddParameters(cmd, s);
        cmd.ExecuteNonQuery();
        if (s.IsOnDispensaryRegister && s.Observations.Count > 0)
        {
            cmd.CommandText = "SELECT last_insert_rowid()";
            InsertObservations(conn, Convert.ToInt64(cmd.ExecuteScalar()), s.Observations);
        }
    }

    public static void Update(Student s)
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE students SET
                group_id = $groupId, last_name = $lastName, first_name = $firstName,
                middle_name = $middleName, birth_date = $birthDate, oms = $oms,
                address = $address,
                medical_exam_date = $medicalExamDate, fluorography_date = $fluorographyDate,
                health_group = $healthGroup,
                dispensary_registered = $dispensaryRegistered,
                dispensary_details = $dispensaryDetails
            WHERE id = $id AND deleted_at IS NULL
            """;
        AddParameters(cmd, s);
        cmd.Parameters.AddWithValue("$id", s.Id);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Студент больше не доступен для редактирования.");

        cmd.CommandText = "DELETE FROM student_observations WHERE student_id = $id";
        cmd.ExecuteNonQuery();
        if (s.IsOnDispensaryRegister)
            InsertObservations(conn, s.Id, s.Observations);
        tx.Commit();
    }

    private static void InsertObservations(SqliteConnection conn, long studentId, List<DispensaryObservation> observations)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO student_observations (student_id, specialty, icd_code, diagnosis)
            VALUES ($id, $specialty, $code, $diagnosis)
            """;
        cmd.Parameters.AddWithValue("$id", studentId);
        var specialty = cmd.Parameters.Add("$specialty", SqliteType.Text);
        var code = cmd.Parameters.Add("$code", SqliteType.Text);
        var diagnosis = cmd.Parameters.Add("$diagnosis", SqliteType.Text);
        foreach (var item in observations)
        {
            specialty.Value = item.Specialty;
            code.Value = item.IcdCode;
            diagnosis.Value = item.Diagnosis;
            cmd.ExecuteNonQuery();
        }
    }

    private static void LoadObservations(SqliteConnection conn, List<Student> students)
    {
        var registered = students.Where(s => s.IsOnDispensaryRegister).ToDictionary(s => s.Id);
        if (registered.Count == 0) return;

        using var cmd = conn.CreateCommand();
        var ids = new List<string>(registered.Count);
        foreach (var id in registered.Keys)
        {
            var parameter = $"$id{ids.Count}";
            ids.Add(parameter);
            cmd.Parameters.AddWithValue(parameter, id);
        }
        cmd.CommandText = $"""
            SELECT student_id, specialty, icd_code, diagnosis FROM student_observations
            WHERE student_id IN ({string.Join(", ", ids)}) ORDER BY student_id, id
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            registered[reader.GetInt64(0)].Observations.Add(new DispensaryObservation
            {
                Specialty = reader.GetString(1),
                IcdCode = reader.GetString(2),
                Diagnosis = reader.GetString(3),
            });
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
        cmd.Parameters.AddWithValue("$medicalExamDate", s.MedicalExamDate);
        cmd.Parameters.AddWithValue("$fluorographyDate", s.FluorographyDate);
        cmd.Parameters.AddWithValue("$healthGroup", s.HealthGroup);
        cmd.Parameters.AddWithValue("$dispensaryRegistered", s.IsOnDispensaryRegister ? 1 : 0);
        // Текст «у кого состоит» имеет смысл только при отметке «Да».
        cmd.Parameters.AddWithValue("$dispensaryDetails", s.IsOnDispensaryRegister ? s.DispensaryDetails : "");
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
        MedicalExamDate = reader.GetString(9),
        FluorographyDate = reader.GetString(10),
        HealthGroup = reader.GetString(11),
        ArchiveReason = reader.IsDBNull(12) ? "" : reader.GetString(12),
        IsOnDispensaryRegister = reader.GetInt64(13) != 0,
        DispensaryDetails = reader.GetString(14),
        IsArchived = !reader.IsDBNull(15),
    };
}
