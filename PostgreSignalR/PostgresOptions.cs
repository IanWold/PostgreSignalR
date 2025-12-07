namespace PostgreSignalR;

public class PostgresOptions
{
    public string Prefix { get; set; } = "postgresignalr";
    public required string ConnectionString { get; set; }
    public Action? OnInitialized { get; set; }
}
