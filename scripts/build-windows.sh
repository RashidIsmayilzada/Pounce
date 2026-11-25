#!/usr/bin/env bash
# Builds Pounce for Windows x64: the Launcher (Pounce.exe) with the Engine, a trimmed Java 25 runtime and the model.
#   scripts/build-windows.sh        build (reuses the Java runtime if the module list and JDK version are unchanged)
# Output: build/Pounce-windows-x64/Pounce/ and build/Pounce-<version>-windows-x64.zip with its .sha256. The Pounce folder
# holds Pounce.exe with the .NET files, runtime/ (the Java runtime), engine/pounce-engine.jar, models/ and
# native/onnxruntime/ (ONNX Runtime's DLLs, which the Launcher points the Engine at with -Donnxruntime.native.path,
# so that ONNX Runtime doesn't unpack them into %TEMP% on every start).
# Runs on Windows under Git Bash (as CI does), and on Linux, WSL or macOS by cross-building.
# Needs: JDK 25 (JAVA_HOME_25, or JAVA_HOME), Maven, the .NET 10 SDK, curl; and unzip when cross-building.
# Cross-building: jlink can only link the modules of a JDK with its own version, so the jmods of the Temurin
# Windows x64 JDK matching the local JDK exactly are downloaded once into build/cache/ and checked against
# the SHA-256 that Adoptium publishes.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build"
STAGE="$OUT/Pounce-windows-x64"
DEST="$STAGE/Pounce"
CACHE="$OUT/cache"
MODEL="$ROOT/models/bge-small-en-v1.5"
VERSION="${POUNCE_VERSION:-0.1.0}"   # set by the release workflow
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'build-windows.sh: %s\n' "$*" >&2; exit 1; }
sha256() { if command -v sha256sum >/dev/null; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }

case "$(uname -s)" in MINGW* | MSYS* | CYGWIN*) ON_WINDOWS=true ;; *) ON_WINDOWS=false ;; esac

JAVA_HOME="${JAVA_HOME_25:-${JAVA_HOME:-}}"
[ -n "$JAVA_HOME" ] || fail "set JAVA_HOME (or JAVA_HOME_25) to a JDK 25"
if $ON_WINDOWS; then JAVA_HOME="$(cygpath -u "$JAVA_HOME")"; fi
export JAVA_HOME
[ -f "$JAVA_HOME/release" ] || fail "$JAVA_HOME is not a JDK (no release file)"
# The exact runtime version, such as 25.0.4+7 (from JAVA_RUNTIME_VERSION="25.0.4+7-LTS").
JDK_VERSION=$(sed -n 's/^JAVA_RUNTIME_VERSION="\([^"-]*\).*"$/\1/p' "$JAVA_HOME/release" | tr -d '\r')
[ "${JDK_VERSION%%[.+]*}" = 25 ] || fail "JAVA_HOME must be a JDK 25; $JAVA_HOME is '${JDK_VERSION:-unknown}'"

# One module list for both platforms: the one in build-app.sh.
MODULES=$(sed -n 's/^MODULES="\(.*\)"$/\1/p' "$ROOT/scripts/build-app.sh")
[ -n "$MODULES" ] || fail "could not read MODULES=\"...\" from scripts/build-app.sh"

if command -v dotnet >/dev/null; then DOTNET=dotnet
elif [ -x "$HOME/.dotnet/dn" ]; then DOTNET="$HOME/.dotnet/dn"   # a wrapper that sets up ICU on this machine
elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
else fail "the .NET 10 SDK is needed: no dotnet on PATH or in ~/.dotnet"; fi

