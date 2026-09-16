using Microsoft.Data.Sqlite;

namespace MedSystem.Data;

/// <summary>Создание и восстановление согласованных копий SQLite-базы.</summary>
public static class BackupService
{
    private const int AutomaticBackupLimit = 10;
    private static readonly TimeSpan AutomaticBackupInterval = TimeSpan.FromDays(1);

    public static string BackupDirectory => Path.Combine(
        Path.GetDirectoryName(Db.DbPath) ?? AppContext.BaseDirectory,
        "Backups");

    /// <summary>
    /// Создаёт автоматическую копию, если предыдущая старше суток.
    /// Возвращает путь новой копии либо null, если копия пока не требуется.
    /// </summary>
    public static string? CreateAutomaticBackupIfDue()
    {
        if (!File.Exists(Db.DbPath))
            return null;

        Directory.CreateDirectory(BackupDirectory);
        var latest = Directory.EnumerateFiles(BackupDirectory, "med_system_auto_*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest != null && DateTime.UtcNow - latest.LastWriteTimeUtc < AutomaticBackupInterval)
            return null;

        var destination = Path.Combine(
            BackupDirectory,
            $"med_system_auto_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        CreateBackup(destination);
        DeleteOldAutomaticBackups();
        return destination;
    }

    public static string CreateManualBackup(string destinationPath) =>
        CreateBackup(destinationPath);

    public static string? GetLatestBackupPath()
    {
        if (!Directory.Exists(BackupDirectory))
            return null;

        return Directory.EnumerateFiles(BackupDirectory, "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Проверяет копию, сохраняет текущую базу и заменяет её выбранной копией.
    /// После вызова приложение следует перезапустить.
    /// </summary>
    public static string RestoreBackup(string backupPath)
    {
        ValidateBackup(backupPath);

        Directory.CreateDirectory(BackupDirectory);
        var safetyBackup = Path.Combine(
            BackupDirectory,
            $"med_system_before_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        CreateBackup(safetyBackup);

        using var source = OpenReadOnly(backupPath);
        using var destination = Db.Open();
        source.BackupDatabase(destination);
        return safetyBackup;
    }

    public static void ValidateBackup(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл резервной копии не найден.", path);

        using var conn = OpenReadOnly(path);

        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SQLite сообщает о повреждении выбранной базы данных.");
        }

        string[] requiredTables =
        [
            "employees", "students", "groups", "medicines",
            "appeals", "icd_codes", "system_info",
        ];

        using var tables = conn.CreateCommand();
        tables.CommandText = $"""
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ({string.Join(", ", requiredTables.Select((_, i) => $"$table{i}"))})
            """;
        for (var i = 0; i < requiredTables.Length; i++)
            tables.Parameters.AddWithValue($"$table{i}", requiredTables[i]);

        if (Convert.ToInt32(tables.ExecuteScalar()) != requiredTables.Length)
            throw new InvalidDataException("Выбранный файл не является полной базой MedSystem.");
    }

    private static string CreateBackup(string destinationPath)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        if (string.Equals(Path.GetFullPath(Db.DbPath), fullDestination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя сохранить резервную копию поверх рабочей базы.");

        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidOperationException("Не удалось определить папку резервной копии.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = Db.Open())
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Pooling = false,
            }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            ValidateBackup(temporaryPath);
            File.Move(temporaryPath, fullDestination, overwrite: true);
            return fullDestination;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void DeleteOldAutomaticBackups()
    {
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "med_system_auto_*.db")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(AutomaticBackupLimit))
        {
            file.Delete();
        }
    }
}
