namespace PostgreSignalR;

internal static class StringExtensions
{
    public static string EscapeQutoes(this string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
