using Cove.Core.Contracts;
using Cove.Files.Jobs;

namespace Cove.Tests.Files.Jobs;

public sealed class JobServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    // The card's done-when: five fake jobs, two at a time, progress reported, one cancelled midway.
    [Fact]
    public async Task FiveJobs_RunTwoAtATime_ReportProgress_AndOneIsCancelledMidway()
    {
        var bus = new RecordingEventBus();
        var runner = new FakeJobRunner();
        var service = new JobService(bus, [runner]);
        Task allEnded = WaitForEndedJobsAsync(bus, 5);

        var ids = Enumerable.Range(1, 5).Select(n => service.Start(FakeJob(n))).ToList();
        Guid toCancel = ids[0];

        // Cancel the first job once it's at least 30% done.
        bus.Subscribe(JobEventTypes.Progress, e =>
        {
            if (e.GetData<JobProgress>() is { } p && p.JobId == toCancel && p.Percent >= 30)
            {
                service.Cancel(toCancel);
            }
        });

        await allEnded;
        var events = bus.Events;

        Assert.Equal(JobService.MaxConcurrentJobs, runner.MaxRunningAtOnce);
        Assert.Single(events, e => e.Type == JobEventTypes.Cancelled && e.GetData<JobResult>()?.JobId == toCancel);
        Assert.DoesNotContain(events, e => IsProgress(e, toCancel, 100));
        Assert.Equal(4, events.Count(e => e.Type == JobEventTypes.Completed));
        Assert.DoesNotContain(events, e => e.Type == JobEventTypes.Failed);
        foreach (var id in ids.Skip(1))
        {
            Assert.Contains(events, e => IsProgress(e, id, 100));
        }
    }

    [Fact]
    public async Task CancellingAQueuedJob_EndsItWithoutEverRunningIt()
    {
        var bus = new RecordingEventBus();
        var runner = new FakeJobRunner();
        var service = new JobService(bus, [runner]);
        Task allEnded = WaitForEndedJobsAsync(bus, 3);

        service.Start(FakeJob(1));
        service.Start(FakeJob(2));
        await WaitUntilAsync(() => runner.StartedCount == 2);

        Guid queued = service.Start(FakeJob(3)); // both slots are busy, so this one waits
        service.Cancel(queued);
        await allEnded;

        Assert.Equal(2, runner.StartedCount);
        Assert.Contains(bus.Events, e => e.Type == JobEventTypes.Cancelled && e.GetData<JobResult>()?.JobId == queued);
    }

    [Fact]
    public async Task AJobThatThrows_PublishesFailedWithAMessage()
    {
        var bus = new RecordingEventBus();
        var service = new JobService(bus, [new FakeJobRunner { ShouldFail = true }]);
        Task ended = WaitForEndedJobsAsync(bus, 1);

        Guid id = service.Start(FakeJob(1));
        await ended;

        var failed = Assert.Single(bus.Events, e => e.Type == JobEventTypes.Failed);
        Assert.Equal(id, failed.GetData<JobResult>()?.JobId);
        Assert.False(string.IsNullOrEmpty(failed.Message));
    }

    [Fact]
    public async Task AJobWithNoRunnerForItsKind_Fails()
    {
        var bus = new RecordingEventBus();
        var service = new JobService(bus, [new FakeJobRunner()]); // only handles Convert
        Task ended = WaitForEndedJobsAsync(bus, 1);

        service.Start(new Job(JobKind.Upload, ["C:\\fake\\photo.png"], new Dictionary<string, string>()));
        await ended;

        Assert.Single(bus.Events, e => e.Type == JobEventTypes.Failed);
    }

    [Fact]
    public void CancellingAnUnknownJob_DoesNothing()
    {
        var bus = new RecordingEventBus();
        var service = new JobService(bus, [new FakeJobRunner()]);

        service.Cancel(Guid.NewGuid());

        Assert.Empty(bus.Events);
    }

    private static Job FakeJob(int number) =>
        new(JobKind.Convert, [$"C:\\fake\\photo{number}.png"], new Dictionary<string, string>());

    private static bool IsProgress(CoveEvent e, Guid jobId, double percent) =>
        e.Type == JobEventTypes.Progress
        && e.GetData<JobProgress>() is { } p
        && p.JobId == jobId
        && p.Percent == percent;

    private static bool IsEnd(CoveEvent e) =>
        e.Type is JobEventTypes.Completed or JobEventTypes.Failed or JobEventTypes.Cancelled;

    // Subscribe BEFORE starting jobs, then await the returned task.
    private static Task WaitForEndedJobsAsync(RecordingEventBus bus, int count)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int ended = 0;
        bus.SubscribeAll(e =>
        {
            if (IsEnd(e) && Interlocked.Increment(ref ended) == count)
            {
                done.TrySetResult();
            }
        });
        return done.Task.WaitAsync(TestTimeout);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }
}