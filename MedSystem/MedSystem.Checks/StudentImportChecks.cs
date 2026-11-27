using System.IO.Compression;
using System.Xml.Linq;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Data;
using MedSystem.Data.Repositories;
using Microsoft.Data.Sqlite;

public static class StudentImportChecks
{
    public static void Run()
    {
        var original = Db.DbPath;
        Db.DbPath = Path.Combine(Path.GetDirectoryName(original)!, $"import-{Guid.NewGuid():N}.db");
        try
        {
            DatabaseInitializer.Initialize();
            Verify();
        }
        finally
        {
            Db.DbPath = original;
        }
    }

    private static void Verify()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement P(params string[] runs) => new(w + "p", runs.Select(t => new XElement(w + "r", new XElement(w + "t", t))));
        XElement Cell(params XElement[] paragraphs) => new(w + "tc", paragraphs);
        XElement Row(params XElement[] cells) => new(w + "tr", cells);
        XElement TextRow(params string[] values) => Row(values.Select(v => Cell(P(v))).ToArray());
        var address = new string('я', 280);
        var merged = Cell(P("Груп", "па  ", "52 ТЕСТ"));
        merged.AddFirst(new XElement(w + "tcPr", new XElement(w + "gridSpan", new XAttribute(w + "val", 4))));
        var table = new XElement(w + "tbl",
            Row(merged),
            Row(Cell(P("Место жительства", " студента")), Cell(P("ФИО ", "студента")), Cell(P("Мать (ФИО)")), Cell(P("СНИЛС"))),
            Row(Cell(P("Город"), P(address)), Cell(P("Пример", "ова Ада")), Cell(P("Пропустить")), Cell(P("000"))),
            TextRow("Дом", "Примерова Ада", "", ""),
            TextRow("", "Образцова Ева", "", ""),
            TextRow("", "", "", ""));
        StudentRoster Parse(params XElement[] body)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var part = zip.CreateEntry("word/document.xml").Open())
                new XDocument(new XElement(w + "document", new XElement(w + "body", body))).Save(part);
            stream.Position = 0;
            return StudentDocxReader.Read(stream);
        }
        var roster = Parse(table);
        Check(roster.GroupName == "52 ТЕСТ" && roster.Students.Count == 3, "group heading and variable row count");
        Check(roster.Students[0].Address == "Город " + address && roster.Students[0].LastName == "Примерова",
            "long address, paragraphs, runs and reordered columns");
        Check(roster.IgnoredColumns.SequenceEqual(["Мать (ФИО)", "СНИЛС"]) &&
            roster.Students.All(s => s.Oms == ""), "guardian and unsupported identifiers stay out of student fields");
        var alternate = Parse(P("Список студентов группы: АБ-17"), new XElement(w + "tbl",
            TextRow("Имя", "Дата рождения", "Фамилия", "Полис ОМС", "Группа здоровья"),
            TextRow("Ада", "01.01.2000", "Примерова", "1234567890123456", "Вторая")));
        Check(alternate.GroupName == "АБ-17" && alternate.Students.Single() is
            { FirstName: "Ада", LastName: "Примерова", BirthDate: "01.01.2000", Oms: "1234567890123456", HealthGroup: "Вторая" },
            "separate name columns and optional card data");
        var manual = Parse(new XElement(w + "tbl", TextRow("Ф.И.О."), TextRow("Примерова Ада")));
        Check(manual.GroupName == "" && !new StudentImport(manual).CanSave, "missing group requires preview choice");

        Check(GroupRepository.Insert("52 Тест"), "existing group");
        var group = GroupRepository.GetAll().Single();
        var preview = new StudentImport(roster);
        var first = preview.Rows[0];
        var second = preview.Rows[1];
        var third = preview.Rows[2];
        Check(first.Included && !second.Included && third.Included, "duplicate inside roster");
        preview.Include(first, false);
        Check(!first.Included && second.Included, "exclude first copy and choose second");
        preview.Include(first, true);
        third.Student.FirstName = "!";
        preview.Refresh();
        Check(!preview.CanSave, "editing invalid field updates preview");
        preview.Include(third, false);
        Check(preview.CanSave && preview.Save() == 1, "exclude invalid and batch insert");
        Check(GroupRepository.GetAll().Count == 1 &&
            StudentRepository.GetPage(new() { GroupId = group.Id }).Items.Single().Address == "Город " + address,
            "reuse existing group and preserve entire address");

        var repeat = new StudentImport(Parse(table));
        Check(!repeat.Rows[0].Included && !repeat.Rows[1].Included && repeat.Rows[2].Included,
            "defaults on repeated import");
        repeat.Include(repeat.Rows[0], true);
        Check(repeat.Rows[0].Included && repeat.Rows[0].Status.Contains("Совпадение"), "allow deliberate namesake");
        repeat.GroupName = "77-Б";
        repeat.Refresh();
        Check(repeat.Rows[0].Included && !repeat.Rows[1].Included, "changed target recalculates internal matches");
        repeat.GroupName = group.Name;
        repeat.Refresh();
        Check(!repeat.Rows[0].Included, "changing identity invalidates previous consent");
        repeat.Rows[0].Student.LastName = "Новая";
        repeat.Refresh();
        Check(repeat.Rows[0].Included, "editing name recalculates matches");
        repeat.Rows[0].Student.LastName = "Примерова";
        repeat.Refresh();
        repeat.Include(repeat.Rows[0], true);
        repeat.Include(repeat.Rows[2], false);
        Check(repeat.Save() == 1 && GroupRepository.GetStudentCount(group.Id) == 2, "save deliberate namesake");
        Expect<InvalidOperationException>(() => repeat.Save(), "same confirmed batch cannot be submitted twice");
        Check(GroupRepository.GetStudentCount(group.Id) == 2, "repeat batch writes no rows");

        var atomic = new StudentImport(Parse(table)) { GroupName = "Новая группа" };
        atomic.Refresh();
        using (var conn = Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TRIGGER import_failure BEFORE INSERT ON students WHEN NEW.last_name = 'Образцова'
                BEGIN SELECT RAISE(ABORT, 'fixture'); END;
                """;
            cmd.ExecuteNonQuery();
        }
        Expect<SqliteException>(() => atomic.Save(), "database failure after first insertion");
        Check(GroupRepository.GetAll().Count == 1 && StudentRepository.GetPage(new()).TotalCount == 2,
            "entire batch and new group rolled back");
        GroupRepository.ArchiveWithStudents(group.Id, "Выпуск");
        var successor = new StudentImport(manual) { GroupName = group.Name };
        successor.Refresh();
        Check(successor.Rows.Single().Included && successor.Save() == 1, "same group name can be reused after graduation");
        var active = GroupRepository.GetAll().Single();
        Check(active.Id != group.Id && GroupRepository.GetAll(archived: true).Single().Id == group.Id,
            "archived group stays archived");
        var sameName = new StudentImport(manual) { GroupName = group.Name };
        sameName.Refresh();
        Check(!sameName.Rows.Single().Included, "match uses the new active group");
        sameName.Rows.Single().Student.FirstName = "Ева";
        sameName.Refresh();
        Check(sameName.Save() == 1 && GroupRepository.GetStudentCount(active.Id) == 2,
            "existing active group wins over archived group with same name");
        var crossGroup = new StudentImport(alternate);
        StudentRepository.Insert(new Student
        {
            GroupId = group.Id, LastName = "Другая", FirstName = "Имя", Oms = alternate.Students.Single().Oms,
        });
        Expect<InvalidOperationException>(() => crossGroup.Save(), "concurrent OMS match requires review");
        Check(!crossGroup.Rows.Single().Included && GroupRepository.GetAll().Count == 1, "new match detected before write");
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException($"FAILED student import: {scenario}");
    }

    private static void Expect<T>(Action action, string scenario) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"FAILED student import: {scenario}");
    }
}
