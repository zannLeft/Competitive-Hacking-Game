using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLaptopVisual : NetworkBehaviour
{
    [Header("Sockets")]
    [SerializeField]
    private Transform handSocketL;

    [SerializeField]
    private Transform lapSocket;

    [Header("Laptop")]
    [SerializeField]
    private GameObject laptopRoot;

    [SerializeField]
    private Animator laptopAnimator;

    [SerializeField]
    private LaptopScreenController laptopScreen;

    [Header("Laptop Animator Params")]
    [SerializeField]
    private string laptopOpenBool = "Open";

    [Header("Attach Smoothing")]
    [SerializeField]
    private float attachBlendTime = 0.10f;

    private int _openHash;
    private Coroutine _attachRoutine;
    private Vector3 _baseLocalScale = Vector3.one;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _openHash = Animator.StringToHash(laptopOpenBool);

        if (laptopRoot != null)
        {
            _baseLocalScale = laptopRoot.transform.localScale;

            if (laptopScreen == null)
                laptopScreen = laptopRoot.GetComponentInChildren<LaptopScreenController>(true);

            if (laptopScreen != null)
                laptopScreen.ConfigureForOwner(IsOwner);
        }

        ForceHiddenInHand();
    }

    // ----------------------------
    // Animation Events
    // ----------------------------

    // SitDown clip:
    // Call when left hand reaches backpack / "grabs" laptop.
    public void AE_LaptopShowInHand()
    {
        if (laptopRoot == null)
            return;

        AttachInstant(handSocketL);

        // Laptop appears closed/off in the hand.
        laptopScreen?.SetScreenOn(false);

        laptopRoot.SetActive(true);
    }

    // SitDown clip:
    // Call when laptop reaches lap placement pose.
    public void AE_LaptopPlaceOnLap()
    {
        if (laptopRoot == null)
            return;

        AttachSmooth(lapSocket, attachBlendTime);
    }

    // SitDown clip:
    // Call when the lid should start/open.
    // This does NOT turn on the screen anymore.
    public void AE_LaptopOpen()
    {
        if (laptopAnimator != null)
            laptopAnimator.SetBool(_openHash, true);
    }

    // SitDown clip:
    // Call slightly AFTER AE_LaptopOpen, when you want the screen to light up.
    public void AE_LaptopScreenOn()
    {
        laptopScreen?.SetScreenOn(true);
    }

    // StandUp clip:
    // Call when the lid should start/close.
    // This does NOT turn off the screen anymore.
    public void AE_LaptopClose()
    {
        if (laptopAnimator != null)
            laptopAnimator.SetBool(_openHash, false);
    }

    // StandUp clip:
    // Call slightly AFTER AE_LaptopClose, when you want the screen to go dark.
    public void AE_LaptopScreenOff()
    {
        laptopScreen?.SetScreenOn(false);
    }

    // StandUp clip:
    // Call when hand grabs laptop from lap.
    public void AE_LaptopBackToHand()
    {
        if (laptopRoot == null)
            return;

        // Safety: make sure screen is off before carrying it away.
        laptopScreen?.SetScreenOn(false);

        AttachSmooth(handSocketL, attachBlendTime);
    }

    // StandUp clip:
    // Call when left hand reaches backpack / laptop is put away.
    public void AE_LaptopHide()
    {
        if (laptopRoot == null)
            return;

        laptopScreen?.SetScreenOn(false);

        AttachInstant(handSocketL);
        laptopRoot.SetActive(false);
    }

    // ----------------------------
    // Helpers
    // ----------------------------

    private void ForceHiddenInHand()
    {
        if (laptopRoot == null)
            return;

        AttachInstant(handSocketL);

        if (laptopAnimator != null)
            laptopAnimator.SetBool(_openHash, false);

        laptopScreen?.SetScreenOn(false);

        laptopRoot.SetActive(false);
    }

    private void AttachInstant(Transform socket)
    {
        if (socket == null || laptopRoot == null)
            return;

        if (_attachRoutine != null)
        {
            StopCoroutine(_attachRoutine);
            _attachRoutine = null;
        }

        Transform t = laptopRoot.transform;
        t.SetParent(socket, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = _baseLocalScale;
    }

    private void AttachSmooth(Transform socket, float blendTime)
    {
        if (socket == null || laptopRoot == null)
            return;

        if (_attachRoutine != null)
            StopCoroutine(_attachRoutine);

        _attachRoutine = StartCoroutine(CoAttachSmooth(socket, blendTime));
    }

    private IEnumerator CoAttachSmooth(Transform socket, float blendTime)
    {
        Transform t = laptopRoot.transform;

        // Keep current world pose, so there is no instant visual snap.
        t.SetParent(socket, worldPositionStays: true);

        Vector3 startLocalPos = t.localPosition;
        Quaternion startLocalRot = t.localRotation;
        Vector3 startLocalScale = t.localScale;

        float timer = 0f;
        float duration = Mathf.Max(0.001f, blendTime);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float a = Mathf.Clamp01(timer / duration);

            // smoothstep
            a = a * a * (3f - 2f * a);

            t.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, a);
            t.localRotation = Quaternion.Slerp(startLocalRot, Quaternion.identity, a);
            t.localScale = Vector3.Lerp(startLocalScale, _baseLocalScale, a);

            yield return null;
        }

        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = _baseLocalScale;

        _attachRoutine = null;
    }
}