# Custom Table

While the built-in table payload strategy does have a built-in table, there are a number of reasons you might want to use the strategy with a custom table. If you want to be able to set up triggers on this table, or implement robust cleanup handling, it's better to implement your own payload table.

To use your custom table with the built-in strategy, you'll need to make sure that your table meets these requirements:

1. It has a `payload` column of type `BYTEA`,
2. It will succeed for the statement `INSERT INTO <table name here> (payload) VALUES (<the payload param here>) RETURNING id`,
3. The `id` column can be represented in C# as a `long` and uniquely identifies the payload.

Further, if you want to use the built-in automatic cleanup, your table must have a `created_at` column with a timestamp type.

This example implements a custom table with a `pg_cron` job scheduled to perform cleanup on the table. If you're implementing your own cleanup, do be sure to configure the table backplane strategy to not use automatic cleanup.

* Program.cs contains notes on the configuration options chosen, and
* Migrations/1_CreatePayloadTable.sql contains notes on the table and pg_cron implementations.
