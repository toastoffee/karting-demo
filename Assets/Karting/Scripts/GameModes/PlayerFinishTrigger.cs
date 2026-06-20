using UnityEngine;

public class PlayerFinishTrigger : MonoBehaviour
{
    GameFlowManager m_GameFlowManager;

    void Awake()
    {
        m_GameFlowManager = FindObjectOfType<GameFlowManager>();
        DebugUtility.HandleErrorIfNullFindObject<GameFlowManager, PlayerFinishTrigger>(m_GameFlowManager, this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (m_GameFlowManager == null)
            return;

        if (!PlayerVehicleUtility.IsPlayerVehicle(other))
            return;

        m_GameFlowManager.NotifyPlayerReachedFinish();
    }
}
