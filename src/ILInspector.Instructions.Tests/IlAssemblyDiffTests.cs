using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

public class IlAssemblyDiffTests
{
    public static int MemberA() => 1;

    public static int MemberB() => 2;

    public static int MemberC(int value) => value + 1;

    public abstract class Bodyless
    {
        public abstract int Missing();
    }

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
        Assert.Equal(0, result.PairOperandDiffCount);
        Assert.Equal(0, result.PairOpcodeDiffCount);
        Assert.Equal(0, result.PairUnavailableCount);
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

    [Fact]
    public void CompareStreams_DoesNotDisposeCallerOwnedStreams()
    {
        var assemblyPath = typeof(IlAssemblyDiffTests).Assembly.Location;
        using var oldStream = File.OpenRead(assemblyPath);
        using var newStream = File.OpenRead(assemblyPath);

        _ = IlAssemblyDiff.CompareStreams(oldStream, "old.dll", newStream, "new.dll", maxExamples: 0);

        Assert.True(oldStream.CanRead);
        Assert.True(newStream.CanRead);
    }

    [Fact]
    public void CompareMembers_SameMethod_IsExactAndPreservesDefaultSubject()
    {
        using var stream = File.OpenRead(typeof(IlAssemblyDiffTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, nameof(MemberA));

        var result = IlAssemblyDiff.CompareMembers(pe, reader, method, pe, reader, method);

        Assert.True(result.Diff.IsExact);
        Assert.True(result.Diff.IsAvailable);
        Assert.Equal(IlBodyDiffOutcome.Exact, result.Diff.Outcome);
        Assert.Equal(result.Old.Identity, result.Old.Label);
        Assert.Equal(result.Old.Identity, result.New.Identity);
        Assert.Contains(nameof(MemberA), result.Old.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareMembers_DifferentMethods_ReturnsChangedBodyDiffAndCustomLabels()
    {
        using var stream = File.OpenRead(typeof(IlAssemblyDiffTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var result = IlAssemblyDiff.CompareMembers(
            pe,
            reader,
            FindMethod(reader, nameof(MemberA)),
            pe,
            reader,
            FindMethod(reader, nameof(MemberB)),
            oldLabel: "old-member",
            newLabel: "new-member");

        Assert.False(result.Diff.IsExact);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, result.Diff.Outcome);
        Assert.NotEmpty(result.Diff.Rows);
        Assert.Equal("old-member", result.Old.Label);
        Assert.Equal("new-member", result.New.Label);
        Assert.Contains(nameof(MemberA), result.Old.Identity, StringComparison.Ordinal);
        Assert.Contains(nameof(MemberB), result.New.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareMembers_OpcodeSequenceChange_ReturnsOpcodeDiff()
    {
        using var stream = File.OpenRead(typeof(IlAssemblyDiffTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var result = IlAssemblyDiff.CompareMembers(
            pe,
            reader,
            FindMethod(reader, nameof(MemberA)),
            pe,
            reader,
            FindMethod(reader, nameof(MemberC)));

        Assert.Equal(IlBodyDiffOutcome.OpcodeDiff, result.Diff.Outcome);
    }

    [Fact]
    public void CompareMembers_BodylessOldMethod_ReturnsOldBodyMissing()
    {
        using var stream = File.OpenRead(typeof(IlAssemblyDiffTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var result = IlAssemblyDiff.CompareMembers(
            pe,
            reader,
            FindMethod(reader, nameof(Bodyless), nameof(Bodyless.Missing)),
            pe,
            reader,
            FindMethod(reader, nameof(MemberA)));

        var failure = Assert.Single(result.Diff.FailureRows);
        Assert.False(result.Diff.IsAvailable);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, result.Diff.Outcome);
        Assert.Equal(IlDiffFailureKind.OldBodyMissing, failure.Kind);
        Assert.Equal("old", failure.Side);
    }

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string methodName)
        => FindMethod(reader, nameof(IlAssemblyDiffTests), methodName);

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string typeName, string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == methodName)
                    return methodHandle;
            }
        }

        throw new InvalidOperationException($"{typeName}.{methodName} not found.");
    }
}
