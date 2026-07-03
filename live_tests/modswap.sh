#!/usr/bin/env bash
#
# modswap.sh: fast enable/disable of a visual mod for kerbcast testing,
# without CKAN install/uninstall churn. Moves a mod's GameData folder(s)
# into a sibling staging dir (outside GameData, so KSP does not load it)
# and back. A KSP restart is required for the change to take effect, since
# KSP reads GameData only at startup.
#
# This is for the rare clean "mod absent" baseline. For routine isolation
# (capture on vs off with the mod present), prefer kerbcast's per-integration
# settings.cfg toggles plus a flight-scene re-entry: no restart, no file move.
#
# Usage:
#   KSP_GAMEDATA=/path/to/GameData ./modswap.sh status
#   KSP_GAMEDATA=/path/to/GameData ./modswap.sh disable scatterer
#   KSP_GAMEDATA=/path/to/GameData ./modswap.sh enable  scatterer
#   ./modswap.sh disable-all | enable-all
#
# If KSP_GAMEDATA is unset, the common Steam Deck path is tried.
#
# Mod keys: scatterer eve tufx firefly deferred parallax
# Note: a mod's hard dependencies (Kopernicus, ModuleManager, textures) are
# left in place; they are inert without the mod that uses them. EVE's "eve"
# key moves both the EVE engine and the Spectra cloud config so clouds fully
# disappear. Deferred ships under the folder "zzz_Deferred".

set -euo pipefail

GAMEDATA="${KSP_GAMEDATA:-$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program/GameData}"
STAGING="$(dirname "$GAMEDATA")/kerbcast-modtest-disabled"

# mod key -> space-separated GameData folder name(s)
folders_for() {
  case "$1" in
    scatterer) echo "Scatterer" ;;
    eve)       echo "EnvironmentalVisualEnhancements Spectra" ;;
    tufx)      echo "TUFX" ;;
    firefly)   echo "Firefly FireflyAPI" ;;
    deferred)  echo "zzz_Deferred" ;;
    parallax)  echo "ParallaxContinued" ;;
    *)         return 1 ;;
  esac
}

ALL_KEYS="scatterer eve tufx firefly deferred parallax"

die() { echo "modswap: $*" >&2; exit 1; }

[ -d "$GAMEDATA" ] || die "GameData not found at '$GAMEDATA'. Set KSP_GAMEDATA."

state_of() { # folder -> present | disabled | missing
  if [ -d "$GAMEDATA/$1" ]; then echo present
  elif [ -d "$STAGING/$1" ]; then echo disabled
  else echo missing; fi
}

cmd_status() {
  printf '%-10s %-12s %s\n' MOD STATE FOLDERS
  for key in $ALL_KEYS; do
    local agg="present" any=0
    for f in $(folders_for "$key"); do
      any=1
      case "$(state_of "$f")" in
        disabled) [ "$agg" = present ] && agg=disabled ;;
        missing)  agg=missing ;;
      esac
    done
    [ "$any" = 1 ] || agg="?"
    printf '%-10s %-12s %s\n' "$key" "$agg" "$(folders_for "$key")"
  done
  echo
  echo "GameData: $GAMEDATA"
  echo "Staging:  $STAGING"
}

move_one() { # src_dir dst_dir folder
  local src="$1/$3" dst="$2/$3"
  [ -d "$src" ] || return 0
  mkdir -p "$2"
  [ -e "$dst" ] && die "refusing to overwrite existing '$dst'"
  mv "$src" "$dst"
  echo "  moved $3 -> $(basename "$2")"
}

cmd_disable() {
  local key="$1"; folders_for "$key" >/dev/null || die "unknown mod '$key' (try: $ALL_KEYS)"
  echo "disabling $key (restart KSP to take effect):"
  for f in $(folders_for "$key"); do move_one "$GAMEDATA" "$STAGING" "$f"; done
}

cmd_enable() {
  local key="$1"; folders_for "$key" >/dev/null || die "unknown mod '$key' (try: $ALL_KEYS)"
  echo "enabling $key (restart KSP to take effect):"
  for f in $(folders_for "$key"); do move_one "$STAGING" "$GAMEDATA" "$f"; done
}

case "${1:-status}" in
  status)      cmd_status ;;
  disable)     [ $# -ge 2 ] || die "usage: modswap.sh disable <mod>"; cmd_disable "$2" ;;
  enable)      [ $# -ge 2 ] || die "usage: modswap.sh enable <mod>";  cmd_enable "$2" ;;
  disable-all) for k in $ALL_KEYS; do cmd_disable "$k"; done ;;
  enable-all)  for k in $ALL_KEYS; do cmd_enable "$k"; done ;;
  *)           die "unknown command '$1' (status|disable|enable|disable-all|enable-all)" ;;
esac
