using Cove.App.Fakes;
using Cove.Core.Contracts;
using Cove.Tests.Files.Jobs;

namespace Cove.Tests.App.Fakes;

public sealed class FakeEventSourceTests
{
    // Short so tests don't wait; the real default is 10 s.
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    // Generous so a busy CI machine never fails a test.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task EachTick_PublishesASongChangeAndADownload()
    {
        var bus = new RecordingEventBus();
        using var source = new FakeEventSource(bus, Interval);

        source.Start();
        await WaitUntilAsync(() => bus.Events.Count(e => e.Type == CoveEventTypes.FileArrived) >= 2);

        var song = bus.Events.First(e => e.Type == CoveEventTypes.SongChanged);
        Assert.Equal(AlertPriority.SongChanged, song.Priority);
        Assert.False(string.IsNullOrEmpty(song.Message));

        var download = bus.Events.First(e => e.Type == CoveEventTypes.FileArrived);
        Assert.Equal(AlertPriority.DownloadFinished, download.Priority);
        Assert.False(string.IsNullOrEmpty(download.Message));
    }

    [Fact]
    public void BeforeStart_NothingIsPublished()
    {
        var bus = new RecordingEventBus();
        using var source = new FakeEventSource(bus, Interval);

        Assert.Empty(bus.Events);
    }

    [Fact]
    public async Task AfterDispose_NoMoreEventsArrive()
    {
        var bus = new RecordingEventBus();
        var source = new FakeEventSource(bus, Interval);
        source.Start();
        await WaitUntilAsync(() => bus.Events.Count > 0);

        source.Dispose();
        await Task.Delay(Interval * 2); // let a callback already in flight finish
        int countAfterStop = bus.Events.Count;
        await Task.Delay(Interval * 4);

        Assert.Equal(countAfterStop, bus.Events.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for fake events.");
            await Task.Delay(10);
        }
    }
}
