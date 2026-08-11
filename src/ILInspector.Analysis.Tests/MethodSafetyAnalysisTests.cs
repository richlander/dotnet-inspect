using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

public sealed class MethodSafetyAnalysisTests
{
    static readonly TypeRef s_int =
        TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void DeclarationAndLocalsPreserveSafetyEvidenceOrder()
    {
        var method = Method(
            TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "Unsafe"),
            [TypeRef.Pointer(s_int)]);
        var declarationEvidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var declaration =
            MethodSafetyAnalysis.InspectDeclaration(
                method,
                declarationEvidence);

        Assert.True(declaration.HasUnsafeApiMember);
        Assert.True(declaration.HasUnsafeSignature);
        Assert.Collection(
            declarationEvidence,
            evidence => Assert.Equal("api", evidence.Kind),
            evidence => Assert.Equal("signature", evidence.Kind));

        var context = Context(
            method,
            [TypeRef.Pinned(s_int), TypeRef.Pointer(s_int)]);
        var localEvidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var locals = MethodSafetyAnalysis.InspectLocals(
            context,
            localEvidence);

        Assert.True(locals.HasUnsafeLocals);
        Assert.Collection(
            localEvidence,
            evidence =>
            {
                Assert.Equal("Pinned local", evidence.Reason);
                Assert.StartsWith("V_0:", evidence.Detail);
            },
            evidence =>
            {
                Assert.Equal("Pointer local", evidence.Reason);
                Assert.StartsWith("V_1:", evidence.Detail);
            });
    }

    [Fact]
    public void OccurrencesTrackPointerValuesThroughLocals()
    {
        var method = Method(
            TypeRef.Definition("Fixture", "Fixtures", "Pointers"),
            [TypeRef.Pointer(s_int)]);
        // ldarg.0; stloc.0; ldloc.0; ldind.i4; ret
        byte[] il = [0x02, 0x0A, 0x06, 0x4A, 0x2A];
        var context = Context(method, [s_int], il);

        var occurrence = Assert.Single(
            MethodSafetyAnalysis.CollectOccurrences(
                context,
                _ => throw new InvalidOperationException(
                    "No calli is present.")));

        Assert.Equal(method, occurrence.Method);
        Assert.Equal(3, occurrence.ILOffset);
        Assert.Equal(UnsafetyKind.Deref, occurrence.Kind);
        Assert.Equal("int", occurrence.Detail);
    }

    static MethodIdentity Method(
        TypeRef declaringType,
        TypeRef[] parameters)
        => new(
            "Fixture",
            Guid.Empty,
            declaringType,
            "M",
            [.. parameters],
            s_int,
            MetadataToken: 0x06000001,
            IsStatic: true);

    static MethodBodyAnalysisContext Context(
        MethodIdentity method,
        TypeRef[] locals,
        byte[]? il = null)
    {
        il ??= [0x2A];
        var instructions = MethodInstructions.Decode(
            il,
            il.Length,
            []);
        Assert.True(instructions.IsComplete);
        return new MethodBodyAnalysisContext(
            method,
            instructions,
            [],
            [],
            [.. locals]);
    }
}
