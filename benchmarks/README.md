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

The other variables you acn specify:

* `CLIENTS`: the numbre of clients to connect.
* `PUBLISH_COUNT`: The number of messages to publish. Default 20000.
* `CONCURRENCY`: The maximum number of concurrent requests (from server). Default 128.
* `PAYLOAD_BYTES`: The number of bytes in the payload. Default 128.
* `WARMUP_SECONDS`: The number of seconds to warm up. Default 10.
* `MEASURE_SECONDS`: For `single` runs, the number of seconds to measure. Messages/second will be `PUBLISH_COUNT / MEASURE_SECONDS`.