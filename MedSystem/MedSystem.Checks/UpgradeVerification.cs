using Microsoft.Data.Sqlite;
using MedSystem.Data;

/// <summary>
/// Проверка обновления реальной базы: файл копируется во временную папку,
/// копия обновляется до текущей схемы и сравнивается с исходными данными.
/// Исходный файл открывается только для чтения и не изменяется.
/// Запуск: dotnet run --project MedSystem/MedSystem.Checks -- --verify-upgrade путь\к\med_system.db
/// </summary>
internal static class UpgradeVerification
{
    /// <summary>Колонки, которые обновление удаляет намеренно.</summary>
    private static readonly HashSet<(string Table, string Column)> DroppedColumns =
    [
        ("students", "sanminimum_date"),
    ];

    public static int Run(string sourcePath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Файл не найден: {sourcePath}");
            return 2;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"MedSystem.Upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var copyPath = Path.Combine(directory, "med_system.db");
        var originalDbPath = Db.DbPath;
        var problems = new List<string>();
        try
        {
            using (var source = OpenReadOnly(sourcePath))
            using (var copy = new SqliteConnection($"Data Source={copyPath};Pooling=False"))
            {
                copy.Open();
                source.BackupDatabase(copy);
            }

            var before = Snapshot(copyPath);
            var versionBefore = ReadScalar(copyPath, "PRAGMA user_version");
            Console.WriteLine($"Исходная схема: v{versionBefore}");
            foreach (var (table, rows) in before.Tables)
                Console.WriteLine($"  {table}: {rows.Count} строк");

            Db.DbPath = copyPath;
            DatabaseInitializer.Initialize();
            DatabaseInitializer.Initialize();
            SqliteConnection.ClearAllPools();

            var after = Snapshot(copyPath);
            Console.WriteLine($"Схема после обновления: v{ReadScalar(copyPath, "PRAGMA user_version")}");

            foreach (var (table, beforeRows) in before.Tables)
            {
                if (!after.Tables.TryGetValue(table, out var afterRows))
                {
                    problems.Add($"таблица {table} пропала");
                    continue;
                }
                if (beforeRows.Count != afterRows.Count && table != "icd_codes" && table != "system_info")
                    problems.Add($"{table}: было {beforeRows.Count} строк, стало {afterRows.Count}");

                var changedValues = 0;
                var droppedNonEmpty = new Dictionary<string, int>();
                foreach (var (key, oldRow) in beforeRows)
                {
                    if (!afterRows.TryGetValue(key, out var newRow))
                    {
                        problems.Add($"{table}: пропала строка {key}");
                        continue;
                    }
                    foreach (var (column, oldValue) in oldRow)
                    {
                        if (DroppedColumns.Contains((table, column)))
                        {
                            if (oldValue is not ("n" or "t:"))
                                droppedNonEmpty[column] = droppedNonEmpty.GetValueOrDefault(column) + 1;
                            continue;
                        }
                        if (!newRow.TryGetValue(column, out var newValue))
                            problems.Add($"{table}: пропала колонка {column}");
                        else if (newValue != oldValue)
                            changedValues++;
                    }
                }
                if (changedValues > 0)
                    problems.Add($"{table}: изменилось значений: {changedValues}");
                foreach (var (column, count) in droppedNonEmpty)
                    Console.WriteLine($"  Внимание: {table}.{column} удалена, непустых значений было {count}");
            }

            var integrity = ReadScalar(copyPath, "PRAGMA integrity_check");
            var foreignKeyProblems = ReadScalar(copyPath, "SELECT COUNT(*) FROM pragma_foreign_key_check");
            var linkedAppeals = ReadScalar(copyPath, "SELECT COUNT(*) FROM appeals WHERE student_id IS NOT NULL");
            var totalAppeals = ReadScalar(copyPath, "SELECT COUNT(*) FROM appeals");
            Console.WriteLine($"integrity_check: {integrity}; нарушений внешних ключей: {foreignKeyProblems}");
            Console.WriteLine($"Обращений привязано к студентам: {linkedAppeals} из {totalAppeals}");
            if (integrity != "ok")
                problems.Add($"integrity_check: {integrity}");
            if (foreignKeyProblems != "0")
                problems.Add($"нарушений внешних ключей: {foreignKeyProblems}");

            if (problems.Count == 0)
            {
                Console.WriteLine("Обновление прошло без потери данных.");
                return 0;
            }

            Console.Error.WriteLine("Обнаружены проблемы:");
            foreach (var problem in problems)
                Console.Error.WriteLine($"  - {problem}");
            return 1;
        }
        finally
        {
            Db.DbPath = originalDbPath;
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record DatabaseSnapshot(
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> Tables);

    private static DatabaseSnapshot Snapshot(string path)
    {
        using var conn = OpenReadOnly(path);
        var tableNames = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tableNames.Add(reader.GetString(0));
        }

        var tables = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        foreach (var table in tableNames)
        {
            var rows = new Dictionary<string, Dictionary<string, string>>();
            using var cmd = conn.CreateCommand();
            // Ключ строки — rowid (для icd_codes и system_info — первичный ключ-текст).
            cmd.CommandText = table is "icd_codes" or "system_info"
                ? $"SELECT * FROM \"{table}\""
                : $"SELECT rowid AS __key, * FROM \"{table}\"";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, string>();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = Encode(reader.GetValue(i));
                var key = row.TryGetValue("__key", out var rowId) ? rowId : Encode(reader.GetValue(0));
                row.Remove("__key");
                rows[key] = row;
            }
            tables[table] = rows;
        }
        return new DatabaseSnapshot(tables);
    }

    private static string Encode(object value) => value switch
    {
        DBNull => "n",
        long l => $"i:{l}",
        double d => $"r:{d:R}",
        string s => $"t:{s}",
        byte[] b => $"b:{Convert.ToHexString(b)}",
        _ => $"?:{value}",
    };

    private static string ReadScalar(string path, string sql)
    {
        using var conn = OpenReadOnly(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        conn.Open();
        return conn;
    }
}
