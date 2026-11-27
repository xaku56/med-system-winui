using System.ComponentModel;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using Microsoft.Data.Sqlite;

namespace MedSystem.Data.Repositories;

public sealed class StudentImportRow(Student student, int number) : INotifyPropertyChanged
{
    public Student Student { get; } = student;
    public int Number { get; } = number;
    public string Title => string.IsNullOrWhiteSpace(Student.GroupName)
        ? $"{Number}. {Student.FullName}"
        : $"{Number}. {Student.FullName} · {Student.GroupName}";
    public bool Included { get; internal set; }
    public string Status { get; internal set; } = "";
    public string Error { get; internal set; } = "";
    internal bool ExcludedByUser { get; set; }
    internal string? Consent { get; set; }
    internal string? Match { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}

public sealed class StudentImport
{
    private sealed record Identity(string Name, string Group, string Birth, string Oms)
    {
        public static Identity Of(Student student, string group) => new(
            Key(student.FullName), Key(group), Key(student.BirthDate), Key(student.Oms));
        private static string Key(string value) => StudentDocxReader.Normalize(value).ToUpperInvariant();
    }

    private sealed record Match(string Id, string Description);

    // Names in a group are useful even when a roster has neither DOB nor OMS.
    // The other two keys also catch matches across groups, including the archive and trash.
    private sealed class MatchIndex
    {
        private readonly Dictionary<(string Name, string Group), List<Match>> _byGroup = new();
        private readonly Dictionary<(string Name, string Birth), List<Match>> _byBirth = new();
        private readonly Dictionary<string, List<Match>> _byOms = new();

        public void Add(Identity person, Match match, bool matchGroup = true)
        {
            if (matchGroup && person.Name.Length > 0) Add(_byGroup, (person.Name, person.Group), match);
            if (person.Name.Length > 0 && person.Birth.Length > 0) Add(_byBirth, (person.Name, person.Birth), match);
            if (person.Oms.Length > 0) Add(_byOms, person.Oms, match);
        }

        public IEnumerable<Match> Find(Identity person) =>
            (_byGroup.GetValueOrDefault((person.Name, person.Group)) ?? [])
            .Concat(_byBirth.GetValueOrDefault((person.Name, person.Birth)) ?? [])
            .Concat(_byOms.GetValueOrDefault(person.Oms) ?? []);

        private static void Add<TKey>(Dictionary<TKey, List<Match>> index, TKey key, Match match) where TKey : notnull
        {
            if (!index.TryGetValue(key, out var matches))
                index[key] = matches = [];
            matches.Add(match);
        }
    }

    private MatchIndex _existing;
    public IReadOnlyList<StudentImportRow> Rows { get; }
    public string GroupName { get; set; }
    public IReadOnlyList<string> IgnoredColumns { get; }
    public bool CanSave => ValidGroup && Rows.Any(r => r.Included) && Rows.All(r => !r.Included || r.Error.Length == 0);
    private bool ValidGroup => StudentDocxReader.Normalize(GroupName).Length is > 0 and <= 64;

    public StudentImport(StudentRoster roster)
    {
        GroupName = roster.GroupName;
        IgnoredColumns = roster.IgnoredColumns;
        Rows = roster.Students.Select((s, i) => new StudentImportRow(s, i + 1)).ToList();
        using var conn = Db.Open();
        _existing = LoadMatches(conn);
        Refresh();
    }

    public void Include(StudentImportRow row, bool include)
    {
        row.ExcludedByUser = !include;
        row.Consent = include ? row.Match : null;
        Refresh();
    }

