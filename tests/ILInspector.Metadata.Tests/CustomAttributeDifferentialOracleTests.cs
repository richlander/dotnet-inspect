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

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            CustomAttribute attribute = generated.Attribute;

            if (!CustomAttributeValueGuard.IsSafeToDecode(generated.Reader, attribute))
                continue;

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
    [Fact]
    public void Generator_ReachesArraysBoxedValuesAndBothEnumSpellings()
    {
        bool array = false;
        bool boxed = false;
        bool enumHandle = false;
        bool enumName = false;
        bool nullArray = false;
        bool nullString = false;
        bool int64Enum = false;

        for (int seed = 0; seed < SeedCount; seed++)
        {
            using var generated = CustomAttributeDifferentialOracle.Generate(seed);
            int64Enum |= generated.EnumUnderlying == PrimitiveTypeCode.Int64;
            foreach (var shape in generated.Shapes)
                Visit(shape);
        }

        Assert.True(array, "no SZARRAY was generated");
        Assert.True(boxed, "no boxed argument was generated");
        Assert.True(enumHandle, "no handle-spelled enum was generated");
        Assert.True(enumName, "no serialized-name enum was generated");
        Assert.True(nullArray, "no null array was generated");
        Assert.True(nullString, "no null string was generated");
        Assert.True(int64Enum, "no Int64-underlying enum was generated");

        void Visit(CustomAttributeDifferentialOracle.Shape shape)
        {
            switch (shape)
            {
                case CustomAttributeDifferentialOracle.ArrayShape a:
                    array = true;
                    nullArray |= a.Count < 0;
                    Visit(a.Element);
                    break;
                case CustomAttributeDifferentialOracle.BoxedShape b:
                    boxed = true;
                    // A boxed enum is the serialized-name spelling: the value
                    // blob carries 0x55 and the type name, with no handle.
                    enumName |= b.Inner is CustomAttributeDifferentialOracle.EnumHandleShape;
                    Visit(b.Inner);
                    break;
                case CustomAttributeDifferentialOracle.EnumHandleShape:
                    enumHandle = true;
                    break;
                case CustomAttributeDifferentialOracle.StringShape s:
                    nullString |= s.Value is null;
                    break;
            }
        }
    }
}


