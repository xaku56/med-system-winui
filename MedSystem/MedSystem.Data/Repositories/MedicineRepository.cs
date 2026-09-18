using MedSystem.Core.Models;

namespace MedSystem.Data.Repositories;

public sealed class MedicinePageRequest
{
    public string SearchText { get; init; } = "";
    public int StatusFilter { get; init; }
    public int LowQuantityThreshold { get; init; } = 5;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}

public sealed class MedicinePageResult
{
    public required List<Medicine> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}

public static class MedicineRepository
{
    public static long Count()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM medicines WHERE deleted_at IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static MedicinePageResult GetPage(MedicinePageRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var requestedPage = Math.Max(request.Page, 1);
        var where = new List<string> { "deleted_at IS NULL" };

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();

        var search = request.SearchText.Trim();
        if (search.Length > 0)
        {
            where.Add("contains_ci(name, $search)");
            cmd.Parameters.AddWithValue("$search", search);
        }

        switch (request.StatusFilter)
        {
            case 1:
                where.Add("quantity <= $lowQuantityThreshold");
                cmd.Parameters.AddWithValue("$lowQuantityThreshold", request.LowQuantityThreshold);
                break;
            case 2:
                where.Add("(medicine_status(expiration_date) & 2) <> 0");
                break;
            case 3:
                where.Add("(medicine_status(expiration_date) & 1) <> 0");
                break;
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT COUNT(*) FROM medicines WHERE {whereSql}";
        var totalCount = Convert.ToInt64(cmd.ExecuteScalar());
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, totalPages);

        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        cmd.CommandText = $"""
            SELECT id, name, dosage, quantity, expiration_date
            FROM medicines
            WHERE {whereSql}
            ORDER BY name, id
            LIMIT $limit OFFSET $offset
            """;
        using var reader = cmd.ExecuteReader();
        var items = new List<Medicine>(pageSize);
        while (reader.Read())
            items.Add(Map(reader));

        return new MedicinePageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
        };
    }

    public static bool HasOrderCandidates(int lowQuantityThreshold)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM medicines
                WHERE deleted_at IS NULL
                  AND (quantity <= $threshold OR (medicine_status(expiration_date) & 3) <> 0)
            )
            """;
        cmd.Parameters.AddWithValue("$threshold", lowQuantityThreshold);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    public static List<Medicine> GetOrderCandidates(int lowQuantityThreshold)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, dosage, quantity, expiration_date
            FROM medicines
            WHERE deleted_at IS NULL
              AND (quantity <= $threshold OR (medicine_status(expiration_date) & 3) <> 0)
            ORDER BY name, id
            """;
        cmd.Parameters.AddWithValue("$threshold", lowQuantityThreshold);
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
