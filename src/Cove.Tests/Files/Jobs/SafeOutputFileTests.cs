using System.IO;
using Cove.Files.Jobs;

namespace Cove.Tests.Files.Jobs;

public sealed class SafeOutputFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CoveTests", Guid.NewGuid().ToString("N"));

    public SafeOutputFileTests() => Directory.CreateDirectory(_folder);

    // Runs after each test: removes this test's own temp folder.
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Done-when, part 1.
    [Fact]
    public void ConvertingTheSameFileThreeTimes_GivesThreeFiles()
    {
        string input = CreateInput("photo.heic");

        string first = FakeConvert(input, ".jpg");
        string second = FakeConvert(input, ".jpg");
        string third = FakeConvert(input, ".jpg");

        Assert.Equal("photo.jpg", Path.GetFileName(first));
        Assert.Equal("photo (1).jpg", Path.GetFileName(second));
        Assert.Equal("photo (2).jpg", Path.GetFileName(third));
        Assert.Equal(4, Directory.GetFiles(_folder).Length); // input + 3 outputs, no temp files left
    }

    // Done-when, part 2.
    [Fact]
    public void ACancelledJob_LeavesNothingBehind()
    {
        string input = CreateInput("photo.heic");

        using (var output = SafeOutputFile.Create(input, ".jpg"))
        {
            File.WriteAllText(output.TempPath, "half-written");
            // The job is cancelled here, so Commit is never called.
        }

        Assert.Single(Directory.GetFiles(_folder)); // only the original is left
        Assert.True(File.Exists(input));
    }

    [Fact]
    public void KeepingTheSameFormat_NeverOverwritesTheOriginal()
    {
        string input = CreateInput("photo.jpg");

        string saved = FakeConvert(input, ".jpg");

        Assert.Equal("photo (1).jpg", Path.GetFileName(saved));
        Assert.Equal("original", File.ReadAllText(input));
    }

    [Fact]
    public void TheTempFile_IsNextToTheOriginal_AndKeepsTheRealExtension()
    {
        string input = CreateInput("photo.heic");

        using var output = SafeOutputFile.Create(input, "jpg"); // no dot on purpose

        Assert.Equal(_folder, Path.GetDirectoryName(output.TempPath));
        Assert.Equal(".jpg", Path.GetExtension(output.TempPath));
        Assert.Contains(".cove-partial-", Path.GetFileName(output.TempPath));
    }

    [Fact]
    public void CommittingWithoutWritingAnything_Throws()
    {
        string input = CreateInput("photo.heic");

        using var output = SafeOutputFile.Create(input, ".jpg");

        Assert.Throws<FileNotFoundException>(() => output.Commit());
    }

    [Fact]
    public void GetFreePath_SkipsEveryTakenName()
    {
        CreateInput("report.pdf");
        CreateInput("report (1).pdf");

        string free = SafeOutputFile.GetFreePath(Path.Combine(_folder, "report.pdf"));

        Assert.Equal("report (2).pdf", Path.GetFileName(free));
    }

    private string CreateInput(string fileName)
    {
        string path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, "original");
        return path;
    }

    // Stands in for a real converter: writes to the temp file, then commits.
    private static string FakeConvert(string inputPath, string extension)
    {
        using var output = SafeOutputFile.Create(inputPath, extension);
        File.WriteAllText(output.TempPath, "converted");
        return output.Commit();
    }
}