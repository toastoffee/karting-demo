using System;
using System.Collections.Generic;
using System.Reflection;
using KartGame.KartSystems;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KartGame.Multiplayer
{
    public class MainSceneMultiplayerBootstrap : MonoBehaviour
    {
        public const string SceneName = "MainScene";

        const string BuiltinFontName = "LegacyRuntime.ttf";
        const string AiStateMessageName = "KartGame.Multiplayer.AIState";
        [Header("Networking")]
        [SerializeField] GameObject playerPrefab;
        [SerializeField] string defaultAddress = "127.0.0.1";
        [SerializeField] ushort defaultPort = 7777;
        [SerializeField] int maxPlayers = 4;
        [SerializeField] float aiStateSendRate = 20.0f;

        NetworkManager m_NetworkManager;
        UnityTransport m_Transport;

        InputField m_AddressInput;
        InputField m_PortInput;
        Button m_CreateRoomButton;
        Button m_JoinRoomButton;
        Button m_LeaveRoomButton;
        Text m_StatusText;
        Text m_PlayersText;
        bool m_EventsRegistered;
        bool m_AiMessageRegistered;

        GameObject m_ScenePlayerRoot;
        Vector3 m_BaseSpawnPosition;
        Quaternion m_BaseSpawnRotation;
        readonly List<AiKartBinding> m_AiKarts = new List<AiKartBinding>();
        float m_NextAiStateSendTime;
        Vector3 m_PendingLocalSpawnPosition;
        Quaternion m_PendingLocalSpawnRotation = Quaternion.identity;
        Vector3 m_PendingLocalSpawnVelocity;
        Vector3 m_PendingLocalSpawnAngularVelocity;
        int m_PendingLocalSnapFrames;

        sealed class AiKartBinding
        {
            public string Path;
            public ArcadeKart Kart;
            public Behaviour Agent;
            public Behaviour DecisionRequester;
            public Behaviour BehaviorParameters;
            public KeyboardInput KeyboardInput;
            public Rigidbody Rigidbody;
            public Collider[] Colliders;
            public WheelCollider[] WheelColliders;
        }

        void Awake()
        {
            if (playerPrefab == null)
                playerPrefab = Resources.Load<GameObject>("Multiplayer/NetworkKartPlayer");

            CacheSceneReferences();
            EnsureNetworkManager();
            EnsureUi();
            UpdateSessionUiState();
            UpdateStatus("Ready. Create a room or join from another client.");
            UpdatePlayerCount();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnregisterNetworkEvents();
            UnregisterAiMessageHandler();
        }

        void FixedUpdate()
        {
            ApplyPendingLocalPlayerSnap();

            if (m_NetworkManager == null || !m_NetworkManager.IsServer || !m_NetworkManager.IsListening)
                return;

            if (Time.unscaledTime < m_NextAiStateSendTime)
                return;

            m_NextAiStateSendTime = Time.unscaledTime + (1.0f / Mathf.Max(aiStateSendRate, 1.0f));
            BroadcastAiState();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName)
                return;

            CacheSceneReferences();
            EnsureUi();
            UpdateSessionUiState();
            UpdatePlayerCount();
        }

        void CacheSceneReferences()
        {
            if (m_ScenePlayerRoot == null)
            {
                GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
                if (taggedPlayer != null)
                {
                    m_ScenePlayerRoot = ResolvePlayerRoot(taggedPlayer);
                }
                else
                {
                    var keyboardInputs = FindObjectsOfType<KeyboardInput>(true);
                    for (int i = 0; i < keyboardInputs.Length; i++)
                    {
                        if (keyboardInputs[i] != null && keyboardInputs[i].GetComponent("KartAgent") == null)
                        {
                            m_ScenePlayerRoot = ResolvePlayerRoot(keyboardInputs[i].gameObject);
                            break;
                        }
                    }
                }

                if (m_ScenePlayerRoot != null)
                {
                    m_BaseSpawnPosition = m_ScenePlayerRoot.transform.position;
                    m_BaseSpawnRotation = m_ScenePlayerRoot.transform.rotation;
                }
            }

            RebuildAiBindings();
        }

        void RebuildAiBindings()
        {
            m_AiKarts.Clear();

            Type agentType = Type.GetType("KartGame.AI.KartAgent, KartGame.AI");
            if (agentType == null)
                return;

            var agents = FindObjectsOfType(agentType, true);
            Array.Sort(agents, (a, b) =>
            {
                var componentA = a as Component;
                var componentB = b as Component;
                return string.CompareOrdinal(
                    GetHierarchyPath(componentA != null ? componentA.transform : null),
                    GetHierarchyPath(componentB != null ? componentB.transform : null));
            });

            for (int i = 0; i < agents.Length; i++)
            {
                var agent = agents[i] as Component;
                if (agent == null)
                    continue;

                var root = agent.GetComponent<ArcadeKart>();
                if (root == null)
                    continue;

                m_AiKarts.Add(new AiKartBinding
                {
                    Path = GetHierarchyPath(agent.transform),
                    Kart = root,
                    Agent = agent as Behaviour,
                    DecisionRequester = agent.GetComponent("DecisionRequester") as Behaviour,
                    BehaviorParameters = agent.GetComponent("BehaviorParameters") as Behaviour,
                    KeyboardInput = agent.GetComponent<KeyboardInput>(),
                    Rigidbody = agent.GetComponent<Rigidbody>(),
                    Colliders = agent.GetComponentsInChildren<Collider>(true),
                    WheelColliders = agent.GetComponentsInChildren<WheelCollider>(true),
                });
            }
        }

        void EnsureNetworkManager()
        {
            m_NetworkManager = FindObjectOfType<NetworkManager>();
            if (m_NetworkManager == null)
            {
                var networkObject = new GameObject("NetworkManager");
                m_NetworkManager = networkObject.AddComponent<NetworkManager>();
                m_Transport = networkObject.AddComponent<UnityTransport>();
            }
            else
            {
                m_Transport = m_NetworkManager.GetComponent<UnityTransport>();
                if (m_Transport == null)
                    m_Transport = m_NetworkManager.gameObject.AddComponent<UnityTransport>();
            }

            if (m_NetworkManager.NetworkConfig == null)
                m_NetworkManager.NetworkConfig = new NetworkConfig();

            m_NetworkManager.NetworkConfig.NetworkTransport = m_Transport;
            m_NetworkManager.NetworkConfig.ConnectionApproval = true;
            m_NetworkManager.NetworkConfig.PlayerPrefab = playerPrefab;

            m_NetworkManager.ConnectionApprovalCallback = ApprovalCheck;
            RegisterNetworkEvents();
            RegisterAiMessageHandler();
        }

        void RegisterNetworkEvents()
        {
            if (m_NetworkManager == null || m_EventsRegistered)
                return;

            m_NetworkManager.OnServerStarted += OnServerStarted;
            m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            m_EventsRegistered = true;
        }

        void UnregisterNetworkEvents()
        {
            if (m_NetworkManager == null || !m_EventsRegistered)
                return;

            m_NetworkManager.OnServerStarted -= OnServerStarted;
            m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            m_EventsRegistered = false;
        }

        void RegisterAiMessageHandler()
        {
            if (m_NetworkManager == null || m_AiMessageRegistered || m_NetworkManager.CustomMessagingManager == null)
                return;

            m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(AiStateMessageName, OnAiStateReceived);
            m_AiMessageRegistered = true;
        }

        void UnregisterAiMessageHandler()
        {
            if (m_NetworkManager == null || !m_AiMessageRegistered || m_NetworkManager.CustomMessagingManager == null)
                return;

            m_NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(AiStateMessageName);
            m_AiMessageRegistered = false;
        }

        void EnsureUi()
        {
            if (GameObject.Find("MainSceneMultiplayerCanvas") != null)
                return;

            var canvasObject = new GameObject("MainSceneMultiplayerCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();

            var panel = CreateUiObject("Panel", canvasObject.transform);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.07f, 0.10f, 0.84f);

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.52f, 0.04f);
            panelRect.anchorMax = new Vector2(0.98f, 0.96f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 6;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            CreateText(panel.transform, "Title", "MainScene Multiplayer", 20, FontStyle.Bold, 38);
            CreateText(panel.transform, "Hint", "Host simulates AI karts. Clients receive AI state sync only.", 13, FontStyle.Normal, 34);

            m_AddressInput = CreateInputField(panel.transform, "Server Address", defaultAddress);
            m_PortInput = CreateInputField(panel.transform, "Port", defaultPort.ToString());

            m_CreateRoomButton = CreateButton(panel.transform, "Create Room", StartHost);
            m_JoinRoomButton = CreateButton(panel.transform, "Join Room", StartClient);
            m_LeaveRoomButton = CreateButton(panel.transform, "Leave Room", LeaveRoom);

            m_StatusText = CreateText(panel.transform, "Status", string.Empty, 13, FontStyle.Normal, 30);
            m_PlayersText = CreateText(panel.transform, "Players", string.Empty, 13, FontStyle.Normal, 22);
        }

        GameObject CreateUiObject(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        Text CreateText(Transform parent, string objectName, string content, int fontSize, FontStyle fontStyle, float preferredHeight = -1.0f)
        {
            var textObject = CreateUiObject(objectName, parent);
            var text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;

            var layout = textObject.AddComponent<LayoutElement>();
            float defaultHeight = fontSize + 18;
            layout.minHeight = preferredHeight > 0.0f ? preferredHeight : defaultHeight;
            layout.preferredHeight = preferredHeight > 0.0f ? preferredHeight : defaultHeight;
            return text;
        }

        InputField CreateInputField(Transform parent, string placeholder, string value)
        {
            var root = CreateUiObject($"{placeholder} Input", parent);
            var background = root.AddComponent<Image>();
            background.color = new Color(0.14f, 0.16f, 0.20f, 0.95f);

            var layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 36;
            layout.preferredHeight = 36;

            var inputField = root.AddComponent<InputField>();
            inputField.targetGraphic = background;

            var textViewport = CreateUiObject("Text", root.transform);
            var text = textViewport.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.fontSize = 15;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;

            var placeholderObject = CreateUiObject("Placeholder", root.transform);
            var placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            placeholderText.fontSize = 15;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = new Color(1.0f, 1.0f, 1.0f, 0.35f);
            placeholderText.text = placeholder;
            placeholderText.alignment = TextAnchor.MiddleLeft;

            StretchRect(textViewport.GetComponent<RectTransform>(), new Vector2(12, 8), new Vector2(-12, -8));
            StretchRect(placeholderObject.GetComponent<RectTransform>(), new Vector2(12, 8), new Vector2(-12, -8));

            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            inputField.text = value;
            return inputField;
        }

        Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreateUiObject(label, parent);
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.45f, 0.78f, 0.95f);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 34;
            layout.preferredHeight = 34;

            var text = CreateText(buttonObject.transform, "Label", label, 15, FontStyle.Bold, 34);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            StretchRect(text.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
            return button;
        }

        void StretchRect(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool roomHasSpace = m_NetworkManager != null && m_NetworkManager.ConnectedClientsIds.Count < maxPlayers;
            bool isLocalHostSpawn = m_NetworkManager != null && request.ClientNetworkId == NetworkManager.ServerClientId;

            response.Approved = roomHasSpace;
            response.CreatePlayerObject = roomHasSpace;
            response.Pending = false;
            response.Reason = roomHasSpace ? string.Empty : "Room is full.";
            response.Position = isLocalHostSpawn
                ? m_PendingLocalSpawnPosition
                : GetSpawnPosition(m_NetworkManager != null ? m_NetworkManager.ConnectedClientsIds.Count : 0);
            response.Rotation = isLocalHostSpawn
                ? m_PendingLocalSpawnRotation
                : m_BaseSpawnRotation;
        }

        Vector3 GetSpawnPosition(int playerIndex)
        {
            Vector3[] spawnOffsets =
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(2.5f, 0.0f, -2.5f),
                new Vector3(-2.5f, 0.0f, -5.0f),
                new Vector3(5.0f, 0.0f, -5.0f),
            };

            return m_BaseSpawnPosition + (m_BaseSpawnRotation * spawnOffsets[playerIndex % spawnOffsets.Length]);
        }

        void StartHost()
        {
            if (!PrepareSession(isHostAuthority: true))
                return;

            ApplyTransportSettings("0.0.0.0");
            if (m_NetworkManager.StartHost())
            {
                RegisterAiMessageHandler();
                m_NextAiStateSendTime = 0.0f;
                UpdateStatus($"Hosting on {GetAddress()}:{GetPort()}");
            }
            else
            {
                RestoreSinglePlayerSceneState();
                UpdateStatus("Failed to create room.");
            }
        }

        void StartClient()
        {
            if (!PrepareSession(isHostAuthority: false))
                return;

            ApplyTransportSettings(null);
            if (m_NetworkManager.StartClient())
            {
                RegisterAiMessageHandler();
                UpdateStatus($"Joining {GetAddress()}:{GetPort()}");
            }
            else
            {
                RestoreSinglePlayerSceneState();
                UpdateStatus("Failed to join room.");
            }
        }

        bool PrepareSession(bool isHostAuthority)
        {
            if (m_NetworkManager == null || m_Transport == null)
            {
                UpdateStatus("NetworkManager is not ready.");
                return false;
            }

            if (playerPrefab == null)
            {
                UpdateStatus("Network player prefab is missing.");
                return false;
            }

            if (m_NetworkManager.IsListening)
            {
                UpdateStatus("A room is already running.");
                return false;
            }

            CacheSceneReferences();
            RefreshSpawnAnchor();
            CachePendingLocalSpawnState();
            SetSinglePlayerSceneState(false);
            SetAiSimulationAuthority(isHostAuthority);
            RegisterAiMessageHandler();
            return true;
        }

        void ApplyTransportSettings(string listenAddressOverride)
        {
            if (m_Transport == null)
                return;

            MethodInfo method = typeof(UnityTransport).GetMethod(
                "SetConnectionData",
                new[] { typeof(string), typeof(ushort), typeof(string) });

            method?.Invoke(m_Transport, new object[] { GetAddress(), GetPort(), listenAddressOverride });
        }

        void LeaveRoom()
        {
            if (m_NetworkManager != null && m_NetworkManager.IsListening)
                m_NetworkManager.Shutdown();

            SetAiSimulationAuthority(true);
            UnregisterAiMessageHandler();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        string GetAddress()
        {
            return string.IsNullOrWhiteSpace(m_AddressInput != null ? m_AddressInput.text : string.Empty)
                ? defaultAddress
                : m_AddressInput.text.Trim();
        }

        ushort GetPort()
        {
            if (m_PortInput != null && ushort.TryParse(m_PortInput.text, out ushort parsedPort))
                return parsedPort;

            return defaultPort;
        }

        void OnServerStarted()
        {
            UpdateSessionUiState();
            UpdatePlayerCount();
        }

        void OnClientConnected(ulong clientId)
        {
            UpdateSessionUiState();
            UpdatePlayerCount();

            if (m_NetworkManager != null && clientId == m_NetworkManager.LocalClientId)
            {
                string role = m_NetworkManager.IsHost ? "Host" : "Client";
                UpdateStatus($"{role} connected. Local client id: {clientId}");
            }
            else
            {
                UpdateStatus($"Player {clientId} joined.");
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            UpdateSessionUiState();
            UpdatePlayerCount();

            if (m_NetworkManager != null && clientId == m_NetworkManager.LocalClientId)
            {
                if (!m_NetworkManager.IsListening)
                    RestoreSinglePlayerSceneState();

                UpdateStatus("Disconnected from room.");
            }
            else
                UpdateStatus($"Player {clientId} left.");
        }

        void BroadcastAiState()
        {
            if (m_NetworkManager == null || m_NetworkManager.CustomMessagingManager == null || m_AiKarts.Count == 0)
                return;

            int valueSize = sizeof(int) + m_AiKarts.Count * ((3 + 4 + 3 + 3) * sizeof(float));
            using (var writer = new FastBufferWriter(valueSize, Allocator.Temp))
            {
                writer.WriteValueSafe(m_AiKarts.Count);
                for (int i = 0; i < m_AiKarts.Count; i++)
                {
                    var binding = m_AiKarts[i];
                    Vector3 position = binding.Kart != null ? binding.Kart.transform.position : Vector3.zero;
                    Quaternion rotation = binding.Kart != null ? binding.Kart.transform.rotation : Quaternion.identity;
                    Vector3 velocity = binding.Rigidbody != null ? binding.Rigidbody.velocity : Vector3.zero;
                    Vector3 angularVelocity = binding.Rigidbody != null ? binding.Rigidbody.angularVelocity : Vector3.zero;

                    writer.WriteValueSafe(position);
                    writer.WriteValueSafe(rotation);
                    writer.WriteValueSafe(velocity);
                    writer.WriteValueSafe(angularVelocity);
                }

                m_NetworkManager.CustomMessagingManager.SendNamedMessageToAll(
                    AiStateMessageName,
                    writer,
                    NetworkDelivery.UnreliableSequenced);
            }
        }

        void OnAiStateReceived(ulong senderClientId, FastBufferReader reader)
        {
            if (m_NetworkManager == null || m_NetworkManager.IsServer)
                return;

            reader.ReadValueSafe(out int count);
            int bindingCount = Mathf.Min(count, m_AiKarts.Count);

            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out Quaternion rotation);
                reader.ReadValueSafe(out Vector3 velocity);
                reader.ReadValueSafe(out Vector3 angularVelocity);

                if (i >= bindingCount)
                    continue;

                var binding = m_AiKarts[i];
                if (binding.Kart == null)
                    continue;

                binding.Kart.transform.SetPositionAndRotation(position, rotation);
                if (binding.Rigidbody != null)
                {
                    binding.Rigidbody.velocity = velocity;
                    binding.Rigidbody.angularVelocity = angularVelocity;
                }
            }
        }

        public void RegisterLocalPlayer(ArcadeKart localPlayerKart)
        {
            if (localPlayerKart != null)
                SnapLocalPlayerToPendingState(localPlayerKart);

            var gameFlow = FindObjectOfType<GameFlowManager>();
            if (gameFlow != null)
                gameFlow.RefreshKarts(localPlayerKart);
        }

        void SetSinglePlayerSceneState(bool active)
        {
            if (m_ScenePlayerRoot == null)
                return;

            m_ScenePlayerRoot.SetActive(active);
        }

        void RefreshSpawnAnchor()
        {
            if (m_ScenePlayerRoot == null)
                return;

            m_BaseSpawnPosition = m_ScenePlayerRoot.transform.position;
            m_BaseSpawnRotation = m_ScenePlayerRoot.transform.rotation;
        }

        void CachePendingLocalSpawnState()
        {
            if (m_ScenePlayerRoot == null)
                return;

            m_PendingLocalSpawnPosition = m_ScenePlayerRoot.transform.position;
            m_PendingLocalSpawnRotation = m_ScenePlayerRoot.transform.rotation;
            m_PendingLocalSpawnVelocity = Vector3.zero;
            m_PendingLocalSpawnAngularVelocity = Vector3.zero;
            m_PendingLocalSnapFrames = 12;
        }

        void ApplyPendingLocalPlayerSnap()
        {
            if (m_PendingLocalSnapFrames <= 0 || m_NetworkManager == null || !m_NetworkManager.IsListening)
                return;

            var localPlayer = FindLocalNetworkPlayerKart();
            if (localPlayer == null)
                return;

            SnapLocalPlayerToPendingState(localPlayer);
            m_PendingLocalSnapFrames--;
        }

        ArcadeKart FindLocalNetworkPlayerKart()
        {
            var players = FindObjectsOfType<NetworkKartPlayer>(true);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].IsOwner)
                    return players[i].GetComponent<ArcadeKart>();
            }

            return null;
        }

        void SnapLocalPlayerToPendingState(ArcadeKart localPlayerKart)
        {
            if (localPlayerKart == null)
                return;

            localPlayerKart.transform.SetPositionAndRotation(m_PendingLocalSpawnPosition, m_PendingLocalSpawnRotation);

            var rigidbody = localPlayerKart.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.position = m_PendingLocalSpawnPosition;
                rigidbody.rotation = m_PendingLocalSpawnRotation;
                rigidbody.velocity = m_PendingLocalSpawnVelocity;
                rigidbody.angularVelocity = m_PendingLocalSpawnAngularVelocity;
                rigidbody.Sleep();
                rigidbody.WakeUp();
            }
        }

        void RestoreSinglePlayerSceneState()
        {
            SetSinglePlayerSceneState(true);
            SetAiSimulationAuthority(true);

            if (m_ScenePlayerRoot == null)
                return;

            var kart = m_ScenePlayerRoot.GetComponent<ArcadeKart>();
            var gameFlow = FindObjectOfType<GameFlowManager>();
            if (kart != null && gameFlow != null)
                gameFlow.RefreshKarts(kart);
        }

        GameObject ResolvePlayerRoot(GameObject candidate)
        {
            if (candidate == null)
                return null;

            var kart = candidate.GetComponentInParent<ArcadeKart>();
            if (kart != null)
                return kart.gameObject;

            var keyboardInput = candidate.GetComponentInParent<KeyboardInput>();
            if (keyboardInput != null)
                return keyboardInput.gameObject;

            return candidate;
        }

        void SetAiSimulationAuthority(bool authoritative)
        {
            RebuildAiBindings();

            for (int i = 0; i < m_AiKarts.Count; i++)
            {
                var binding = m_AiKarts[i];

                if (binding.Agent != null)
                    binding.Agent.enabled = authoritative;

                if (binding.DecisionRequester != null)
                    binding.DecisionRequester.enabled = authoritative;

                if (binding.BehaviorParameters != null)
                    binding.BehaviorParameters.enabled = authoritative;

                if (binding.KeyboardInput != null)
                    binding.KeyboardInput.enabled = false;

                if (binding.Kart != null)
                {
                    binding.Kart.enabled = authoritative;
                    binding.Kart.SetCanMove(authoritative);
                }

                if (binding.Rigidbody != null)
                {
                    binding.Rigidbody.isKinematic = !authoritative;
                    if (!authoritative)
                    {
                        binding.Rigidbody.velocity = Vector3.zero;
                        binding.Rigidbody.angularVelocity = Vector3.zero;
                    }
                }

                ToggleColliders(binding.Colliders, authoritative);
                ToggleWheelColliders(binding.WheelColliders, authoritative);
            }
        }

        void ToggleColliders(Collider[] colliders, bool enabled)
        {
            if (colliders == null)
                return;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = enabled;
            }
        }

        void ToggleWheelColliders(WheelCollider[] colliders, bool enabled)
        {
            if (colliders == null)
                return;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = enabled;
            }
        }

        string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            var names = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        void UpdateStatus(string message)
        {
            if (m_StatusText != null)
                m_StatusText.text = $"Status: {message}";
        }

        void UpdatePlayerCount()
        {
            if (m_PlayersText == null)
                return;

            int playerCount = 0;
            if (m_NetworkManager != null && m_NetworkManager.IsListening)
                playerCount = m_NetworkManager.ConnectedClientsIds.Count;

            m_PlayersText.text = $"Players: {playerCount}/{maxPlayers}";
        }

        void UpdateSessionUiState()
        {
            bool isConnected = m_NetworkManager != null && m_NetworkManager.IsListening;

            if (m_AddressInput != null)
                m_AddressInput.gameObject.SetActive(!isConnected);

            if (m_PortInput != null)
                m_PortInput.gameObject.SetActive(!isConnected);

            if (m_CreateRoomButton != null)
                m_CreateRoomButton.gameObject.SetActive(!isConnected);

            if (m_JoinRoomButton != null)
                m_JoinRoomButton.gameObject.SetActive(!isConnected);

            if (m_LeaveRoomButton != null)
                m_LeaveRoomButton.gameObject.SetActive(isConnected);
        }
    }
}
