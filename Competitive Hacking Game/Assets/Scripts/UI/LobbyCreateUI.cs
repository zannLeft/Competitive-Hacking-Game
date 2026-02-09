using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyCreateUI : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button createPublicButton;
    [SerializeField] private Button createPrivateButton;
    [SerializeField] private TMP_InputField lobbyNameInputField;

    private void Awake()
    {
        createPublicButton.onClick.AddListener(() => {
            LobbyManager.Instance.CreateLobby(lobbyNameInputField.text, false);
        });

        createPrivateButton.onClick.AddListener(() => {
            LobbyManager.Instance.CreateLobby(lobbyNameInputField.text, true);
        });

        closeButton.onClick.AddListener(Hide);
    }

    public void Show()
    {
        Debug.Log("[UI] LobbyCreateUI.Show()");
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        Debug.Log("[UI] LobbyCreateUI.Hide()");
        gameObject.SetActive(false);
    }
}