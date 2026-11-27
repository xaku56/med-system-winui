using Microsoft.Data.Sqlite;

namespace MedSystem.Data;

internal static class AppealMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE complaint_templates (text TEXT NOT NULL PRIMARY KEY);
            INSERT INTO complaint_templates (text) VALUES
                ('Головная боль'), ('Боль в животе'), ('Боль в горле'),
                ('Головокружение'), ('Тошнота'), ('Слабость');
            CREATE INDEX idx_appeals_journal ON appeals
                (created_at, (created_time = ''), created_time, number, id) WHERE deleted_at IS NULL;
            """;
        command.ExecuteNonQuery();
    }
}
