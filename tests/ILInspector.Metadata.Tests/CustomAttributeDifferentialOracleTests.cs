using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Compares <see cref="CustomAttributeValueGuard"/> against SRM's decoder over
/// generated blobs, rather than over shapes somebody thought of.
/// </summary>
/// <remarks>
/// The guard's whole contract is agreement with an independent implementation,
/// and every defect found in it so far was a divergence discovered by hand.
/// These cases gate invariant I1 — the guard skips exactly the bytes SRM
/// consumes — in <em>both</em> directions, which no SRM-side observation can
/// do alone. See <c>docs/design/custom-attribute-value-decoding.md</c>.
/// </remarks>
public sealed class CustomAttributeDifferentialOracleTests
{
    /// <summary>
    /// Enough seeds to cross the grammar's shape combinations without making
    /// the suite slow; each is reproducible from its seed alone.
    /// </summary>
    const int SeedCount = 600;

    /// <summary>
    /// Invokes the guard the way the product's <c>AttributeDecoder.TryDecode</c>
    /// does: with the enum-width resolver taken from the very
    /// <c>ArgTypeProvider</c> instance SRM is then handed, so the two walkers
    /// share one width decision rather than re-deriving it.
    /// </summary>
    /// <remarks>
    /// I1 is a claim about <em>that</em> configuration. The resolver-less
    /// overload reaches enum widths by a different route
    /// (<c>FromSerializedName</c>/<c>FromHandle</c>) that the design document
    /// places outside I1, so asserting the boundary against it would gate a
    /// path the product never runs.
    /// </remarks>
    static bool GuardAsProductInvokesIt(
        MetadataReader reader,
        CustomAttribute attribute,
        out CustomAttributeValueGuard.Boundary boundary)
    {
        var provider = new AttributeDecoder.ArgTypeProvider(
            reader,
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);

        return CustomAttributeValueGuard.IsSafeToDecode(
            reader,
            attribute,
            out boundary,
            beforeMaterialize: null,
            provider.GetUnderlyingEnumType);
    }

    [Fact]
    public void GeneratedBlobs_GuardStopsExactlyWhereTheBlobEnds()
    {
        var divergences = new List<string>();

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            CustomAttribute attribute = generated.Attribute;

            bool safe = GuardAsProductInvokesIt(
                generated.Reader,
                attribute,
                out var boundary);

            if (!safe)
            {
                divergences.Add($"guard refused a well-formed blob: {generated.Describe()}");
                continue;
            }

            if (!boundary.Known)
            {
                divergences.Add($"guard reported no boundary: {generated.Describe()}");
                continue;
            }

            if (boundary.ValueOffset != generated.ValueLength)
            {
                divergences.Add(
                    $"guard stopped at {boundary.ValueOffset}, blob ends at " +
                    $"{generated.ValueLength}: {generated.Describe()}");
            }
        }

