using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LobbySceneUIController : MonoBehaviour
{
    [Header("Scene UI refs (Lobby Scene)")]
    [SerializeField]
    private GameObject lobbyUI;

    [SerializeField]
    private PregameUI pregameUI;

    [SerializeField]
    private LobbyCreateUI lobbyCreateUI;

    [SerializeField]
    private GameObject lobbyCam;

    [SerializeField]
    private string lobbyCameraTag = "LobbyCamera";

    public void RebindIfNeeded(string lobbySceneName)
    {
        if (SceneManager.GetActiveScene().name != lobbySceneName)
            return;

        if (lobbyUI == null)
        {
            var ui = FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (ui != null)
                lobbyUI = ui.gameObject;
        }

        if (pregameUI == null)
            pregameUI = FindFirstObjectByType<PregameUI>(FindObjectsInactive.Include);

        if (lobbyCreateUI == null)
            lobbyCreateUI = FindFirstObjectByType<LobbyCreateUI>(FindObjectsInactive.Include);

        if (lobbyCam == null)
        {
            var camObj = GameObject.FindWithTag(lobbyCameraTag);
            if (camObj != null)
                lobbyCam = camObj;
        }
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
        if (pregameUI != null)
        {
            pregameUI.gameObject.SetActive(true);
            pregameUI.Show();
            pregameUI.SetPregameUI();
        }
    }

    public void ShowLobbyScreen()
    {
        // Rebind in case refs were lost on scene load
        LobbyManager.Instance?.Services?.InitializeUnityAuthentication();

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
    }
}
