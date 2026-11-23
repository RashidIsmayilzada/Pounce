#!/usr/bin/env bash
# Builds build/Pounce.app: the ⌥Space Launcher with the Engine, a trimmed Java 25 runtime and the model inside.
#   scripts/build-app.sh            build (reuses the Java runtime if the module list is unchanged)
# Needs: JDK 25 (JAVA_HOME, or the sdkman Temurin 25), Maven, Xcode's Swift toolchain.
# Signed ad hoc for this Mac only; Developer ID signing and notarization are for distribution.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build"
APP="$OUT/Pounce.app"
JAVA_HOME="${JAVA_HOME_25:-$HOME/.sdkman/candidates/java/25.0.5.1+2-tem}"
export JAVA_HOME
# From `jdeps --print-module-deps` over the Engine jar, plus extra charsets for PDFs and zipfs.
MODULES="java.base,java.compiler,java.desktop,java.instrument,java.management,java.naming,java.net.http,java.prefs,java.scripting,java.security.jgss,java.sql,jdk.incubator.vector,jdk.jfr,jdk.unsupported,jdk.charsets,jdk.zipfs"
MODEL="$ROOT/models/bge-small-en-v1.5"

step() { printf '\n==> %s\n' "$*"; }

[ -f "$MODEL/model.onnx" ] || { step "Fetching the embedding model"; "$ROOT/scripts/fetch-model.sh"; }

step "Building the Engine"
(cd "$ROOT" && mvn -B -q -DskipTests -pl pounce-api -am package)

step "Building the Launcher"
(cd "$ROOT/macos-app" && swift build -c release --product PounceLauncher 2>&1 | tail -1)

RUNTIME="$OUT/runtime-$(echo "$MODULES" | shasum | cut -c1-8)"
if [ ! -x "$RUNTIME/bin/java" ]; then
  step "Making a trimmed Java runtime"
  rm -rf "$OUT"/runtime-*
  "$JAVA_HOME/bin/jlink" --add-modules "$MODULES" --strip-debug --no-header-files --no-man-pages \
    --compress=zip-6 --output "$RUNTIME"
fi

step "Assembling Pounce.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/engine" "$APP/Contents/Resources/models"
cp "$ROOT/macos-app/.build/release/PounceLauncher" "$APP/Contents/MacOS/Pounce"
cp -R "$RUNTIME" "$APP/Contents/Resources/runtime"
cp "$ROOT/pounce-api/target/pounce-api-0.1.0-SNAPSHOT.jar" "$APP/Contents/Resources/engine/pounce-engine.jar"
cp -R "$MODEL" "$APP/Contents/Resources/models/"
WHY="Pounce indexes the files in this folder so you can search them. Nothing leaves your Mac."
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>dev.pounce.Pounce</string>
  <key>CFBundleName</key><string>Pounce</string>
  <key>CFBundleDisplayName</key><string>Pounce</string>
  <key>CFBundleExecutable</key><string>Pounce</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSDocumentsFolderUsageDescription</key><string>$WHY</string>
  <key>NSDesktopFolderUsageDescription</key><string>$WHY</string>
  <key>NSDownloadsFolderUsageDescription</key><string>$WHY</string>
</dict></plist>
PLIST

step "Signing (ad hoc)"
codesign --force --deep --sign - "$APP"
codesign --verify --deep "$APP"

step "Done"
du -sh "$APP" "$APP/Contents/Resources/"* | sed "s#$OUT/##"
