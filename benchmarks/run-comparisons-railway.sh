#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# BEST-EFFORT DRAFT - commands not verified against a live Railway account.
#
# I have no Railway CLI or credentials available in the environment I wrote
# this in, so none of the `railway` invocations below have actually been run
# against real infrastructure. They're my best understanding of the current
# CLI cross-checked against docs.railway.com, but Railway's CLI syntax has
# changed across major versions and a few specifics weren't confirmed by what
# I could verify (see README section "Railway script: what's unverified").
#
# This defaults to dry-run (it only prints the `railway` commands it would
# run). Pass --apply once you've compared the printed commands against
# `railway --help` / `railway <subcommand> --help` on your machine.
# ============================================================================
#
# Runs the same scenario matrix as run-comparisons.sh, but against a Railway
# project instead of local docker-compose, so the backplanes are exercised
# over a real network between isolated hosts rather than one shared Docker
# engine. It assumes the one-time setup in benchmarks/RAILWAY_SETUP.md has
# already been done (project, environment, 11 services + Postgres/Redis
# plugins already exist) - this script only handles the repeatable part:
# switching each scenario's config, redeploying, running the driver to
# completion, and collecting its logs.
#
# Usage:
#   ./benchmarks/run-comparisons-railway.sh --list                     # print the matrix, exit
#   ./benchmarks/run-comparisons-railway.sh --apply                    # run every scenario for real
#   ./benchmarks/run-comparisons-railway.sh --apply redis-dedicated     # run one scenario for real
#   ./benchmarks/run-comparisons-railway.sh redis-dedicated             # dry-run: print the commands only
#
# Environment:
#   RAILWAY_ENVIRONMENT   Railway environment to operate in. Default "benchmarks".
#   MAX_SERVERS           Must match however many server1..serverN services you
#                         provisioned in RAILWAY_SETUP.md. Default 10.

cd "$(dirname "${BASH_SOURCE[0]}")/.."

: "${RAILWAY_ENVIRONMENT:=production}"
: "${MAX_SERVERS:=10}"

: "${MODE:=sweep}"
: "${CLIENTS_PER_SERVER:=500}"
: "${PUBLISH_COUNT:=20000}"
: "${CONCURRENCY:=128}"
: "${PAYLOAD_BYTES:=128}"
: "${WARMUP_SECONDS:=10}"
: "${TARGET_RATE:=100}"
: "${SLO_P99_MS:=250}"
: "${SWEEP_START_RATE:=100}"
: "${SWEEP_STEP_RATE:=100}"
: "${SWEEP_MAX_RATE:=2000}"
: "${SWEEP_TRIAL_SECONDS:=15}"
: "${BATCH_SIZE:=25}"
: "${REPEATS_PER_RATE:=3}"
# Cloud builds/rollouts are slower than local docker-compose; give the driver's
# own internal health-check loop more room before it gives up on a server.
: "${HEALTH_CHECK_TIMEOUT_SECONDS:=180}"

# name                          backplane  shared  num_servers  payload_strategy
scenarios=(
  "redis-dedicated               redis    false 2  event"
  "postgres-dedicated            postgres false 2  event"
  "redis-shared                  redis    true  2  event"
  "postgres-shared               postgres true  2  event"
  "postgres-dedicated-table      postgres false 2  table"
  "postgres-shared-table         postgres true  2  table"
  "redis-dedicated-5servers      redis    false 5  event"
  "postgres-dedicated-5servers   postgres false 5  event"
  "redis-dedicated-10servers     redis    false 10 event"
  "postgres-dedicated-10servers  postgres false 10 event"
)

print_matrix() {
  printf '%-30s %-9s %-7s %-11s %s\n' "NAME" "BACKPLANE" "SHARED" "NUM_SERVERS" "PAYLOAD_STRATEGY"
  for entry in "${scenarios[@]}"; do
    read -r name backplane shared num_servers strategy <<< "$entry"
    printf '%-30s %-9s %-7s %-11s %s\n' "$name" "$backplane" "$shared" "$num_servers" "$strategy"
  done
}

