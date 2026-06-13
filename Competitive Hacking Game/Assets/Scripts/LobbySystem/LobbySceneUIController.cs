using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LobbySceneUIController : MonoBehaviour
{
    [Header("Scene UI refs")]
    [SerializeField]
    private GameObject lobbyUI;

    [SerializeField]
    private PregameUI pregameUI;

    [SerializeField]
    private LobbyCreateUI lobbyCreateUI;

    [SerializeField]
    private GameObject lobbyCam;

    [SerializeField]
    private GameObject gameUI;

    [Header("Scene Names")]
    [SerializeField]
    private string lobbySceneName = "MainScene";

    [SerializeField]
    private string interiorSceneName = "Interior_01";

    [Header("Tags")]
    [SerializeField]
    private string lobbyCameraTag = "LobbyCamera";

    private bool _lastInGameplayState;
    private bool _hasAppliedState;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    public void Configure(string lobbyScene, string interiorScene)
    {
        lobbySceneName = lobbyScene;
        interiorSceneName = interiorScene;
    }

    private void Start()
    {
        RebindIfNeeded(lobbySceneName);
        ApplyNetworkPlayState(force: true);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName)
            RebindIfNeeded(lobbySceneName);

        ApplyNetworkPlayState(force: true);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        ApplyNetworkPlayState(force: true);
    }

    private void Update()
    {
        // Cheap safety check in case additive scene state changes before callbacks/rebinds settle.
        ApplyNetworkPlayState(force: false);
    }

    public void RebindIfNeeded(string lobbySceneName)
    {
        if (SceneManager.GetActiveScene().name != lobbySceneName)
            return;

        if (lobbyUI == null)
        {
            var ui = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (ui != null)
                lobbyUI = ui.gameObject;
        }

        if (pregameUI == null)
            pregameUI = FindAnyObjectByType<PregameUI>(FindObjectsInactive.Include);

        if (lobbyCreateUI == null)
            lobbyCreateUI = FindAnyObjectByType<LobbyCreateUI>(FindObjectsInactive.Include);

        if (lobbyCam == null)
        {
            var camObj = GameObject.FindWithTag(lobbyCameraTag);
            if (camObj != null)
                lobbyCam = camObj;
        }

        if (gameUI == null)
            gameUI = FindSceneObjectByName("GameUI", lobbySceneName);
    }

    private void ApplyNetworkPlayState(bool force)
    {
        bool inGameplay = IsInteriorLoaded();

        if (!force && _hasAppliedState && inGameplay == _lastInGameplayState)
            return;

        _hasAppliedState = true;
        _lastInGameplayState = inGameplay;

        if (inGameplay)
        {
            ShowGameUI();
        }
        else
        {
            // Only show PregameUI if we are still in a connected player session.
            // Otherwise main menu/lobby select should stay visible.
            if (IsInPlayerSession())
                ShowPregameUI();
        }
    }

    private bool IsInteriorLoaded()
    {
        var s = SceneManager.GetSceneByName(interiorSceneName);
        return s.IsValid() && s.isLoaded;
    }

    private bool IsInPlayerSession()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;

        if (nm == null)
            return false;

        if (!nm.IsConnectedClient)
            return false;

        return nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
    }

    public void HideMenuUI()
    {
        if (lobbyUI != null)
            lobbyUI.SetActive(false);

        if (lobbyCam != null)
            lobbyCam.SetActive(false);

        if (lobbyCreateUI != null)
            lobbyCreateUI.gameObject.SetActive(false);
    }

    public void ShowPregameUI()
    {
        HideMenuUI();

        if (gameUI != null)
            gameUI.SetActive(false);

        if (pregameUI != null)
        {
            pregameUI.gameObject.SetActive(true);
            pregameUI.Show();
            pregameUI.SetPregameUI();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void HidePregameUI()
    {
        if (pregameUI != null)
            pregameUI.gameObject.SetActive(false);
    }

    public void ShowGameUI()
    {
        HideMenuUI();

        if (pregameUI != null)
            pregameUI.gameObject.SetActive(false);

        if (gameUI != null)
            gameUI.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void HideGameUI()
    {
        if (gameUI != null)
            gameUI.SetActive(false);
    }

    public void ShowLobbyScreen()
    {
        // Main menu / lobby selection should not render or light the city scene.
        // City_Base is loaded only after joining/creating a lobby session.
        LoadCityBase.UnloadLocalIfLoaded();

        LobbyManager.Instance?.Services?.InitializeUnityAuthentication();

        if (gameUI != null)
            gameUI.SetActive(false);

        if (lobbyUI != null)
            lobbyUI.SetActive(true);

        if (lobbyCreateUI != null)
            lobbyCreateUI.gameObject.SetActive(false);

        if (pregameUI != null)
            pregameUI.gameObject.SetActive(false);

        if (lobbyCam != null)
            lobbyCam.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _hasAppliedState = false;
    }

    private GameObject FindSceneObjectByName(string objectName, string sceneName)
    {
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (var obj in allObjects)
        {
            if (obj == null)
                continue;

            if (obj.name != objectName)
                continue;

            if (!obj.scene.IsValid())
                continue;

            if (obj.scene.name != sceneName)
                continue;

            return obj;
        }

        return null;
    }
}