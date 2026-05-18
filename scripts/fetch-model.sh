#!/usr/bin/env bash
# Downloads the all-MiniLM-L6-v2 ONNX model and tokenizer vocabulary from HuggingFace.
# Run from the repo root: bash scripts/fetch-model.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODELS_DIR="$SCRIPT_DIR/../src/ContextOS.Embeddings/Models"

mkdir -p "$MODELS_DIR"

MODEL_PATH="$MODELS_DIR/all-MiniLM-L6-v2.onnx"
VOCAB_PATH="$MODELS_DIR/vocab.txt"

HF_BASE="https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main"

if [ -f "$MODEL_PATH" ]; then
  echo "Model already present: $MODEL_PATH"
else
  echo "Downloading all-MiniLM-L6-v2 ONNX model (~22 MB)..."
  curl -L --progress-bar "$HF_BASE/onnx/model.onnx" -o "$MODEL_PATH"
  echo "Saved: $MODEL_PATH"
fi

if [ -f "$VOCAB_PATH" ]; then
  echo "Vocab already present: $VOCAB_PATH"
else
  echo "Downloading vocab.txt..."
  curl -L --progress-bar "$HF_BASE/vocab.txt" -o "$VOCAB_PATH"
  echo "Saved: $VOCAB_PATH"
fi

echo ""
echo "Done. Build and run 'dotnet test' to verify."
