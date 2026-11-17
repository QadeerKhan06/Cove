// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>What happened when a command ran. The pill shows <see cref="Message"/> briefly.</summary>
/// <param name="Succeeded">Whether the command did what was asked.</param>
/// <param name="Message">Short feedback for the user, e.g. "Timer set for 10 min", or why it failed.</param>
public sealed record CommandResult(bool Succeeded, string? Message = null)
{
    /// <summary>A successful result, with optional feedback.</summary>
    public static CommandResult Ok(string? message = null) => new(true, message);

    /// <summary>A failed result. <paramref name="message"/> should tell the user what to try instead.</summary>
    public static CommandResult Fail(string message) => new(false, message);
}
