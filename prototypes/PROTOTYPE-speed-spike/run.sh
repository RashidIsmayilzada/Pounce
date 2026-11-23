#!/usr/bin/env bash
# PROTOTYPE — throwaway speed/RAM spike. Usage: ./run.sh [int8|fp32] [scaleDocs]
set -euo pipefail
cd "$(dirname "$0")"
VARIANT="${1:-int8}"; SCALE="${2:-50000}"
SCRATCH="${POUNCE_SCRATCH:?set POUNCE_SCRATCH to a scratch folder holding models-bge/}"
MODEL="$SCRATCH/models-bge/model_$VARIANT.onnx"; TOKENIZER="$SCRATCH/models-bge/tokenizer.json"
INDEX="$SCRATCH/PROTOTYPE-wipe-me-index-$VARIANT"
export JAVA_HOME="${SPIKE_JAVA_HOME:-/Library/Java/JavaVirtualMachines/temurin-24.jdk/Contents/Home}"  # needs JDK 24+
export DJL_OFFLINE=true OPT_OUT_TRACKING=true DJL_CACHE_DIR="$SCRATCH/djlcache"

[ -d target/lib ] || mvn -q dependency:copy-dependencies -DoutputDirectory=target/lib
mkdir -p target/classes
"$JAVA_HOME/bin/javac" -d target/classes -cp "target/lib/*" src/main/java/Spike.java
JVM=(-Xmx192m -XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:+UseCompactObjectHeaders
     -XX:G1PeriodicGCInterval=5000 --add-modules jdk.incubator.vector --enable-native-access=ALL-UNNAMED
     -cp "target/classes:target/lib/*")

echo "=== INDEX ($VARIANT, $SCALE docs) ==="
"$JAVA_HOME/bin/java" -Xmx512m "${JVM[@]:1}" Spike index "$INDEX" "$MODEL" "$TOKENIZER" "$SCALE" \
  "$HOME/projects" "$HOME/Documents" 2>&1 | grep -v "WARNING"
echo; echo "=== SEARCH ($VARIANT) — fresh process, -Xmx192m ==="
"$JAVA_HOME/bin/java" "${JVM[@]}" Spike search "$INDEX" "$MODEL" "$TOKENIZER" 2>&1 | grep -v "WARNING"
