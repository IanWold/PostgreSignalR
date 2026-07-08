#!/usr/bin/env bash
set -euo pipefail

# Runs the backplane comparison matrix described in benchmarks/README.md.
#
# Each scenario is torn down (including volumes) before it starts, so a scenario
# never inherits leftover Postgres/Redis state (payload tables, shared-load rows,
# stale NOTIFY listeners) from the previous run.
#
# WARNING: this is slow. At the defaults below, one sweep is ~40 rate steps x
# REPEATS_PER_RATE trials x SWEEP_TRIAL_SECONDS - roughly 30 minutes per scenario,
# so the full matrix is several hours. Pass scenario names to run a subset, or
# override REPEATS_PER_RATE / SWEEP_MAX_RATE / SWEEP_STEP_RATE / SWEEP_TRIAL_SECONDS
# in your environment for a quicker smoke test.
#
# Usage:
#   ./benchmarks/run-comparisons.sh                                # run every scenario
#   ./benchmarks/run-comparisons.sh --list                          # print the matrix and exit
#   ./benchmarks/run-comparisons.sh redis-dedicated postgres-shared # run only the named scenarios
#   REPEATS_PER_RATE=1 SWEEP_MAX_RATE=300 ./benchmarks/run-comparisons.sh redis-dedicated
#                                                                    # quick smoke test of one scenario

cd "$(dirname "${BASH_SOURCE[0]}")/.."

: "${MODE:=sweep}"
: "${CLIENTS_PER_SERVER:=500}"
: "${PUBLISH_COUNT:=20000}"
: "${CONCURRENCY:=128}"
: "${PAYLOAD_BYTES:=128}"
: "${REPEATS_PER_RATE:=3}"

export MODE CLIENTS_PER_SERVER PUBLISH_COUNT CONCURRENCY PAYLOAD_BYTES REPEATS_PER_RATE

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

echo "Baseline: MODE=$MODE CLIENTS_PER_SERVER=$CLIENTS_PER_SERVER PUBLISH_COUNT=$PUBLISH_COUNT CONCURRENCY=$CONCURRENCY PAYLOAD_BYTES=$PAYLOAD_BYTES REPEATS_PER_RATE=$REPEATS_PER_RATE"
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
  read -r name backplane shared num_servers strategy <<< "$entry"
  log_file="$results_dir/$name.log"

  echo "=============================================="
  echo "Scenario: $name"
  echo "  BACKPLANE=$backplane SIMULATE_SHARED_LOAD=$shared NUM_SERVERS=$num_servers PAYLOAD_STRATEGY=$strategy"
  echo "  Log: $log_file"
  echo "=============================================="

  docker compose down --volumes --remove-orphans >/dev/null 2>&1 || true

  status=0
  BACKPLANE="$backplane" \
  SIMULATE_SHARED_LOAD="$shared" \
  NUM_SERVERS="$num_servers" \
  PAYLOAD_STRATEGY="$strategy" \
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
