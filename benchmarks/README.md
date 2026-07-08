# Benchmarks

The benchmarks use four projects:

* [PostgreSignalR.Benchmarks](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks) is the executable that performs the benchmarks.
* [PostgreSignalR.Benchmarks.Server](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Server) is a server implementation the benchmarks use to test the backplanes.
* [PostgreSignalR.Benchmarks.SharedLoad](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.SharedLoad) optionally simulates other traffic on the backplane's Postgres/Redis instance, unrelated to SignalR.
* [PostgreSignalR.Benchmarks.Abstractions](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Abstractions) is a shared class.

The benchmarks are run through docker compose. The docker-compose yml will create containers for postgres and redis, 10 fixed server slots (`server1`-`server10`), the shared-load generator, and one driver container which will run the benchmarks. The benchmarks can run either the Redis or Postgres backplanes.

```
BACKPLANE=postgres MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
BACKPLANE=redis MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
```

There are two modes it can run in:

* `single` runs a single round of tests
* `sweep` will run many rounds of tests, incrementing the number of clients. It will sweep up and down.

The other variables you can specify:

* `NUM_SERVERS`: the number of server nodes to spread the backplane fanout across, from 2 to 10 (docker-compose.yml defines 10 fixed slots, `server1`-`server10`; raise the ceiling there if you need more). `server1` is always the sole publish target; the rest are subscribers, each getting `CLIENTS_PER_SERVER` connections. This lets you test whether fanout latency degrades as the number of subscribing nodes grows, separately from load on any one node. Default 2 (one publisher, one subscriber). Ignored if `SERVER_URLS` is set.
* `SERVER_URLS`: a comma-separated list of server base URLs to use instead of the `server1..serverN` docker-compose naming (e.g. `https://bench-server-1.example.com,https://bench-server-2.example.com`). Use this to point the driver at servers deployed somewhere other than this docker-compose setup (i.e. cloud provider). The first URL is always the publish target; the rest are subscribers, same as `NUM_SERVERS`. At least 2 URLs are required.
* `CLIENTS_PER_SERVER`: the number of clients to connect to *each* subscriber node. Total clients connected = `CLIENTS_PER_SERVER * (NUM_SERVERS - 1)`, so every subscriber always carries equal load - raising `NUM_SERVERS` raises total client count too. Default 500.
* `PUBLISH_COUNT`: The number of messages to publish. Default 20000.
* `CONCURRENCY`: The maximum number of concurrent `SendAsync` calls in flight on the server at any time, for the lifetime of the run. Default 128.
* `PAYLOAD_BYTES`: The number of bytes of filler content included in each message's payload. Default 128.
* `WARMUP_SECONDS`: The number of seconds to warm up. Default 10.
* `MEASURE_SECONDS`: For `single` runs, the number of seconds to measure. Messages/second will be `PUBLISH_COUNT / MEASURE_SECONDS`.
* `REPEATS_PER_RATE`: The number of independent trials to run at each rate (each rate in a `sweep`, or the single trial in `single` mode). Latency percentiles are computed over the pooled samples from all repeats; `Sent`/`Missing`/`Fanout Copies` are summed. Default 1.
* `HEALTH_CHECK_TIMEOUT_SECONDS`: How long the driver waits (polling once per second) for each server's `/health` endpoint before giving up and failing the run. Default 60.
* `PAYLOAD_STRATEGY`: Only applies when `BACKPLANE=postgres`
    * `event` (default) sends payloads inline in the notification event.
    * `table` uses PostgreSignalR's payload table strategy instead (`AddBackplaneTablePayloadStrategy` with `StorageMode=Always`).

The `sweep` output table's `Rate (msg/s)` column is the offered rate, i.e. what the driver was asked to send - it is not necessarily what was achieved. The `Achieved (msg/s)` column is the rate actually measured (messages sent / actual dispatch time), which falls below the target once the driver or server can't keep up. When achieved rate drops more than 5% below target, a warning is printed, since the latency figures on that row reflect the achieved rate, not the labeled one. The same applies to `single` mode's `Sent ... achieved` line.

## Connection strings

`ConnectionStrings__Postgres` and `ConnectionStrings__Redis` accept either the native keyword=value formats Npgsql/StackExchange.Redis expect, or a `postgres://`/`postgresql://` and `redis://`/`rediss://` URI - the format most cloud providers  hand out as `DATABASE_URL`/`REDIS_URL`. URIs are converted automatically (`rediss://` and a Postgres URI's `sslmode` query param both map through correctly); values already in native format are passed through unchanged, so the local docker-compose setup is unaffected. This lets `server`, `shared-load`, and the driver's backplane connections point at a real managed database instead of the containers the compose file provisions.

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

## Recreating all my Benchmarks

I generated `run-comparisons.sh` to run a set of 10 predefined scenarios that I think give a good comparison across several different use cases. Each scenario takes 30 minuets to run, so the whole suite takes a little over 5 hours.

If you're just interested in running certain scenarios, you can execute `run-comparisons.sh --list` to see all of them and list scenarios out to run, like `run-comparisons.sh redis-dedicated postgres-shared`.

Logs are saved to `results/<timestamp>/<scenario>.log`
