using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class LaptopMinigameBase : MonoBehaviour
{
    public event Action Completed;
    public event Action Failed;
    public event Action AlarmTriggered;
    public event Action ActionPerformed;

    public bool IsRunning { get; private set; }
    public bool IsPrepared { get; private set; }
    public bool IsSuspended { get; private set; }
    public LaptopMinigameContext Context { get; private set; }

    /// <summary>
    /// Override for modules that can pause their current session while the laptop is
    /// closed and resume it when the same router assignment is opened again.
    /// </summary>
    public virtual bool SupportsSessionResume => false;

    /// <summary>
    /// Builds any expensive, reusable UI before the player opens the laptop.
    /// Safe to call more than once.
    /// </summary>
    public void Prepare()
    {
        if (IsPrepared)
            return;

        OnPrepare();
        IsPrepared = true;
    }

    /// <summary>
    /// Incremental version used by the owner-side prewarm pass. Heavy minigames can
    /// spread UI construction across several frames instead of causing a visible hitch.
    /// </summary>
    public IEnumerator PrepareIncrementally(int operationsPerFrame)
    {
        if (IsPrepared)
            yield break;

        IEnumerator routine = OnPrepareIncrementally(Mathf.Max(1, operationsPerFrame));

        if (routine != null)
        {
            while (routine.MoveNext())
                yield return routine.Current;
        }

        IsPrepared = true;
    }

    public void Begin(LaptopMinigameContext context)
    {
        if (IsRunning)
            Abort();

        // Fallback for unusual cases where the player reaches the laptop before the
        // background prewarm has completed. Normally this is already prepared.
        Prepare();

        bool canResume =
            SupportsSessionResume
            && IsSuspended
            && ContextMatches(Context, context);

        // A pooled minigame can still contain suspended progress from another router.
        // Discard that stale state before beginning the new assignment.
        if (IsSuspended && !canResume)
        {
            IsSuspended = false;
            OnAbort();
            Context = default;
        }

        Context = context;
        IsRunning = true;
        IsSuspended = false;

        if (canResume)
            OnResume(context);
        else
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

    /// <summary>
    /// Pauses a resumable minigame without discarding its board, current round, timer,
    /// or player position. Non-resumable minigames fall back to a normal abort.
    /// </summary>
    public void Suspend()
    {
        if (!IsRunning)
            return;

        if (!SupportsSessionResume)
        {
            Abort();
            return;
        }

        IsRunning = false;
        IsSuspended = true;
        OnSuspend();
    }

    /// <summary>
    /// Fully discards both an active run and any suspended progress.
    /// </summary>
    public void Abort()
    {
        if (!IsRunning && !IsSuspended)
            return;

        IsRunning = false;
        IsSuspended = false;
        OnAbort();
        Context = default;
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
        IsSuspended = false;
        OnCompleted();
        Completed?.Invoke();
    }

    protected void FailMinigame()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        IsSuspended = false;
        OnFailed();
        Failed?.Invoke();
    }

    protected virtual void OnPrepare() { }

    protected virtual IEnumerator OnPrepareIncrementally(int operationsPerFrame)
    {
        OnPrepare();
        yield break;
    }

    protected abstract void OnBegin(LaptopMinigameContext context);

    /// <summary>
    /// Called when Begin receives the same router/minigame assignment that was
    /// previously suspended. Resumable modules should restore presentation only;
    /// their gameplay fields are intentionally left intact while suspended.
    /// </summary>
    protected virtual void OnResume(LaptopMinigameContext context)
    {
        OnBegin(context);
    }

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

    protected virtual void OnSuspend() { }

    protected virtual void OnAbort() { }

    protected virtual void OnCompleted() { }

    protected virtual void OnFailed() { }

    private static bool ContextMatches(
        LaptopMinigameContext first,
        LaptopMinigameContext second
    )
    {
        return first.MinigameId == second.MinigameId
            && first.Difficulty == second.Difficulty
            && first.Seed == second.Seed
            && string.Equals(
                first.NetworkId,
                second.NetworkId,
                StringComparison.Ordinal
            );
    }
}
