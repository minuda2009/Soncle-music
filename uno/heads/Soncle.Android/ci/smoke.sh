#!/usr/bin/env bash
# Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
#
# Emulator smoke test for the Uno Android head (Soncle Uno), run by
# .github/workflows/uno-android-preview.yml inside reactivecircus/android-emulator-runner, which runs
# each line of its `script:` as a separate command; that is why this logic lives in a file.
#
# It installs the APK, cold-starts the app, waits for the head's `first-frame-ms=` log line, checks
# the process is alive, takes a screenshot and the idle memory; then restarts it with the
# `soncle.demo=play` extra, waits for `audio-started`, lets the test sound play for ~10 s and takes
# the memory and a screenshot again. It fails if the app crashes, never draws, or never starts the
# AudioTrack. Numbers go to $GITHUB_STEP_SUMMARY, labelled as emulator numbers (not a phone).
#
# Only the app's own `Soncle` log tag and the crash buffer for this package are saved; the full
# device log is never written out.
#
# Usage: smoke.sh <apk> [output-dir]

set -euo pipefail

PKG=com.minuda2009.soncle.uno
APK=${1:?usage: smoke.sh <apk> [output-dir]}
OUT=${2:-smoke}
mkdir -p "$OUT"

summary() {
  printf '%s\n' "$*"
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"; fi
}

# adb shell output can carry \r line endings.
sh_adb() { adb shell "$@" | tr -d '\r'; }

soncle_log() { adb logcat -d -s Soncle:V | tr -d '\r'; }

APP_UID=""
crash_log() {
  # The crash buffer, limited to this app: by uid when logcat supports it, otherwise only the
  # entries that name the package's process.
  if [ -n "$APP_UID" ] && adb logcat -d -b crash --uid="$APP_UID" > "$OUT/.crash" 2>/dev/null; then
    tr -d '\r' < "$OUT/.crash" | grep -v '^--------- beginning of' || true
  else
    adb logcat -d -b crash | tr -d '\r' | grep -A 60 -F "Process: $PKG" || true
  fi
  rm -f "$OUT/.crash"
}

save_logs() {
  soncle_log > "$OUT/$1-soncle.log" || true
  crash_log > "$OUT/$1-crash.log" || true
}

fail() {
  echo "::error::$*"
  save_logs failure
  summary "**Smoke test failed:** $*"
  exit 1
}

check_no_crash() {
  local crashes
  crashes=$(crash_log)
  if [ -n "$crashes" ]; then
    printf '%s\n' "$crashes" | sed -n '1,40p'
    fail "$1: the app crashed (see the crash log in the soncle-uno-smoke artifact)"
  fi
}

check_alive() {
  local pid
  pid=$(sh_adb pidof "$PKG" || true)
  [ -n "$pid" ] || fail "$1: the $PKG process is not running"
}

# Total PSS in kB from `dumpsys meminfo` (the "TOTAL PSS:" summary line on current Android,
# the "TOTAL" table row otherwise).
mem_kb() {
  local info kb
  info=$(sh_adb dumpsys meminfo "$PKG")
  kb=$(printf '%s\n' "$info" | awk '/TOTAL PSS:/ { for (i = 1; i <= NF; i++) if ($i == "PSS:") { print $(i + 1); exit } }')
  if [ -z "$kb" ]; then
    kb=$(printf '%s\n' "$info" | awk '$1 == "TOTAL" && $2 ~ /^[0-9]+$/ { print $2; exit }')
  fi
  printf '%s' "$kb"
}

mb() { awk -v kb="$1" 'BEGIN { if (kb == "") print "n/a"; else printf "%.1f MB", kb / 1024 }'; }

# Waits up to $2 seconds for a Soncle log line matching the sed expression $1 and leaves the capture
# in $FOUND (a global, not a command substitution, so a crash detected here really exits the script).
FOUND=""
wait_for_line() {
  local expr=$1 limit=$2 what=$3 i
  FOUND=""
  for i in $(seq 1 "$limit"); do
    FOUND=$(soncle_log | sed -n "$expr" | tail -n 1)
    [ -n "$FOUND" ] && return 0
    check_no_crash "$what"
    sleep 1
  done
  return 0
}

echo "Installing $APK"
adb install -r "$APK" > /dev/null
APP_UID=$(sh_adb cmd package list packages -U "$PKG" | sed -n "s/^package:$PKG uid:\([0-9]*\).*/\1/p" | head -n 1)

ACT=$(sh_adb cmd package resolve-activity --brief "$PKG" | tail -n 1)
case "$ACT" in
  "$PKG"/*) ;;
  *) fail "no launcher activity resolved for $PKG (got '$ACT')" ;;
esac
echo "Launcher activity: $ACT"

# ---- 1. cold start to the shell ------------------------------------------------------------------
sh_adb am force-stop "$PKG"
adb logcat -c
adb logcat -b crash -c || true

start_out=$(sh_adb am start -W -n "$ACT")
printf '%s\n' "$start_out"
total_ms=$(printf '%s\n' "$start_out" | sed -n 's/^TotalTime: *\([0-9]*\).*/\1/p' | tail -n 1)

wait_for_line 's/.*first-frame-ms=\([0-9]*\).*/\1/p' 120 "cold start"
first_frame=$FOUND
[ -n "$first_frame" ] || fail "no first-frame-ms line within 120 s of launch"
check_alive "after the first frame"

sleep 5
check_no_crash "idle"
check_alive "idle"
adb exec-out screencap -p > "$OUT/shell.png"
idle_kb=$(mem_kb)
save_logs shell

# ---- 2. the test sound via the demo extra --------------------------------------------------------
sh_adb am force-stop "$PKG"
adb logcat -c
sh_adb am start -W -n "$ACT" --es soncle.demo play > /dev/null

wait_for_line 's/.*\(audio-started .*\)$/\1/p' 60 "demo start"
audio=$FOUND
if [ -z "$audio" ]; then
  if soncle_log | grep 'audio-focus-denied' > /dev/null; then
    fail "audio focus was denied, the test sound did not start"
  fi
  fail "no audio-started line within 60 s of the demo launch"
fi
decoded=$(soncle_log | sed -n 's/.*\(sample-decoded .*\)$/\1/p' | tail -n 1)

sleep 10
check_no_crash "while playing"
check_alive "while playing"
if soncle_log | grep 'audio-focus-lost' > /dev/null; then
  fail "audio focus was lost while the test sound played"
fi
if soncle_log | grep -E 'audio-(write|thread)-failed' > /dev/null; then
  fail "the audio device failed while the test sound played"
fi
adb exec-out screencap -p > "$OUT/playing.png"
playing_kb=$(mem_kb)
save_logs playing

sh_adb am force-stop "$PKG"

summary "### Soncle Uno smoke test (emulator numbers, not a phone)"
summary ""
summary "Android emulator, API 34, x86_64, google_apis, on ubuntu-latest (KVM). Start-up and memory on"
summary "a shared VM are only a smoke signal; the docs/WINUI_PLAN.md §9 gate is measured on a phone."
summary ""
summary "| Measure | Emulator |"
summary "| --- | --- |"
summary "| am start -W TotalTime (cold) | ${total_ms:-n/a} ms |"
summary "| first-frame-ms (process start to first Uno frame) | ${first_frame} ms |"
summary "| Total PSS idle (5 s after first frame) | $(mb "$idle_kb") |"
summary "| Total PSS while the test sound plays (~10 s) | $(mb "$playing_kb") |"
summary "| ${decoded:-sample-decoded: n/a} | |"
summary "| ${audio} | |"
