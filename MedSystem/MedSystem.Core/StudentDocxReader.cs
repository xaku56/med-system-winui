using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MedSystem.Core.Models;

namespace MedSystem.Core;

public sealed record StudentRoster(string GroupName, List<Student> Students, List<string> IgnoredColumns);

public static class StudentDocxReader
{
    // Headings can span runs, paragraphs and merged cells. Column numbers refer
    // to the document grid so merging a cell does not shift subsequent values.
    public static StudentRoster Read(Stream source)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("В файле отсутствует основной документ Word.");
        const int maxSize = 16 * 1024 * 1024;
        if (entry.Length > maxSize)
            throw new InvalidDataException("Текст документа превышает 16 МБ.");
        using var xml = entry.Open();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxSize,
        });
        var document = XDocument.Load(reader);
        var w = document.Root?.Name.Namespace ?? XNamespace.None;
        var body = document.Root?.Element(w + "body")
            ?? throw new InvalidDataException("Не удалось прочитать содержимое документа Word.");
        var students = new List<Student>();
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string currentGroup = "";

        foreach (var block in body.Elements())
        {
            if (block.Name == w + "p")
            {
                if (GroupHeading(Text(block, w)) is { } heading)
                    currentGroup = heading;
                continue;
            }
            if (block.Name != w + "tbl")
                continue;

            Dictionary<string, int>? columns = null;
            foreach (var row in block.Elements(w + "tr"))
            {
                var cells = Cells(row, w);
                var nonEmpty = cells.Values.Where(t => t.Length > 0).ToList();
                if (nonEmpty.Count == 0)
                    continue;
                if (nonEmpty.Count == 1 && GroupHeading(nonEmpty[0]) is { } heading)
                {
                    currentGroup = heading;
                    continue;
                }
                var header = cells.Select(c => (c.Key, Field: HeaderField(c.Value))).ToArray();
                if (header.Any(c => c.Field == "full") ||
                    (header.Any(c => c.Field == "last") && header.Any(c => c.Field == "first")))
                {
                    columns = new();
                    foreach (var (index, field) in header)
                    {
                        if (field.Length > 0)
                            columns.TryAdd(field, index);
                        else if (cells[index].Length > 0 && !cells[index].StartsWith('№'))
                            ignored.Add(cells[index]);
                    }
                    continue;
                }
                if (columns == null)
                    continue;

                string Get(string field) => columns.TryGetValue(field, out var index)
                    ? cells.GetValueOrDefault(index, "") : "";
                var full = Get("full").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                var student = new Student
                {
                    LastName = Get("last"), FirstName = Get("first"), MiddleName = Get("middle"),
                    BirthDate = Get("birth"), Address = Get("address"), Oms = Get("oms"),
                    MedicalExamDate = Get("exam"), FluorographyDate = Get("fluoro"),
                    HealthGroup = Get("health"),
                };
                if (full.Length > 0)
                {
                    student.LastName = full[0];
                    student.FirstName = full.ElementAtOrDefault(1) ?? "";
                    student.MiddleName = full.ElementAtOrDefault(2) ?? "";
                }
                if (columns.Keys.All(key => key == "group" || Get(key).Length == 0))
                    continue;

                var group = Get("group");
                student.GroupName = group.Length > 0 ? group : currentGroup;
                groups.Add(student.GroupName);
                students.Add(student);
            }
        }
        if (students.Count == 0)
            throw new InvalidDataException("В документе не найдена таблица с колонкой ФИО или отдельными колонками фамилии и имени.");
        // With several source groups, require the user to enter the final destination.
        groups.Remove("");
        return new(groups.Count == 1 ? groups.Single() : "", students, ignored.ToList());
    }

    private static Dictionary<int, string> Cells(XElement row, XNamespace w)
    {
        var index = (int?)row.Element(w + "trPr")?.Element(w + "gridBefore")?.Attribute(w + "val") ?? 0;
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(w + "tc"))
        {
            cells[index] = Text(cell, w);
            var span = (int?)cell.Element(w + "tcPr")?.Element(w + "gridSpan")?.Attribute(w + "val") ?? 1;
            index = checked(index + Math.Max(1, span));
        }
        return cells;
    }

    private static string Text(XElement element, XNamespace w) => Normalize(string.Join(' ',
        element.DescendantsAndSelf(w + "p").Select(p => string.Concat(p.Descendants()
            .Where(n => n.Name == w + "t" || n.Name == w + "tab" || n.Name == w + "br" || n.Name == w + "cr")
            .Select(n => n.Name == w + "t" ? n.Value : " ")))));

    public static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? GroupHeading(string text)
    {
        var match = Regex.Match(text, @"(?:^|\s)групп[аы](?:\s*[:№]\s*|\s+(?:номер\s+)?)(.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var value = match.Groups[1].Value.Trim(' ', '«', '»', '"', '.');
        return value.Length is > 0 and <= 64 && !value.Equals("здоровья", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static string HeaderField(string text)
    {
        var label = Normalize(text.ToLowerInvariant().Replace("ё", "е").Replace(".", ""));
        label = Regex.Replace(label, @"\s+(?:студента|учащегося|обучающегося)\b", "");
        return label switch
        {
            "фио" or "ф и о" or "фамилия имя отчество" or "фамилия, имя, отчество" => "full",
            "фамилия" => "last",
            "имя" => "first",
            "отчество" => "middle",
            "дата рождения" => "birth",
            "омс" or "полис омс" or "номер полиса омс" or "номер омс" => "oms",
            "адрес" or "адрес проживания" or "домашний адрес" or "место жительства" or
                "место проживания" or "адрес места жительства" or "адрес регистрации" => "address",
            "медосмотр" or "дата медосмотра" or "дата медицинского осмотра" => "exam",
            "флюорография" or "дата флюорографии" => "fluoro",
            "группа здоровья" => "health",
            "группа" or "номер группы" => "group",
            _ => "",
        };
    }
}
