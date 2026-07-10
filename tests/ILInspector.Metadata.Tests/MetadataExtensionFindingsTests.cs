using System.Collections.Immutable;
using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataExtensionFindingsTests
{
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    [Fact]
    public void ScannerBindsMethodsAndPropertiesToCanonicalIdentity()
    {
        using var stream = File.OpenRead(typeof(MetadataFindings).Assembly.Location);
        var members = ExtensionMethodScanner.FindAllExtensions(stream).ToList();

        var methods = members
            .Where(static member => member.MethodName == "GetFullTypeName")
            .ToList();
        var property = Assert.Single(
            members,
            static member => member.MethodName == "IsPublic" && member.Kind == "property");

        Assert.Equal(3, methods.Count);
        Assert.Equal(3, methods.Select(static method => method.Anchor).Distinct().Count());
        Assert.All(methods, static method =>
        {
            Assert.NotNull(method.Anchor);
            Assert.Equal("System.Reflection.Metadata.MetadataReader", method.CanonicalExtendedType);
            Assert.False(string.IsNullOrEmpty(method.ReturnType));
            Assert.StartsWith("M:", method.Anchor.CanonicalSignature, StringComparison.Ordinal);
        });
        Assert.Equal("System.Reflection.Metadata.TypeDefinition", property.CanonicalExtendedType);
        Assert.Equal("System.Boolean", property.ReturnType);
        Assert.StartsWith("P:", property.Anchor!.CanonicalSignature, StringComparison.Ordinal);
        Assert.Contains(
            "(System.Reflection.Metadata.TypeDefinition)",
            property.Anchor.CanonicalSignature,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SamePropertyNameOnDifferentReceiversProducesDistinctMembers()
    {
        using var stream = File.OpenRead(typeof(MetadataExtensionFindingsTests).Assembly.Location);
        var members = ExtensionMethodScanner.FindAllExtensions(stream).ToList();
        var properties = members
            .Where(static member =>
                member.ExtensionClass.EndsWith(
                    nameof(ExtensionPropertyIdentityFixture),
                    StringComparison.Ordinal)
                && member.MethodName == "HasValue")
            .ToList();

        Assert.Equal(2, properties.Count);
        Assert.Equal(2, properties.Select(static property => property.Anchor).Distinct().Count());
        Assert.Equal(
            ["System.Int32", "System.String"],
            properties
                .Select(static property => property.CanonicalExtendedType!)
                .Order(StringComparer.Ordinal)
                .ToArray());

        var capacity = Assert.Single(
            members,
            static member =>
                member.ExtensionClass.EndsWith(
                    nameof(ExtensionPropertyIdentityFixture),
                    StringComparison.Ordinal)
                && member.MethodName == "Capacity");
        Assert.Equal("System.Text.StringBuilder", capacity.CanonicalExtendedType);
        Assert.Equal("System.Int32", capacity.ReturnType);
        Assert.DoesNotContain(
            members,
            static member => member.MethodName == nameof(ExtensionPropertyIdentityFixture.Ordinary));
        Assert.DoesNotContain(
            members,
            static member => member.MethodName == "Standalone");
        Assert.DoesNotContain(
            members,
            static member => member.MethodName == "Hidden");

        using var allStream = File.OpenRead(typeof(MetadataExtensionFindingsTests).Assembly.Location);
        Assert.Contains(
            ExtensionMethodScanner.FindAllExtensions(allStream, includeAll: true),
            static member => member.MethodName == "Hidden");
    }

    [Fact]
    public void DisplayChangesDoNotChangeExtensionObservations()
    {
        var member = CreateMember("M:Sample.Extensions.M(System.String)");
        var comparison = MetadataFindings.CompareExtensionMembers(
            [member],
            [member with { Signature = "different display text" }],
            Subject);

        var pair = Assert.Single(Pairs(comparison));
        var present = Assert.IsType<PairFinding<ExtensionMemberObservation>.Present>(pair.Value);
        Assert.DoesNotContain(
            "different display text",
            present.New.Payload.Anchor.CanonicalSignature,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadChangesAreChangedButReceiverChangesAreExactTransitions()
    {
        var member = CreateMember("M:Sample.Extensions.M(System.String)");
        var returnChange = MetadataFindings.CompareExtensionMembers(
            [member],
            [member with { ReturnType = "System.Int64" }],
            Subject);
        var receiverChange = MetadataFindings.CompareExtensionMembers(
            [member],
            [CreateMember(
                "M:Sample.Extensions.M(System.Object)",
                canonicalExtendedType: "System.Object")],
            Subject);

        Assert.Equal(PairKind.Changed, Assert.Single(Pairs(returnChange)).Kind);
        Assert.Equal(1, Pairs(receiverChange).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(receiverChange).Count(static pair => pair.Kind == PairKind.Removed));
    }

    [Fact]
    public void MissingShapeOrUnknownKindFailsTheWholeCensus()
    {
        ExtensionMethodInfo unbound = new(
            "M",
            "Sample.Extensions",
            "Sample",
            "string",
            "void M(this string value)");
        var unknown = CreateMember("M:Sample.Extensions.M(System.String)") with
        {
            Kind = "event",
        };

        var missingShape = MetadataFindings.InspectExtensionMembers([unbound], Subject);
        var unknownKind = MetadataFindings.CompareExtensionMembers([unknown], [], Subject);

        var failedInspection =
            Assert.IsType<FindingInspection<ExtensionMemberObservation>.Failed>(missingShape.Value);
        Assert.Contains("structured member shape", failedInspection.Error.Reason, StringComparison.Ordinal);
        var failedComparison =
            Assert.IsType<FindingComparison<ExtensionMemberObservation>.Failed>(unknownKind.Value);
        Assert.Contains("unsupported member kind", failedComparison.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRecordEqualityAndDeconstructionIgnoreAdditiveShape()
    {
        ExtensionMethodInfo enriched = CreateMember("M:Sample.Extensions.M(System.String)");
        ExtensionMethodInfo legacy = new(
            "M",
            "Sample.Extensions",
            "Sample",
            "string",
            "void M(this string value)",
            "Sample",
            "method");

        var (name, extensionClass, @namespace, extendedType, signature, assembly, kind) = enriched;

        Assert.Equal(legacy, enriched);
        Assert.Equal(legacy.GetHashCode(), enriched.GetHashCode());
        Assert.Equal("M", name);
        Assert.Equal("Sample.Extensions", extensionClass);
        Assert.Equal("Sample", @namespace);
        Assert.Equal("string", extendedType);
        Assert.Equal("void M(this string value)", signature);
        Assert.Equal("Sample", assembly);
        Assert.Equal("method", kind);
    }

    [Fact]
    public void RealScannerOutputProjectsWithoutChangingTheCensus()
    {
        using var stream = File.OpenRead(typeof(MetadataFindings).Assembly.Location);
        var members = ExtensionMethodScanner.FindAllExtensions(stream).ToList();

        var inspection = MetadataFindings.InspectExtensionMembers(members, Subject);

        Assert.Equal(members.Count, Findings(inspection).Length);
        Assert.All(members, static member =>
        {
            Assert.NotNull(member.Anchor);
            Assert.False(string.IsNullOrEmpty(member.CanonicalExtendedType));
            Assert.False(string.IsNullOrEmpty(member.ReturnType));
        });
        Assert.NotEmpty(members);
    }

    static ExtensionMethodInfo CreateMember(
        string canonicalSignature,
        string canonicalExtendedType = "System.String")
    {
        string fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
        var anchor = new MemberAnchor(
            $"extension:M~{fingerprint}",
            canonicalSignature,
            fingerprint,
            "Sample.Extensions",
            "M");
        return new ExtensionMethodInfo(
            "M",
            "Sample.Extensions",
            "Sample",
            "string",
            "void M(this string value)",
            "Sample",
            "method")
        {
            Anchor = anchor,
            ReturnType = "System.Void",
            CanonicalExtendedType = canonicalExtendedType,
        };
    }

    static ImmutableArray<Finding<T>> Findings<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new Xunit.Sdk.XunitException($"Expected complete inspection: {inspection}");

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected complete comparison: {comparison.Failure}");
}

public static class ExtensionPropertyIdentityFixture
{
    public static int Ordinary { get; set; }
    public static void set_Standalone(int value) { }

    extension(string value)
    {
        public bool HasValue => value.Length > 0;

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public bool Hidden => false;
    }

    extension(int value)
    {
        public bool HasValue => value != 0;
    }

    extension(System.Text.StringBuilder builder)
    {
        public int Capacity
        {
            get => builder.Capacity;
            set => builder.Capacity = value;
        }
    }
}