    public void Refresh()
    {
        var pending = new MatchIndex();
        foreach (var row in Rows)
        {
            var person = Identity.Of(row.Student, GroupName);
            var matches = _existing.Find(person).Concat(pending.Find(person)).DistinctBy(m => m.Id).ToList();
            // The permission is valid for this identity and this exact set of matching records.
            var signature = matches.Count == 0 ? null : string.Join('\u001f',
                new[] { person.Name, person.Group, person.Birth, person.Oms }
                    .Concat(matches.Select(m => m.Id).Order(StringComparer.Ordinal)));
            if (row.Consent != signature) row.Consent = null;
            row.Match = signature;
            row.Included = !row.ExcludedByUser && (signature == null || row.Consent == signature);
            row.Error = string.Join(" ", Validators.ValidateStudent(row.Student));
            var status = matches.Count > 0 ? "Совпадение: " + string.Join(", ", matches.Select(m => m.Description).Take(3)) : "";
            row.Status = string.Join(" · ", new[] { status, row.Error }.Where(s => s.Length > 0));
            if (row.Included)
                pending.Add(person, new Match($"row-{row.Number}", $"строка {row.Number} в этом файле"));
            row.Changed();
        }
    }

    public int Save()
    {
        var selected = Rows.Where(r => r.Included).ToList();
        using var conn = Db.Open();
        using var transaction = conn.BeginTransaction(deferred: false);
        _existing = LoadMatches(conn);
        Refresh();
        if (!selected.SequenceEqual(Rows.Where(r => r.Included)))
            throw new InvalidOperationException("Список совпадений изменился. Проверьте отмеченные строки и подтвердите импорт ещё раз.");
        if (!CanSave)
            throw new InvalidOperationException("Укажите итоговую группу, отметьте студентов и исправьте ошибки в выбранных строках.");

        var groupId = ResolveGroup(conn, StudentDocxReader.Normalize(GroupName));
        foreach (var row in selected)
        {
            row.Student.GroupId = groupId;
            StudentRepository.Insert(conn, row.Student);
        }
        transaction.Commit();
        return selected.Count;
    }

    private static MatchIndex LoadMatches(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.last_name, s.first_name, s.middle_name, s.birth_date, s.oms,
                   COALESCE(g.name, ''), s.archived_at, s.deleted_at, g.archived_at, g.deleted_at
            FROM students s LEFT JOIN groups g ON g.id = s.group_id
            """;
        using var reader = cmd.ExecuteReader();
        var result = new MatchIndex();
        while (reader.Read())
        {
            var person = new Student
            {
                LastName = reader.GetString(1), FirstName = reader.GetString(2), MiddleName = reader.GetString(3),
                BirthDate = reader.GetString(4), Oms = reader.GetString(5),
            };
            var group = reader.GetString(6);
            var status = !reader.IsDBNull(8) ? "в корзине" : !reader.IsDBNull(7) ? "в архиве" : "в базе";
            var details = new[] { person.FullName, group, person.BirthDate, status };
            result.Add(Identity.Of(person, group), new Match($"db-{reader.GetInt64(0)}",
                string.Join(", ", details.Where(d => d.Length > 0))), matchGroup: reader.IsDBNull(9) && reader.IsDBNull(10));
        }
        return result;
    }

    private static long ResolveGroup(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM groups WHERE archived_at IS NULL AND deleted_at IS NULL ORDER BY id";
        var matches = new List<(long Id, string Name)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var existing = reader.GetString(1);
                if (string.Equals(StudentDocxReader.Normalize(existing), name, StringComparison.OrdinalIgnoreCase))
                    matches.Add((reader.GetInt64(0), existing));
            }
        }
        if (matches.Count > 0)
        {
            var exact = matches.Where(g => g.Name == name).ToList();
            var candidates = exact.Count == 1 ? exact : matches;
            if (candidates.Count != 1)
                throw new InvalidOperationException("Найдено несколько похожих названий групп. Укажите точное название группы.");
            return candidates[0].Id;
        }
        cmd.CommandText = "INSERT INTO groups (name) VALUES ($name) RETURNING id";
        cmd.Parameters.AddWithValue("$name", name);
        return (long)cmd.ExecuteScalar()!;
    }
}
