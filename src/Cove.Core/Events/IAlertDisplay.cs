using Cove.Core.Contracts;

namespace Cove.Core.Events;

/// <summary>
/// What <see cref="AlertQueue"/> needs from the pill. The pill (Cove.UI) implements it, since
/// Cove.Core can't reference Cove.UI (CLAUDE.md 8.1); tests use a fake.
/// </summary>
public interface IAlertDisplay
{
    /// <summary>A panel closed, so alerts can show again.</summary>
    event EventHandler? PanelClosed;

    /// <summary>The user clicked a button on the alert that's showing.</summary>
    event EventHandler<CoveAction>? ActionClicked;

    /// <summary>Shows the alert without taking focus, in place of any alert already showing.</summary>
    /// <returns>False while a panel is open: nothing is shown.</returns>
    bool ShowAlert(CoveEvent alert);

    /// <summary>Hides the alert, if one is showing.</summary>
    void HideAlert();
}
