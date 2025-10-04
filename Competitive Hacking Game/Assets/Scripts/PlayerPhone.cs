using UnityEngine;
using Unity.Netcode; // optional, for future networking (not used in this local version)

[DisallowMultipleComponent]
public class PlayerPhone : MonoBehaviour
{
    [Header("Prefab / Attach")]
    public GameObject phonePrefab;         // assign your Phone prefab in inspector
    public Transform phoneAttach;          // assign the PhoneAttach_R transform in inspector

    [Header("Offsets (local to attach)")]
    public Vector3 attachLocalPosition = Vector3.zero;
    public Vector3 attachLocalEuler = Vector3.zero;
    public Vector3 initialScale = Vector3.one;

    // runtime
    private GameObject spawnedPhone;

    // Call this when right mouse button is held
    public void ShowPhone()
    {
        if (spawnedPhone != null) return;

        if (phonePrefab == null || phoneAttach == null)
        {
            Debug.LogWarning("PlayerPhone: missing phonePrefab or phoneAttach", this);
            return;
        }

        spawnedPhone = Instantiate(phonePrefab, phoneAttach);
        spawnedPhone.transform.localPosition = attachLocalPosition;
        spawnedPhone.transform.localEulerAngles = attachLocalEuler;
        spawnedPhone.transform.localScale = initialScale;
    }

    // Call this when right mouse button is released
    public void HidePhone()
    {
        if (spawnedPhone == null) return;
        Destroy(spawnedPhone);
        spawnedPhone = null;
    }

    // Convenience toggle
    public void SetPhone(bool show)
    {
        if (show) ShowPhone(); else HidePhone();
    }

    // Optional: ensure phone is destroyed if player dies / prefab disabled
    void OnDisable()
    {
        HidePhone();
    }
}
