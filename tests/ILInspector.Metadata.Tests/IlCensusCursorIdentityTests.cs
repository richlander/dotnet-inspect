using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ILDiff;
using ILInspector.Instructions;
using Inspector.Findings;

using static ILInspector.Metadata.Tests.FindingDiffAnyIdentityTests;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The identity gate of <c>docs/design/finding-census.md#gates</c>: over the
/// identity images, the IL cursor's atoms differ where Finding Diff Any's walk
/// differs, which a formatted operand does not satisfy. Each case names two
/// callees whose formatted spellings coincide and whose keys do not; and under
/// the Body policy the cursor's key for an operand row is the key Finding Diff
/// Any memoizes, byte for byte.
/// </summary>
public sealed class IlCensusCursorIdentityTests
{
    static readonly FindingSubject Subject = new("M", "M");

    // call MethodDef 1 or 2; ret
    static readonly byte[] CallFirst = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
    static readonly byte[] CallSecond = [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A];

    [Fact]
    public void MemberNamesWithSeparators_DistinguishCallees()
    {
        // Method D::M of N.C (MethodDef 1) and method M of N.C::D (MethodDef 2)
        // both format as `N.C::D::M`.
        TypeSpec[] callees = [new("N", "C", null, ReturnOne, ["D::M"]), new("N", "C::D", null, ReturnOne)];
        AssertCallersDiffer(
            Image([.. callees, new("N", "Caller", null, CallFirst)]),
            Image([.. callees, new("N", "Caller", null, CallSecond)]),
            callerMethod: 3);
    }

    [Fact]
    public void NamespaceAndDottedName_DistinguishCallees()
    {
        // N.A.B::M (MethodDef 1) and B::M in namespace N.A (MethodDef 2) both
        // format as `N.A.B::M`.
        TypeSpec[] callees = [new("N", "A.B", null, ReturnOne), new("N.A", "B", null, ReturnOne)];
        AssertCallersDiffer(
            Image([.. callees, new("N", "Caller", null, CallFirst)]),
            Image([.. callees, new("N", "Caller", null, CallSecond)]),
            callerMethod: 3);
    }

    [Fact]
    public void IllFormedNamesThatDecodeAlike_DistinguishCallees()
    {
        // The callee's stored name differs between the images; both decode to
        // U+FFFD U+FFFD X, so the formatted operands coincide.
        TypeSpec[] types = [new("N", "C", null, ReturnOne, ["M", "QQX"]), new("N", "Caller", null, CallSecond)];
        AssertCallersDiffer(
            Patch(Image(types), "QQX"u8, [0xFF, 0xFE, (byte)'X']),
            Patch(Image(types), "QQX"u8, [0xFE, 0xFF, (byte)'X']),
            callerMethod: 3);
    }

