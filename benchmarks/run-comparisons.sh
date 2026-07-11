#!/usr/bin/env bash
set -euo pipefail

# Runs the backplane comparison matrix described in benchmarks/README.md.
#
# Each scenario is torn down (including volumes) before it starts, so a scenario
# never inherits leftover Postgres/Redis state (payload tables, shared-load rows,
# stale NOTIFY listeners) from the previous run.
#
# WARNING: this is slow - each scenario's rate sweep and CLIENTS_PER_SERVER are tuned per
# scenario now (see --list), and a full sweep is commonly 20-30 minutes, so the full matrix
# (16 scenarios) is several hours. Pass scenario names to run a subset, or override
# REPEATS_PER_RATE / SWEEP_TRIAL_SECONDS in your environment for a quicker smoke test
# (CLIENTS_PER_SERVER and the sweep rate bounds themselves are per-scenario, not overridable
# this way - edit the scenarios array directly if you need different values there).
#
# Usage:
#   ./benchmarks/run-comparisons.sh                                # run every scenario
#   ./benchmarks/run-comparisons.sh --list                          # print the matrix and exit
#   ./benchmarks/run-comparisons.sh redis-dedicated postgres-shared # run only the named scenarios
#   REPEATS_PER_RATE=1 SWEEP_TRIAL_SECONDS=5 ./benchmarks/run-comparisons.sh redis-dedicated
#                                                                    # quick smoke test of one scenario

cd "$(dirname "${BASH_SOURCE[0]}")/.."

: "${MODE:=sweep}"
: "${PUBLISH_COUNT:=20000}"
: "${CONCURRENCY:=128}"
: "${PAYLOAD_BYTES:=128}"
: "${REPEATS_PER_RATE:=3}"

export MODE PUBLISH_COUNT CONCURRENCY PAYLOAD_BYTES REPEATS_PER_RATE

# Four groups, each answering a different question - see run-comparisons-railway.sh for the
# full rationale (this mirrors the same matrix):
#   1. redis/postgres-dedicated/shared: core backplane comparison + max sustainable rate.
#   2. postgres-*-table: same, with the payload-table strategy instead of the default event one.
#   3. *-clients-N: fixed low rate, CLIENTS_PER_SERVER pushed up in steps - max sustainable
#      client count. Client count isn't swept within one run like rate is, so each checkpoint
#      is its own scenario.
#   4. *-dedicated-Nservers: fixed client count/rate, more subscriber nodes - fan-out width.
#
# name                            backplane  shared  num_servers  payload_strategy  clients_per_server  sweep_start  sweep_step  sweep_max
scenarios=(
  "redis-dedicated                  redis    false 2  event  50   100 100 2000"
  "postgres-dedicated               postgres false 2  event  50   100 100 2000"
  "postgres-dedicated-table         postgres false 2  table  50   100 100 2000"
  "redis-shared                     redis    true  2  event  50   100 100 2000"
  "postgres-shared                  postgres true  2  event  50   100 100 2000"
  "postgres-shared-table            postgres true  2  table  50   100 100 2000"
  "redis-clients-500                redis    false 2  event  500  10  10  100"
  "redis-clients-2000               redis    false 2  event  2000 10  10  100"
  "redis-clients-4000               redis    false 2  event  4000 10  10  100"
  "postgres-clients-500             postgres false 2  event  500  10  10  100"
  "postgres-clients-2000            postgres false 2  event  2000 10  10  100"
  "postgres-clients-4000            postgres false 2  event  4000 10  10  100"
  "postgres-clients-500-table       postgres false 2  table  500  10  10  100"
  "postgres-clients-2000-table      postgres false 2  table  2000 10  10  100"
  "postgres-clients-4000-table      postgres false 2  table  4000 10  10  100"
  "redis-dedicated-5servers         redis    false 5  event  50   100 100 2000"
  "postgres-dedicated-5servers      postgres false 5  event  50   100 100 2000"
  "redis-dedicated-10servers        redis    false 10 event  50   100 100 2000"
  "postgres-dedicated-10servers     postgres false 10 event  50   100 100 2000"
)

print_matrix() {
  printf '%-30s %-9s %-7s %-11s %-9s %-19s %-12s %-11s %s\n' \
    "NAME" "BACKPLANE" "SHARED" "NUM_SERVERS" "STRATEGY" "CLIENTS_PER_SERVER" "SWEEP_START" "SWEEP_STEP" "SWEEP_MAX"
  for entry in "${scenarios[@]}"; do
    read -r name backplane shared num_servers strategy clients_per_server sweep_start sweep_step sweep_max <<< "$entry"
    printf '%-30s %-9s %-7s %-11s %-9s %-19s %-12s %-11s %s\n' \
      "$name" "$backplane" "$shared" "$num_servers" "$strategy" "$clients_per_server" "$sweep_start" "$sweep_step" "$sweep_max"
  done
}

requested=()
list_only=false

for arg in "$@"; do
  case "$arg" in
    --list|-l) list_only=true ;;
    *) requested+=("$arg") ;;
  esac
done

if $list_only; then
  print_matrix
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

timestamp=$(date +%Y%m%d-%H%M%S)
results_dir="benchmarks/results/$timestamp"
mkdir -p "$results_dir"

echo "Baseline: MODE=$MODE PUBLISH_COUNT=$PUBLISH_COUNT CONCURRENCY=$CONCURRENCY PAYLOAD_BYTES=$PAYLOAD_BYTES REPEATS_PER_RATE=$REPEATS_PER_RATE (CLIENTS_PER_SERVER and sweep bounds are per-scenario now, see --list)"
echo "Results directory: $results_dir"
echo "Scenarios: $(for e in "${selected[@]}"; do printf '%s ' "${e%% *}"; done)"
echo

summary=()

cleanup() {
  echo "Tearing down compose stack..."
  docker compose down --volumes --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

for entry in "${selected[@]}"; do
  read -r name backplane shared num_servers strategy clients_per_server sweep_start sweep_step sweep_max <<< "$entry"
  log_file="$results_dir/$name.log"

  echo "=============================================="
  echo "Scenario: $name"
  echo "  BACKPLANE=$backplane SIMULATE_SHARED_LOAD=$shared NUM_SERVERS=$num_servers PAYLOAD_STRATEGY=$strategy"
  echo "  CLIENTS_PER_SERVER=$clients_per_server SWEEP=$sweep_start-$sweep_max step $sweep_step"
  echo "  Log: $log_file"
  echo "=============================================="

  docker compose down --volumes --remove-orphans >/dev/null 2>&1 || true

  status=0
  BACKPLANE="$backplane" \
  SIMULATE_SHARED_LOAD="$shared" \
  NUM_SERVERS="$num_servers" \
  PAYLOAD_STRATEGY="$strategy" \
  CLIENTS_PER_SERVER="$clients_per_server" \
  SWEEP_START_RATE="$sweep_start" \
  SWEEP_STEP_RATE="$sweep_step" \
  SWEEP_MAX_RATE="$sweep_max" \
  docker compose up --build --abort-on-container-exit --exit-code-from driver 2>&1 | tee "$log_file" || status=$?

  summary+=("$name:$status")

  echo
done

docker compose down --volumes --remove-orphans >/dev/null 2>&1 || true
trap - EXIT

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
echo "=============================================="

if [ "$failures" -gt 0 ]; then
  exit 1
fi
