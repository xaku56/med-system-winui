using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public static class StudentRepository
{
    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM students WHERE deleted_at IS NULL AND archived_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static List<Student> GetAll(bool archived = false)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.id, s.group_id, g.name, s.last_name, s.first_name, s.middle_name,
                   s.birth_date, s.oms, s.address,
                   s.sanminimum_date, s.medical_exam_date, s.fluorography_date,
                   s.health_group, s.archive_reason
            FROM students s
            LEFT JOIN groups g ON s.group_id = g.id
            WHERE s.deleted_at IS NULL
              AND s.archived_at IS {(archived ? "NOT NULL" : "NULL")}
            ORDER BY s.last_name, s.first_name, s.middle_name
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<Student>();
        while (reader.Read())
        {
            result.Add(new Student
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
            });
        }
        return result;
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
        return new Student
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
}
