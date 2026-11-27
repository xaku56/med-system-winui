namespace MedSystem.Data.Repositories;

public static class SpecialtyRepository
{
    public static List<string> GetAll()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM medical_specialties ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    public static void Save(string? original, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 255)
            throw new ArgumentException("Введите специальность длиной от 1 до 255 символов.");

        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$original", (object?)original ?? DBNull.Value);
        cmd.CommandText = """
            SELECT 1 FROM medical_specialties
            WHERE equals_ci(name, $name) AND ($original IS NULL OR name <> $original)
            """;
        if (cmd.ExecuteScalar() != null)
            throw new InvalidOperationException("Такая специальность уже есть в каталоге.");

        cmd.CommandText = original == null
            ? "INSERT INTO medical_specialties (name) VALUES ($name)"
            : "UPDATE medical_specialties SET name = $name WHERE name = $original";
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Специальность больше не найдена в каталоге.");
        tx.Commit();
    }

    public static void Delete(string name)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM medical_specialties WHERE name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }
}
