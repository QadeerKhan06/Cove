using System.IO;
using Cove.Core.Contracts;
using Cove.Files.Convert;
using Cove.Files.Jobs;
using Cove.Tests.Files.Jobs;
using ImageMagick;

namespace Cove.Tests.Files.Convert;

// Runs convert jobs through the real JobService, the way the Files panel will.
public sealed class ConvertJobRunnerTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CoveTests", Guid.NewGuid().ToString("N"));
    private readonly RecordingEventBus _bus = new();

    public ConvertJobRunnerTests() => Directory.CreateDirectory(_folder);

    // Runs after each test: removes this test's own temp folder.
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task RealImages_ConvertThroughTheJobService()
    {
        string first = CreateImage("first.png");
        string second = CreateImage("second.bmp");

        CoveEvent end = await RunAsync(new ImageConverter(), [first, second], "jpg");

        Assert.Equal(JobEventTypes.Completed, end.Type);
        Assert.Equal("Saved 2 files", end.Message);
        var result = end.GetData<JobResult>();
        Assert.Equal(["first.jpg", "second.jpg"], result!.OutputPaths.Select(Path.GetFileName));
        Assert.All(result.OutputPaths, path => Assert.True(File.Exists(path)));
        Assert.Contains(_bus.Events, e => e.Type == JobEventTypes.Progress && e.GetData<JobProgress>()!.Percent == 100);
        AssertFolderHolds("first.png", "second.bmp", "first.jpg", "second.jpg");
    }

    [Fact]
    public async Task OneBadFile_TheOthersAreSaved_AndTheUserIsTold()
    {
        string good = CreateImage("good.png");
        string broken = Path.Combine(_folder, "broken.png");
        File.WriteAllText(broken, "this is not a picture");

        CoveEvent end = await RunAsync(new ImageConverter(), [good, broken], "jpg");

        Assert.Equal(JobEventTypes.Completed, end.Type);
        Assert.Equal("Saved good.jpg. Couldn't convert broken.png", end.Message);
        AssertFolderHolds("good.png", "broken.png", "good.jpg");
    }

    [Fact]
    public async Task EveryFileFails_TheJobFails_AndLeavesNothingBehind()
    {
        string broken = Path.Combine(_folder, "broken.png");
        File.WriteAllText(broken, "this is not a picture");

        CoveEvent end = await RunAsync(new ImageConverter(), [broken], "jpg");

        Assert.Equal(JobEventTypes.Failed, end.Type);
        AssertFolderHolds("broken.png");
    }

    [Fact]
    public async Task CancellingMidway_SavesNothing_EvenFilesAlreadyConverted()
    {
        string[] inputs = [CreateImage("a.png"), CreateImage("b.png"), CreateImage("c.png")];
        var converter = new StuckOnSecondFileConverter();
        var service = new JobService(_bus, [new ConvertJobRunner([converter])]);
        Task<CoveEvent> ended = WaitForEndAsync();

        Guid id = service.Start(ConvertJob(inputs, "jpg"));
        await converter.SecondFileStarted.WaitAsync(TestTimeout); // a.png is converted but not saved yet
        service.Cancel(id);
        CoveEvent end = await ended;

        Assert.Equal(JobEventTypes.Cancelled, end.Type);
        AssertFolderHolds("a.png", "b.png", "c.png");
    }

    [Fact]
    public async Task AnUnknownFormat_FailsBeforeConvertingAnything()
    {
        var converter = new StuckOnSecondFileConverter();

        CoveEvent end = await RunAsync(converter, [CreateImage("a.png")], "mp4");

        Assert.Equal(JobEventTypes.Failed, end.Type);
        Assert.Equal(0, converter.FilesStarted);
    }

    [Fact]
    public async Task AJobWithNoFiles_Fails()
    {
        CoveEvent end = await RunAsync(new ImageConverter(), [], "jpg");

        Assert.Equal(JobEventTypes.Failed, end.Type);
    }

    private async Task<CoveEvent> RunAsync(IFileConverter converter, string[] inputs, string format)
    {
        var service = new JobService(_bus, [new ConvertJobRunner([converter])]);
        Task<CoveEvent> ended = WaitForEndAsync();
        service.Start(ConvertJob(inputs, format));
        return await ended;
    }

    private static Job ConvertJob(string[] inputs, string format) =>
        new(JobKind.Convert, inputs, new Dictionary<string, string> { [ConvertOptions.FormatKey] = format });

    // Subscribe BEFORE starting the job, then await the returned task.
    private Task<CoveEvent> WaitForEndAsync()
    {
        var done = new TaskCompletionSource<CoveEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bus.SubscribeAll(e =>
        {
            if (e.Type is JobEventTypes.Completed or JobEventTypes.Failed or JobEventTypes.Cancelled)
            {
                done.TrySetResult(e);
            }
        });
        return done.Task.WaitAsync(TestTimeout);
    }

    private string CreateImage(string name)
    {
        string path = Path.Combine(_folder, name);
        using var image = new MagickImage(MagickColors.Blue, 20, 20);
        using FileStream stream = File.Create(path);
        image.Write(stream, Path.GetExtension(name) == ".bmp" ? MagickFormat.Bmp : MagickFormat.Png);
        return path;
    }

    // Also proves no temporary ".cove-partial-" files were left behind.
    private void AssertFolderHolds(params string[] names) =>
        Assert.Equal(names.Order(), Directory.GetFiles(_folder).Select(Path.GetFileName).Order());

    // Converts the first file instantly, then waits on the second until the job is cancelled.
    private sealed class StuckOnSecondFileConverter : IFileConverter
    {
        private readonly TaskCompletionSource _secondFileStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _filesStarted;

        public Task SecondFileStarted => _secondFileStarted.Task;

        public int FilesStarted => Volatile.Read(ref _filesStarted);

        public IReadOnlyCollection<string> OutputFormats { get; } = ["jpg"];

        public bool CanConvert(string inputPath, string format) => format == "jpg";

        public async Task ConvertAsync(
            string inputPath, string outputPath, ConvertOptions options, IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(outputPath, "converted", cancellationToken);
            if (Interlocked.Increment(ref _filesStarted) == 2)
            {
                _secondFileStarted.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }
}
