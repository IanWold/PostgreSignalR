namespace PostgreSignalR;

internal static class StringExtensions
{
    public static string EscapeQuotes(this string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
