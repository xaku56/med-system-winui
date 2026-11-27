using Microsoft.Data.Sqlite;

namespace MedSystem.Data;

/// <summary>
/// Создание и обновление схемы БД.
/// </summary>
public static class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 3;

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
            medical_exam_date TEXT NOT NULL,
            fluorography_date TEXT NOT NULL,
            health_group TEXT NOT NULL DEFAULT '',
            dispensary_registered INTEGER NOT NULL DEFAULT 0,
            dispensary_details TEXT NOT NULL DEFAULT '',
            archived_at TEXT,
            archive_reason TEXT,
            deleted_at TEXT,
            FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE RESTRICT
        )
        """;

    public static void Initialize()
    {
        BackupBeforeUpgrade();

        // Для замены родительской таблицы groups используется отдельное соединение.
        using var conn = Db.Open(pooling: false);
        var rebuildGroups = GetSchemaVersion(conn) < 3;
        if (rebuildGroups)
            Execute(conn, "PRAGMA foreign_keys = OFF");
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
                deleted_at TEXT,
                created_time TEXT NOT NULL DEFAULT '',
                student_id INTEGER REFERENCES students(id) ON DELETE SET NULL,
                height_cm INTEGER,
                weight_kg REAL,
                temperature REAL,
                bp_systolic INTEGER,
                bp_diastolic INTEGER,
                pulse INTEGER,
                saturation INTEGER,
                ambulance_called INTEGER NOT NULL DEFAULT 0
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

        // Индексы по deleted_at — только после миграций: в базах схемы 0 этой колонки ещё нет.
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_appeals_active_number ON appeals(deleted_at, number DESC, id DESC)");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_medicines_active_name ON medicines(deleted_at, name, id)");
        SeedIcdCodes(conn);
        if (rebuildGroups)
        {
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check";
            if (Convert.ToInt64(check.ExecuteScalar()) != 0)
                throw new InvalidOperationException("Обновление отменено: в базе нарушены связи между записями.");
        }

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
                    oms, address, medical_exam_date, fluorography_date
                )
                SELECT id, group_id, last_name, first_name, middle_name, birth_date,
                       oms, address, medical_exam_date, fluorography_date
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

        if (version < 2)
        {
            // Санминимум у студентов больше не ведётся: колонку заменяет диспансерный учёт.
            AddColumnIfMissing(conn, "students", "dispensary_registered", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, "students", "dispensary_details", "TEXT NOT NULL DEFAULT ''");
            if (ColumnExists(conn, "students", "sanminimum_date"))
                Execute(conn, "ALTER TABLE students DROP COLUMN sanminimum_date");

            AddColumnIfMissing(conn, "appeals", "created_time", "TEXT NOT NULL DEFAULT ''");
            AddColumnIfMissing(conn, "appeals", "student_id", "INTEGER REFERENCES students(id) ON DELETE SET NULL");
            AddColumnIfMissing(conn, "appeals", "height_cm", "INTEGER");
            AddColumnIfMissing(conn, "appeals", "weight_kg", "REAL");
            AddColumnIfMissing(conn, "appeals", "temperature", "REAL");
            AddColumnIfMissing(conn, "appeals", "bp_systolic", "INTEGER");
            AddColumnIfMissing(conn, "appeals", "bp_diastolic", "INTEGER");
            AddColumnIfMissing(conn, "appeals", "pulse", "INTEGER");
            AddColumnIfMissing(conn, "appeals", "saturation", "INTEGER");
            AddColumnIfMissing(conn, "appeals", "ambulance_called", "INTEGER NOT NULL DEFAULT 0");
            Execute(conn, "CREATE INDEX IF NOT EXISTS idx_appeals_student ON appeals(student_id, deleted_at)");

            LinkExistingAppealsToStudents(conn);
            Execute(conn, "PRAGMA user_version = 2");
        }

        if (version < 3)
        {
            // Старый UNIQUE(name) резервировал название группы даже после выпуска.
            Execute(conn, """
                CREATE TABLE groups_v3 (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL,
                    archived_at TEXT, archive_reason TEXT, deleted_at TEXT
                );
                INSERT INTO groups_v3 (id, name, archived_at, archive_reason, deleted_at)
                    SELECT id, name, archived_at, archive_reason, deleted_at FROM groups;
                UPDATE sqlite_sequence SET seq = MAX(seq, COALESCE(
                    (SELECT seq FROM sqlite_sequence WHERE name = 'groups'), 0))
                    WHERE name = 'groups_v3';
                DROP TABLE groups;
                ALTER TABLE groups_v3 RENAME TO groups;
                CREATE UNIQUE INDEX idx_groups_active_name ON groups(name)
                    WHERE archived_at IS NULL AND deleted_at IS NULL;
                CREATE INDEX idx_groups_deleted_at ON groups(deleted_at);
                """);
            MedicalMigration.Apply(conn);
            AppealMigration.Apply(conn);
            Execute(conn, "PRAGMA user_version = 3");
        }
    }

    /// <summary>
    /// Привязывает старые обращения к студентам, если ФИО (и дата рождения,
    /// когда она указана) совпадает ровно с одним студентом и ни с одним сотрудником.
    /// Неоднозначные обращения остаются без привязки — ФИО в них сохранено текстом.
    /// </summary>
    private static void LinkExistingAppealsToStudents(SqliteConnection conn)
    {
        const string matchingStudents = """
            FROM students s
            WHERE equals_ci(trim(s.last_name || ' ' || s.first_name || ' ' || s.middle_name), trim(a.sender))
              AND (trim(a.birth_date) = '' OR s.birth_date = trim(a.birth_date))
            """;
        Execute(conn, $"""
            UPDATE appeals AS a
            SET student_id = (SELECT s.id {matchingStudents})
            WHERE a.student_id IS NULL
              AND (SELECT COUNT(*) {matchingStudents}) = 1
              AND NOT EXISTS (
                  SELECT 1 FROM employees e
                  WHERE equals_ci(trim(e.last_name || ' ' || e.first_name || ' ' || e.middle_name), trim(a.sender))
              )
            """);
    }

    /// <summary>
    /// Перед обновлением схемы существующей базы сохраняет её копию в Backups.
    /// Ежедневная автокопия могла быть сделана раньше и не содержать последних записей.
    /// </summary>
    private static void BackupBeforeUpgrade()
    {
        if (!File.Exists(Db.DbPath))
            return;

        int version;
        bool hasData;
        using (var conn = Db.Open())
        {
            version = GetSchemaVersion(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'students'";
            hasData = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        if (!hasData || version >= CurrentSchemaVersion)
            return;

        BackupService.CreateUpgradeBackup(Path.Combine(
            BackupService.BackupDirectory,
            $"med_system_before_upgrade_v{version}_to_v{CurrentSchemaVersion}_{DateTime.Now:yyyyMMdd_HHmmss}.db"));
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
