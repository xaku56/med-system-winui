using MedSystem.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace MedSystem.Core;

public static class IcdImport
{
    // Две колонки в UTF-8. Поддерживаем и обычный CSV, и разделитель Excel для русской локали.
    public static List<IcdCode> Read(TextReader source)
    {
        string? first;
        do { first = source.ReadLine(); } while (first != null && string.IsNullOrWhiteSpace(first));
        if (first == null)
            throw new InvalidDataException("Файл пуст.");

        int separator = first.IndexOfAny([';', ',', '\t']);
        if (separator < 0)
            throw new InvalidDataException("Нужны две колонки: код и наименование, разделённые точкой с запятой, запятой или табуляцией.");

        using var parser = new TextFieldParser(new StringReader(first + "\n" + source.ReadToEnd()));
        parser.SetDelimiters(first[separator].ToString());
        var codes = new Dictionary<string, IcdCode>(StringComparer.OrdinalIgnoreCase);
        bool firstRow = true;
        while (!parser.EndOfData)
        {
            long line = parser.LineNumber;
            var cells = parser.ReadFields();
            if (cells == null || cells.Length != 2)
                throw new InvalidDataException($"Строка {line}: ожидаются код и наименование диагноза.");
            var code = cells[0].Trim().ToUpperInvariant();
            var name = cells[1].Trim();
            if (firstRow && code is ("КОД" or "КОД МКБ" or "КОД МКБ-10" or "CODE"))
            {
                firstRow = false;
                continue;
            }

            firstRow = false;
            if (code.Length is 0 or > 10 || name.Length is 0 or > 255)
                throw new InvalidDataException($"Строка {line}: укажите код (до 10 символов) и наименование (до 255 символов).");
            if (codes.TryGetValue(code, out var existing) && existing.Name != name)
                throw new InvalidDataException($"Строка {line}: код {code} встречается с разными наименованиями.");
            codes.TryAdd(code, new IcdCode { Code = code, Name = name });
        }

        if (codes.Count == 0)
            throw new InvalidDataException("В файле нет диагнозов.");
        return codes.Values.ToList();
    }
}
