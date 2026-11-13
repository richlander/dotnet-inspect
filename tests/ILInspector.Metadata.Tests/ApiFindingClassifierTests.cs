extern alias extensionnew;
extern alias extensionold;

using System.Reflection.PortableExecutable;
using Inspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Validates <see cref="ApiFindingClassifier"/> -- the Finding-pair-driven classifier that
/// produces every production <see cref="ApiDiff"/>, reusing <see cref="ApiDiffAnalyzer"/>'s
/// fact helpers over the Finding lane's type and member correspondence. Fuzzy/soft-key
/// member matches are, by default, an unrelated removal and addition, while
/// <see cref="ApiDiffNormalization.NormalizeMemberProjection"/> treats them as the same
/// member observed under two projections.
/// </summary>
public class ApiFindingClassifierTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Fact]
    public void ConstraintResolutionFailureDoesNotSuppressTypeAddition()
    {
        var oldSurface = Surface();
        oldSurface.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "resolve generic parameter constraints",
                0x01000001,
                MetadataTypeNameFailureMechanism.Metadata,
                "MalformedMetadata",
                "A dependency could not be opened.")
            {
                SourceAssemblyPath = "/inputs/Old.dll",
            });
        var newSurface = Surface(Type("Added"));
        var options = new ApiDiffOptions(ApiDiffScope.All);

        ApiDiff classified =
            ClassifyFor(
                oldSurface,
                newSurface,
                options);

        Assert.Contains(
            classified.TypeDiffs.SelectMany(
                static type => type.Changes),
            static change => change.Kind == ChangeKind.TypeAdded);
        Assert.Single(classified.InspectionFailures);
        Assert.Equal(
            "/inputs/Old.dll",
            Assert.Single(classified.InspectionFailures)
                .SourceAssemblyPath);
    }

    [Fact]
    public void TypeChanges_AreClassifiedFromTypeCorrespondence()
    {
        var oldSurface = Surface(
            Type("Widget", isByRefLike: false, members: [Method("Run", "void Run()")]),
            Type("Removed"));
        var newSurface = Surface(
            Type("Widget", isByRefLike: true, members: [Method("Run", "void Run()")]),
            Type("Added"));

        var options = new ApiDiffOptions(ApiDiffScope.All);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        // Only the whole-type addition and removal are classified; byref-like is not a
        // classified type facet, so the corresponding Widget has no row.
        Assert.Equal(
            [
                ("TestNamespace.Added", ChangeKind.TypeAdded, ChangeClassification.Additive),
                ("TestNamespace.Removed", ChangeKind.TypeRemoved, ChangeClassification.Breaking),
            ],
            classified.TypeDiffs
                .SelectMany(t => t.Changes.Select(c => (t.TypeFullName, c.Kind, c.Classification)))
                .Order());
        Assert.All(
            classified.TypeDiffs.SelectMany(t => t.Changes),
            c =>
            {
                Assert.Equal(ApiChangeCategory.Signature, c.Category);
                Assert.Equal(ApiChangeSubjectKind.Type, c.Subject?.Kind);
            });
    }

    [Fact]
    public void HardMatchedMemberChanges_AreClassifiedFromMemberCorrespondence()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Removed", "void Removed()"),
            Method("WasVirtual", "void WasVirtual()", isVirtual: true),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Added", "void Added()"),
            Method("WasVirtual", "void WasVirtual()", isVirtual: false),
        ]));

        var options = new ApiDiffOptions(ApiDiffScope.All);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        var widget = Assert.Single(classified.TypeDiffs);
        Assert.Equal("TestNamespace.Widget", widget.TypeFullName);
        Assert.Equal(
            [
                ("Added", ChangeKind.MemberAdded, ChangeClassification.Additive),
                ("Removed", ChangeKind.MemberRemoved, ChangeClassification.Breaking),
                ("WasVirtual", ChangeKind.VirtualRemoved, ChangeClassification.Breaking),
            ],
            widget.Changes
                .Select(c => (c.Subject!.MemberName!, c.Kind, c.Classification))
                .Order());
        Assert.All(widget.Changes, c => Assert.Equal(ApiChangeSubjectKind.Member, c.Subject?.Kind));
    }

    [Fact]
    public void FuzzyMatchedMember_StrictDefault_ReportsUnrelatedRemovalAndAddition()
    {
        var oldSurface = ExtractSurface(typeof(extensionold::ExtensionInstanceFixture.Widget).Assembly.Location);
        var newSurface = ExtractSurface(typeof(extensionnew::ExtensionInstanceFixture.Widget).Assembly.Location);

        var classified = ClassifyFor(oldSurface, newSurface, ApiDiffOptions.Default);

        // Strict default: whole-type removal for WidgetExtensions (member-level suppressed
        // because the type itself is gone), plus a plain MemberAdded for Widget.Measure.
        Assert.Contains(
            classified.TypeDiffs.SelectMany(t => t.Changes),
            c => c.Kind == ChangeKind.TypeRemoved
                && c.Subject!.TypeFullName == "ExtensionInstanceFixture.WidgetExtensions");
        var widgetDiff = classified.TypeDiffs.Single(t => t.TypeFullName == "ExtensionInstanceFixture.Widget");
        var measure = Assert.Single(widgetDiff.Changes, c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.Additive, measure.Classification);
        Assert.NotNull(measure.Subject?.NewIdentity);
        Assert.DoesNotContain(widgetDiff.Changes, c => c.Kind == ChangeKind.MemberRemoved);

        // No leftover member-level removal for the vanished extension method: it is fully
        // covered by the WidgetExtensions TypeRemoved change, not double-reported.
        Assert.DoesNotContain(
            classified.TypeDiffs.SelectMany(t => t.Changes),
            c => c.Kind == ChangeKind.MemberRemoved);
    }

    [Fact]
    public void FuzzyMatchedMember_Normalized_TreatsProjectionAsSameMember()
    {
        var oldSurface = ExtractSurface(typeof(extensionold::ExtensionInstanceFixture.Widget).Assembly.Location);
        var newSurface = ExtractSurface(typeof(extensionnew::ExtensionInstanceFixture.Widget).Assembly.Location);

        var options = new ApiDiffOptions(Normalization: ApiDiffNormalization.NormalizeMemberProjection);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        var widgetDiff = classified.TypeDiffs.SingleOrDefault(t => t.TypeFullName == "ExtensionInstanceFixture.Widget");

        // Normalized: no plain MemberAdded/MemberRemoved for the projected member. Whether
        // any change appears at all depends on whether the two projections' raw signature
        // text differs (the extension method includes the `this Widget` receiver parameter
        // in its signature, the instance method does not), so a MemberSignatureChanged is
        // the only change kind allowed to remain for this member.
        var memberChanges = (widgetDiff?.Changes ?? []).Where(c => c.Subject?.Kind == ApiChangeSubjectKind.Member);
        Assert.All(memberChanges, c => Assert.Equal(ChangeKind.MemberSignatureChanged, c.Kind));
    }

    [Fact]
    public void MemberAdded_ToInterface_IsPotentiallyBreakingNotAdditive()
    {
        var oldSurface = Surface(Type("IWidget", kind: "interface"));
        var newSurface = Surface(Type("IWidget", kind: "interface", members: [Method("Run", "void Run()")]));

        var options = new ApiDiffOptions(ApiDiffScope.All);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        // Adding a member to an interface is PotentiallyBreaking (existing implementers
        // must add it too), not Additive.
        var classifiedChange = Assert.Single(classified.TypeDiffs.SelectMany(t => t.Changes), c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, classifiedChange.Classification);
    }

    [Fact]
    public void MemberAdded_ToClass_RemainsAdditive()
    {
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", members: [Method("Run", "void Run()")]));

        var classified = ClassifyFor(oldSurface, newSurface, new ApiDiffOptions(ApiDiffScope.All));

        var classifiedChange = Assert.Single(classified.TypeDiffs.SelectMany(t => t.Changes), c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.Additive, classifiedChange.Classification);
    }

    [Fact]
    public void CompilerGeneratedMembers_DoNotSurfaceAsApiChanges()
    {
        // Backing field for an auto-property (matches MemberFilters.IsCompilerGenerated's
        // "__BackingField" heuristic) that only exists on the new side. The Finding lane's
        // raw member census intentionally does not filter it (see
        // MetadataFindingsTests.ApiSurfaceMembersAreNotFilteredByNameHeuristics), so
        // classification -- not the producer -- must apply MemberFilters.IsCompilerGenerated.
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", members: [Method("<Name>k__BackingField", "string <Name>k__BackingField")]));

        var classified = ClassifyFor(oldSurface, newSurface, new ApiDiffOptions(ApiDiffScope.All));

        Assert.True(classified.IsEmpty);
        Assert.DoesNotContain(classified.TypeDiffs.SelectMany(t => t.Changes), c => c.Kind == ChangeKind.MemberAdded);
    }

    static ApiDiff ClassifyFor(ApiSurface oldSurface, ApiSurface newSurface, ApiDiffOptions options)
    {
        var comparison = MetadataFindings.CompareApi(oldSurface, newSurface, Subject, options, memberAcceptanceThreshold: 85);
        return ApiFindingClassifier.Classify(comparison.Types, comparison.Members, oldSurface, newSurface, options);
    }

    static ApiSurface Surface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiSurface ExtractSurface(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader);
    }

    static ApiType Type(
        string name,
        string kind = "class",
        string? ns = "TestNamespace",
        bool isByRefLike = false,
        List<ApiMember>? members = null)
        => new()
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            IsByRefLike = isByRefLike,
            Members = members ?? [],
        };

    static ApiMember Method(string name, string signature, bool isVirtual = false)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            IsVirtual = isVirtual,
        };
}