apply=false
teardown_only=false
requested=()

for arg in "$@"; do
  case "$arg" in
    --list|-l) print_matrix; exit 0 ;;
    --apply) apply=true ;;
    --teardown) teardown_only=true ;;
    *) requested+=("$arg") ;;
  esac
done

# Every railway invocation goes through this so --apply is the one switch
# between "print what would happen" and "actually spend money."
run() {
  if $apply; then
    echo "+ $*"
    "$@"
  else
    echo "[dry-run] $*"
  fi
}

# `railway variables --set` appears to trigger a redeploy on its own - confirmed by Railway
# refusing an explicit redeploy called right after with "currently building, deploying".
# This is only a best-effort nudge in case that's not always the case; a failure here is
# expected and fine (it almost always means the variable change already triggered one), so
# it must not be allowed to abort the whole run the way `run` (and set -e) would.
try_redeploy() {
  local name=$1

  if $apply; then
    echo "+ railway redeploy --service $name --yes (best-effort - a failure here just means the variable change above already triggered a redeploy)"
    railway redeploy --service "$name" --yes || echo "  (redeploy call failed/skipped for $name - assuming the variable change already triggered one)"
  else
    echo "[dry-run] railway redeploy --service $name --yes (best-effort, ignored if it fails)"
  fi
}

if $teardown_only; then
  echo "Tearing down Railway environment '$RAILWAY_ENVIRONMENT'..."
  run railway environment delete "$RAILWAY_ENVIRONMENT" --yes
  exit 0
fi

selected=()

