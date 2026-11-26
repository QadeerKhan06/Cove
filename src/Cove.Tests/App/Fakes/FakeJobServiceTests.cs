using Cove.App.Fakes;
using Cove.Core.Contracts;
using Cove.Tests.Files.Jobs;

namespace Cove.Tests.App.Fakes;

public sealed class FakeJobServiceTests
{
    // Short so tests don't wait; the real default is 5 s.
    private static readonly TimeSpan JobLength = TimeSpan.FromMilliseconds(200);

    // Generous so a busy CI machine never fails a test.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AJob_ReportsProgressUpTo100_ThenCompletes()
    {
        var bus = new RecordingEventBus();
        var service = new FakeJobService(bus, JobLength);

        Guid id = service.Start(MakeJob());
        await WaitUntilAsync(() => bus.Events.Any(IsEnd));

        var progress = bus.Events
            .Where(e => e.Type == JobEventTypes.Progress)
            .Select(e => e.GetData<JobProgress>())
            .ToList();
        Assert.Equal(10, progress.Count);
        Assert.All(progress, p => Assert.Equal(id, p?.JobId));
        Assert.Equal(100, progress[^1]?.Percent);

        var completed = Assert.Single(bus.Events, e => e.Type == JobEventTypes.Completed);
        Assert.Equal(AlertPriority.JobCompleted, completed.Priority);
        Assert.True(completed.GetData<JobResult>()?.Succeeded);
        Assert.DoesNotContain(bus.Events, e => e.Type == JobEventTypes.Failed);
    }

    [Fact]
    public async Task AJobAskedToFail_PublishesFailedWithAMessage()
    {
        var bus = new RecordingEventBus();
        var service = new FakeJobService(bus, JobLength);

        service.Start(MakeJob(new Dictionary<string, string> { [FakeJobService.FailOption] = "true" }));
        await WaitUntilAsync(() => bus.Events.Any(IsEnd));

        var failed = Assert.Single(bus.Events, e => e.Type == JobEventTypes.Failed);
        Assert.Equal(AlertPriority.JobFailed, failed.Priority);
        Assert.False(string.IsNullOrEmpty(failed.Message));
        Assert.False(failed.GetData<JobResult>()?.Succeeded);
        Assert.DoesNotContain(bus.Events, e => e.Type == JobEventTypes.Completed);
    }

    [Fact]
    public async Task CancellingARunningJob_PublishesCancelledWithNoAlertText()
    {
        var bus = new RecordingEventBus();
        var service = new FakeJobService(bus, TimeSpan.FromSeconds(5)); // long, so it's surely still running

        Guid id = service.Start(MakeJob());
        await WaitUntilAsync(() => bus.Events.Any(e => e.Type == JobEventTypes.Progress));
        service.Cancel(id);
        await WaitUntilAsync(() => bus.Events.Any(IsEnd));

        var cancelled = Assert.Single(bus.Events, e => e.Type == JobEventTypes.Cancelled);
        Assert.Null(cancelled.Message);
        Assert.DoesNotContain(bus.Events, e => e.Type == JobEventTypes.Completed);
    }

    [Fact]
    public void CancellingAnUnknownJob_DoesNothing()
    {
        var bus = new RecordingEventBus();
        var service = new FakeJobService(bus, JobLength);

        service.Cancel(Guid.NewGuid());

        Assert.Empty(bus.Events);
    }

    private static Job MakeJob(Dictionary<string, string>? options = null) =>
        new(JobKind.Convert, FakeFileSource.SampleFiles, options ?? new Dictionary<string, string>());

    private static bool IsEnd(CoveEvent e) =>
        e.Type is JobEventTypes.Completed or JobEventTypes.Failed or JobEventTypes.Cancelled;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the fake job.");
            await Task.Delay(10);
        }
    }
}
