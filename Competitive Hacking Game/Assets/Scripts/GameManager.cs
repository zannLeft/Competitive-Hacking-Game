using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Services.Lobbies;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject lobbyManagerPrefab;
    [SerializeField] private GameObject networkManagerPrefab;

    [Header("Lobby Scene Objects")]
    [SerializeField] private GameObject lobbyCamera;     // Assign in inspector
    [SerializeField] private GameObject lobbyUI;         // Assign in inspector
    [SerializeField] private GameObject lobbyCreateUI;   // Assign in inspector
    [SerializeField] private GameObject pregameUI;       // Assign in inspector

    public bool ReturningFromMatch { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Only instantiate if not already in scene
        if (FindObjectOfType<LobbyManager>() == null && lobbyManagerPrefab != null)
            Instantiate(lobbyManagerPrefab);

        if (FindObjectOfType<NetworkManager>() == null && networkManagerPrefab != null)
            Instantiate(networkManagerPrefab);

        // Listen for scene changes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public void SetReturningFromMatch(bool value)
    {
        ReturningFromMatch = value;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "LobbyScene")
        {
            if (!ReturningFromMatch)
            {
                // First load of game
                if (lobbyCamera != null) lobbyCamera.SetActive(true);
                if (lobbyUI != null) lobbyUI.SetActive(true);
                if (lobbyCreateUI != null) lobbyCreateUI.SetActive(false);
                if (pregameUI != null) pregameUI.SetActive(false);
            }
            else
            {
                // Returning from a match
                if (lobbyCamera != null) lobbyCamera.SetActive(false);
                if (lobbyUI != null) lobbyUI.SetActive(false);
                if (lobbyCreateUI != null) lobbyCreateUI.SetActive(false);
                if (pregameUI != null) pregameUI.SetActive(true); // show pregame UI
            }

            // Reset flag after handling
            ReturningFromMatch = false;
        }
    }
}
