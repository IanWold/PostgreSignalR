# Benchmarks

The benchmarks use four projects:

* [PostgreSignalR.Benchmarks](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks) is the executable that performs the benchmarks.
* [PostgreSignalR.Benchmarks.Server](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Server) is a server implementation the benchmarks use to test the backplanes.
* [PostgreSignalR.Benchmarks.SharedLoad](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.SharedLoad) optionally simulates other traffic on the backplane's Postgres/Redis instance, unrelated to SignalR.
* [PostgreSignalR.Benchmarks.Abstractions](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Abstractions) is a shared class.

The benchmarks are run through docker compose. The docker-compose yml will create containers for postgres and redis, two server containers, the shared-load generator, and one driver container which will run the benchmarks. The benchmarks can run either the Redis or Postgres backplanes.

```
BACKPLANE=postgres MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
BACKPLANE=redis MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
```

There are two modes it can run in:

* `single` runs a single round of tests
* `sweep` will run many rounds of tests, incrementing the number of clients. It will sweep up and down.

The other variables you can specify:

* `CLIENTS`: the number of clients to connect.
* `PUBLISH_COUNT`: The number of messages to publish. Default 20000.
* `CONCURRENCY`: The maximum number of concurrent `SendAsync` calls in flight on the server at any time, for the lifetime of the run. Default 128.
* `PAYLOAD_BYTES`: The number of bytes of filler content included in each message's payload. Default 128.
* `WARMUP_SECONDS`: The number of seconds to warm up. Default 10.
* `MEASURE_SECONDS`: For `single` runs, the number of seconds to measure. Messages/second will be `PUBLISH_COUNT / MEASURE_SECONDS`.
* `REPEATS_PER_RATE`: The number of independent trials to run at each rate (each rate in a `sweep`, or the single trial in `single` mode). Latency percentiles are computed over the pooled samples from all repeats; `Sent`/`Missing`/`Fanout Copies` are summed. Default 1.
* `PAYLOAD_STRATEGY`: Only applies when `BACKPLANE=postgres`
    * `event` (default) sends payloads inline in the notification event.
    * `table` uses PostgreSignalR's payload table strategy instead (`AddBackplaneTablePayloadStrategy` with `StorageMode=Always`).

The `sweep` output table's `Rate (msg/s)` column is the offered rate, i.e. what the driver was asked to send - it is not necessarily what was achieved. The `Achieved (msg/s)` column is the rate actually measured (messages sent / actual dispatch time), which falls below the target once the driver or server can't keep up. When achieved rate drops more than 5% below target, a warning is printed, since the latency figures on that row reflect the achieved rate, not the labeled one. The same applies to `single` mode's `Sent ... achieved` line.

## Dedicated vs. Shared Backplane

By default the benchmarks give Postgres/Redis to the backplane exclusively - nothing else is talking to them. That's a best case, and not how these are typically deployed in production: Redis is frequently shared with other caching/session traffic, and the whole point of a Postgres backplane is usually to reuse a database you already run for your application, not stand up a dedicated instance.

`PostgreSignalR.Benchmarks.SharedLoad` simulates that other traffic. It runs a simple, continuous CRUD-ish workload (mostly writes/reads, occasional updates and cleanup deletes for Postgres; mostly sets/gets, occasional counters and deletes for Redis) against the same Postgres database or Redis instance used as the backplane, in a separate table/keyspace so it doesn't interact with SignalR's own messages - it just simulates realistic CPU/IO/connection/lock contention.

* `SIMULATE_SHARED_LOAD`: `true` to enable the generator, `false` (default) to leave it idle.
* `SHARED_LOAD_CONCURRENCY`: number of parallel workers generating load. Default 16.
* `SHARED_LOAD_OPS_PER_SEC`: approximate total operations/second across all workers. Default 200.

To compare all four scenarios:

```
# Dedicated Redis backplane
BACKPLANE=redis MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver

# Dedicated Postgres backplane
BACKPLANE=postgres MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver

# Shared Redis backplane
BACKPLANE=redis SIMULATE_SHARED_LOAD=true MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver

# Shared Postgres backplane
BACKPLANE=postgres SIMULATE_SHARED_LOAD=true MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
```