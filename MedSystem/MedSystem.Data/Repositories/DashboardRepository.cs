namespace MedSystem.Data.Repositories;

public sealed class DashboardSnapshot
{
    public long Employees { get; init; }
    public long Students { get; init; }
    public long Medicines { get; init; }
    public long Appeals { get; init; }
    public long ExpiredEmployees { get; init; }
    public long ExpiringEmployees { get; init; }
    public long ExpiredStudents { get; init; }
    public long ExpiringStudents { get; init; }
    public long LowMedicines { get; init; }
    public long ExpiredMedicines { get; init; }
    public long ExpiringMedicines { get; init; }
    public long Trash { get; init; }
    public DateTime? LatestBackupAt { get; init; }
}

public static class DashboardRepository
{
    public static DashboardSnapshot GetSnapshot(int lowMedicineThreshold)
    {
        using var conn = Db.Open();
        var employees = GetPeopleStats(conn, "employees");
        var students = GetPeopleStats(conn, "students");

        using var medicines = conn.CreateCommand();
        medicines.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(quantity <= $threshold), 0),
                   COALESCE(SUM((medicine_status(expiration_date) & 1) <> 0), 0),
                   COALESCE(SUM((medicine_status(expiration_date) & 2) <> 0), 0)
            FROM medicines
            WHERE deleted_at IS NULL
            """;
        medicines.Parameters.AddWithValue("$threshold", lowMedicineThreshold);
        using var medicineReader = medicines.ExecuteReader();
        medicineReader.Read();
        var medicineCount = medicineReader.GetInt64(0);
        var lowMedicines = medicineReader.GetInt64(1);
        var expiredMedicines = medicineReader.GetInt64(2);
        var expiringMedicines = medicineReader.GetInt64(3);
        medicineReader.Close();

        using var totals = conn.CreateCommand();
        totals.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM appeals WHERE deleted_at IS NULL),
                (SELECT COUNT(*) FROM students WHERE deleted_at IS NOT NULL)
                  + (SELECT COUNT(*) FROM employees WHERE deleted_at IS NOT NULL)
                  + (SELECT COUNT(*) FROM medicines WHERE deleted_at IS NOT NULL)
                  + (SELECT COUNT(*) FROM appeals WHERE deleted_at IS NOT NULL)
                  + (SELECT COUNT(*) FROM groups WHERE deleted_at IS NOT NULL)
            """;
        using var totalsReader = totals.ExecuteReader();
        totalsReader.Read();
        var appealCount = totalsReader.GetInt64(0);
        var trashCount = totalsReader.GetInt64(1);

        var latestBackup = BackupService.GetLatestBackupPath();
        return new DashboardSnapshot
        {
            Employees = employees.Total,
            Students = students.Total,
            Medicines = medicineCount,
            Appeals = appealCount,
            ExpiredEmployees = employees.Expired,
            ExpiringEmployees = employees.Expiring,
            ExpiredStudents = students.Expired,
            ExpiringStudents = students.Expiring,
            LowMedicines = lowMedicines,
            ExpiredMedicines = expiredMedicines,
            ExpiringMedicines = expiringMedicines,
            Trash = trashCount,
            LatestBackupAt = latestBackup == null ? null : File.GetLastWriteTime(latestBackup),
        };
    }

    private static (long Total, long Expired, long Expiring) GetPeopleStats(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*),
                   COALESCE(SUM((person_status(sanminimum_date, medical_exam_date, fluorography_date) & 1) <> 0), 0),
                   COALESCE(SUM((person_status(sanminimum_date, medical_exam_date, fluorography_date) & 2) <> 0), 0)
            FROM {table}
            WHERE deleted_at IS NULL AND archived_at IS NULL
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }
}
