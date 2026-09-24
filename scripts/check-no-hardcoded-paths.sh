#!/usr/bin/env bash
# Fails if the sources contain paths, user names or machine names baked in at build time.
# Everything must be discovered at runtime on the PC where the app runs
# (Steam registry, libraryfolders.vdf, %LOCALAPPDATA%\Packages, all userdata accounts).
set -u
cd "$(dirname "$0")/.."
status=0
check() { # $1 = description, $2 = PCRE, rest = paths
  local desc="$1" pattern="$2"; shift 2
  local hits
  hits=$(grep -rnIP --exclude=check-no-hardcoded-paths.sh "$pattern" "$@" 2>/dev/null)
  if [ -n "$hits" ]; then echo "FAIL: $desc"; echo "$hits"; status=1; else echo "PASS: $desc"; fi
}
# Personal data: forbidden everywhere in code, scripts and tests.
check "no user profile paths (\\Users\\)" '(?i)[\\/]Users[\\/]' src scripts tests build.ps1
check "no personal user name (Denis, except the publisher name 'Denis Ugarov')" 'Denis(?! Ugarov)' src scripts tests build.ps1
check "no OneDrive paths" '(?i)OneDrive' src scripts tests build.ps1
check "no machine names" '(?i)DESKTOP-[A-Z0-9]+' src scripts tests build.ps1
check "no Linux home paths" '/home/' src scripts tests build.ps1
# Absolute drive paths: forbidden in app code and build scripts (tests may use synthetic ones).
check "no absolute drive paths in app code and build" '(?<![A-Za-z])[A-Za-z]:\\\\?[\\/]?[A-Za-z]' src scripts/*.cs build.ps1
exit $status
