using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using KartGame.KartSystems;
using UnityEngine.SceneManagement;

public enum GameState{Play, Won, Lost}

public class GameFlowManager : MonoBehaviour
{
    [Header("Parameters")]
    [Tooltip("Duration of the fade-to-black at the end of the game")]
    public float endSceneLoadDelay = 3f;
    [Tooltip("The canvas group of the fade-to-black screen")]
    public CanvasGroup endGameFadeCanvasGroup;

    [Header("Win")]
    [Tooltip("This string has to be the name of the scene you want to load when winning")]
    public string winSceneName = "WinScene";
    [Tooltip("Duration of delay before the fade-to-black, if winning")]
    public float delayBeforeFadeToBlack = 4f;
    [Tooltip("Duration of delay before the win message")]
    public float delayBeforeWinMessage = 2f;
    [Tooltip("Sound played on win")]
    public AudioClip victorySound;

    [Tooltip("Prefab for the win game message")]
    public DisplayMessage winDisplayMessage;

    public PlayableDirector raceCountdownTrigger;

    [Header("Lose")]
    [Tooltip("This string has to be the name of the scene you want to load when losing")]
    public string loseSceneName = "LoseScene";
    [Tooltip("Prefab for the lose game message")]
    public DisplayMessage loseDisplayMessage;

    [Header("Race Finish")]
    [Tooltip("If enabled, completing objectives is not enough to win. The player must also cross the finish trigger.")]
    public bool requirePlayerFinishToWin;


    public GameState gameState { get; private set; }

    public bool autoFindKarts = true;
    public ArcadeKart playerKart;

    ArcadeKart[] karts;
    ObjectiveManager m_ObjectiveManager;
    TimeManager m_TimeManager;
    float m_TimeLoadEndGameScene;
    string m_SceneToLoad;
    float elapsedTimeBeforeEndScene = 0;
    bool m_RaceStarted;

    void Start()
    {
        RefreshKarts(playerKart);

        m_ObjectiveManager = FindObjectOfType<ObjectiveManager>();
		DebugUtility.HandleErrorIfNullFindObject<ObjectiveManager, GameFlowManager>(m_ObjectiveManager, this);

        m_TimeManager = FindObjectOfType<TimeManager>();
        DebugUtility.HandleErrorIfNullFindObject<TimeManager, GameFlowManager>(m_TimeManager, this);

        AudioUtility.SetMasterVolume(1);

        winDisplayMessage.gameObject.SetActive(false);
        loseDisplayMessage.gameObject.SetActive(false);

        m_TimeManager.StopRace();
        m_RaceStarted = false;
        ApplyMovementStateToKarts(false);

        //run race countdown animation
        ShowRaceCountdownAnimation();
        StartCoroutine(ShowObjectivesRoutine());

        StartCoroutine(CountdownThenStartRaceRoutine());
    }

    IEnumerator CountdownThenStartRaceRoutine() {
        yield return new WaitForSeconds(3f);
        StartRace();
    }

    void StartRace() {
        m_RaceStarted = true;
        ApplyMovementStateToKarts(true);
        m_TimeManager.StartRace();
    }

    public void RefreshKarts(ArcadeKart preferredPlayerKart = null)
    {
        if (autoFindKarts)
            karts = FindObjectsOfType<ArcadeKart>();

        if (preferredPlayerKart != null)
            playerKart = preferredPlayerKart;
        else if ((playerKart == null || !playerKart.gameObject.activeInHierarchy) && karts != null && karts.Length > 0)
            playerKart = karts[0];

        if (playerKart != null)
            DebugUtility.HandleErrorIfNullFindObject<ArcadeKart, GameFlowManager>(playerKart, this);

        ApplyMovementStateToKarts(m_RaceStarted && gameState == GameState.Play);
    }

    void ApplyMovementStateToKarts(bool canMove)
    {
        if (karts == null)
            return;

        foreach (ArcadeKart kart in karts)
        {
            if (kart == null)
                continue;

			kart.SetCanMove(canMove);
        }
    }

    void ShowRaceCountdownAnimation() {
        raceCountdownTrigger.Play();
    }

    IEnumerator ShowObjectivesRoutine() {
        while (m_ObjectiveManager.Objectives.Count == 0)
            yield return null;
        yield return new WaitForSecondsRealtime(0.2f);
        for (int i = 0; i < m_ObjectiveManager.Objectives.Count; i++)
        {
           if (m_ObjectiveManager.Objectives[i].displayMessage)m_ObjectiveManager.Objectives[i].displayMessage.Display();
           yield return new WaitForSecondsRealtime(1f);
        }
    }


    void Update()
    {

        if (gameState != GameState.Play)
        {
            elapsedTimeBeforeEndScene += Time.deltaTime;
            if(elapsedTimeBeforeEndScene >= endSceneLoadDelay)
            {

                float timeRatio = 1 - (m_TimeLoadEndGameScene - Time.time) / endSceneLoadDelay;
                endGameFadeCanvasGroup.alpha = timeRatio;

                float volumeRatio = Mathf.Abs(timeRatio);
                float volume = Mathf.Clamp(1 - volumeRatio, 0, 1);
                AudioUtility.SetMasterVolume(volume);

                // See if it's time to load the end scene (after the delay)
                if (Time.time >= m_TimeLoadEndGameScene)
                {
                    if (Application.CanStreamedLevelBeLoaded(m_SceneToLoad))
                    {
                        SceneManager.LoadScene(m_SceneToLoad);
                        gameState = GameState.Play;
                    }
                    else
                    {
                        Debug.LogError($"Scene '{m_SceneToLoad}' is not enabled in Build Settings. Add it in File > Build Settings or ProjectSettings/EditorBuildSettings.asset.");
                        enabled = false;
                    }
                }
            }
        }
        else
        {
            if (!requirePlayerFinishToWin && m_ObjectiveManager.AreAllObjectivesCompleted())
                EndGame(true);

            if (m_TimeManager.IsFinite && m_TimeManager.IsOver)
                EndGame(false);
        }
    }

    public void NotifyPlayerReachedFinish()
    {
        if (gameState != GameState.Play)
            return;

        if (m_ObjectiveManager.AreAllObjectivesCompleted())
            EndGame(true);
    }

    public void EndGame(bool win)
    {
        // unlocks the cursor before leaving the scene, to be able to click buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        m_TimeManager.StopRace();
        m_RaceStarted = false;

        // Remember that we need to load the appropriate end scene after a delay
        gameState = win ? GameState.Won : GameState.Lost;
        endGameFadeCanvasGroup.gameObject.SetActive(true);
        if (win)
        {
            m_SceneToLoad = winSceneName;
            m_TimeLoadEndGameScene = Time.time + endSceneLoadDelay + delayBeforeFadeToBlack;

            // play a sound on win
            var audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = victorySound;
            audioSource.playOnAwake = false;
            audioSource.outputAudioMixerGroup = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.HUDVictory);
            audioSource.PlayScheduled(AudioSettings.dspTime + delayBeforeWinMessage);

            // create a game message
            winDisplayMessage.delayBeforeShowing = delayBeforeWinMessage;
            winDisplayMessage.gameObject.SetActive(true);
        }
        else
        {
            m_SceneToLoad = loseSceneName;
            m_TimeLoadEndGameScene = Time.time + endSceneLoadDelay + delayBeforeFadeToBlack;

            // create a game message
            loseDisplayMessage.delayBeforeShowing = delayBeforeWinMessage;
            loseDisplayMessage.gameObject.SetActive(true);
        }
    }
}
