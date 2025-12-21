# Table Payload Setup

PostgreSignalR uses [PostgreSQL's NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html) to send messages between connected servers. By default, PostgreSignalR sends its messages directly inside the NOTIFY payload. This works for the majority of scenarios, however Postgres limits the payload size to 8kb, which may be too limited for some scenarios.

In order to send messages larger than 8kb, Postgres recommends storing the payload in a table on the database, and passing the id reference for that payload within the NOTIFY payload itself. PostgreSignalR has a concept of "payload strategies" to support this or other patterns for dealing with large payloads. While PostgreSignalR does allow you to write your own payload strategy (see below), it does come with a built-in table payload strategy, which is what this example application demonstrates configuring.

Using the built-in table payload strategy, PostgreSignalR will:

1. Write all paylods to a table dedicated to holding these payloads,
2. Only send id references to this table though NOTIFY,
3. Read from this table after LISTEN yields a notification.

There are two primary configurations to consider when using the built-in table payload strategy. First, should the table be used to hold _all_ payloads or just payloads that are large? Second, how should records be cleaned up from the table?

The built-in table payload strategy allows configuring an automatic cleanup, but this cleanup runs from the server, keeping a timer in memory and dispatching delete requests to the database at a specified frequency. This method is limited: if you frequently tear down servers the cleanup might not get triggered. On top of that, you may not want a timer running in the background of your server, and/or you may not want periodic connections to the database. If you want to implement a better cleanup strategy, see the _Custom Table_ example below.

See the comments in Program.cs for detailed explanations of all of the options.

### Built-In Table

The table payload strategy example demonstrates using a payload table that is provided by PostgreSignalR. You can customize the schema in which the table sits and the name of this table, and the SQL to create the table is already written for you. Invoking `app.InitializePostgresBackplanePayloadTableAsync()` from Program.cs will construct the table for you.

If you need more control over the table (such as wanting to implement a better way of handling cleanup), the built-in table payload strategy does allow you to implement a custom table:
