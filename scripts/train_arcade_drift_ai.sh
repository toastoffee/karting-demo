#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG_PATH="$ROOT_DIR/Assets/Karting/Prefabs/AI/kart_mg_trainer_config.yaml"
RUN_ID="${1:-arcade_drift_$(date +%Y%m%d_%H%M%S)}"
ENV_PATH="${2:-}"
RESULTS_DIR="${ML_RESULTS_DIR:-$ROOT_DIR/results}"

if [[ -n "$ENV_PATH" ]]; then
  if command -v mlagents-learn >/dev/null 2>&1; then
    TRAIN_CMD=(mlagents-learn)
  elif python3 -c "import mlagents" >/dev/null 2>&1; then
    TRAIN_CMD=(python3 -m mlagents.trainers.learn)
  else
    cat <<'EOF'
ML-Agents Python trainer is not installed.

Recommended setup for this project:
  python3 -m venv .venv-mlagents
  source .venv-mlagents/bin/activate
  python -m pip install --upgrade pip
  python -m pip install mlagents==0.26.0

This version matches the local Unity ML-Agents package documentation for com.unity.ml-agents 2.0.1.
EOF
    exit 1
  fi
else
  if python3 -c "import mlagents" >/dev/null 2>&1; then
    TRAIN_CMD=(python3 "$ROOT_DIR/scripts/mlagents_editor_learn.py")
  else
    cat <<'EOF'
ML-Agents Python trainer is not installed.

Recommended setup for this project:
  python3 -m venv .venv-mlagents
  source .venv-mlagents/bin/activate
  python -m pip install --upgrade pip
  python -m pip install mlagents==0.26.0

This version matches the local Unity ML-Agents package documentation for com.unity.ml-agents 2.0.1.
EOF
    exit 1
  fi
fi

if [[ ${#TRAIN_CMD[@]} -eq 0 ]]; then
  cat <<'EOF'
ML-Agents Python trainer is not installed.

Recommended setup for this project:
  python3 -m venv .venv-mlagents
  source .venv-mlagents/bin/activate
  python -m pip install --upgrade pip
  python -m pip install mlagents==0.26.0

This version matches the local Unity ML-Agents package documentation for com.unity.ml-agents 2.0.1.
EOF
  exit 1
fi

mkdir -p "$RESULTS_DIR"

CMD=(
  "${TRAIN_CMD[@]}"
  "$CONFIG_PATH"
  "--run-id=$RUN_ID"
  "--results-dir=$RESULTS_DIR"
  "--force"
  "--time-scale=20"
)

if [[ -n "$ENV_PATH" ]]; then
  CMD+=("--env=$ENV_PATH" "--no-graphics")
  echo "Starting drift training against player build:"
  echo "  env: $ENV_PATH"
else
  echo "Starting drift training against the Unity Editor."
  echo "Open scene: Assets/Karting/Scenes/MLTraining/KartClassic_Training.unity"
  echo "This path uses the single-process editor runner for macOS compatibility."
fi

echo "Run ID: $RUN_ID"
echo "Results: $RESULTS_DIR"
echo
"${CMD[@]}"
