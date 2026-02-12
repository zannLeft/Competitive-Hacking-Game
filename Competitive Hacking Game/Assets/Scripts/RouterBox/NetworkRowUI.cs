using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkRowUI : MonoBehaviour
{
    [SerializeField]
    private TMP_Text nameText;

    [SerializeField]
    private Image[] bars; // assign in inspector left->right

    public void Set(string networkName, float strength01)
    {
        if (nameText)
            nameText.text = networkName;

        if (bars == null || bars.Length == 0)
            return;

        int activeBars = Mathf.RoundToInt(strength01 * bars.Length);
        activeBars = Mathf.Clamp(activeBars, 0, bars.Length);

        for (int i = 0; i < bars.Length; i++)
            bars[i].enabled = (i < activeBars);
    }
}
