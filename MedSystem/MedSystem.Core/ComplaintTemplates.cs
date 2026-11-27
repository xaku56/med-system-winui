namespace MedSystem.Core;

public static class ComplaintTemplates
{
    public const int MaxLength = 2000;

    public static string Append(string current, string template)
    {
        var text = current.Length == 0 || current.EndsWith('\n') || current.EndsWith('\r')
            ? current + template
            : current + "\n" + template;
        if (text.Length > MaxLength)
            throw new ArgumentException($"После вставки длина жалоб превысит {MaxLength} символов.");
        return text;
    }
}
