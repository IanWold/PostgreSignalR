# Examples

These example applications demonstrate different ways you can configure PostgreSignalR to suit your needs.

## [Basic Setup](/PostgreSignalR.Examples.BasicSetup)

This project demonstrates the basic configuration options for PostgreSignalR, not considering any advanced setups below. The default options will be good for the majority of use cases, but some simple configurations may want to be chosen.

The philosophy this project uses for default configuration options are:

1. Defaults should cover the majority of cases,
2. They should be the simplest options, and
3. They should add the least amount of burden to the user.

The options demonstrated in this example are: `Prefix`, `ChannelNameNormalization`, and `OnInitialized`. See the comments in [Program.cs](/PostgreSignalR.Examples.BasicSetup/Program.cs) for detailed explanations of each.

In order to run this example, you will need to provide your own Postgres connection string in [appsettings.json](/PostgreSignalR.Examples.BasicSetup/appsettings.json).

## [Table Payload Strategy](/PostgreSignalR.Examples.TablePayloadSetup)

PostgreSignalR uses [PostgreSQL's NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html) to send messages between connected servers. By default, PostgreSignalR sends its messages directly inside the NOTIFY payload. This works for the majority of scenarios, however Postgres limits the payload size to 8kb, which may be too limited for some scenarios.

In order to send messages larger than 8kb, Postgres recommends storing the payload in a table on the database, and passing the id reference for that payload within the NOTIFY payload itself. PostgreSignalR has a concept of "payload strategies" to support this or other patterns for dealing with large payloads. While PostgreSignalR does allow you to write your own payload strategy (see below), it does come with a built-in table payload strategy, which is what this example application demonstrates configuring.

Using the built-in table payload strategy, PostgreSignalR will:

1. Write all paylods to a table dedicated to holding these payloads,
2. Only send id references to this table though NOTIFY,
3. Read from this table after LISTEN yields a notification.

There are two primary configurations to consider when using the built-in table payload strategy. First, should the table be used to hold _all_ payloads or just payloads that are large? Second, how should records be cleaned up from the table?

The built-in table payload strategy allows configuring an automatic cleanup, but this cleanup runs from the server, keeping a timer in memory and dispatching delete requests to the database at a specified frequency. This method is limited: if you frequently tear down servers the cleanup might not get triggered. On top of that, you may not want a timer running in the background of your server, and/or you may not want periodic connections to the database. If you want to implement a better cleanup strategy, see the _Custom Table_ example below.

See the comments in [Program.cs](/PostgreSignalR.Examples.TablePayloadSetup/Program.cs) for detailed explanations of all of the options.

### Built-In Table

The table payload strategy example demonstrates using a payload table that is provided by PostgreSignalR. You can customize the schema in which the table sits and the name of this table, and the SQL to create the table is already written for you. Invoking `app.InitializePostgresBackplanePayloadTableAsync()` from [Program.cs](/PostgreSignalR.Examples.TablePayloadSetup/Program.cs) will construct the table for you.

If you need more control over the table (such as wanting to implement a better way of handling cleanup), the built-in table payload strategy does allow you to implement a custom table:

### [Custom Table](/PostgreSignalR.Examples.CustomTable)

While the built-in table payload strategy does have a built-in table, there are a number of reasons you might want to use the strategy with a custom table. If you want to be able to set up triggers on this table, or implement robust cleanup handling, it's better to implement your own payload table.

To use your custom table with the built-in strategy, you'll need to make sure that your table meets these requirements:

1. It has a `payload` column of type `BYTEA`,
2. It will succeed for the statement `INSERT INTO <table name here> (payload) VALUES (<the payload param here>) RETURNING id`,
3. The `id` column can be represented in C# as a `long` and uniquely identifies the payload.

Further, if you want to use the built-in automatic cleanup, your table must have a `created_at` column with a timestamp type.

This [Custom Table](/PostgreSignalR.Examples.CustomTable) example implements a custom table with a `pg_cron` job scheduled to perform cleanup on the table. If you're implementing your own cleanup, do be sure to configure the table backplane strategy to not use automatic cleanup.

## [Custom Payload Strategy](/PostgreSignalR.Examples.CustomPayloadStrategy)

