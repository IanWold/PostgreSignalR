# Benchmarks

The benchmarks use three projects:

* [PostgreSignalR.Benchmarks](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks) is the executable that performs the benchmarks.
* [PostgreSignalR.Benchmarks.Server](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Server) is a server implementation the benchmarks use to test the backplanes.
* [PostgreSignalR.Benchmarks.Abstractions](https://github.com/IanWold/PostgreSignalR/tree/main/benchmarks/PostgreSignalR.Benchmarks.Abstractions) holds types shared by the other two.

The benchmarks are run through docker compose. The docker-compose yml will create containers for postgres and redis, two server containers, and one driver container which will run the benchmarks. The benchmarks can run either the Redis or Postgres backplanes.

```
BACKPLANE=postgres MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
BACKPLANE=redis MODE=sweep docker compose up --build --abort-on-container-exit --exit-code-from driver
```

The first server publishes messages (through its `/publish` endpoint), and the clients connect to the other servers, so every message crosses the backplane.

## Modes

Set with `MODE`:

* `single` (default) runs a single round of tests at `TARGET_RATE` messages/second for `MEASURE_SECONDS`.
* `sweep` runs many rounds of tests, incrementing the publish rate from `SWEEP_START_RATE` to `SWEEP_MAX_RATE`, and stops early if p99 latency exceeds `SLO_P99_MS`. It will sweep up and down.
* `churn` measures connection churn: `CHURN_CONCURRENCY` loops each connect a new client, invoke a no-op hub method, and disconnect, for `CHURN_SECONDS`. The hub call makes each cycle wait until the backplane has set the connection up.
* `churn-group` is `churn`, but each connection joins its own new group instead of invoking the no-op.

`single` and `sweep` report end-to-end latency (publish on the first server to first receipt on a client), messages missing, and extra copies delivered. The churn modes report connects+disconnects per second and the latency of a whole cycle. A client's disconnect returns before the server has finished unsubscribing it, so after the run the driver also waits for the servers' listener channel counts to get back to where they started ("Listener drain") and reports the rate including that time.

The churn modes still connect `CLIENTS` steady clients first (as the other modes do), so the churn happens on servers that already have connections. Set `CLIENTS=0` for churn on its own. Churn connections use WebSockets and skip negotiation, so the driver opens one socket per cycle.

## Targets

`single` and `sweep` send to every client by default. Set `TARGET` to send each message to one target instead, rotating through all of them:

* `all` (default) sends to all clients.
* `connection` sends to a single connection.
* `user` sends to a user. Client `i` connects as user `user-{i % USER_COUNT}`.
* `group` sends to a group. Client `i` joins group `group-{i % GROUP_COUNT}`.

Clients fill the client servers in contiguous blocks, while users and groups are assigned round robin, so with more than one client server a user's connections and a group's members are spread across servers. No client connects to the publishing server, so every target is on a different server than the sender. (With a single server in `SERVERS`, that server both publishes and hosts the clients.)

## Servers

Compose runs `servera` and `serverb` by default. Profiles add more:

* `--profile servers4` adds `serverc` and `serverd`.
* `--profile servers6` adds `serverc` to `serverf`.

The driver only uses the servers listed in `SERVERS`, so list them there too. The first one listed publishes:

```
SERVERS=http://servera:8080,http://serverb:8080,http://serverc:8080,http://serverd:8080 \
BACKPLANE=postgres TARGET=connection \
docker compose --profile servers4 up --build --abort-on-container-exit --exit-code-from driver
```

Remove the extra servers afterwards with `docker compose --profile servers4 down` (or `servers6`).

## Listener channels

With the postgres backplane, each server has a `GET /listener` endpoint returning its routing mode, the number of channels its listener connection is LISTENing on, and the number of LISTENs it has run. The backplane initializes on its first connection or send, so both are 0 until then.

For targeted (`TARGET` other than `all`) and churn runs, the driver prints these for every server at the end of the run. For churn it also prints each server's channel count before the run and the number of LISTENs run during it.

## Variables

Driver:

* `MODE`: `single`, `sweep`, `churn` or `churn-group`. Default `single`.
* `TARGET`: `all`, `connection`, `user` or `group`. Default `all`.
* `SERVERS`: Comma separated server URLs; the first one publishes. Default: `SERVER_A,SERVER_B` (`http://servera:8080,http://serverb:8080`).
* `CLIENTS`: The number of clients to connect. Default 500.
* `USER_COUNT`: With `TARGET=user`, the number of distinct users. Default `CLIENTS` (one connection per user).
* `GROUP_COUNT`: With `TARGET=group`, the number of groups. Default 10.
* `PUBLISH_COUNT`: Sizes the warmup, which publishes `min(2000, PUBLISH_COUNT / 10)` messages. Default 20000.
* `CONCURRENCY`: The maximum number of concurrent sends (on the publishing server). Default 128.
* `PAYLOAD_BYTES`: The number of bytes in the payload. Default 128.
* `WARMUP_SECONDS`: The number of seconds to warm up. For the churn modes, the warmup is an unmeasured churn of this length. Default 10.
* `MEASURE_SECONDS`: For `single` runs, the number of seconds to measure. Default 30.
* `TARGET_RATE`: For `single` runs, the messages/second to publish. Default 100.
* `SWEEP_START_RATE`, `SWEEP_STEP_RATE`, `SWEEP_MAX_RATE`: The messages/second for `sweep`. Defaults 100, 100 and 2000.
* `SWEEP_TRIAL_SECONDS`: The length of each `sweep` round. Default 15.
* `SLO_P99_MS`: `sweep` stops when p99 latency exceeds this. Default 250.
* `BATCH_SIZE`: Messages per `/publish` request. Default 25.
* `CHURN_CONCURRENCY`: The number of concurrent connect/disconnect loops. Default 16.
* `CHURN_SECONDS`: The length of the measured churn. Default `MEASURE_SECONDS`.

Server (set for every server by compose):

* `BACKPLANE`: `none`, `redis` or `postgres`. Default `none`.
* `CHANNEL_ROUTING`: `PerTarget` or `Shared`, setting `PostgresBackplaneOptions.ChannelRouting` with the postgres backplane. Default `PerTarget`. Servers with different values don't exchange messages, so they all get the same one.

Compose:

* `POSTGRES_IMAGE`: The Postgres image. Default `postgres:16`; any of `postgres:15` to `postgres:19` work.

## Comparing PerTarget and Shared routing

Under `PerTarget`, each server LISTENs on a channel per local connection, user and group, so its channel count grows with its clients, and connecting, disconnecting and joining groups each run a LISTEN or UNLISTEN. Under `Shared`, each server LISTENs on a fixed set of channels and receives every targeted message. The modes that show the difference are the targeted sends (more so with more servers, since under `Shared` every server receives every message) and the churn modes.

Run each workload under both modes, keeping the driver's output:

```bash
mkdir -p benchmarks/results-local

run() { # run <name> [VAR=value...]
  name=$1; shift
  env BACKPLANE=postgres "$@" docker compose up --build --abort-on-container-exit --exit-code-from driver
  docker compose logs --no-log-prefix driver > "benchmarks/results-local/$name.log"
  docker compose down
}

for routing in PerTarget Shared; do
  for target in connection user group; do
    run "$routing-$target" CHANNEL_ROUTING=$routing MODE=sweep TARGET=$target
  done
  run "$routing-churn" CHANNEL_ROUTING=$routing MODE=churn
  run "$routing-churn-group" CHANNEL_ROUTING=$routing MODE=churn-group
done
```

To compare with more servers or another Postgres version, export these before the loop (and keep them set for `docker compose down`):

```bash
export COMPOSE_PROFILES=servers6
export SERVERS=http://servera:8080,http://serverb:8080,http://serverc:8080,http://serverd:8080,http://servere:8080,http://serverf:8080
export POSTGRES_IMAGE=postgres:19
```

In the results, compare the latency tables (or churn rates) between the two logs for each workload. The listener channel table at the end of each log confirms which routing mode each server ran and shows the channel counts behind the difference.
