using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadCityBase : MonoBehaviour
{
    private const string DefaultCityBaseSceneName = "City_Base";

    [SerializeField]
    private string cityBaseSceneName = DefaultCityBaseSceneName;

    [Header("Menu Behaviour")]
    [SerializeField]
    private bool unloadCityBaseOnAwake = true;

    public static LoadCityBase Instance { get; private set; }

    public string CityBaseSceneName => string.IsNullOrWhiteSpace(cityBaseSceneName)
        ? DefaultCityBaseSceneName
        : cityBaseSceneName;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
            Debug.LogWarning("[LoadCityBase] More than one LoadCityBase exists. Using the first instance for scene settings.");

        // The main menu/lobby-selection screen should not have City_Base loaded anymore.
        // City_Base is loaded only after a network lobby session starts.
        if (unloadCityBaseOnAwake)
            UnloadLocalIfLoaded(CityBaseSceneName);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void LoadForLobbyAsHostIfNeeded()
    {
        LoadForLobbyAsHostIfNeeded(CityBaseSceneName);
    }

    public void UnloadLocalIfLoaded()
    {
        UnloadLocalIfLoaded(CityBaseSceneName);
    }

    public bool IsLoaded()
    {
        return IsLoaded(CityBaseSceneName);
    }

    public static void LoadForLobbyAsHostIfNeeded(string sceneName = DefaultCityBaseSceneName)
    {
        sceneName = NormalizeSceneName(sceneName);

        if (IsLoaded(sceneName))
            return;

        var nm = NetworkManager.Singleton;

        if (nm != null && nm.IsListening && nm.IsServer && nm.SceneManager != null)
        {
            // Host loads City_Base through Netcode so joining clients receive exactly one copy.
            nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            Debug.Log($"[LoadCityBase] Host requested Netcode additive load for '{sceneName}'.");
            return;
        }

        // Fallback for non-network debug use only.
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        Debug.Log($"[LoadCityBase] Requested local additive load for '{sceneName}'.");
    }

    public static void UnloadLocalIfLoaded(string sceneName = DefaultCityBaseSceneName)
    {
        sceneName = NormalizeSceneName(sceneName);

        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.IsValid() || !scene.isLoaded || scene.name != sceneName)
                continue;

            SceneManager.UnloadSceneAsync(scene);
            Debug.Log($"[LoadCityBase] Requested local unload for '{sceneName}'.");
        }
    }

    public static bool IsLoaded(string sceneName = DefaultCityBaseSceneName)
    {
        sceneName = NormalizeSceneName(sceneName);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (scene.IsValid() && scene.isLoaded && scene.name == sceneName)
                return true;
        }

        return false;
    }

    private static string NormalizeSceneName(string sceneName)
    {
        return string.IsNullOrWhiteSpace(sceneName) ? DefaultCityBaseSceneName : sceneName;
    }
}
