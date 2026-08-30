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

    [Fact]
    public void GeneratedBlobs_GuardStopsExactlyWhereTheBlobEnds()
    {
        var divergences = new List<string>();

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            CustomAttribute attribute = generated.Attribute;

            bool safe = CustomAttributeValueGuard.IsSafeToDecode(
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

            bool safe = CustomAttributeValueGuard.IsSafeToDecode(
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
        bool array = false;
        bool boxed = false;
        bool nullArray = false;
        bool emptyArray = false;
        bool objectArray = false;
        bool systemType = false;
        bool enumHandleSpelled = false;
        bool int32Enum = false;
        bool int64Enum = false;

        var emitted = new HashSet<byte>();
        var primitives = new HashSet<PrimitiveTypeCode>();
        var stringForms = new HashSet<CustomAttributeDifferentialOracle.SerStringForm>();

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            emitted.UnionWith(generated.InlineElementTypes);
            primitives.UnionWith(generated.Primitives);
            stringForms.UnionWith(generated.StringForms);
            foreach (var shape in generated.Shapes)
                Visit(shape, boxedContext: false, generated.EnumUnderlying);
        }

        Assert.True(array, "no SZARRAY was generated");
        Assert.True(boxed, "no boxed argument was generated");
        Assert.True(nullArray, "no null array was generated");
        Assert.True(emptyArray, "no zero-length array was generated");
        Assert.True(objectArray, "no object[] was generated");
        Assert.True(systemType, "no System.Type argument was generated");
        Assert.True(enumHandleSpelled, "no handle-spelled enum was generated");
        Assert.True(int32Enum, "no enum over an Int32 underlying type was generated");
        Assert.True(int64Enum, "no enum over an Int64 underlying type was generated");

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

        void Visit(
            CustomAttributeDifferentialOracle.Shape shape,
            bool boxedContext,
            PrimitiveTypeCode underlying)
        {
            switch (shape)
            {
                case CustomAttributeDifferentialOracle.ArrayShape a:
                    array = true;
                    nullArray |= a.Count < 0;
                    emptyArray |= a.Count == 0;
                    objectArray |= a.Element is CustomAttributeDifferentialOracle.BoxedShape;
                    // An array element inherits its parent's encoding context.
                    Visit(a.Element, boxedContext, underlying);
                    break;
                case CustomAttributeDifferentialOracle.BoxedShape b:
                    boxed = true;
                    Visit(b.Inner, boxedContext: true, underlying);
                    break;
                case CustomAttributeDifferentialOracle.EnumHandleShape:
                    // Recorded only where an enum actually occurs, so an image
                    // that happens to be configured Int64 but contains no enum
                    // cannot satisfy the width assertions. The serialized-name
                    // spelling is asserted from the emitted bytes instead.
                    if (!boxedContext)
                        enumHandleSpelled = true;
                    int32Enum |= underlying == PrimitiveTypeCode.Int32;
                    int64Enum |= underlying == PrimitiveTypeCode.Int64;
                    break;
                case CustomAttributeDifferentialOracle.SystemTypeShape:
                    systemType = true;
                    break;

            }
        }
    }
}
