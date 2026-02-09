using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadCityBase : MonoBehaviour
{
    [SerializeField]
    private string cityBaseSceneName = "City_Base";

    private void Awake()
    {
        // If already loaded (e.g. returning from play mode), do nothing
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.name == cityBaseSceneName)
                return;
        }

        SceneManager.LoadSceneAsync(cityBaseSceneName, LoadSceneMode.Additive);
    }
}
