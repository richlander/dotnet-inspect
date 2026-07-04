using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public class BodySignalDiffTests
{
    [Fact]
    public void CompareUnsafe_SurfacesAddedUnsafeOperation()
    {
        var oldIndex = LibraryBodyIndex.Open(DiffFixturePath("DiffFixtures.V1"));
        var newIndex = LibraryBodyIndex.Open(DiffFixturePath("DiffFixtures.V2"));

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row =>
            row.Kind == BodySignalDiffKind.Added
            && row.Signal == "stackalloc"
            && row.Member.Contains("AddsUnsafe", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareUnsafe_SelfDiffHasNoRows()
    {
        var index = LibraryBodyIndex.Open(DiffFixturePath("DiffFixtures.V2"));

        var diff = BodySignalDiff.CompareUnsafe(index, index);

        Assert.Empty(diff.Rows);
    }

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
