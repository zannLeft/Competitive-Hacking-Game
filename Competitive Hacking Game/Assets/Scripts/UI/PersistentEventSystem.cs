using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#else
using UnityEngine.EventSystems;
#endif

[DisallowMultipleComponent]
public class PersistentEventSystem : MonoBehaviour
{
    private void Awake()
    {
        // If there's already a current EventSystem which is NOT this one, destroy this GameObject.
        if (EventSystem.current != null && EventSystem.current.gameObject != gameObject)
        {
            Destroy(gameObject);
            return;
        }

        // Ensure we have an EventSystem component
        if (GetComponent<EventSystem>() == null)
        {
            gameObject.AddComponent<EventSystem>();
        }

        // Ensure appropriate input module exists
    #if ENABLE_INPUT_SYSTEM
        if (GetComponent<InputSystemUIInputModule>() == null)
        {
            gameObject.AddComponent<InputSystemUIInputModule>();
        }
    #else
        if (GetComponent<StandaloneInputModule>() == null)
        {
            gameObject.AddComponent<StandaloneInputModule>();
        }
    #endif

        // Persist across scene loads
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // If the persistent one is destroyed and a scene has an EventSystem, it will become EventSystem.current automatically.
    }
}
