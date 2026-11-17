// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// Lets any module open a panel, e.g. a download Alert's "Convert" button opening the
/// Files panel with that file loaded. Implemented by the pill.
/// </summary>
/// <example>
/// <code>
/// new CoveAction("Convert", () =&gt;
/// {
///     panels.OpenPanel("files", new[] { downloadedPath });
///     return Task.CompletedTask;
/// });
/// </code>
/// </example>
public interface IPanelHost
{
    /// <summary>
    /// Opens the panel with this <paramref name="panelId"/>, passing <paramref name="parameter"/>
    /// to its <see cref="IPanel.OnOpened"/>. Does nothing (and logs) if no such panel is registered.
    /// </summary>
    void OpenPanel(string panelId, object? parameter = null);

    /// <summary>Closes whatever panel is open, if any.</summary>
    void ClosePanel();
}
