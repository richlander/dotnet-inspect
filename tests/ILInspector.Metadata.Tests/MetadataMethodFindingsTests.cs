using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMethodFindingsTests
{
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    [Fact]
    public void ScannerBindsClassifiedMethodsToCanonicalIdentity()
    {
        using var stream = File.OpenRead(typeof(MetadataMethodFindingsTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        var method = Assert.Single(
            MethodClassificationScanner.Scan(peReader),
            static method => method.MethodName == nameof(ClassifiedPointerMethod));

        Assert.NotNull(method.Anchor);
        Assert.Contains(
            $"{nameof(MetadataMethodFindingsTests)}.{nameof(ClassifiedPointerMethod)}",
            method.Anchor.CanonicalSignature,
            StringComparison.Ordinal);
        Assert.Contains("System.Int32*", method.Anchor.CanonicalSignature, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplaySignatureChangesDoNotChangeMethodObservations()
    {
        var anchor = CreateAnchor("M:Sample.Native.M(System.Int32)");
        ClassifiedMethodInfo oldMethod = new(
            "M",
            "Sample.Native",
            "Sample",
            "int M(int value)",
            MethodClassification.PInvoke,
            "native",
            anchor);
        ClassifiedMethodInfo newMethod = oldMethod with
        {
            Signature = "System.Int32 M(System.Int32 value)",
        };

        var pair = Assert.Single(Pairs(MetadataFindings.CompareClassifiedMethods(
            [oldMethod],
            [newMethod],
            Subject)));

        var present = Assert.IsType<PairFinding<ClassifiedMethodObservation>.Present>(pair.Value);
        Assert.DoesNotContain(
            "int M",
            present.New.Payload.Anchor.CanonicalSignature,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleChangesAreChangedButClassificationChangesAreExactTransitions()
    {
        var anchor = CreateAnchor("M:Sample.Native.M()");
        ClassifiedMethodInfo pinvoke = new(
            "M",
            "Sample.Native",
            "Sample",
            "void M()",
            MethodClassification.PInvoke,
            "old",
            anchor);

        var moduleChange = MetadataFindings.CompareClassifiedMethods(
            [pinvoke],
            [pinvoke with { ModuleName = "new" }],
            Subject);
        var classificationChange = MetadataFindings.CompareClassifiedMethods(
            [pinvoke],
            [pinvoke with { Classification = MethodClassification.Unsafe, ModuleName = null }],
            Subject);

        var changed = Assert.Single(Pairs(moduleChange));
        Assert.Equal(PairKind.Changed, changed.Kind);
        Assert.Null(changed.Detail);
        Assert.Equal(1, Pairs(classificationChange).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(classificationChange).Count(static pair => pair.Kind == PairKind.Removed));
    }

    [Fact]
    public void MissingStructuredIdentityFailsTheWholeCensus()
    {
        ClassifiedMethodInfo unbound = new(
            "M",
            "Sample.Native",
            "Sample",
            "void M()",
            MethodClassification.PInvoke);

        var inspection = MetadataFindings.InspectClassifiedMethods([unbound], Subject);
        var comparison = MetadataFindings.CompareClassifiedMethods([unbound], [], Subject);

        var failedInspection = Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(inspection.Value);
        Assert.Contains("structured member identity", failedInspection.Error.Reason, StringComparison.Ordinal);
        var failedComparison = Assert.IsType<FindingComparison<ClassifiedMethodObservation>.Failed>(comparison.Value);
        Assert.Contains("old:", failedComparison.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RealScannerOutputProjectsWithoutChangingTheCensus()
    {
        using var stream = File.OpenRead(typeof(MetadataMethodFindingsTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var methods = MethodClassificationScanner.Scan(peReader);

        var inspection = MetadataFindings.InspectClassifiedMethods(methods, Subject);

        Assert.Equal(methods.Count, Findings(inspection).Length);
        Assert.All(methods, static method => Assert.NotNull(method.Anchor));
        Assert.NotEmpty(methods);
    }

    public static unsafe int* ClassifiedPointerMethod(int* value) => value;

    static MemberAnchor CreateAnchor(string canonicalSignature)
    {
        string fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
        return new MemberAnchor(
            $"M~{fingerprint}",
            canonicalSignature,
            fingerprint,
            "Sample.Native",
            "M");
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
