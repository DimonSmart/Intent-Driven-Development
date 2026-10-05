#!/bin/zsh

set -u

SCRIPT_DIR="${0:A:h}"
cd "$SCRIPT_DIR" || exit 1

CONFIGURATION="Debug"
FRAMEWORK="net10.0"
TEST_DLL="$PWD/tests/Idd.Factory.LiveTests/bin/$CONFIGURATION/$FRAMEWORK/Idd.Factory.LiveTests.dll"
CLEANUP_EVAL_TEMP_ROOT=0

while (( $# > 0 )); do
  case "$1" in
    --model)
      [[ $# -ge 2 && -n "$2" ]] || { echo "--model requires a value." >&2; exit 2; }
      export IDD_FACTORY_EVAL_MODEL="$2"
      export IDD_FACTORY_EVAL_MODEL_SOURCE="argument"
      shift 2
      ;;
    --reasoning)
      [[ $# -ge 2 && -n "$2" ]] || { echo "--reasoning requires a value." >&2; exit 2; }
      export IDD_FACTORY_EVAL_REASONING_EFFORT="$2"
      export IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE="argument"
      shift 2
      ;;
    --help|-h)
      echo "Usage: $0 [--model <model>] [--reasoning <effort>]"
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: $0 [--model <model>] [--reasoning <effort>]" >&2
      exit 2
      ;;
  esac
done

: "${IDD_FACTORY_EVAL_TIMEOUT_MINUTES:=20}"
export IDD_FACTORY_EVAL_TIMEOUT_MINUTES

if [[ -z "${IDD_FACTORY_EVAL_TEMP_ROOT:-}" ]]; then
  IDD_FACTORY_EVAL_TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/idd-factory-native-eval.XXXXXXXX")" || exit 1
  CLEANUP_EVAL_TEMP_ROOT=1
fi
export IDD_FACTORY_EVAL_TEMP_ROOT

if [[ -z "${IDD_FACTORY_EVAL_ARTIFACT_DIR:-}" ]]; then
  EVAL_ID="$(date -u '+%Y%m%d-%H%M%S')-$(uuidgen | tr '[:upper:]' '[:lower:]' | tr -d '-')"
  IDD_FACTORY_EVAL_ARTIFACT_DIR="$PWD/artifacts/factory-evals/$EVAL_ID"
fi
mkdir -p "$IDD_FACTORY_EVAL_ARTIFACT_DIR" || exit 1
export IDD_FACTORY_EVAL_ARTIFACT_DIR
export IDD_FACTORY_EVAL_KEEP_TEMP=1

echo "[$(date '+%Y-%m-%d %H:%M:%S')] Starting IDD Factory live tests."
echo "Codex timeout: $IDD_FACTORY_EVAL_TIMEOUT_MINUTES minutes"
echo "Model override: ${IDD_FACTORY_EVAL_MODEL:-repository config}"
echo "Reasoning override: ${IDD_FACTORY_EVAL_REASONING_EFFORT:-repository config}"
echo "Live artifacts: $IDD_FACTORY_EVAL_ARTIFACT_DIR"
echo "Eval temp root: $IDD_FACTORY_EVAL_TEMP_ROOT"

# Windows prevents replacing a DLL while a process has it open. On macOS,
# check whether a process has the test assembly open and stop only matching
# live-test hosts before running the checks.
if [[ -e "$TEST_DLL" ]]; then
  while IFS= read -r pid; do
    [[ -n "$pid" ]] || continue
    process_command="$(ps -p "$pid" -o command= 2>/dev/null || true)"
    if [[ "$process_command" == *testhost* && "$process_command" == *Idd.Factory.LiveTests* ]]; then
      kill -TERM "$pid" 2>/dev/null || exit 4
    fi
  done < <(lsof -t "$TEST_DLL" 2>/dev/null | sort -u)
  sleep 0.5
fi

export IDD_RUN_LIVE_FACTORY_EVALS=1

if [[ -z "${IDD_FACTORY_EVAL_VERSION:-}" ]]; then
  unset IDD_FACTORY_EVAL_VERSION
fi

INITIAL_STATUS="$(git status --porcelain=v1 --untracked-files=all)"
if [[ $? -ne 0 ]]; then
  echo "Could not read repository status." >&2
  exit 1
fi

dotnet build tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --nologo
TEST_EXIT_CODE=$?
if [[ "$TEST_EXIT_CODE" == 0 ]]; then
  dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj \
    --no-build --nologo \
    --filter 'Category=LiveFactoryEval' \
    --logger 'trx;LogFileName=live-tests.trx' \
    --results-directory "$IDD_FACTORY_EVAL_ARTIFACT_DIR"
  TEST_EXIT_CODE=$?
fi

FINAL_STATUS="$(git status --porcelain=v1 --untracked-files=all)"
STATUS_EXIT_CODE=$?
if [[ "$STATUS_EXIT_CODE" != 0 ]]; then
  echo "Could not read repository status after validation." >&2
  [[ "$TEST_EXIT_CODE" != 0 ]] || TEST_EXIT_CODE=1
elif [[ "$INITIAL_STATUS" != "$FINAL_STATUS" ]]; then
  echo "Validation changed the repository working tree." >&2
  echo "Before:" >&2
  print -r -- "$INITIAL_STATUS" >&2
  echo "After:" >&2
  print -r -- "$FINAL_STATUS" >&2
  [[ "$TEST_EXIT_CODE" != 0 ]] || TEST_EXIT_CODE=1
fi
if [[ "$TEST_EXIT_CODE" != 0 ]]; then
  echo "Live validation failed with exit code $TEST_EXIT_CODE." >&2
fi

echo "[$(date '+%Y-%m-%d %H:%M:%S')] IDD Factory live tests finished with exit code $TEST_EXIT_CODE."
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Running idd-factory-report."

if [[ ! -e "$IDD_FACTORY_EVAL_TEMP_ROOT/workspace/.git" || ! -e "$IDD_FACTORY_EVAL_TEMP_ROOT/codex-home" ]]; then
  echo "idd-factory-report could not run because the live-eval workspace or Codex home is unavailable."
  REPORT_EXIT_CODE=2
else
  dotnet run --project ./tools/idd-factory-report/Idd.Factory.Report.csproj -- latest \
    --repo "$IDD_FACTORY_EVAL_TEMP_ROOT/workspace" \
    --codex-home "$IDD_FACTORY_EVAL_TEMP_ROOT/codex-home" \
    --json "$IDD_FACTORY_EVAL_ARTIFACT_DIR/factory-report.json" \
    --markdown "$IDD_FACTORY_EVAL_ARTIFACT_DIR/factory-report.md"
  REPORT_EXIT_CODE=$?
  if [[ "$REPORT_EXIT_CODE" == 0 ]]; then
    echo
    echo "===== Human-readable Factory report ====="
    cat "$IDD_FACTORY_EVAL_ARTIFACT_DIR/factory-report.md"
    echo "===== End Factory report ====="
    echo "Markdown report: $IDD_FACTORY_EVAL_ARTIFACT_DIR/factory-report.md"
    echo "TRX results:     $IDD_FACTORY_EVAL_ARTIFACT_DIR/live-tests.trx"
  fi
fi

if [[ "$CLEANUP_EVAL_TEMP_ROOT" == 1 ]]; then
  rm -rf -- "$IDD_FACTORY_EVAL_TEMP_ROOT"
fi

if [[ "$TEST_EXIT_CODE" != 0 ]]; then
  exit "$TEST_EXIT_CODE"
fi
exit "$REPORT_EXIT_CODE"
