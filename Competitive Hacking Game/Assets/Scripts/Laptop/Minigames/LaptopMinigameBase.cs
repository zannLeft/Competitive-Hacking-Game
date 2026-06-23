using System;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class LaptopMinigameBase : MonoBehaviour
{
    public event Action Completed;
    public event Action Failed;

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

    public void PrimaryPressed()
    {
        if (!IsRunning)
            return;

        OnPrimaryPressed();
    }

    public void PrimaryReleased()
    {
        if (!IsRunning)
            return;

        OnPrimaryReleased();
    }

    public void Abort()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        OnAbort();
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

    protected virtual void OnPrimaryPressed() { }

    protected virtual void OnPrimaryReleased() { }

    protected virtual void OnAbort() { }

    protected virtual void OnCompleted() { }

    protected virtual void OnFailed() { }
}
