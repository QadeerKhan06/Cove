// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// An action that can be run by name from the command bar ("compress discord") or by voice
/// ("compress discord"). Both go through the same command router, so every command works by voice
/// automatically.
/// </summary>
/// <remarks>
/// Named <c>ICoveCommand</c>, not <c>ICommand</c>, so it never clashes with WPF's
/// <c>System.Windows.Input.ICommand</c>, which the UI uses for buttons.
/// </remarks>
/// <example>
/// <code>
/// public sealed class TimerCommand : ICoveCommand
/// {
///     public string Name =&gt; "timer";
///     public IReadOnlyList&lt;string&gt; Aliases =&gt; new[] { "countdown" };
///     public string Description =&gt; "Start a timer, e.g. \"timer 10\" for ten minutes.";
///     public string ArgumentHint =&gt; "&lt;minutes&gt;";
///     public IReadOnlyList&lt;string&gt; SpokenArguments =&gt; Array.Empty&lt;string&gt;(); // numbers come from the voice grammar
///
///     public Task&lt;CommandResult&gt; RunAsync(string arguments, CancellationToken ct)
///     {
///         if (!int.TryParse(arguments, out var minutes) || minutes is &lt; 1 or &gt; 600)
///             return Task.FromResult(CommandResult.Fail("Say or type a number of minutes, like \"timer 10\"."));
///
///         _timers.Start(TimeSpan.FromMinutes(minutes));
///         return Task.FromResult(CommandResult.Ok($"Timer set for {minutes} min"));
///     }
/// }
/// </code>
/// </example>
public interface ICoveCommand
{
    /// <summary>The word that runs this command: lowercase, one word, unique, e.g. "compress".</summary>
    string Name { get; }

    /// <summary>Other words that run it, e.g. "shrink". Lowercase; must not clash with other commands.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>One sentence shown in suggestions and in the Voice settings list.</summary>
    string Description { get; }

    /// <summary>What arguments look like, shown in suggestions, e.g. "&lt;format&gt;". Empty if none.</summary>
    string ArgumentHint { get; }

    /// <summary>
    /// Extra words voice should recognize after the name, e.g. "jpg", "pdf", "discord", "email".
    /// Numbers are handled by the voice grammar. Keep this list short and distinct.
    /// </summary>
    IReadOnlyList<string> SpokenArguments { get; }

    /// <summary>
    /// Runs the command. <paramref name="arguments"/> is everything after the name, trimmed
    /// (empty if none), e.g. "discord" for "compress discord". Start long work as a job and
    /// return straight away; don't wait for it to finish.
    /// </summary>
    Task<CommandResult> RunAsync(string arguments, CancellationToken cancellationToken);
}
