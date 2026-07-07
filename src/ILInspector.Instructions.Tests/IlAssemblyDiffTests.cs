using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

public class IlAssemblyDiffTests
{
    [Fact]
    public void Compare_SameAssembly_HasNoPairChangesOrFailures()
    {
        using var stream = File.OpenRead(typeof(IlAssemblyDiffTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var result = IlAssemblyDiff.Compare(pe, reader, pe, reader, maxExamples: 3);

        Assert.True(result.ComparedBodyCount > 0);
        Assert.Equal(result.ComparedBodyCount, result.SelfDiffExactCount);
        Assert.Equal(result.ComparedBodyCount, result.PairExactCount);
        Assert.Equal(0, result.ChangedBodyCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.FailureBuckets);
        Assert.Empty(result.TopHunkKinds);
        Assert.Empty(result.TopOpcodeFamilies);
        Assert.Empty(result.Examples);
    }

    [Fact]
    public void CompareFiles_SameAssembly_PreservesInputPaths()
    {
        var assemblyPath = typeof(IlAssemblyDiffTests).Assembly.Location;

        var pair = IlAssemblyDiff.CompareFiles(assemblyPath, assemblyPath, maxExamples: 3);

        Assert.Equal(assemblyPath, pair.Old);
        Assert.Equal(assemblyPath, pair.New);
        Assert.True(pair.Diff.ComparedBodyCount > 0);
        Assert.Equal(0, pair.Diff.ChangedBodyCount);
        Assert.Equal(0, pair.Diff.FailureCount);
    }

    [Fact]
    public void CompareStreams_SameAssembly_PreservesSourceNames()
    {
        var assemblyPath = typeof(IlAssemblyDiffTests).Assembly.Location;
        using var oldStream = File.OpenRead(assemblyPath);
        using var newStream = File.OpenRead(assemblyPath);

        var pair = IlAssemblyDiff.CompareStreams(oldStream, "old.dll", newStream, "new.dll", maxExamples: 3);

        Assert.Equal("old.dll", pair.Old);
        Assert.Equal("new.dll", pair.New);
        Assert.True(pair.Diff.ComparedBodyCount > 0);
        Assert.Equal(0, pair.Diff.ChangedBodyCount);
        Assert.Equal(0, pair.Diff.FailureCount);
    }
}