if [ ${#requested[@]} -eq 0 ]; then
  selected=("${scenarios[@]}")
else
  for name in "${requested[@]}"; do
    found=false

    for entry in "${scenarios[@]}"; do
      if [ "${entry%% *}" == "$name" ]; then
        selected+=("$entry")
        found=true
        break
      fi
    done

    if ! $found; then
      echo "Unknown scenario: $name" >&2
      echo "Run with --list to see available scenarios." >&2
      exit 1
    fi
  done
fi

run railway environment "$RAILWAY_ENVIRONMENT"

timestamp=$(date +%Y%m%d-%H%M%S)
results_dir="benchmarks/results-railway/$timestamp"
mkdir -p "$results_dir"

echo "Environment: $RAILWAY_ENVIRONMENT"
echo "Mode: $([ "$apply" = true ] && echo APPLY || echo DRY-RUN)"
echo "Results directory: $results_dir"
echo "Scenarios: $(for e in "${selected[@]}"; do printf '%s ' "${e%% *}"; done)"
echo

# Sets BACKPLANE/PAYLOAD_STRATEGY on all MAX_SERVERS server services and
# BACKPLANE/SIMULATE_SHARED_LOAD on shared-load, then redeploys both so the
# new config actually takes effect. All 10 slots are updated regardless of
# this scenario's num_servers, same as the docker-compose version - the ones
# beyond num_servers just sit unused.
configure_scenario() {
  local backplane=$1 shared=$2 strategy=$3

  for i in $(seq 1 "$MAX_SERVERS"); do
    local name="server$i"
    run railway variables --service "$name" --set "BACKPLANE=$backplane" --set "PAYLOAD_STRATEGY=$strategy"
    try_redeploy "$name"
  done

  run railway variables --service shared-load --set "BACKPLANE=$backplane" --set "SIMULATE_SHARED_LOAD=$shared"
  try_redeploy shared-load
}

# Builds SERVER_URLS from the first num_servers of the MAX_SERVERS private
# hostnames (server1.railway.internal, ... - see RAILWAY_SETUP.md), sets the
# rest of the driver's env to match run-comparisons.sh's baseline, deploys it,
# and polls its logs until a new "Done." line (the driver's own final line)
# appears.
run_driver() {
  local num_servers=$1 log_file=$2

  local server_urls=""
  for i in $(seq 1 "$num_servers"); do
    server_urls+="http://server$i.railway.internal:8080,"
  done
  server_urls="${server_urls%,}"

  run railway variables --service driver \
    --set "SERVER_URLS=$server_urls" \
    --set "MODE=$MODE" \
    --set "CLIENTS_PER_SERVER=$CLIENTS_PER_SERVER" \
    --set "PUBLISH_COUNT=$PUBLISH_COUNT" \
    --set "CONCURRENCY=$CONCURRENCY" \
    --set "PAYLOAD_BYTES=$PAYLOAD_BYTES" \
    --set "WARMUP_SECONDS=$WARMUP_SECONDS" \
    --set "TARGET_RATE=$TARGET_RATE" \
    --set "SLO_P99_MS=$SLO_P99_MS" \
    --set "SWEEP_START_RATE=$SWEEP_START_RATE" \
    --set "SWEEP_STEP_RATE=$SWEEP_STEP_RATE" \
    --set "SWEEP_MAX_RATE=$SWEEP_MAX_RATE" \
    --set "SWEEP_TRIAL_SECONDS=$SWEEP_TRIAL_SECONDS" \
    --set "BATCH_SIZE=$BATCH_SIZE" \
    --set "REPEATS_PER_RATE=$REPEATS_PER_RATE" \
    --set "HEALTH_CHECK_TIMEOUT_SECONDS=$HEALTH_CHECK_TIMEOUT_SECONDS"

  try_redeploy driver

  if $apply; then
    # This CLI's `railway logs` has no --follow/streaming mode (confirmed: it rejects the
    # flag outright), so this polls with repeated one-shot fetches instead of tailing a
    # single background process. Each fetch may or may not be scoped to just the latest
    # deployment - unconfirmed - so rather than trust the first "Done." we see (which could
    # be left over from a previous scenario if fetches aren't scoped that way), we record how
    # many completions are present before waiting, and wait for that count to increase.
    echo "+ railway logs --service driver (establishing baseline)"
    railway logs --service driver > "$log_file" 2>/dev/null || true

    local baseline
    baseline=$(grep -c "^Done\.$" "$log_file" 2>/dev/null || true)
    baseline=${baseline:-0}

    local waited=0
    local max_wait=3600
    local success=false

    while (( waited < max_wait )); do
      sleep 10
      waited=$((waited + 10))

      railway logs --service driver > "$log_file" 2>/dev/null || true

      local current
      current=$(grep -c "^Done\.$" "$log_file" 2>/dev/null || true)
      current=${current:-0}

      if (( current > baseline )); then
        success=true
        break
      fi
    done

    if ! $success; then
      echo "  Warning: did not see a new \"Done.\" line within ${max_wait}s - check $log_file"
      return 1
    fi
  else
    echo "[dry-run] railway logs --service driver (poll for a new \"Done.\" beyond whatever's already there)"
  fi

  return 0
}

summary=()

for entry in "${selected[@]}"; do
  read -r name backplane shared num_servers strategy <<< "$entry"
  log_file="$results_dir/$name.log"

  echo "=============================================="
  echo "Scenario: $name"
  echo "  BACKPLANE=$backplane SIMULATE_SHARED_LOAD=$shared NUM_SERVERS=$num_servers PAYLOAD_STRATEGY=$strategy"
  echo "  Log: $log_file"
  echo "=============================================="

  configure_scenario "$backplane" "$shared" "$strategy"

  status=0
  run_driver "$num_servers" "$log_file" || status=$?

  summary+=("$name:$status")

  echo
done

echo
echo "================= Summary ================="

failures=0

for entry in "${summary[@]}"; do
  name="${entry%%:*}"
  status="${entry##*:}"

  if [ "$status" -eq 0 ]; then
    echo "  OK    $name"
  else
    echo "  FAIL  $name (exit $status)"
    failures=$((failures + 1))
  fi
done

echo "Logs saved to: $results_dir"
echo "Services are left running (redeploy is much slower here than local docker-compose down/up)."
echo "Run './benchmarks/run-comparisons-railway.sh --apply --teardown' when you're done, to stop billing."
echo "=============================================="

if [ "$failures" -gt 0 ]; then
  exit 1
fi
