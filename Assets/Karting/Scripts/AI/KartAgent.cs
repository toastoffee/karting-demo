using KartGame.KartSystems;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KartGame.AI
{
    /// <summary>
    /// Sensors hold information such as the position of rotation of the origin of the raycast and its hit threshold
    /// to consider a "crash".
    /// </summary>
    [System.Serializable]
    public struct Sensor
    {
        public Transform Transform;
        public float RayDistance;
        public float HitValidationDistance;
    }

    /// <summary>
    /// We only want certain behaviours when the agent runs.
    /// Training would allow certain functions such as OnAgentReset() be called and execute, while Inferencing will
    /// assume that the agent will continuously run and not reset.
    /// </summary>
    public enum AgentMode
    {
        Training,
        Inferencing
    }

    /// <summary>
    /// The KartAgent will drive the inputs for the KartController.
    /// </summary>
    public class KartAgent : Agent, IInput
    {
#region Training Modes
        [Tooltip("Are we training the agent or is the agent production ready?")]
        public AgentMode Mode = AgentMode.Training;
        [Tooltip("What is the initial checkpoint the agent will go to? This value is only for inferencing.")]
        public ushort InitCheckpointIndex;

#endregion

#region Senses
        [Header("Observation Params")]
        [Tooltip("What objects should the raycasts hit and detect?")]
        public LayerMask Mask;
        [Tooltip("Sensors contain ray information to sense out the world, you can have as many sensors as you need.")]
        public Sensor[] Sensors;
        [Header("Checkpoints"), Tooltip("What are the series of checkpoints for the agent to seek and pass through?")]
        public Collider[] Colliders;
        [Tooltip("What layer are the checkpoints on? This should be an exclusive layer for the agent to use.")]
        public LayerMask CheckpointMask;

        [Space]
        [Tooltip("Would the agent need a custom transform to be able to raycast and hit the track? " +
            "If not assigned, then the root transform will be used.")]
        public Transform AgentSensorTransform;
#endregion

#region Rewards
        [Header("Rewards"), Tooltip("What penatly is given when the agent crashes?")]
        public float HitPenalty = -1f;
        [Tooltip("How much reward is given when the agent successfully passes the checkpoints?")]
        public float PassCheckpointReward;
        [Tooltip("Should typically be a small value, but we reward the agent for moving in the right direction.")]
        public float TowardsCheckpointReward;
        [Tooltip("Typically if the agent moves faster, we want to reward it for finishing the track quickly.")]
        public float SpeedReward;
        [Tooltip("Reward the agent when it keeps accelerating")]
        public float AccelerationReward;
        [Space]
        [Tooltip("Small reward for attempting drift input while carrying speed into a corner.")]
        public float DriftInputReward = 0.004f;
        [Tooltip("Reward applied while the kart sustains a useful drift towards the next checkpoint.")]
        public float DriftMaintainReward = 0.008f;
        [Tooltip("Bonus reward when a drift exits into a turbo boost.")]
        public float DriftTurboReward = 0.25f;
        [Tooltip("Lateral speed at which the drift reward reaches full strength.")]
        public float DriftLateralSpeedRewardThreshold = 3.0f;
        #endregion

        #region ResetParams
        [Header("Inference Reset Params")]
        [Tooltip("What is the unique mask that the agent should detect when it falls out of the track?")]
        public LayerMask OutOfBoundsMask;
        [Tooltip("What are the layers we want to detect for the track and the ground?")]
        public LayerMask TrackMask;
        [Tooltip("How far should the ray be when casted? For larger karts - this value should be larger too.")]
        public float GroundCastDistance;
#endregion

#region Debugging
        [Header("Debug Option")] [Tooltip("Should we visualize the rays that the agent draws?")]
        public bool ShowRaycasts;
#endregion

        ArcadeKart m_Kart;
        bool m_Acceleration;
        bool m_Brake;
        float m_Steering;
        int m_CheckpointIndex;

        bool m_EndEpisode;
        float m_LastAccumulatedReward;
        bool m_PreviousDriftTurboActive;

        void Awake()
        {
            m_Kart = GetComponent<ArcadeKart>();
            if (AgentSensorTransform == null) AgentSensorTransform = transform;
        }

        void Start()
        {
            // If the agent is training, then at the start of the simulation, pick a random checkpoint to train the agent.
            OnEpisodeBegin();

            if (Mode == AgentMode.Inferencing) m_CheckpointIndex = InitCheckpointIndex;
        }

        void Update()
        {
            if (m_EndEpisode)
            {
                m_EndEpisode = false;
                AddReward(m_LastAccumulatedReward);
                EndEpisode();
                OnEpisodeBegin();
            }
        }

        void LateUpdate()
        {
            switch (Mode)
            {
                case AgentMode.Inferencing:
                    if (ShowRaycasts) 
                        Debug.DrawRay(transform.position, Vector3.down * GroundCastDistance, Color.cyan);

                    // We want to place the agent back on the track if the agent happens to launch itself outside of the track.
                    if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, GroundCastDistance, TrackMask)
                        && ((1 << hit.collider.gameObject.layer) & OutOfBoundsMask) > 0)
                    {
                        // Reset the agent back to its last known agent checkpoint
                        if (!TryGetCheckpoint(m_CheckpointIndex, out var checkpointCollider))
                            break;

                        var checkpoint = checkpointCollider.transform;
                        transform.localRotation = checkpoint.rotation;
                        transform.position = checkpoint.position;
                        m_Kart.Rigidbody.velocity = default;
                        m_Steering = 0f;
                        m_Acceleration = m_Brake = false;
                    }

                    break;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var maskedValue = 1 << other.gameObject.layer;
            var triggered = maskedValue & CheckpointMask;

            if (triggered <= 0 || !HasCheckpointRoute())
                return;

            FindCheckpointIndex(other, out var index);

            // Ensure that the agent touched the checkpoint and the new index is greater than the m_CheckpointIndex.
            if ((index > m_CheckpointIndex) || (index == 0 && m_CheckpointIndex == Colliders.Length - 1))
            {
                AddReward(PassCheckpointReward);
                m_CheckpointIndex = index;
            }
        }

        void FindCheckpointIndex(Collider checkPoint, out int index)
        {
            for (int i = 0; i < Colliders.Length; i++)
            {
                if (Colliders[i] != null && Colliders[i].GetInstanceID() == checkPoint.GetInstanceID())
                {
                    index = i;
                    return;
                }
            }
            index = -1;
        }

        bool HasCheckpointRoute()
        {
            if (Colliders == null || Colliders.Length == 0)
                return false;

            for (int i = 0; i < Colliders.Length; i++)
            {
                if (Colliders[i] != null)
                    return true;
            }

            return false;
        }

        bool TryGetCheckpoint(int index, out Collider checkpoint)
        {
            checkpoint = null;

            if (Colliders == null || index < 0 || index >= Colliders.Length)
                return false;

            checkpoint = Colliders[index];
            return checkpoint != null;
        }

        bool TryGetNextCheckpoint(out Collider checkpoint, out int index)
        {
            checkpoint = null;
            index = -1;

            if (!HasCheckpointRoute())
                return false;

            for (int offset = 1; offset <= Colliders.Length; offset++)
            {
                var candidateIndex = (m_CheckpointIndex + offset) % Colliders.Length;
                if (TryGetCheckpoint(candidateIndex, out checkpoint))
                {
                    index = candidateIndex;
                    return true;
                }
            }

            return false;
        }

        float Sign(float value)
        {
            if (value > 0)
            {
                return 1;
            } 
            if (value < 0)
            {
                return -1;
            }
            return 0;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            sensor.AddObservation(m_Kart.LocalSpeed());

            // Add an observation for direction of the agent to the next checkpoint.
            var hasNextCheckpoint = TryGetNextCheckpoint(out var nextCollider, out _);
            var direction = hasNextCheckpoint
                ? (nextCollider.transform.position - m_Kart.transform.position).normalized
                : m_Kart.transform.forward;
            sensor.AddObservation(hasNextCheckpoint ? Vector3.Dot(m_Kart.Rigidbody.velocity.normalized, direction) : 0f);
            var localVelocity = m_Kart.transform.InverseTransformDirection(m_Kart.Rigidbody.velocity);
            sensor.AddObservation(Mathf.Clamp(localVelocity.x / Mathf.Max(m_Kart.GetMaxSpeed(), 0.001f), -1.0f, 1.0f));
            sensor.AddObservation(m_Steering);
            sensor.AddObservation(m_Acceleration);
            sensor.AddObservation(m_Brake);
            sensor.AddObservation(m_Kart.IsDrifting);
            sensor.AddObservation(m_Kart.HasActiveDriftTurbo);
            sensor.AddObservation(Mathf.Clamp01(m_Kart.DriftElapsedTime / Mathf.Max(m_Kart.DriftTurboDuration, 0.001f)));

            if (ShowRaycasts && hasNextCheckpoint)
                Debug.DrawLine(AgentSensorTransform.position, nextCollider.transform.position, Color.magenta);

            m_LastAccumulatedReward = 0.0f;
            m_EndEpisode = false;
            for (var i = 0; i < Sensors.Length; i++)
            {
                var current = Sensors[i];
                var xform = current.Transform;
                var hit = Physics.Raycast(AgentSensorTransform.position, xform.forward, out var hitInfo,
                    current.RayDistance, Mask, QueryTriggerInteraction.Ignore);

                if (ShowRaycasts)
                {
                    Debug.DrawRay(AgentSensorTransform.position, xform.forward * current.RayDistance, Color.green);
                    Debug.DrawRay(AgentSensorTransform.position, xform.forward * current.HitValidationDistance, 
                        Color.red);

                    if (hit && hitInfo.distance < current.HitValidationDistance)
                    {
                        Debug.DrawRay(hitInfo.point, Vector3.up * 3.0f, Color.blue);
                    }
                }

                if (hit)
                {
                    if (hitInfo.distance < current.HitValidationDistance)
                    {
                        m_LastAccumulatedReward += HitPenalty;
                        m_EndEpisode = true;
                    }
                }

                sensor.AddObservation(hit ? hitInfo.distance : current.RayDistance);
            }

        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            base.OnActionReceived(actions);
            InterpretDiscreteActions(actions);

            // Find the next checkpoint when registering the current checkpoint that the agent has passed.
            if (!TryGetNextCheckpoint(out var nextCollider, out _))
            {
                m_PreviousDriftTurboActive = m_Kart.HasActiveDriftTurbo;
                return;
            }

            var direction = (nextCollider.transform.position - m_Kart.transform.position).normalized;
            var reward = Vector3.Dot(m_Kart.Rigidbody.velocity.normalized, direction);

            if (ShowRaycasts) Debug.DrawRay(AgentSensorTransform.position, m_Kart.Rigidbody.velocity, Color.blue);

            // Add rewards if the agent is heading in the right direction
            AddReward(reward * TowardsCheckpointReward);
            AddReward((m_Acceleration && !m_Brake ? 1.0f : 0.0f) * AccelerationReward);
            AddReward(m_Kart.LocalSpeed() * SpeedReward);

            var localVelocity = m_Kart.transform.InverseTransformDirection(m_Kart.Rigidbody.velocity);
            var driftHeading = Mathf.Clamp01((reward + 1.0f) * 0.5f);
            var lateralDriftFactor = Mathf.Clamp01(Mathf.Abs(localVelocity.x) / Mathf.Max(DriftLateralSpeedRewardThreshold, 0.001f));

            if (m_Brake && Mathf.Abs(m_Steering) > 0.01f && m_Kart.LocalSpeed() > 0.2f)
                AddReward(DriftInputReward * driftHeading);

            if (m_Kart.IsDrifting)
                AddReward(DriftMaintainReward * driftHeading * lateralDriftFactor);

            if (!m_PreviousDriftTurboActive && m_Kart.HasActiveDriftTurbo)
                AddReward(DriftTurboReward);

            m_PreviousDriftTurboActive = m_Kart.HasActiveDriftTurbo;
        }

        public override void OnEpisodeBegin()
        {
            switch (Mode)
            {
                case AgentMode.Training:
                    if (!HasCheckpointRoute())
                        break;

                    m_CheckpointIndex = Random.Range(0, Colliders.Length - 1);
                    if (!TryGetCheckpoint(m_CheckpointIndex, out var collider))
                        break;

                    transform.localRotation = collider.transform.rotation;
                    transform.position = collider.transform.position;
                    m_Kart.Rigidbody.velocity = default;
                    m_Acceleration = false;
                    m_Brake = false;
                    m_Steering = 0f;
                    m_PreviousDriftTurboActive = false;
                    break;
                default:
                    break;
            }
        }

        void InterpretDiscreteActions(ActionBuffers actions)
        {
            m_Steering = actions.DiscreteActions[0] - 1f;
            m_Acceleration = actions.DiscreteActions[1] >= 1.0f;
            m_Brake = actions.DiscreteActions[2] >= 1.0f;
        }

        public InputData GenerateInput()
        {
            return new InputData
            {
                Accelerate = m_Acceleration,
                Brake = m_Brake,
                TurnInput = m_Steering
            };
        }
    }
}
