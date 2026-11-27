using Cove.Core.Contracts;

namespace Cove.UI.Pill;

/// <summary>
/// What <see cref="PillController"/> needs from the pill's window. <see cref="PillWindow"/> implements
/// it; tests use a fake, so the rules for states and focus can be checked without a real window.
/// </summary>
public interface IPillView
{
    /// <summary>
    /// Esc was pressed in a panel and the panel didn't use it, so it was on its first step
    /// (CLAUDE.md 9.5: Esc goes to the panel first).
    /// </summary>
    event EventHandler? EscapePressed;

    /// <summary>
    /// The pill lost focus to another window (a click elsewhere, Alt+Tab) while in a state that
    /// takes focus. That window has focus now.
    /// </summary>
    event EventHandler? ClickedOutside;

    /// <summary>The user clicked the button on the alert that's showing.</summary>
    event EventHandler<CoveAction>? AlertActionClicked;

    /// <summary>The window that has keyboard focus right now, or 0 if none.</summary>
    nint GetForegroundWindow();

    /// <summary>
    /// Shows an alert without taking focus: its icon, its one-line message and its first button
    /// (the design allows one, SPEC.md §7).
    /// </summary>
    void ShowAlert(CoveEvent alert);

    /// <summary>
    /// Shows a panel's view and gives it keyboard focus. <paramref name="title"/> is the name
    /// screen readers announce; each panel draws its own header.
    /// </summary>
    /// <returns>False if <paramref name="view"/> isn't something the window can show.</returns>
    bool ShowPanel(string title, object view);

    /// <summary>Hides the pill.</summary>
    void Hide();

    /// <summary>Gives keyboard focus back to <paramref name="window"/>, if it still exists.</summary>
    void ReturnFocus(nint window);
}