# The jmods of the Temurin Windows x64 JDK with exactly JDK_VERSION, unpacked under build/cache; prints their folder.
windows_jmods() {
  local feature="${JDK_VERSION%%[.+]*}"
  local name="OpenJDK${feature}U-jmods_x64_windows_hotspot_${JDK_VERSION/+/_}.zip"
  local url="https://github.com/adoptium/temurin${feature}-binaries/releases/download/jdk-${JDK_VERSION/+/%2B}/$name"
  local dir="$CACHE/windows-jmods-$JDK_VERSION"
  if [ ! -f "$dir/java.base.jmod" ]; then
    mkdir -p "$CACHE"
    if [ ! -f "$CACHE/$name" ]; then
      echo "Downloading $url" >&2
      curl -fSL --retry 3 -o "$CACHE/$name.part" "$url" >&2 \
        || fail "no Temurin Windows jmods for $JDK_VERSION; use a Temurin JDK 25 whose version Adoptium publishes"
      mv "$CACHE/$name.part" "$CACHE/$name"
    fi
    local expected actual
    expected=$(curl -fsSL --retry 3 "$url.sha256.txt" | awk '{print $1}')
    actual=$(sha256 "$CACHE/$name" | awk '{print $1}')
    [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || fail "could not read the published checksum $url.sha256.txt"
    [ "$actual" = "$expected" ] || { rm -f "$CACHE/$name"; fail "$name has SHA-256 $actual, Adoptium publishes $expected; deleted it"; }
    echo "SHA-256 verified: $name" >&2
    rm -rf "$dir" "$dir.tmp"
    unzip -q "$CACHE/$name" '*.jmod' -d "$dir.tmp"
    mkdir -p "$dir"
    find "$dir.tmp" -name '*.jmod' -exec mv {} "$dir/" \;
    rm -rf "$dir.tmp"
    [ -f "$dir/java.base.jmod" ] || fail "$name holds no java.base.jmod"
  fi
  echo "$dir"
}

[ -f "$MODEL/model.onnx" ] || { step "Fetching the embedding model"; "$ROOT/scripts/fetch-model.sh"; }

step "Building the Engine"
(cd "$ROOT" && mvn -B -q -DskipTests -pl pounce-api -am package)
shopt -s nullglob
jars=("$ROOT"/pounce-api/target/pounce-api-*.jar)
shopt -u nullglob
[ ${#jars[@]} -eq 1 ] || fail "expected one pounce-api/target/pounce-api-*.jar after the Maven build, found ${#jars[@]}"
ENGINE_JAR="${jars[0]}"

# ONNX Runtime's Windows DLLs, from the onnxruntime jar inside the Engine jar, so they always match the Java classes
# that load them. Without -Donnxruntime.native.path ONNX Runtime unpacks them from that jar into a new %TEMP% folder
# on every start; a loaded DLL can't be deleted, so about 20 MB would stay behind each time, and policies that forbid
# loading DLLs from %TEMP% would stop the Engine. With it, it loads onnxruntime.dll and onnxruntime4j_jni.dll from that
# folder (and fails if either is missing there) and uses onnxruntime_providers_shared.dll from it too (unpacking only
# that one into %TEMP% if it is missing). The JDK's jar tool, rather than unzip, so this works the same under Git Bash.
step "Taking ONNX Runtime's Windows DLLs from the Engine jar"
NATIVES="$OUT/onnxruntime-win-x64"
ORT_DLLS=(onnxruntime.dll onnxruntime4j_jni.dll onnxruntime_providers_shared.dll)
rm -rf "$NATIVES" "$NATIVES.tmp"
mkdir -p "$NATIVES.tmp"
ort_jars=$("$JAVA_HOME/bin/jar" --list --file "$ENGINE_JAR" | tr -d '\r' | grep -E '^BOOT-INF/lib/onnxruntime-[0-9][^/]*\.jar$' || true)
[ "$(printf '%s' "$ort_jars" | grep -c .)" -eq 1 ] \
  || fail "expected one BOOT-INF/lib/onnxruntime-<version>.jar in $ENGINE_JAR, found: ${ort_jars:-none}"
(cd "$NATIVES.tmp" && "$JAVA_HOME/bin/jar" --extract --file "$ENGINE_JAR" "$ort_jars" \
  && "$JAVA_HOME/bin/jar" --extract --file "$ort_jars" ai/onnxruntime/native/win-x64/)
for dll in "${ORT_DLLS[@]}"; do
  [ -f "$NATIVES.tmp/ai/onnxruntime/native/win-x64/$dll" ] \
    || fail "${ort_jars#BOOT-INF/lib/} has no ai/onnxruntime/native/win-x64/$dll; has its layout changed?"
done
mv "$NATIVES.tmp/ai/onnxruntime/native/win-x64" "$NATIVES"
rm -rf "$NATIVES.tmp"
echo "${ort_jars#BOOT-INF/lib/}: $(cd "$NATIVES" && echo *)"

step "Building the Launcher"
rm -rf "$STAGE"
mkdir -p "$DEST"
(cd "$ROOT/windows-app" && "$DOTNET" publish src/Pounce.Launcher -c Release -r win-x64 --self-contained true \
  -p:Version="$VERSION" -o "$DEST" -v quiet)
[ -f "$DEST/Pounce.exe" ] || fail "dotnet publish made no Pounce.exe"

RUNTIME="$OUT/windows-runtime-$(printf '%s %s' "$MODULES" "$JDK_VERSION" | sha256 | cut -c1-8)"
if [ ! -f "$RUNTIME/bin/java.exe" ]; then
  step "Making a trimmed Java $JDK_VERSION runtime for Windows x64"
  rm -rf "$OUT"/windows-runtime-*
  if $ON_WINDOWS; then
    module_path=()   # the local JDK's own modules
  else
    jmods=$(windows_jmods)
    module_path=(--module-path "$jmods")
  fi
  "$JAVA_HOME/bin/jlink" "${module_path[@]}" --add-modules "$MODULES" --strip-debug --no-header-files \
    --no-man-pages --compress=zip-6 --output "$RUNTIME"
  [ -f "$RUNTIME/bin/java.exe" ] || fail "jlink made no bin/java.exe; is the module path a Windows one?"
fi

step "Assembling Pounce for Windows"
mkdir -p "$DEST/engine" "$DEST/models" "$DEST/native"
cp -R "$RUNTIME" "$DEST/runtime"
cp "$ENGINE_JAR" "$DEST/engine/pounce-engine.jar"
cp -R "$MODEL" "$DEST/models/"
cp -R "$NATIVES" "$DEST/native/onnxruntime"

step "Zipping"
ZIP="Pounce-$VERSION-windows-x64.zip"
rm -f "$OUT/$ZIP" "$OUT/$ZIP.sha256"
# The JDK's jar tool writes a plain zip on every OS, so no zip tool is needed on Windows.
"$JAVA_HOME/bin/jar" --create --no-manifest --file "$OUT/$ZIP" -C "$STAGE" Pounce
# Written out, so the line reads "<hash>  <name>" like the macOS one, also where sha256sum would print "*<name>".
(cd "$OUT" && printf '%s  %s\n' "$(sha256 "$ZIP" | cut -d' ' -f1)" "$ZIP" > "$ZIP.sha256")

step "Done"
launcher_kb=$(du -sk "$DEST" | cut -f1)
for part in runtime engine models native; do launcher_kb=$((launcher_kb - $(du -sk "$DEST/$part" | cut -f1))); done
printf '%s\tPounce-windows-x64/Pounce/ (.NET Launcher: %s MB)\n' "$(du -sh "$DEST" | cut -f1)" "$((launcher_kb / 1024))"
du -sh "$DEST"/runtime "$DEST"/engine "$DEST"/models "$DEST"/native/onnxruntime "$OUT/$ZIP" | sed "s#$OUT/##"
cat "$OUT/$ZIP.sha256"
