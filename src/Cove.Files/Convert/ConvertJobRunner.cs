using System.IO;
using System.Runtime.ExceptionServices;
using Cove.Core.Contracts;
using Cove.Files.Jobs;
using Serilog;

namespace Cove.Files.Convert;

/// <summary>
/// Does <see cref="JobKind.Convert"/> jobs by handing each file to the <see cref="IFileConverter"/> that
/// can handle it. Rules (decided in F-03):
/// <list type="bullet">
/// <item>Cancel saves nothing: every result waits as a temporary file until all files have been tried.</item>
/// <item>A file that fails is skipped and named in the result; the others are still saved.</item>
/// <item>If no file could be converted, the whole job fails.</item>
/// </list>
/// </summary>
public sealed class ConvertJobRunner : IJobRunner
{
    private readonly IReadOnlyList<IFileConverter> _converters;

    public ConvertJobRunner(IEnumerable<IFileConverter> converters)
    {
        ArgumentNullException.ThrowIfNull(converters);
        _converters = converters.ToList();
    }

    /// <inheritdoc />
    public JobKind Kind => JobKind.Convert;

    /// <inheritdoc />
    public async Task<JobOutput> RunAsync(Job job, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.InputFiles.Count == 0)
        {
            throw new ArgumentException("A convert job needs at least one file.", nameof(job));
        }

        // A bad setting fails the whole job before any work starts.
        ConvertOptions options = ConvertOptions.Parse(job.Options);
        if (!_converters.Any(c => c.OutputFormats.Contains(options.Format)))
        {
            throw new NotSupportedException($"Cove can't convert to {options.Format}.");
        }

        var converted = new List<(string Input, SafeOutputFile Output)>();
        var errors = new List<(string Input, Exception Error)>();
        try
        {
            for (int index = 0; index < job.InputFiles.Count; index++)
            {
                string input = job.InputFiles[index];
                var fileProgress = new FileProgress(progress, index, job.InputFiles.Count);
                var (output, error) = await TryConvertAsync(input, options, fileProgress, cancellationToken);
                if (output is not null)
                {
                    converted.Add((input, output));
                }
                else if (error is not null)
                {
                    errors.Add((input, error));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<string> saved = SaveAll(converted, errors);
            if (saved.Count == 0)
            {
                ThrowNothingWorked(errors);
            }

            return new JobOutput(saved) { FailedFiles = errors.Select(e => e.Input).ToList() };
        }
        finally
        {
            // Deletes the temporary files of anything not saved (cancel, or a failure above).
            foreach (var (_, output) in converted)
            {
                output.Dispose();
            }
        }
    }

    // Returns the temporary output on success, or the error if this one file couldn't be converted.
    // Cancelling is not a file error: it ends the whole job.
    private async Task<(SafeOutputFile? Output, Exception? Error)> TryConvertAsync(
        string input, ConvertOptions options, IProgress<double> progress, CancellationToken cancellationToken)
    {
        SafeOutputFile? output = null;
        try
        {
            IFileConverter converter = _converters.FirstOrDefault(c => c.CanConvert(input, options.Format))
                ?? throw new NotSupportedException($"Can't convert {Path.GetExtension(input)} files to {options.Format}.");
            output = SafeOutputFile.Create(input, options.Format);
            await converter.ConvertAsync(input, output.TempPath, options, progress, cancellationToken);
            return (output, null);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // ImageMagick may report a cancel as its own error, so turn any error after Cancel into a cancel.
            output?.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception ex)
        {
            output?.Dispose();

            // The type only: messages can contain file paths, which stay out of normal logs (CLAUDE.md 10.3).
            Log.Warning("Skipped one file in a convert job: {ErrorType}", ex.GetType().Name);
            Log.Debug(ex, "Convert failed for one file");
            return (null, ex);
        }
    }

    // No cancel checks in here: renaming takes moments, and stopping halfway would save only some files.
    private static List<string> SaveAll(
        List<(string Input, SafeOutputFile Output)> converted, List<(string Input, Exception Error)> errors)
    {
        var saved = new List<string>();
        foreach (var (input, output) in converted)
        {
            try
            {
                saved.Add(output.Commit());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Couldn't save one converted file: {ErrorType}", ex.GetType().Name);
                errors.Add((input, ex));
            }
        }

        return saved;
    }

    // One file: rethrow its own error with its stack trace, so F-10 can explain it. Several: all of them.
    private static void ThrowNothingWorked(List<(string Input, Exception Error)> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Throw(errors[0].Error);
        }

        throw new AggregateException("None of the files could be converted.", errors.Select(e => e.Error));
    }

    // Turns one file's 0-100 into its share of the whole job, e.g. file 2 of 4 covers 25-50.
    private sealed class FileProgress(IProgress<double> job, int index, int count) : IProgress<double>
    {
        public void Report(double value) => job.Report((index + Math.Clamp(value, 0, 100) / 100) * 100 / count);
    }
}
