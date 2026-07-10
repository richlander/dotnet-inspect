using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataSemanticFindingsTests
{
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    [Fact]
    public void UnionTypes_IgnoreCaseOrderAndPromoteShapeChanges()
    {
        UnionTypeInfo oldUnion = new(
            "Sample.Result",
            "struct",
            ImplementsIUnion: true,
            ["Sample.Success", "Sample.Error"]);
        UnionTypeInfo reordered = oldUnion with
        {
            CaseTypes = ["Sample.Error", "Sample.Success"],
        };
        UnionTypeInfo expanded = oldUnion with
        {
            CaseTypes = ["Sample.Success", "Sample.Error", "Sample.Pending"],
        };
        UnionTypeInfo reclassified = oldUnion with
        {
            Kind = "class",
            ImplementsIUnion = false,
        };

        var reorderedComparison = MetadataFindings.CompareUnionTypes(
            [oldUnion],
            [reordered],
            Subject);
        var expandedComparison = MetadataFindings.CompareUnionTypes(
            [oldUnion],
            [expanded],
            Subject);
        var reclassifiedComparison = MetadataFindings.CompareUnionTypes(
            [oldUnion],
            [reclassified],
            Subject);

        Assert.Equal(PairKind.Present, Assert.Single(Pairs(reorderedComparison)).Kind);
        var expandedPair = Assert.Single(Pairs(expandedComparison));
        Assert.Equal(PairKind.Changed, expandedPair.Kind);
        Assert.Null(expandedPair.Detail);
        Assert.Equal(PairKind.Changed, Assert.Single(Pairs(reclassifiedComparison)).Kind);
    }

    [Fact]
    public void SemanticSignals_AreExactUnorderedOccurrences()
    {
        EcosystemIntegrationSignalInfo dependencyInjection = new(
            "Dependency Injection",
            "Builder",
            "Sample.ServiceCollectionExtensions.AddSample(...)",
            IntegrationSignalShape.Api);
        EcosystemIntegrationSignalInfo logging = new(
            "Logging",
            "Provider",
            "Sample.SampleLoggerProvider");

        var ecosystemComparison = MetadataFindings.CompareEcosystemIntegrations(
            [dependencyInjection],
            [logging, dependencyInjection],
            Subject);
        var telemetryComparison = MetadataFindings.CompareOpenTelemetrySignals(
            [new OpenTelemetrySignalInfo("Tracing", "Sample.ActivitySource")],
            [new OpenTelemetrySignalInfo("Metrics", "Sample.Meter")],
            Subject);
        var switchComparison = MetadataFindings.CompareSwitches(
            [new SwitchInfo("AppContext", "Sample.Switch", "Sample.Old(...)")],
            [new SwitchInfo("AppContext", "Sample.Switch", "Sample.New(...)")],
            Subject);

        Assert.Equal(1, Pairs(ecosystemComparison).Count(static pair => pair.Kind == PairKind.Present));
        Assert.Equal(1, Pairs(ecosystemComparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(telemetryComparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(telemetryComparison).Count(static pair => pair.Kind == PairKind.Removed));
        Assert.Equal(1, Pairs(switchComparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(switchComparison).Count(static pair => pair.Kind == PairKind.Removed));
        Assert.DoesNotContain(Pairs(ecosystemComparison), static pair => pair.Kind == PairKind.Changed);
        Assert.DoesNotContain(Pairs(telemetryComparison), static pair => pair.Kind == PairKind.Changed);
        Assert.DoesNotContain(Pairs(switchComparison), static pair => pair.Kind == PairKind.Changed);
    }

    [Fact]
    public void SignalIdentities_AreCollisionSafe()
    {
        EcosystemIntegrationSignalInfo first = new("A", "B:C", "D");
        EcosystemIntegrationSignalInfo second = new("A:B", "C", "D");

        var comparison = MetadataFindings.CompareEcosystemIntegrations(
            [first],
            [second],
            Subject);

        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Removed));
    }

    [Fact]
    public void SemanticProducers_ReportPublicCollectionParameters()
    {
        UnionTypeInfo valid = new("Sample.Result", "struct", true, []);
        UnionTypeInfo[] containingNull = [null!];

        var inspectException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.InspectUnionTypes(containingNull, Subject));
        var oldException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.CompareUnionTypes(containingNull, [valid], Subject));
        var newException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.CompareUnionTypes([valid], containingNull, Subject));

        Assert.Equal("unions", inspectException.ParamName);
        Assert.Equal("oldUnions", oldException.ParamName);
        Assert.Equal("newUnions", newException.ParamName);
    }

    [Fact]
    public void RealScannerOutputs_ProjectWithoutChangingTheCensus()
    {
        using var stream = File.OpenRead(typeof(MetadataUnionFixture).Assembly.Location);
        using var peReader = new PEReader(stream);

        var unions = UnionTypeScanner.Scan(peReader);
        var ecosystem = EcosystemIntegrationScanner.Scan(peReader);
        var telemetry = OpenTelemetryScanner.Scan(peReader);
        var switches = SwitchScanner.Scan(peReader);

        Assert.Equal(
            unions.Count,
            Findings(MetadataFindings.InspectUnionTypes(unions, Subject)).Length);
        Assert.Equal(
            ecosystem.Count,
            Findings(MetadataFindings.InspectEcosystemIntegrations(ecosystem, Subject)).Length);
        Assert.Equal(
            telemetry.Count,
            Findings(MetadataFindings.InspectOpenTelemetrySignals(telemetry, Subject)).Length);
        Assert.Equal(
            switches.Count,
            Findings(MetadataFindings.InspectSwitches(switches, Subject)).Length);
        Assert.NotEmpty(unions);
    }

    static ImmutableArray<Finding<T>> Findings<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new Xunit.Sdk.XunitException("Expected a complete semantic inspection.");

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected a complete comparison: {comparison.Failure}");
}
