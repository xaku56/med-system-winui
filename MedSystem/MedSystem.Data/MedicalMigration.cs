using Microsoft.Data.Sqlite;

namespace MedSystem.Data;

internal static class MedicalMigration
{
    public static void Apply(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS student_observations (
                id INTEGER PRIMARY KEY,
                student_id INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
                specialty TEXT NOT NULL CHECK(trim(specialty) <> ''),
                icd_code TEXT NOT NULL CHECK(trim(icd_code) <> ''),
                diagnosis TEXT NOT NULL CHECK(trim(diagnosis) <> '')
            );
            CREATE INDEX IF NOT EXISTS idx_student_observations_student ON student_observations(student_id);
            CREATE TABLE IF NOT EXISTS medical_specialties (name TEXT PRIMARY KEY NOT NULL);
            INSERT OR IGNORE INTO medical_specialties (name) VALUES
                ('Терапевт'), ('Кардиолог'), ('Эндокринолог'), ('Невролог'), ('Гастроэнтеролог'),
                ('Офтальмолог'), ('Оториноларинголог'), ('Аллерголог-иммунолог'),
                ('Пульмонолог'), ('Нефролог'), ('Травматолог-ортопед');
            """;
        cmd.ExecuteNonQuery();
    }
}
