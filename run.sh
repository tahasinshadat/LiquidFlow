#!/usr/bin/env bash
#
# Dev runner for the FluidVoice Windows port — build and launch without installing.
#
#   ./run.sh                                  build Debug, launch the app (detached)
#   ./run.sh --release                        build Release instead
#   ./run.sh --no-build                       skip the build, just launch what's in bin/
#   ./run.sh -- --selftest-stt clip.wav       pass args to the app (runs in the foreground)
#   ./run.sh -- --version
#
# Notes:
#   * FluidVoice is single-instance, so this stops any running copy (installed or a previous
#     dev launch) first — otherwise the new process would just signal the old one and exit.
#   * The GUI is launched detached; it lives in the tray. Close it from the tray's Quit, or
#     re-run this script (it stops the old one). Headless --selftest-* runs stay in the shell.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# locate the project whether this script sits at the repo root or inside windows/
PROJ_DIR=""
for c in "$SCRIPT_DIR/windows/src/FluidVoice.App" "$SCRIPT_DIR/src/FluidVoice.App"; do
  [ -f "$c/FluidVoice.App.csproj" ] && PROJ_DIR="$c" && break
done
if [ -z "$PROJ_DIR" ]; then
  echo "error: could not find FluidVoice.App.csproj under $SCRIPT_DIR" >&2
  exit 1
fi

# make the locally-installed .NET SDK visible (installed to ~/dotnet)
export PATH="$HOME/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet not found (looked in ~/dotnet and PATH)" >&2
  exit 1
fi

CONFIG=Debug
BUILD=1
APP_ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    -r|--release) CONFIG=Release; shift ;;
    --debug)      CONFIG=Debug; shift ;;
    --no-build)   BUILD=0; shift ;;
    --)           shift; APP_ARGS=("$@"); break ;;
    -h|--help)    grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)            APP_ARGS+=("$1"); shift ;;
  esac
done

# stop any running instance so this build owns the single-instance slot
if tasklist //FI "IMAGENAME eq FluidVoice.exe" 2>/dev/null | grep -qi "FluidVoice.exe"; then
  echo "==> stopping running FluidVoice instance(s)"
  powershell.exe -NoProfile -Command "Get-Process FluidVoice -EA SilentlyContinue | Stop-Process -Force" >/dev/null 2>&1 || true
  sleep 1
fi

if [ "$BUILD" = "1" ]; then
  echo "==> building ($CONFIG)"
  dotnet build "$PROJ_DIR/FluidVoice.App.csproj" -c "$CONFIG" -v minimal
fi

# resolve the built exe (framework-dependent build → no RID subfolder)
EXE="$(ls "$PROJ_DIR"/bin/"$CONFIG"/net*-windows*/FluidVoice.exe 2>/dev/null | head -1 || true)"
if [ -z "$EXE" ] || [ ! -f "$EXE" ]; then
  echo "error: FluidVoice.exe not found under $PROJ_DIR/bin/$CONFIG — run without --no-build" >&2
  exit 1
fi
EXE_WIN="$(cygpath -w "$EXE" 2>/dev/null || echo "$EXE")"

if [ ${#APP_ARGS[@]} -gt 0 ]; then
  # headless / arg mode — run in the foreground so console output is visible
  echo "==> running: FluidVoice.exe ${APP_ARGS[*]}"
  "$EXE" "${APP_ARGS[@]}"
else
  # GUI mode — launch detached; it lives in the tray
  echo "==> launching FluidVoice ($CONFIG) — look for the waveform icon in the tray"
  cmd //c start "" "$EXE_WIN"
fi
