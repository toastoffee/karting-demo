using KartGame.KartSystems;
using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace KartGame.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(ArcadeKart))]
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkKartPlayer : NetworkBehaviour
    {
        [SerializeField] float nameplateHeight = 1.8f;

        ArcadeKart m_Kart;
        Rigidbody m_Rigidbody;
        BaseInput[] m_InputProviders;
        Collider[] m_Colliders;
        WheelCollider[] m_WheelColliders;
        Renderer[] m_Renderers;
        Behaviour[] m_VisualBehaviours;
        TextMesh m_Nameplate;
        string m_OriginalTag;

        void Awake()
        {
            m_Kart = GetComponent<ArcadeKart>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_InputProviders = GetComponents<BaseInput>();
            m_Colliders = GetComponentsInChildren<Collider>(true);
            m_WheelColliders = GetComponentsInChildren<WheelCollider>(true);
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_VisualBehaviours = new Behaviour[]
            {
                GetComponent<KartAnimation>(),
                GetComponent<KartPlayerAnimator>()
            };
            m_OriginalTag = gameObject.tag;
            EnsureNameplate();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnershipState();
            ApplyVisuals();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (CompareTag("Player") && !string.IsNullOrEmpty(m_OriginalTag))
                gameObject.tag = m_OriginalTag;
        }

        void LateUpdate()
        {
            if (m_Nameplate == null || Camera.main == null)
                return;

            m_Nameplate.transform.position = transform.position + (Vector3.up * nameplateHeight);
            m_Nameplate.transform.forward = Camera.main.transform.forward;
        }

        void ApplyOwnershipState()
        {
            bool isLocalOwner = IsOwner;

            if (m_Kart != null)
            {
                m_Kart.enabled = isLocalOwner;
                m_Kart.SetCanMove(isLocalOwner);
            }

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = !isLocalOwner;
                m_Rigidbody.interpolation = isLocalOwner ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (!isLocalOwner)
                {
                    m_Rigidbody.velocity = Vector3.zero;
                    m_Rigidbody.angularVelocity = Vector3.zero;
                }
            }

            for (int i = 0; i < m_InputProviders.Length; i++)
            {
                if (m_InputProviders[i] != null)
                    m_InputProviders[i].enabled = isLocalOwner;
            }

            for (int i = 0; i < m_Colliders.Length; i++)
            {
                if (m_Colliders[i] != null)
                    m_Colliders[i].enabled = isLocalOwner;
            }

            for (int i = 0; i < m_WheelColliders.Length; i++)
            {
                if (m_WheelColliders[i] != null)
                    m_WheelColliders[i].enabled = isLocalOwner;
            }

            for (int i = 0; i < m_VisualBehaviours.Length; i++)
            {
                if (m_VisualBehaviours[i] != null)
                    m_VisualBehaviours[i].enabled = isLocalOwner;
            }

            gameObject.tag = isLocalOwner ? "Player" : "Untagged";

            if (isLocalOwner)
            {
                AttachCamera();
                var bootstrap = FindObjectOfType<MainSceneMultiplayerBootstrap>();
                if (bootstrap != null && m_Kart != null)
                    bootstrap.RegisterLocalPlayer(m_Kart);
            }
        }

        void AttachCamera()
        {
            Type virtualCameraType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine");
            if (virtualCameraType == null)
                return;

            Component virtualCamera = FindObjectOfType(virtualCameraType) as Component;
            if (virtualCamera == null)
                return;

            PropertyInfo followProperty = virtualCameraType.GetProperty("Follow", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo lookAtProperty = virtualCameraType.GetProperty("LookAt", BindingFlags.Instance | BindingFlags.Public);
            followProperty?.SetValue(virtualCamera, transform);
            lookAtProperty?.SetValue(virtualCamera, transform);
        }

        void EnsureNameplate()
        {
            var existing = GetComponentInChildren<TextMesh>(true);
            if (existing != null && existing.gameObject.name == "Nameplate")
            {
                m_Nameplate = existing;
                return;
            }

            var nameplateObject = new GameObject("Nameplate");
            nameplateObject.transform.SetParent(transform, false);
            nameplateObject.transform.localPosition = Vector3.up * nameplateHeight;

            m_Nameplate = nameplateObject.AddComponent<TextMesh>();
            m_Nameplate.fontSize = 48;
            m_Nameplate.characterSize = 0.075f;
            m_Nameplate.anchor = TextAnchor.MiddleCenter;
            m_Nameplate.alignment = TextAlignment.Center;
        }

        void ApplyVisuals()
        {
            Color color = GetColorForClient(OwnerClientId);

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] == null || m_Renderers[i].sharedMaterial == null)
                    continue;

                m_Renderers[i].material.color = color;
            }

            if (m_Nameplate != null)
            {
                m_Nameplate.text = $"P{OwnerClientId}";
                m_Nameplate.color = color;
            }
        }

        Color GetColorForClient(ulong clientId)
        {
            Color[] palette =
            {
                new Color(0.95f, 0.35f, 0.25f),
                new Color(0.20f, 0.75f, 0.35f),
                new Color(0.20f, 0.55f, 0.95f),
                new Color(0.95f, 0.80f, 0.20f),
                new Color(0.85f, 0.35f, 0.90f),
                new Color(0.20f, 0.85f, 0.85f),
            };

            return palette[clientId % (ulong)palette.Length];
        }
    }
}
