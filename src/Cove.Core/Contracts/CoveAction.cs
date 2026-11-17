// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// A button on an Alert, such as "Open" or "Mute". Runs on the UI thread when clicked;
/// start long work in the background instead of awaiting it here.
/// </summary>
/// <param name="Label">Short button text, one or two words.</param>
/// <param name="InvokeAsync">What happens when the button is clicked.</param>
public sealed record CoveAction(string Label, Func<Task> InvokeAsync);
