# Custom Payload Strategy

If you need full control over how messages are sent over NOTIFY, you can implement a custom `IPostgresBackplanePayloadStrategy`. This interface defines two methods: one called to dispatch messages over NOTIFY, and another called one a notification has been received from Postgres.

This example extends the custom table example by adding an AFTER INSERT trigger on the table, using the trigger to call pg_notify.

* Program.cs shows how to register the custom strategy, and
* Migrations/1_CreatePayloadTable.sql contains notes on the table implementation.
