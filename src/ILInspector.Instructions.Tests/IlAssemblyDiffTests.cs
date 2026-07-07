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
}
