using Cove.Core.Contracts;
using Cove.Files.Jobs;

namespace Cove.Tests.Files.Jobs;

/// <summary>
/// Pretends to convert a file: reports 10, 20 ... 100% with a short pause between steps.
/// Records how many jobs ran at the same time so tests can check the two-at-a-time rule.
/// </summary>
internal sealed class FakeJobRunner : IJobRunner
{
    private int _running;
    private int _maxRunning;
    private int _started;

    public JobKind Kind => JobKind.Convert;

    /// <summary>When true, every job throws instead of finishing.</summary>
    public bool ShouldFail { get; init; }

    public TimeSpan StepDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The most jobs that were ever running at the same moment.</summary>
    public int MaxRunningAtOnce => Volatile.Read(ref _maxRunning);

    /// <summary>How many jobs actually started running (queued-then-cancelled ones don't count).</summary>
    public int StartedCount => Volatile.Read(ref _started);

    public async Task<JobOutput> RunAsync(Job job, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _started);
        RecordRunning(Interlocked.Increment(ref _running));
        try
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Fake failure for tests.");
            }

            for (int step = 1; step <= 10; step++)
            {
                await Task.Delay(StepDelay, cancellationToken);
                progress.Report(step * 10);
            }

            return new JobOutput([job.InputFiles[0] + ".out"]);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private void RecordRunning(int runningNow)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxRunning);
            if (runningNow <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxRunning, runningNow, seen) != seen);
    }
}