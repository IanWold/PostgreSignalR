# Benchmarks

The benchmarks use three projects:

* [PostgreSignalR.Benchmarks](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks) is the executable that performs the benchmarks.
* [PostgreSignalR.Benchmarks.Server](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Server) is a server implementation the benchmarks use to test the backplanes.
* [PostgreSignalR.Benchmarks.Abstractions](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Abstractions) is a shared class.

The benchmarks are run through docker compose. The docker-compose yml will create containers for postgres and redis, two server containers, and one driver container which will run the benchmarks. The benchmarks can run either the Redis or Postgres backplanes.

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