using System;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class LaptopMinigameBase : MonoBehaviour
{
    public event Action Completed;
    public event Action Failed;
    public event Action AlarmTriggered;
    public event Action ActionPerformed;

    public bool IsRunning { get; private set; }
    public LaptopMinigameContext Context { get; private set; }

    public void Begin(LaptopMinigameContext context)
    {
        if (IsRunning)
            Abort();

        Context = context;
        IsRunning = true;
        OnBegin(context);
    }

    public void SetNavigation(Vector2 input)
    {
        if (!IsRunning)
            return;

        OnNavigationChanged(input);
    }

    public void JumpPressed()
    {
        if (!IsRunning)
            return;

        OnJumpPressed();
    }

    public void JumpReleased()
    {
        if (!IsRunning)
            return;

        OnJumpReleased();
    }

    public void InteractPressed()
    {
        if (!IsRunning)
            return;

        OnInteractPressed();
    }

    public void InteractReleased()
    {
        if (!IsRunning)
            return;

        OnInteractReleased();
    }

    // Compatibility aliases for older minigames. New minigames should use the
    // explicit Jump and Interact channels so Space and E can have different roles.
    public void PrimaryPressed() => InteractPressed();
    public void PrimaryReleased() => InteractReleased();

    public void Abort()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        OnAbort();
    }

    /// <summary>
    /// Call this when the minigame accepts a meaningful player action.
    /// PlayerLaptopHacker uses it for the immediate local and networked keypress sound.
    /// </summary>
    protected void TriggerActionPerformed()
    {
        if (!IsRunning)
            return;

        ActionPerformed?.Invoke();
    }

    protected void TriggerAlarm()
    {
        if (!IsRunning)
            return;

        AlarmTriggered?.Invoke();
    }

    protected void CompleteMinigame()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        OnCompleted();
        Completed?.Invoke();
    }

    protected void FailMinigame()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        OnFailed();
        Failed?.Invoke();
    }

    protected abstract void OnBegin(LaptopMinigameContext context);

    protected virtual void OnNavigationChanged(Vector2 input) { }

    protected virtual void OnJumpPressed() { }

    protected virtual void OnJumpReleased() { }

    protected virtual void OnInteractPressed()
    {
        OnPrimaryPressed();
    }

    protected virtual void OnInteractReleased()
    {
        OnPrimaryReleased();
    }

    // Legacy extension points kept so older minigame prefabs/scripts continue
    // compiling. New code should override OnJump... or OnInteract... explicitly.
    protected virtual void OnPrimaryPressed() { }

    protected virtual void OnPrimaryReleased() { }

    protected virtual void OnAbort() { }

    protected virtual void OnCompleted() { }

    protected virtual void OnFailed() { }
}
