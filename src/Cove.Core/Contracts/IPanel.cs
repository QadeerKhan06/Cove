// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// A full view shown inside the pill's Panel state, such as Files, Now Playing or Timer.
/// Modules return their panels from <see cref="IModule.Panels"/>; the pill lists them in the
/// Cove key Menu under their <see cref="MenuLetter"/>.
/// </summary>
/// <remarks>
/// Cove.Core doesn't reference WPF, so <see cref="CreateView"/> returns <see cref="object"/>.
/// It must return a WPF <c>System.Windows.UIElement</c> built from the UI kit; the pill checks this.
/// </remarks>
/// <example>
/// <code>
/// public sealed class TimerPanel : IPanel
/// {
///     public string Id =&gt; "timer";
///     public string Title =&gt; "Timer";
///     public char MenuLetter =&gt; 'T';
///     public object CreateView() =&gt; new TimerView { DataContext = new TimerViewModel() };
///     public void OnOpened(object? parameter) { }
///     public void OnClosed() { }
/// }
/// </code>
/// </example>
public interface IPanel
{
    /// <summary>Stable id used to open this panel from code, e.g. "files", "media", "timer".</summary>
    string Id { get; }

    /// <summary>Title shown at the top of the panel, e.g. "Files".</summary>
    string Title { get; }

    /// <summary>
    /// The letter that opens this panel from the Cove key Menu, e.g. 'D' for Files.
    /// Must be unique across all panels; the pill logs and skips a duplicate.
    /// </summary>
    char MenuLetter { get; }

    /// <summary>Creates the panel's view. Called on the UI thread each time the panel opens.</summary>
    object CreateView();

    /// <summary>
    /// Called after the panel opens. <paramref name="parameter"/> is whatever the opener passed
    /// to <see cref="IPanelHost.OpenPanel"/>, e.g. a list of file paths for the Files panel; null if none.
    /// </summary>
    void OnOpened(object? parameter);

    /// <summary>Called after the panel closes. Running jobs or timers keep going.</summary>
    void OnClosed();
}
