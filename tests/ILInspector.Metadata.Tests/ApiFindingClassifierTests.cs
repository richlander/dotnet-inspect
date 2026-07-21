extern alias extensionnew;
extern alias extensionold;

using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Validates <see cref="ApiFindingClassifier"/> -- the Finding-pair-driven classifier
/// intended to replace <see cref="ApiDiffAnalyzer"/>'s own type/member matching walk
/// (issue #2893 follow-up). Type-level classification must stay byte-identical to the
/// legacy analyzer. Member-level classification intentionally diverges for fuzzy/soft-key
/// matches: strict (default) reports them as an unrelated removal+addition just like the
/// legacy analyzer would, while <see cref="ApiDiffNormalization.NormalizeMemberProjection"/>
/// treats them as the same member observed under two projections.
/// </summary>
public class ApiFindingClassifierTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Fact]
    public void TypeFacetChanges_MatchLegacyAnalyzerExactly()
    {
        var oldSurface = Surface(
            Type("Widget", isByRefLike: false, members: [Method("Run", "void Run()")]),
            Type("Removed"));
        var newSurface = Surface(
            Type("Widget", isByRefLike: true, members: [Method("Run", "void Run()")]),
            Type("Added"));

        var options = new ApiDiffOptions(ApiDiffScope.All);
        var legacy = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        // Type-level changes must match exactly: same type names, same change kinds.
        Assert.Equal(
            legacy.TypeDiffs.Select(t => t.TypeFullName).Order(StringComparer.Ordinal),
            classified.TypeDiffs.Select(t => t.TypeFullName).Order(StringComparer.Ordinal));

        foreach (var legacyTypeDiff in legacy.TypeDiffs)
        {
            var classifiedTypeDiff = classified.TypeDiffs.Single(t => t.TypeFullName == legacyTypeDiff.TypeFullName);
            var legacyTypeKinds = legacyTypeDiff.Changes
                .Where(c => c.Category == ApiChangeCategory.Signature && c.Subject?.Kind == ApiChangeSubjectKind.Type)
                .Select(c => c.Kind)
                .Order();
            var classifiedTypeKinds = classifiedTypeDiff.Changes
                .Where(c => c.Category == ApiChangeCategory.Signature && c.Subject?.Kind == ApiChangeSubjectKind.Type)
                .Select(c => c.Kind)
                .Order();
            Assert.Equal(legacyTypeKinds, classifiedTypeKinds);
        }
    }

    [Fact]
    public void HardMatchedMemberChanges_MatchLegacyAnalyzerExactly()
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
        var legacy = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        var classified = ClassifyFor(oldSurface, newSurface, options);

        var legacyKinds = legacy.TypeDiffs.SelectMany(t => t.Changes)
            .Where(c => c.Subject?.Kind == ApiChangeSubjectKind.Member)
            .Select(c => c.Kind)
            .Order();
        var classifiedKinds = classified.TypeDiffs.SelectMany(t => t.Changes)
            .Where(c => c.Subject?.Kind == ApiChangeSubjectKind.Member)
            .Select(c => c.Kind)
            .Order();

        Assert.Equal(legacyKinds, classifiedKinds);
        Assert.Contains(ChangeKind.MemberAdded, classifiedKinds);
        Assert.Contains(ChangeKind.MemberRemoved, classifiedKinds);
        Assert.Contains(ChangeKind.VirtualRemoved, classifiedKinds);
    }

    [Fact]
    public void FuzzyMatchedMember_StrictDefault_ReportsUnrelatedRemovalAndAddition()
    {
        var oldSurface = ExtractSurface(typeof(extensionold::ExtensionInstanceFixture.Widget).Assembly.Location);
        var newSurface = ExtractSurface(typeof(extensionnew::ExtensionInstanceFixture.Widget).Assembly.Location);

        // Legacy analyzer's own per-type walk never recognizes the extension method's static
        // -> instance projection as the same member either: WidgetExtensions disappears as a
        // whole type (TypeRemoved), and Widget's new instance method is a plain MemberAdded.
        var legacy = ApiDiffAnalyzer.Compare(oldSurface, newSurface);
        var classified = ClassifyFor(oldSurface, newSurface, ApiDiffOptions.Default);

        Assert.Contains(
            legacy.TypeDiffs.SelectMany(t => t.Changes),
            c => c.Kind == ChangeKind.TypeRemoved
                && c.Subject!.TypeFullName == "ExtensionInstanceFixture.WidgetExtensions");
        Assert.Contains(
            legacy.TypeDiffs.SelectMany(t => t.Changes),
            c => c.Kind == ChangeKind.MemberAdded && c.Subject!.NewIdentity is not null);

        // Classifier: whole-type removal for WidgetExtensions (member-level suppressed
        // because the type itself is gone), plus a plain MemberAdded for Widget.Measure --
        // matching legacy's total shape for this scenario.
        Assert.Contains(
            classified.TypeDiffs.SelectMany(t => t.Changes),
            c => c.Kind == ChangeKind.TypeRemoved
                && c.Subject!.TypeFullName == "ExtensionInstanceFixture.WidgetExtensions");
        var widgetDiff = classified.TypeDiffs.Single(t => t.TypeFullName == "ExtensionInstanceFixture.Widget");
        Assert.Single(widgetDiff.Changes, c => c.Kind == ChangeKind.MemberAdded);
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
