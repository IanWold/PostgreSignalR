# Running the benchmarks on Railway (one-time setup)

`run-comparisons-railway.sh` only handles the *repeatable* part of a Railway run - switching each scenario's config, redeploying, running the driver, and collecting logs. It assumes the project/services below already exist, because creating them is a one-time task best done through Railway's dashboard, where you get live validation, rather than scripted blind CLI calls.

**Heads up**: I wrote this without access to a live Railway account or CLI, so I could not test any of this end-to-end. The shape of it (private networking hostnames, `railway.json` restart policy key, the general CLI command set) is cross-checked against docs.railway.com, but exact CLI flag syntax for some commands wasn't fully confirmed by what I could verify - see "What's unverified" at the bottom. Expect to adjust a few commands against what `railway --help` actually shows you.

## 1. Project and environment

Create a Railway project from this repo, then create a dedicated environment for benchmarking (keeps this isolated from - and easy to wholesale delete separately from - any other use of the same project):

```
railway login
railway init            # or `railway link` if the project already exists
railway environment new benchmarks
railway environment benchmarks
```

## 2. Databases

Add both a Postgres and a Redis plugin to the environment - both always exist regardless of which backplane a given scenario uses, same as the local docker-compose setup:

```
railway add --database postgres
railway add --database redis
```

Note the variable names Railway exposes for each (typically `DATABASE_URL` and `REDIS_URL`, referenced from other services as `${{Postgres.DATABASE_URL}}` / `${{Redis.REDIS_URL}}` - confirm the exact plugin/variable names in the dashboard, they're what you'll reference in step 4).

## 3. Server services (server1 .. server10)

Create 10 services, each deployed from this repo with:

- **Root directory**: repo root (so the Dockerfile's `COPY benchmarks/ benchmarks/` resolves correctly, same as `context: .` in docker-compose.yml)
- **Dockerfile path**: `benchmarks/PostgreSignalR.Benchmarks.Server/Dockerfile`
- **Service name**: `server1`, `server2`, ... `server10` exactly - the run script builds `SERVER_URLS` from `http://server{N}.railway.internal:8080`, which only resolves if the service names match.
- **Private networking**: enabled by default for services in the same project/environment (no extra config per Railway's docs), reachable at `<service-name>.railway.internal` on whatever port the container listens on - our Dockerfile already sets `ASPNETCORE_URLS=http://0.0.0.0:8080`, so no `$PORT` handling is needed.
- **Variables** (same for all 10):
  ```
  ASPNETCORE_ENVIRONMENT=Production
  Logging__LogLevel__Default=Warning
  ConnectionStrings__Postgres=${{Postgres.DATABASE_URL}}
  ConnectionStrings__Redis=${{Redis.REDIS_URL}}
  ```
  `BACKPLANE` and `PAYLOAD_STRATEGY` are set per-scenario by the run script - don't set them here.

If you'd rather not create these one at a time by hand, `railway add --repo <this-repo>` (run 10 times, renaming/reconfiguring the Dockerfile path each time) is the CLI equivalent - just confirm the actual flags against `railway add --help`, since I couldn't verify the exact non-interactive syntax for repeated same-repo services.

## 4. shared-load service

Same repo/root directory, with:

- **Dockerfile path**: `benchmarks/PostgreSignalR.Benchmarks.SharedLoad/Dockerfile`
- **Variables**:
  ```
  ConnectionStrings__Postgres=${{Postgres.DATABASE_URL}}
  ConnectionStrings__Redis=${{Redis.REDIS_URL}}
  ```
  `BACKPLANE` and `SIMULATE_SHARED_LOAD` are set per-scenario by the run script.

## 5. driver service

Same repo/root directory, with:

- **Dockerfile path**: `benchmarks/PostgreSignalR.Benchmarks/Dockerfile`
- **Restart policy: `NEVER`.** The driver is a one-shot job - it runs a scenario and exits - not a long-lived server. Without this, Railway will likely treat the exit as a crash and keep restarting it. Set this via the dashboard's deploy settings, or a `railway.json` alongside the driver's Dockerfile:
  ```json
  {
    "$schema": "https://railway.app/railway.schema.json",
    "deploy": {
      "restartPolicyType": "NEVER"
    }
  }
  ```
- All of `SERVER_URLS`, `MODE`, `CLIENTS_PER_SERVER`, etc. are set per-scenario by the run script - you don't need to set anything here up front.

## 6. Run it

```
./benchmarks/run-comparisons-railway.sh --list                 # sanity check the matrix
./benchmarks/run-comparisons-railway.sh redis-dedicated         # dry-run: prints the commands only
./benchmarks/run-comparisons-railway.sh --apply redis-dedicated # runs it for real
./benchmarks/run-comparisons-railway.sh --apply                 # runs the full 10-scenario matrix
./benchmarks/run-comparisons-railway.sh --apply --teardown      # deletes the whole environment when done
```

Unlike the local `run-comparisons.sh`, this does **not** tear down and recreate infrastructure between scenarios - redeploying 11 services per scenario is already slow enough on real cloud infrastructure without also destroying and rebuilding them each time. Services are left running between scenarios and only deleted by an explicit `--teardown` - remember to run that when you're done, since this bills for real compute the whole time it's up.

## What's unverified

I don't have a Railway CLI or account to test against, so treat these as the first things to check if something doesn't work:

- `railway variables --service NAME --set KEY=value` - confirmed working, including multiple `--set` flags in one call.
- `railway redeploy --service NAME --yes` - confirmed the flags are accepted, but **setting a variable already triggers a redeploy on its own** - an explicit `redeploy` called right after routinely fails with "cannot be redeployed... currently building, deploying" because one is already in flight. The script now treats `redeploy` as a best-effort nudge (failure is logged, not fatal) rather than relying on it.
- `railway logs --service NAME` - confirmed there's no `--follow`/streaming mode (`railway logs --service <SERVICE> [DEPLOYMENT_ID]` is the actual usage); the script now polls with repeated one-shot fetches instead. Still unconfirmed: whether a fetch is scoped to just the latest deployment or returns history across deployments - the script waits for the count of `"Done."` lines to *increase* rather than matching the first occurrence, which is correct either way.
- `railway environment delete NAME --yes` - the exact non-interactive teardown syntax.
- Whether `${{Postgres.DATABASE_URL}}`/`${{Redis.REDIS_URL}}` are the actual variable names Railway's plugins expose - check your dashboard, they may differ.

Run each of these by hand once against your actual Railway CLI before trusting the full `--apply` run, and let me know what's different so I can fix the script against ground truth instead of docs.
