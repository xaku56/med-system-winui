using Microsoft.Data.Sqlite;

namespace MedSystem.Data;

/// <summary>
/// Создание и обновление схемы БД.
/// </summary>
public static class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 1;

    private const string EmployeesTableSql = """
        CREATE TABLE IF NOT EXISTS employees (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            last_name TEXT NOT NULL,
            first_name TEXT NOT NULL,
            middle_name TEXT NOT NULL,
            birth_date TEXT NOT NULL,
            affiliation TEXT CHECK(affiliation IN ('основной', 'внешний')),
            passport_series TEXT NOT NULL,
            passport_number TEXT NOT NULL,
            passport_issued_by TEXT NOT NULL,
            passport_issue_date TEXT NOT NULL,
            passport_department_code TEXT NOT NULL,
            oms TEXT NOT NULL,
            address TEXT NOT NULL,
            sanminimum_date TEXT NOT NULL,
            medical_exam_date TEXT NOT NULL,
            fluorography_date TEXT NOT NULL,
            archived_at TEXT,
            archive_reason TEXT,
            deleted_at TEXT
        )
        """;

    private const string StudentsTableSql = """
        CREATE TABLE IF NOT EXISTS students (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            group_id INTEGER,
            last_name TEXT NOT NULL,
            first_name TEXT NOT NULL,
            middle_name TEXT NOT NULL,
            birth_date TEXT NOT NULL,
            oms TEXT NOT NULL,
            address TEXT NOT NULL,
            sanminimum_date TEXT NOT NULL,
            medical_exam_date TEXT NOT NULL,
            fluorography_date TEXT NOT NULL,
            health_group TEXT NOT NULL DEFAULT '',
            archived_at TEXT,
            archive_reason TEXT,
            deleted_at TEXT,
            FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE RESTRICT
        )
        """;

    public static void Initialize()
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                archived_at TEXT,
                archive_reason TEXT,
                deleted_at TEXT
            )
            """);

        MigrateOptionalFields(conn);

        Execute(conn, EmployeesTableSql);
        Execute(conn, StudentsTableSql);
        if (!ColumnExists(conn, "students", "health_group"))
            Execute(conn, "ALTER TABLE students ADD COLUMN health_group TEXT NOT NULL DEFAULT ''");

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS medicines (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                dosage TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                expiration_date TEXT NOT NULL,
                deleted_at TEXT
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS appeals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                number INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                sender TEXT NOT NULL,
                birth_date TEXT NOT NULL,
                parent_phone TEXT NOT NULL,
                group_name TEXT NOT NULL,
                complaints TEXT NOT NULL,
                diagnosis TEXT NOT NULL,
                actions_recommendations TEXT NOT NULL,
                deleted_at TEXT
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS system_info (
                key TEXT PRIMARY KEY,
                value TEXT
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS icd_codes (
                code TEXT PRIMARY KEY,
                name TEXT NOT NULL
            )
            """);

        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_students_group_id ON students(group_id)");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_appeals_number ON appeals(number)");

        ApplyVersionedMigrations(conn);
        SeedIcdCodes(conn);

        tx.Commit();
    }

    private static void MigrateOptionalFields(SqliteConnection conn)
    {
        if (ColumnIsRequired(conn, "employees", "affiliation"))
        {
            Execute(conn, "ALTER TABLE employees RENAME TO employees_legacy");
            Execute(conn, EmployeesTableSql);
            Execute(conn, """
                INSERT INTO employees (
                    id, last_name, first_name, middle_name, birth_date, affiliation,
                    passport_series, passport_number, passport_issued_by,
                    passport_issue_date, passport_department_code, oms, address,
                    sanminimum_date, medical_exam_date, fluorography_date
                )
                SELECT id, last_name, first_name, middle_name, birth_date, affiliation,
                       passport_series, passport_number, passport_issued_by,
                       passport_issue_date, passport_department_code, oms, address,
                       sanminimum_date, medical_exam_date, fluorography_date
                FROM employees_legacy
                """);
            Execute(conn, "DROP TABLE employees_legacy");
        }

        if (ColumnIsRequired(conn, "students", "group_id"))
        {
            Execute(conn, "ALTER TABLE students RENAME TO students_legacy");
            Execute(conn, StudentsTableSql);
            Execute(conn, """
                INSERT INTO students (
                    id, group_id, last_name, first_name, middle_name, birth_date,
                    oms, address, sanminimum_date, medical_exam_date, fluorography_date
                )
                SELECT id, group_id, last_name, first_name, middle_name, birth_date,
                       oms, address, sanminimum_date, medical_exam_date, fluorography_date
                FROM students_legacy
                """);
            Execute(conn, "DROP TABLE students_legacy");
        }

    }

    private static void ApplyVersionedMigrations(SqliteConnection conn)
    {
        var version = GetSchemaVersion(conn);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"База данных создана более новой версией MedSystem (схема {version}).");

        if (version < 1)
        {
            AddColumnIfMissing(conn, "groups", "archived_at", "TEXT");
            AddColumnIfMissing(conn, "groups", "archive_reason", "TEXT");
            AddColumnIfMissing(conn, "groups", "deleted_at", "TEXT");

            AddColumnIfMissing(conn, "employees", "archived_at", "TEXT");
            AddColumnIfMissing(conn, "employees", "archive_reason", "TEXT");
            AddColumnIfMissing(conn, "employees", "deleted_at", "TEXT");

            AddColumnIfMissing(conn, "students", "archived_at", "TEXT");
            AddColumnIfMissing(conn, "students", "archive_reason", "TEXT");
            AddColumnIfMissing(conn, "students", "deleted_at", "TEXT");

            AddColumnIfMissing(conn, "medicines", "deleted_at", "TEXT");
            AddColumnIfMissing(conn, "appeals", "deleted_at", "TEXT");

            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_groups_deleted_at ON groups(deleted_at)");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_employees_lifecycle_name ON employees(deleted_at, archived_at, last_name, first_name, middle_name, id)");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_students_lifecycle_name ON students(deleted_at, archived_at, last_name, first_name, middle_name, id)");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_students_lifecycle_group ON students(deleted_at, archived_at, group_id)");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_medicines_deleted_at ON medicines(deleted_at)");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_appeals_deleted_at ON appeals(deleted_at)");
            Execute(conn, "INSERT OR IGNORE INTO system_info (key, value) VALUES ('trash_retention_days', '60')");
            Execute(conn, "PRAGMA user_version = 1");
        }
    }

    private static int GetSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void AddColumnIfMissing(
        SqliteConnection conn,
        string tableName,
        string columnName,
        string declaration)
    {
        if (!ColumnExists(conn, tableName, columnName))
            Execute(conn, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration}");
    }

    private static bool ColumnIsRequired(SqliteConnection conn, string tableName, string columnName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{tableName}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == columnName)
                return reader.GetInt64(3) == 1;
        }
        return false;
    }

    private static bool ColumnExists(SqliteConnection conn, string tableName, string columnName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{tableName}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == columnName)
                return true;
        }
        return false;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SeedIcdCodes(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM icd_codes";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count > 0)
            return;

        var codes = new (string Code, string Name)[]
        {
            ("J06.9", "ОРВИ (Острая респираторная вирусная инфекция)"),
            ("J00", "Острый назофарингит (насморк)"),
            ("J11", "Грипп, вирус не идентифицирован"),
            ("J10", "Грипп, вызванный идентифицированным вирусом гриппа"),
            ("J20.9", "Острый бронхит неуточненный"),
            ("J02.9", "Острый фарингит неуточненный"),
            ("J03.9", "Острый тонзиллит неуточненный (ангина)"),
            ("J35.0", "Хронический тонзиллит"),
            ("J30.4", "Аллергический ринит неуточненный"),
            ("S00.0", "Ушиб волосистой части головы"),
            ("S00.1", "Ушиб века и окологлазничной области"),
            ("S60.2", "Ушиб других частей запястья и кисти"),
            ("S90.3", "Ушиб других и неуточненных частей стопы"),
            ("T14.0", "Поверхностная травма неуточненной области (ссадина, царапина)"),
            ("T14.1", "Открытая рана неуточненной области тела"),
            ("S30.0", "Ушиб нижней части спины и таза"),
            ("S80.0", "Ушиб коленного сустава"),
            ("R51", "Головная боль"),
            ("R10.4", "Другие и неуточненные боли в области живота"),
            ("G90.8", "Вегетососудистая дистония (ВСД)"),
            ("K29.9", "Гастродуоденит неуточненный"),
            ("K29.7", "Гастрит неуточненный"),
            ("H52.1", "Миопия (близорукость)"),
            ("M41.9", "Сколиоз неуточненный"),
            ("M40.9", "Кифоз неуточненный"),
            ("L30.9", "Дерматит неуточненный"),
            ("H66.9", "Средний отит неуточненный"),
            ("J45.9", "Астма неуточненная"),
            ("E10.9", "Инсулинозависимый сахарный диабет (без осложнений)"),
            ("T78.4", "Аллергия неуточненная"),
            ("N30.9", "Цистит неуточненный"),
            ("K59.0", "Запор"),
            ("R11", "Тошнота и рвота"),
            ("R50.9", "Лихорадка неуточненная (повышенная температура)"),
        };

        foreach (var (code, name) in codes)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO icd_codes (code, name) VALUES ($code, $name)";
            cmd.Parameters.AddWithValue("$code", code);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.ExecuteNonQuery();
        }
    }
}
