using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

using ILInspector.ILDiff;

namespace ILInspector.ILDiff.Tests;

public class IlAssemblyDiffTests
{
    public static int MemberA() => 1;

    public static int MemberB() => 2;

    public static int MemberC(int value) => value + 1;

    public abstract class Bodyless
    {
        public abstract int Missing();
    }

    /// <summary>
    /// Methods pair by their member key under the Body policy's scope, as
    /// Finding Diff Any pairs members: two methods that spell one key (two
    /// compiler-controlled <c>M()</c>) pair by occurrence, and a parameter
    /// Type whose assembly changed name still pairs, which the text identity
    /// that carried assembly names did not.
    /// </summary>
    [Fact]
    public void Compare_PairsByMemberKey_ByOccurrenceAndWithoutAssemblyNames()
    {
        byte[] same = PairingImage("Lib.One");
        using (var oldPe = new PEReader(new MemoryStream(same)))
        using (var newPe = new PEReader(new MemoryStream(same)))
        {
            var result = IlAssemblyDiff.Compare(oldPe, oldPe.GetMetadataReader(), newPe, newPe.GetMetadataReader(), maxExamples: 3);
            Assert.Equal(3, result.ComparedBodyCount);
            Assert.Equal(3, result.PairExactCount);
            Assert.Empty(result.IdentityFailures);
        }

        using (var oldPe = new PEReader(new MemoryStream(PairingImage("Lib.One"))))
        using (var newPe = new PEReader(new MemoryStream(PairingImage("Lib.Two"))))
        {
            var result = IlAssemblyDiff.Compare(oldPe, oldPe.GetMetadataReader(), newPe, newPe.GetMetadataReader(), maxExamples: 3);
            Assert.Equal(3, result.ComparedBodyCount);
            Assert.Equal(0, result.PairUnavailableCount);
            Assert.Empty(result.IdentityFailures);
        }
    }

    /// <summary>
    /// A Type <c>N.C</c> with two compiler-controlled static methods <c>M()</c>
    /// and one <c>P(Probe)</c>, where <c>Probe</c> is a reference into the
    /// named assembly; every body is <c>ret</c>.
    /// </summary>
    static byte[] PairingImage(string referencedAssembly)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Pairing.dll"), metadata.GetOrAddGuid(new Guid("7b2f3f5e-0d7e-4b8f-9b8e-1f2c3d4e5f61")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Pairing"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle library = metadata.AddAssemblyReference(metadata.GetOrAddString(referencedAssembly), new Version(1, 0, 0, 0), default, default, default, default);
        TypeReferenceHandle probe = metadata.AddTypeReference(library, metadata.GetOrAddString("Dep"), metadata.GetOrAddString("Probe"));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature).MethodSignature().Parameters(0, r => r.Void(), _ => { });
        var probeSignature = new BlobBuilder();
        new BlobEncoder(probeSignature).MethodSignature().Parameters(1, r => r.Void(), p => p.AddParameter().Type().Type(probe, isValueType: false));

        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        var code = new InstructionEncoder(new BlobBuilder());
        code.OpCode(ILOpCode.Ret);
        int body = bodies.AddMethodBody(code);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        foreach ((string name, BlobBuilder signature, MethodAttributes access) in new[]
        {
            ("M", voidSignature, MethodAttributes.PrivateScope),
            ("M", voidSignature, MethodAttributes.PrivateScope),
            ("P", probeSignature, MethodAttributes.Public),
        })
        {
            metadata.AddMethodDefinition(
                access | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                body,
                default);
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                il)
            .Serialize(image);
        return image.ToArray();
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
