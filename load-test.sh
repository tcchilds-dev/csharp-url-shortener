#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  cat <<'HELP'
Usage: ./load-test.sh [hot|mixed|distinct] [k6 run options]

Examples:
  ./load-test.sh
  ./load-test.sh mixed -e RATE=1000
  ./load-test.sh distinct -e RATE=100 -e DURATION=10s

Start the API and its databases first. This command prepares links, runs k6,
then verifies persisted clicks. It fails if any step fails.
Set DATA_FILE and SUMMARY_FILE as shell environment variables to change paths.
HELP
  exit 0
fi

scenario=hot
if [[ $# -gt 0 && "$1" != -* ]]; then
  scenario="$1"
  shift
fi
case "$scenario" in
  hot|mixed|distinct) ;;
  *) echo "Unknown scenario: $scenario. Use hot, mixed, or distinct." >&2; exit 2 ;;
esac

# Resolve paths from the script so it also works when invoked from elsewhere.
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
for dependency in dotnet k6; do
  command -v "$dependency" >/dev/null || {
    echo "Required command not found: $dependency" >&2
    exit 127
  }
done
export DATA_FILE="${DATA_FILE:-load-test-data.json}"
export SUMMARY_FILE="${SUMMARY_FILE:-k6-summary.json}"

echo "Preparing test links..."
dotnet run --project tools/LoadTestData -- prepare

# Keep the exit status without stopping: failed latency thresholds should not
# prevent checking whether the worker persisted the clicks it received.
echo "Running the $scenario workload..."
k6_status=0
k6 run "$@" -e "SCENARIO=$scenario" -e "DATA_FILE=$DATA_FILE" \
  -e "SUMMARY_FILE=$SUMMARY_FILE" k6-test.js || k6_status=$?

echo "Verifying persisted clicks..."
verify_status=0
dotnet run --no-build --project tools/LoadTestData -- verify || verify_status=$?

if (( k6_status != 0 || verify_status != 0 )); then
  echo "Load test failed (k6 exit: $k6_status; verification exit: $verify_status)." >&2
  if (( k6_status != 0 )); then exit "$k6_status"; fi
  exit "$verify_status"
fi
echo "Load test passed: k6 thresholds and persisted click counts match expectations."
