using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public sealed class EmployeePageRequest
{
    public string SearchText { get; init; } = "";
    public int StatusFilter { get; init; }
    public bool Archived { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}

public sealed class EmployeePageResult
{
    public required List<Employee> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}

public static class EmployeeRepository
{
    private const string Columns = """
        id, last_name, first_name, middle_name, birth_date, affiliation,
        passport_series, passport_number, passport_issued_by,
        passport_issue_date, passport_department_code,
        oms, address, sanminimum_date, medical_exam_date, fluorography_date,
        archive_reason
        """;

    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM employees WHERE deleted_at IS NULL AND archived_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static EmployeePageResult GetPage(EmployeePageRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var requestedPage = Math.Max(request.Page, 1);
        var where = new List<string>
        {
            "deleted_at IS NULL",
            request.Archived ? "archived_at IS NOT NULL" : "archived_at IS NULL",
        };

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();

        var search = request.SearchText.Trim();
        if (search.Length > 0)
        {
            where.Add("(contains_ci(last_name || ' ' || first_name || ' ' || middle_name, $search) OR contains_ci(oms, $search))");
            cmd.Parameters.AddWithValue("$search", search);
        }

        if (request.StatusFilter is 1 or 2)
        {
            var statusFlag = request.StatusFilter == 1 ? 1 : 2;
            where.Add("(person_status(sanminimum_date, medical_exam_date, fluorography_date) & $statusFlag) <> 0");
            cmd.Parameters.AddWithValue("$statusFlag", statusFlag);
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT COUNT(*) FROM employees WHERE {whereSql}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, totalPages);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT {Columns} FROM employees
            WHERE {whereSql}
            ORDER BY last_name, first_name, middle_name, id
            LIMIT $limit OFFSET $offset
            """;
        using var reader = cmd.ExecuteReader();
        var items = new List<Employee>(pageSize);
        while (reader.Read())
            items.Add(Map(reader));

        return new EmployeePageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    public static Employee? GetById(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM employees WHERE id = $id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public static List<string> FindDuplicates(Employee employee)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT last_name, first_name, middle_name, birth_date, oms,
                   passport_series, passport_number, archived_at, deleted_at
            FROM employees
            WHERE id <> $id
              AND (
                  ($oms <> '' AND equals_ci(oms, $oms))
                  OR ($passportSeries <> '' AND $passportNumber <> ''
                      AND equals_ci(passport_series, $passportSeries)
                      AND equals_ci(passport_number, $passportNumber))
                  OR ($birthDate <> '' AND birth_date = $birthDate
                      AND equals_ci(last_name, $lastName)
                      AND equals_ci(first_name, $firstName)
                      AND equals_ci(middle_name, $middleName))
              )
            ORDER BY deleted_at IS NOT NULL, archived_at IS NOT NULL, last_name, first_name
            LIMIT 10
            """;
        cmd.Parameters.AddWithValue("$id", employee.Id);
        cmd.Parameters.AddWithValue("$oms", employee.Oms);
        cmd.Parameters.AddWithValue("$passportSeries", employee.PassportSeries);
        cmd.Parameters.AddWithValue("$passportNumber", employee.PassportNumber);
        cmd.Parameters.AddWithValue("$birthDate", employee.BirthDate);
        cmd.Parameters.AddWithValue("$lastName", employee.LastName);
        cmd.Parameters.AddWithValue("$firstName", employee.FirstName);
        cmd.Parameters.AddWithValue("$middleName", employee.MiddleName);

        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            var reasons = new List<string>(3);
            if (!string.IsNullOrEmpty(employee.Oms)
                && string.Equals(reader.GetString(4), employee.Oms, StringComparison.OrdinalIgnoreCase))
                reasons.Add("совпадает ОМС");
            if (!string.IsNullOrEmpty(employee.PassportSeries)
                && !string.IsNullOrEmpty(employee.PassportNumber)
                && string.Equals(reader.GetString(5), employee.PassportSeries, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(6), employee.PassportNumber, StringComparison.OrdinalIgnoreCase))
                reasons.Add("совпадает паспорт");
            if (!string.IsNullOrEmpty(employee.BirthDate)
                && reader.GetString(3) == employee.BirthDate
                && string.Equals(reader.GetString(0), employee.LastName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(1), employee.FirstName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(2), employee.MiddleName, StringComparison.OrdinalIgnoreCase))
                reasons.Add("совпадают ФИО и дата рождения");

            var fullName = string.Join(' ', new[]
            {
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var status = !reader.IsDBNull(8)
                ? "в корзине"
                : !reader.IsDBNull(7) ? "в архиве" : "активная запись";
            result.Add($"{fullName}, {reader.GetString(3)} — {string.Join(", ", reasons)} ({status})");
        }
        return result;
    }

    public static void Insert(Employee e)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO employees (
                last_name, first_name, middle_name, birth_date, affiliation,
                passport_series, passport_number, passport_issued_by,
                passport_issue_date, passport_department_code,
                oms, address, sanminimum_date, medical_exam_date, fluorography_date
            ) VALUES (
                $lastName, $firstName, $middleName, $birthDate, $affiliation,
                $passportSeries, $passportNumber, $passportIssuedBy,
                $passportIssueDate, $passportDepartmentCode,
                $oms, $address, $sanminimumDate, $medicalExamDate, $fluorographyDate
            )
            """;
        AddParameters(cmd, e);
        cmd.ExecuteNonQuery();
    }

    public static void Update(Employee e)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE employees SET
                last_name = $lastName, first_name = $firstName, middle_name = $middleName,
                birth_date = $birthDate, affiliation = $affiliation,
                passport_series = $passportSeries, passport_number = $passportNumber,
                passport_issued_by = $passportIssuedBy, passport_issue_date = $passportIssueDate,
                passport_department_code = $passportDepartmentCode,
                oms = $oms, address = $address, sanminimum_date = $sanminimumDate,
                medical_exam_date = $medicalExamDate, fluorography_date = $fluorographyDate
            WHERE id = $id AND deleted_at IS NULL
            """;
        AddParameters(cmd, e);
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.ExecuteNonQuery();
    }

    public static void MoveToTrash(long id) => TrashRepository.MoveToTrash("employee", id);

    public static void Archive(long id, string reason)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE employees
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE employees
            SET archived_at = NULL, archive_reason = NULL
            WHERE id = $id AND deleted_at IS NULL AND archived_at IS NOT NULL
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static Employee Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        LastName = r.GetString(1),
        FirstName = r.GetString(2),
        MiddleName = r.GetString(3),
        BirthDate = r.GetString(4),
        Affiliation = r.IsDBNull(5) ? "" : r.GetString(5),
        PassportSeries = r.GetString(6),
        PassportNumber = r.GetString(7),
        PassportIssuedBy = r.GetString(8),
        PassportIssueDate = r.GetString(9),
        PassportDepartmentCode = r.GetString(10),
        Oms = r.GetString(11),
        Address = r.GetString(12),
        SanminimumDate = r.GetString(13),
        MedicalExamDate = r.GetString(14),
        FluorographyDate = r.GetString(15),
        ArchiveReason = r.IsDBNull(16) ? "" : r.GetString(16),
    };

    private static void AddParameters(Microsoft.Data.Sqlite.SqliteCommand cmd, Employee e)
    {
        cmd.Parameters.AddWithValue("$lastName", e.LastName);
        cmd.Parameters.AddWithValue("$firstName", e.FirstName);
        cmd.Parameters.AddWithValue("$middleName", e.MiddleName);
        cmd.Parameters.AddWithValue("$birthDate", e.BirthDate);
        cmd.Parameters.AddWithValue("$affiliation", string.IsNullOrWhiteSpace(e.Affiliation) ? DBNull.Value : e.Affiliation);
        cmd.Parameters.AddWithValue("$passportSeries", e.PassportSeries);
        cmd.Parameters.AddWithValue("$passportNumber", e.PassportNumber);
        cmd.Parameters.AddWithValue("$passportIssuedBy", e.PassportIssuedBy);
        cmd.Parameters.AddWithValue("$passportIssueDate", e.PassportIssueDate);
        cmd.Parameters.AddWithValue("$passportDepartmentCode", e.PassportDepartmentCode);
        cmd.Parameters.AddWithValue("$oms", e.Oms);
        cmd.Parameters.AddWithValue("$address", e.Address);
        cmd.Parameters.AddWithValue("$sanminimumDate", e.SanminimumDate);
        cmd.Parameters.AddWithValue("$medicalExamDate", e.MedicalExamDate);
        cmd.Parameters.AddWithValue("$fluorographyDate", e.FluorographyDate);
    }
}
