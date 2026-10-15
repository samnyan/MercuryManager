#!/usr/bin/env bash
set -Eeuo pipefail

# Portable MercuryManager development server helper.
# Usage: ./dev_server.sh {start|stop|restart|status}

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
PROJECT_DIR="$ROOT_DIR/src/MercuryManager.Server"
WORKSPACE_DIR="$PROJECT_DIR/bin/Debug/net10.0/workspace"
# The repo-local workspace must be a real directory, not a symlink: the server
# rejects linked workspace paths for security. Use a separate local workspace
# directory name rather than redirecting `workspace/` through a symlink.
[[ ! -L "$WORKSPACE_DIR" ]] || fail "workspace must be a real directory, not a symlink"
mkdir -p -- "$WORKSPACE_DIR"
WORKSPACE_DIR="$(cd -- "$WORKSPACE_DIR" && pwd -P)"
PID_FILE="$ROOT_DIR/.dev_server.pid"
LOG_FILE="$ROOT_DIR/.dev_server.log"
PORT="${MERCURY_PORT:-5087}"
HOST="${MERCURY_HOST:-0.0.0.0}"
ALLOWED_CLIENTS="${MERCURY_ALLOWED_CLIENTS:-100.0.0.0/8,127.0.0.1,::1}"

fail() { printf 'dev_server: %s\n' "$*" >&2; exit 1; }

[[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT >= 1 && PORT <= 65535 )) || fail "MERCURY_PORT must be between 1 and 65535"
[[ "$HOST" == "0.0.0.0" || "$HOST" == "127.0.0.1" || "$HOST" == "::" || "$HOST" == "::1" ]] || fail "MERCURY_HOST must be an IP literal"

read_pid() {
  [[ -f "$PID_FILE" ]] || return 1
  local value
  value="$(<"$PID_FILE")"
  [[ "$value" =~ ^[0-9]+$ ]] || fail "invalid PID file: $PID_FILE"
  printf '%s\n' "$value"
}

is_our_process() {
  local pid="$1" args
  [[ -r "/proc/$pid/cmdline" ]] || return 1
  args="$(tr '\0' ' ' <"/proc/$pid/cmdline")"
  [[ "$args" == *"$PROJECT_DIR"* && "$args" == *"--port $PORT"* && "$args" == *"--allowed-clients $ALLOWED_CLIENTS"* ]] || return 1
  # Script-managed servers also carry the explicit workspace-root argument.
  # Accept this repo's previous launch invocation during first takeover.
  [[ "$args" == *"--workspace-root $WORKSPACE_DIR"* ]] || [[ "$args" == *"--no-launch"* ]]
}

listener_pids() {
  if command -v ss >/dev/null 2>&1; then
    ss -ltnp "sport = :$PORT" 2>/dev/null | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u || true
  elif command -v fuser >/dev/null 2>&1; then
    fuser -n tcp "$PORT" 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+$' | sort -u || true
  else
    fail "need ss or fuser to inspect port $PORT safely"
  fi
}

stop_server() {
  local pid=""
  if pid="$(read_pid)"; then
    if kill -0 "$pid" 2>/dev/null; then
      is_our_process "$pid" || fail "PID $pid is not this repository's configured server; refusing to stop it"
      printf 'Stopping MercuryManager (PID %s)...\n' "$pid"
      kill "$pid"
      for _ in {1..50}; do
        kill -0 "$pid" 2>/dev/null || break
        sleep 0.1
      done
      if kill -0 "$pid" 2>/dev/null; then
        kill -TERM "$pid" 2>/dev/null || true
        for _ in {1..20}; do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
      fi
      kill -0 "$pid" 2>/dev/null && fail "PID $pid did not stop; inspect it manually"
    fi
    rm -f -- "$PID_FILE"
  fi

  local listening_pid
  while IFS= read -r listening_pid; do
    [[ -n "$listening_pid" ]] || continue
    is_our_process "$listening_pid" || fail "port $PORT is occupied by unrelated PID $listening_pid; refusing to kill it"
    kill "$listening_pid" 2>/dev/null || true
  done < <(listener_pids)
}

start_server() {
  [[ -f "$PROJECT_DIR/MercuryManager.Server.csproj" ]] || fail "run this script from a MercuryManager repository checkout"
  mkdir -p -- "$WORKSPACE_DIR"

  local listening_pid
  while IFS= read -r listening_pid; do
    [[ -n "$listening_pid" ]] || continue
    if is_our_process "$listening_pid"; then
      printf 'MercuryManager already running (PID %s) on %s:%s\n' "$listening_pid" "$HOST" "$PORT"
      printf '%s\n' "$listening_pid" >"$PID_FILE"
      return 0
    fi
    fail "port $PORT is occupied by unrelated PID $listening_pid; refusing to kill it"
  done < <(listener_pids)

  printf 'Building frontend...\n'
  pnpm --dir "$ROOT_DIR/web" build
  printf 'Building server...\n'
  dotnet build "$PROJECT_DIR/MercuryManager.Server.csproj" --no-restore

  printf 'Starting MercuryManager at http://%s:%s (workspace: %s)\n' "$HOST" "$PORT" "$WORKSPACE_DIR"
  nohup dotnet run --no-build --project "$PROJECT_DIR/MercuryManager.Server.csproj" -- \
    --host "$HOST" --port "$PORT" --no-launch \
    --allowed-clients "$ALLOWED_CLIENTS" --workspace-root "$WORKSPACE_DIR" \
    >>"$LOG_FILE" 2>&1 </dev/null &
  local launcher_pid=$!
  printf '%s\n' "$launcher_pid" >"$PID_FILE"

  for _ in {1..100}; do
    if ! kill -0 "$launcher_pid" 2>/dev/null; then
      rm -f -- "$PID_FILE"
      tail -n 80 "$LOG_FILE" >&2 || true
      fail "server process exited during startup"
    fi
    if curl --silent --show-error --fail "http://127.0.0.1:$PORT/" -o /dev/null 2>/dev/null; then
      printf 'MercuryManager ready: http://127.0.0.1:%s/\n' "$PORT"
      return 0
    fi
    sleep 0.2
  done
  tail -n 80 "$LOG_FILE" >&2 || true
  fail "server did not become ready within 20 seconds (PID $launcher_pid)"
}

status_server() {
  local pid
  while IFS= read -r pid; do
    [[ -n "$pid" ]] || continue
    if is_our_process "$pid"; then
      printf 'MercuryManager running (PID %s) at %s:%s\n' "$pid" "$HOST" "$PORT"
      return 0
    fi
    printf 'Port %s occupied by unrelated PID %s\n' "$PORT" "$pid"
    return 1
  done < <(listener_pids)
  printf 'MercuryManager is not running on port %s\n' "$PORT"
  return 1
}

command="${1:-}"
case "$command" in
  start) start_server ;;
  stop) stop_server ;;
  restart) stop_server; start_server ;;
  status) status_server ;;
  *) printf 'Usage: %s {start|stop|restart|status}\n' "$0" >&2; exit 2 ;;
esac
