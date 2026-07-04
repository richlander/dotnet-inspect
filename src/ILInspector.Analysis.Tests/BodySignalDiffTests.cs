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

    [Fact]
    public void CompareUnsafe_MethodKeyDistinguishesSameNameDifferentArityDeclaringTypes()
    {
        var method1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`1"), "Use");
        var method2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`2"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method1],
            [new UnsafeEvidence(method1, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method2],
            [new UnsafeEvidence(method2, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Removed);
        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Added);
    }

    [Fact]
    public void CompareUnsafe_PreservesCountOfRepeatedUnsafeOperations()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null),
            ]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        var added = Assert.Single(diff.Rows);
        Assert.Equal(BodySignalDiffKind.Added, added.Kind);
        Assert.Equal("stackalloc", added.Operation);
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

    static MethodIdentity DiffMethod(TypeRef declaring, string name)
        => new(
            "Asm",
            Guid.Empty,
            declaring,
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
}
