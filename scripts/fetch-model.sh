#!/usr/bin/env bash
# Downloads the embedding model Pounce uses into models/ (git-ignored).
# int8 ONNX export by Xenova of BAAI/bge-small-en-v1.5 (MIT), tokenizer from BAAI.
set -euo pipefail
cd "$(dirname "$0")/.."
DIR=models/bge-small-en-v1.5
mkdir -p "$DIR"
curl -fSL -o "$DIR/model.onnx" https://huggingface.co/Xenova/bge-small-en-v1.5/resolve/main/onnx/model_int8.onnx
curl -fSL -o "$DIR/tokenizer.json" https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main/tokenizer.json
echo "Model ready in $DIR"