        Assert.Empty(divergences);
    }

    [Fact]
    public void GeneratedBlobs_SrmDecodesEveryBlobTheGuardApproves()
    {
        var divergences = new List<string>();
        int approved = 0;

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            CustomAttribute attribute = generated.Attribute;

            if (!CustomAttributeValueGuard.IsSafeToDecode(generated.Reader, attribute))
                continue;

            approved++;

            var decoded = AttributeDecoder.TryDecode(generated.Reader, attribute);
            if (decoded is null)
            {
                divergences.Add($"SRM refused an approved blob: {generated.Describe()}");
                continue;
            }

            if (decoded.Value.FixedArguments.Length != generated.Shapes.Count)
            {
                divergences.Add(
                    $"SRM decoded {decoded.Value.FixedArguments.Length} arguments, generator " +
                    $"emitted {generated.Shapes.Count}: {generated.Describe()}");
            }
        }

        Assert.Empty(divergences);

        // Without this the loop body is skippable: a guard that refused
        // everything would leave `divergences` empty and pass vacuously when
        // this test is run in isolation. Every generated blob is well-formed,
        // so the guard must approve all of them.
        Assert.Equal(SeedCount, approved);
    }

    /// <summary>
    /// Non-vacuity: the boundary must track consumption, not simply report the
    /// blob length. Appending unreachable bytes must move the blob's end
    /// without moving where the walk stops.
    /// </summary>
    [Fact]
    public void TrailingBytes_AreNotConsumed_SoTheBoundaryIsRealNotTheBlobLength()
    {
        var failures = new List<string>();

        for (int seed = 0; seed < 60; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(
                seed,
                trailingGarbageBytes: 7);
            CustomAttribute attribute = generated.Attribute;

            bool safe = GuardAsProductInvokesIt(
                generated.Reader,
                attribute,
                out var boundary);

            if (!safe || !boundary.Known)
            {
                failures.Add($"guard refused or gave no boundary: {generated.Describe()}");
                continue;
            }

            if (boundary.ValueOffset != generated.ValueLength)
            {
                failures.Add(
                    $"guard stopped at {boundary.ValueOffset}, logical end is " +
                    $"{generated.ValueLength}: {generated.Describe()}");
                continue;
            }

            if (boundary.ValueLength <= boundary.ValueOffset)
            {
                failures.Add(
                    "trailing bytes did not extend the blob, so this case proves " +
                    $"nothing: {generated.Describe()}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// The generator must actually reach the shapes the guard's hard cases live
    /// in. Without this, the corpus above could pass by emitting only scalars.
    /// </summary>
    /// <remarks>
    /// Structural facts are read off the shape tree, but the inline
    /// element-type spellings are read off <em>what the writer emitted</em>.
    /// Those two are not the same question: whether a shape is spelled inline
    /// depends on its position, so a <c>System.Type</c> argument spells
    /// <c>0x50</c> only inside a boxed value, and <c>0x51</c> appears only where
    /// an <c>object[]</c> is itself nested in one. Inferring either from the
    /// shape alone would let a shape that never produced the byte satisfy the
    /// assertion for it.
    /// </remarks>
    [Fact]
    public void Generator_ReachesEveryShapeTheCorpusClaimsToCover()
    {
        var emitted = new HashSet<byte>();
        var primitives = new HashSet<PrimitiveTypeCode>();
        var stringForms = new HashSet<CustomAttributeDifferentialOracle.SerStringForm>();
        var arrayCounts = new HashSet<int>();
        var enumWidths = new HashSet<PrimitiveTypeCode>();
        var facts = new HashSet<CustomAttributeDifferentialOracle.EmittedFact>();

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            emitted.UnionWith(generated.InlineElementTypes);
            primitives.UnionWith(generated.Primitives);
            stringForms.UnionWith(generated.StringForms);
            arrayCounts.UnionWith(generated.ArrayCounts);
            enumWidths.UnionWith(generated.EnumWidths);
            facts.UnionWith(generated.Facts);
        }

        // Structural coverage, read from the writer rather than from the shape
        // tree. A shape present in the tree is not a shape written to a blob:
        // the elements of a zero-length array are never emitted, so a corpus
        // whose every `object[]` was empty would otherwise still claim to have
        // covered `object[]`.
        Assert.Equal(
            Enum.GetValues<CustomAttributeDifferentialOracle.EmittedFact>().ToHashSet(),
            facts);

        // Emitted bytes, not inferred ones.
        Assert.Contains((byte)0x50, emitted);   // System.Type, spelled inline
        Assert.Contains((byte)0x51, emitted);   // object as a nested element type
        Assert.Contains((byte)0x55, emitted);   // enum, spelled by serialized name
        Assert.Contains((byte)0x1d, emitted);   // SZARRAY, spelled inline
        Assert.Contains((byte)0x0e, emitted);   // string, spelled inline

        // Every non-string primitive ECMA-335 II.23.3 admits as a fixed argument
        // must actually have been written. The expectation is the grammar, not
        // the generator's own array: anchoring it to that array would let the
        // array be collapsed to a single primitive with the gate still green,
        // because the expectation would collapse along with it.
        Assert.Equal(
            new HashSet<PrimitiveTypeCode>
            {
                PrimitiveTypeCode.Boolean,
                PrimitiveTypeCode.Char,
                PrimitiveTypeCode.SByte,
                PrimitiveTypeCode.Byte,
                PrimitiveTypeCode.Int16,
                PrimitiveTypeCode.UInt16,
                PrimitiveTypeCode.Int32,
                PrimitiveTypeCode.UInt32,
                PrimitiveTypeCode.Int64,
                PrimitiveTypeCode.UInt64,
                PrimitiveTypeCode.Single,
                PrimitiveTypeCode.Double,
            },
            primitives);

        // All four SerString forms, read from what was written rather than from
        // the shape that asked for it.
        Assert.Equal(
            Enum.GetValues<CustomAttributeDifferentialOracle.SerStringForm>().ToHashSet(),
            stringForms);

        // The three distinct SZARRAY encodings, read from the counts actually
        // written rather than from the shape tree that requested them.
        Assert.Contains(arrayCounts, count => count < 0);  // null array
        Assert.Contains(0, arrayCounts);                   // zero-length array
        Assert.Contains(arrayCounts, count => count > 0);  // populated array

        // Enum widths, read from the values actually written. Reading these
        // from the image's configuration would credit a width to an image whose
        // blob carries no enum argument at all.
        Assert.Contains(PrimitiveTypeCode.Int32, enumWidths);
        Assert.Contains(PrimitiveTypeCode.Int64, enumWidths);
    }
}
