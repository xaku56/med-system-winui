using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public static class MedicineRepository
{
    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM medicines WHERE deleted_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static List<Medicine> GetAll()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, dosage, quantity, expiration_date FROM medicines WHERE deleted_at IS NULL ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var result = new List<Medicine>();
        while (reader.Read())
            result.Add(Map(reader));
        return result;
    }

    public static Medicine? GetById(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, dosage, quantity, expiration_date FROM medicines WHERE id = $id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public static void Insert(Medicine m)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO medicines (name, dosage, quantity, expiration_date)
            VALUES ($name, $dosage, $quantity, $expirationDate)
            """;
        AddParameters(cmd, m);
        cmd.ExecuteNonQuery();
    }

    public static void Update(Medicine m)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE medicines
            SET name = $name, dosage = $dosage, quantity = $quantity, expiration_date = $expirationDate
            WHERE id = $id AND deleted_at IS NULL
            """;
        AddParameters(cmd, m);
        cmd.Parameters.AddWithValue("$id", m.Id);
        cmd.ExecuteNonQuery();
    }

    public static void MoveToTrash(long id) => TrashRepository.MoveToTrash("medicine", id);

    /// <summary>
    /// Заказ партий: списывает старые и добавляет новые ОДНОЙ транзакцией.
    /// При ошибке база остаётся в исходном состоянии.
    /// </summary>
    public static void Reorder(IEnumerable<(long OldId, Medicine NewMedicine)> items)
    {
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var (oldId, m) in items)
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM medicines WHERE id = $id";
                del.Parameters.AddWithValue("$id", oldId);
                del.ExecuteNonQuery();
            }
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = """
                    INSERT INTO medicines (name, dosage, quantity, expiration_date)
                    VALUES ($name, $dosage, $quantity, $expirationDate)
                    """;
                AddParameters(ins, m);
                ins.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private static Medicine Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        Dosage = r.GetString(2),
        Quantity = r.GetInt64(3),
        ExpirationDate = r.GetString(4),
    };

    private static void AddParameters(Microsoft.Data.Sqlite.SqliteCommand cmd, Medicine m)
    {
        cmd.Parameters.AddWithValue("$name", m.Name);
        cmd.Parameters.AddWithValue("$dosage", m.Dosage);
        cmd.Parameters.AddWithValue("$quantity", m.Quantity);
        cmd.Parameters.AddWithValue("$expirationDate", m.ExpirationDate);
    }
}
