using System.IO;
using Cove.Core.Contracts;

namespace Cove.App.Fakes;

/// <summary>
/// Pretends the user selected two files, so the Files panel can be built before the real
/// Explorer selection and clipboard sources exist (card K-11).
/// </summary>
/// <remarks>
/// The files don't exist on disk. That's fine for the fakes: <see cref="FakeJobService"/> never opens them.
/// One image and one video, so the panel can show different actions for each type.
/// </remarks>
public sealed class FakeFileSource : IFileSource
{
    /// <summary>The two paths <see cref="GetCurrentFiles"/> always returns.</summary>
    public static IReadOnlyList<string> SampleFiles { get; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Fake photo.heic"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Fake clip.mp4"),
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> GetCurrentFiles() => SampleFiles;
}
