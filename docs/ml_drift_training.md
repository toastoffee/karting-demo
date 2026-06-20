# ML Drift Training

Recommended training scene:
`Assets/Karting/Scenes/MLTraining/KartClassic_Training.unity`

This scene already contains the `KartClassic_MLAgent` setup and is the best starting point for learning the drift-enabled kart behavior.

## What changed for drift learning

- The agent now has a dedicated drift/brake action branch, so it can steer, accelerate, and hold drift at the same time.
- Observations now include lateral velocity, drift state, turbo state, and current drift duration.
- Rewards now include:
  - drift input reward
  - sustained drift reward
  - drift-exit turbo reward

## Trainer config

Training config:
`Assets/Karting/Prefabs/AI/kart_mg_trainer_config.yaml`

The `ArcadeDriver` block is tuned for drift learning with:
- shorter control delay through a lower `DecisionPeriod`
- larger PPO buffer
- normalized observations
- longer horizon for corner entry, sustain, and drift exit

## Python package

The local Unity package is `com.unity.ml-agents 2.0.1`.
The companion Python trainer version recommended by the local package docs is:

```bash
python -m pip install mlagents==0.26.0
```

## Training commands

From the project root, using the Unity Editor as the environment:

```bash
./scripts/train_arcade_drift_ai.sh
```

With a custom run id:

```bash
./scripts/train_arcade_drift_ai.sh arcade_drift_v1
```

With a built standalone player:

```bash
./scripts/train_arcade_drift_ai.sh arcade_drift_build /absolute/path/to/YourKartBuild
```

## Recommended workflow

1. Activate a Python environment that has `mlagents` installed.
2. Run `./scripts/train_arcade_drift_ai.sh`.
3. Open `Assets/Karting/Scenes/MLTraining/KartClassic_Training.unity`.
4. Press Play in Unity after the trainer starts listening.
5. Watch results under `results/<run-id>`.

## TensorBoard

```bash
tensorboard --logdir results
```
