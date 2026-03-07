namespace TgDataPlanner.Services;

public static class MarkdownExtensions
{
    public static string EscapeMarkdownV2(this string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var escapeChars = new[] { '\\', '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        return escapeChars.Aggregate(text, (current, c) => current.Replace(c.ToString(), $"\\{c}"));
    }
}
