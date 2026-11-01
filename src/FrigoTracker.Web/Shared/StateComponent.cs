using FrigoTracker.Web.Services;
using Microsoft.AspNetCore.Components;

namespace FrigoTracker.Web.Shared;

/// <summary>Base for pages that re-render whenever the app state changes (local edit or cloud pull).</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject] protected AppState State { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.Changed += OnStateChanged;
        OnStateInitialized();
    }

    /// <summary>Override instead of OnInitialized.</summary>
    protected virtual void OnStateInitialized() { }

    /// <summary>Called on every state change, before re-rendering.</summary>
    protected virtual void OnStateRefreshed() { }

    private void OnStateChanged() => InvokeAsync(() =>
    {
        OnStateRefreshed();
        StateHasChanged();
    });

    public virtual void Dispose()
    {
        State.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
