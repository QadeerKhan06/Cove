using System.IO;
using Cove.App.Fakes;

namespace Cove.Tests.App.Fakes;

public sealed class FakeFileSourceTests
{
    [Fact]
    public void ReturnsTwoSampleFiles()
    {
        var files = new FakeFileSource().GetCurrentFiles();

        Assert.Equal(2, files.Count);
        Assert.All(files, path => Assert.True(Path.IsPathFullyQualified(path)));
    }
}
