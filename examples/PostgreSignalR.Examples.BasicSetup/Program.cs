using Npgsql;
using PostgreSignalR;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(
        dataSource,
        options =>
        {
            // The prefix is prepended to every channel name used by the backplane.
            // When messaging through Postgres, the backplane calls NOTIFY <channel> '<payload>';
            //     which dispatches messages within a database.
            // If you have multiple different applications using the same database as a backplane,
            //     each application should have a separate prefix to distinguish them.
            // DO NOT, however, use a different prefix for different instances of the same application.
            // PostgreSignalR requires that channel names match exactly.
            options.Prefix = "myapp";
            // The prefix is required to be less than 20 characters. The following throws on startup:
            // options.Prefix = "abcdefghijklmnopqrstuvwxyz";
            // Because channel names have limited length (see below) shorter prefixes are better.
            // The default prefix is:
            // options.Prefix = "postgresignalr";
            // Which can help distinguish backplane notifications from other notifications.
            // However, if you're not using NOTIFY for anything else (or don't care),
            //     then setting prefix to string.Empty would be the most performant
            //     to help avoid triggering channel name normalization (again, see below).



            // Postgres NOTIFY has a requirement that channel names must be:
            //     - A Postgres identifier (a-Z, 0-9, and _; first character must be a letter)
            //     - <= 63 characters in length
            // Depending on the scenario, channel names may need to convey information about
            //     group names, user identifiers, or other complex information
            //     that can cause the channel names to go over 63 characters.
            // PostgreSignalR needs to employ some strategy for normalizing channel names
            //     in order to avoid causing errors with channel name length.
            // The two normalization options are Truncate and HashAlways.
            //
            // Truncate:
            // Truncate is the default value for this option.
            // If the channel name is less than 63 characters, the plain channel name is used.
            // If the channel name is more than 63 characters, then the following is done:
            //     1. The channel name is hashed using SHA256
            //     2. The plain channel name is truncated to 55 characters (63-8 = 55)
            //     3. The first 8 characters of the hash are appended to the truncated name,
            //        producing a channel name of exactly 63 characters
            // The Truncate option preserves as much of the plain channel name as reasonably possible,
            //     running faster and facilitating easier debugging if you're watching notifications.
            //
            // HashAlways:
            // This option always hashes the channel name, irrespective of its starting length.
            // An efficient algorithm with SHA256 is used to produce a 43-character has,
            //     and the raw prefix is prepended to this hash to produce an at-most 63-character name.
            // Because this option runs the hash algoritm for every notification,
            //     it should probably only be used in situations where you need its characteristics
            //     where truncating is frequently triggered,
            //     such as applications that have very long group names.
            options.ChannelNameNormaization = ChannelNameNormaization.Truncate;
            // options.ChannelNameNormaization = ChannelNameNormaization.HashAlways;



            // The backplane does not initialize its connection to Postgres immediately at startup:
            //     if there are no users connected to the SignalR hub, there's no need to connect.
            // If you need to run any logic at the time of initialization,
            //     subscribe to this event.
            options.OnInitialized += () => { /* Do something */ };
        }
    );

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
