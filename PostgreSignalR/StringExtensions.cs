namespace PostgreSignalR;

public static class StringExtensions
{
    public static string EscapeQutoes(this string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