    [Fact]
    public void CallToACompilerControlledMethod_FailsTheOperand()
    {
        // ECMA-335 lets PrivateScope methods share a name and signature; the
        // walk reads a caller of one as Indeterminate, and the cursor refuses
        // the operand rather than comparing the shared key.
        TypeSpec callee = new("N", "C", null, ReturnOne, ["M", "M"], PrivateScope: true);
        byte[] image = Image([callee, new("N", "D", null, CallFirst)]);
        Assert.Equal(FindingDiffAnyState.Indeterminate, Assert.Single(CompareImages(image, image).Types, type => type.FullName == "N.D").Body);

        using var pe = new PEReader(new MemoryStream(image, writable: false));
        IlCensusSide side = IlCensusSide.Create(pe.GetMetadataReader(), IlBodyDiffPolicy.Body);
        var inspection = IlFindings.Inspect(side, pe, MetadataTokens.MethodDefinitionHandle(3), Subject);
        var failed = Assert.IsType<FindingInspection<CanonicalIlOperation>.Failed>(inspection.Value);
        Assert.Contains("compiler-controlled", failed.Error.Reason, StringComparison.Ordinal);

        using var other = new PEReader(new MemoryStream(image, writable: false));
        IlBodyDiffResult diff = IlBodyDiff.Compare(side, pe.GetMethodBody(pe.GetMetadataReader().GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(3)).RelativeVirtualAddress), IlCensusSide.Create(other.GetMetadataReader(), IlBodyDiffPolicy.Body), other.GetMethodBody(other.GetMetadataReader().GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(3)).RelativeVirtualAddress));
        Assert.Equal(IlDiffFailureKind.TokenResolutionFailure, Assert.Single(diff.FailureRows).Kind);
    }

    [Fact]
    public void OperandKeys_AreFindingDiffAnysBytes()
    {
        using var pe = new PEReader(File.OpenRead(typeof(FindingDiffAny).Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        var walk = new FindingDiffAny.Side(pe);
        IlCensusSide cursor = IlCensusSide.Create(reader, IlBodyDiffPolicy.Body);
        var seen = new HashSet<int>();
        byte[] buffer = new byte[64 * 1024];
        int compared = 0;
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            int rva = reader.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0 || IlFindings.Inspect(cursor, pe, handle, Subject).Value is not FindingInspection<CanonicalIlOperation>.Complete complete)
                continue;
            // The atoms before the clauses are the instructions in order; the
            // token an atom was spelled from is its instruction's operand.
            var tokens = MethodInstructions.Decode(pe.GetMethodBody(rva)).Instructions
                .ToDictionary(instruction => instruction.Offset, instruction => (int)instruction.OperandValue);
            foreach (Finding<CanonicalIlOperation> atom in complete.Findings)
            {
                if (atom.Payload.IsExceptionClause
                    || atom.Payload.Operand is not { } operand
                    || !operand.TryGetKey(out StagedSymbolKey<IlOperandPart> key))
                {
                    continue;
                }
                int token = tokens[atom.Payload.Offset];
                if (!seen.Add(token))
                    continue;
                KeyRef walkKey;
                try
                {
                    walkKey = walk.ReferenceKey(MetadataTokens.EntityHandle(token));
                }
                catch (BadImageFormatException)
                {
                    // A compiler-controlled member or a duplicate Type row, which the walk refuses to name.
                    continue;
                }
                SymbolKey<FindingDiffAnyPart> expected = walkKey.IsFrozen ? walkKey.Frozen : walkKey.Detach();
                Span<byte> bytes = buffer.AsSpan(0, key.Length);
                key.CopyTo(bytes);
                Assert.True(expected.Range.SequenceEqual(bytes), $"row 0x{token:X8} spells differently: {operand.Value}");
                compared++;
            }
        }
        Assert.True(compared > 1_000, $"{compared} rows compared");
    }

    /// <summary>
    /// The caller's first atom is a call whose callee changed: the walk reads
    /// the caller's Body as changed, the cursor's atoms differ, their matcher
    /// keys differ, and their formatted spellings coincide, which is the
    /// collision the gate exists to catch.
    /// </summary>
    static void AssertCallersDiffer(byte[] beforeImage, byte[] afterImage, int callerMethod)
    {
        FindingDiffAnyResult walk = CompareImages(beforeImage, afterImage);
        Assert.Equal(FindingDiffAnyState.Changed, Assert.Single(walk.Types, type => type.FullName == "N.Caller").Body);

        using var beforePe = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var afterPe = new PEReader(new MemoryStream(afterImage, writable: false));
        IlCensusSide before = IlCensusSide.Create(beforePe.GetMetadataReader(), IlBodyDiffPolicy.Body);
        IlCensusSide after = IlCensusSide.Create(afterPe.GetMetadataReader(), IlBodyDiffPolicy.Body);
        MethodDefinitionHandle handle = MetadataTokens.MethodDefinitionHandle(callerMethod);
        CanonicalIlOperation x = FirstAtom(IlFindings.Inspect(before, beforePe, handle, Subject));
        CanonicalIlOperation y = FirstAtom(IlFindings.Inspect(after, afterPe, handle, Subject));

        Assert.Equal("call", x.OpcodeFamily);
        Assert.False(IlBodyDiff.AtomsEqual(x, y));
        Assert.NotEqual(IlFindings.GetIdentityKey(x), IlFindings.GetIdentityKey(y));
        Assert.Equal(x.Operand!.Value, y.Operand!.Value);
        Assert.False(IlFindings.Compare(before, beforePe, handle, after, afterPe, handle, Subject).IsExact);

        // The same callee spelled by two sides over one image is one atom.
        IlCensusSide again = IlCensusSide.Create(beforePe.GetMetadataReader(), IlBodyDiffPolicy.Body);
        Assert.True(IlBodyDiff.AtomsEqual(x, FirstAtom(IlFindings.Inspect(again, beforePe, handle, Subject))));
    }

    static CanonicalIlOperation FirstAtom(FindingInspection<CanonicalIlOperation> inspection)
        => Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(inspection.Value).Findings[0].Payload;
}
