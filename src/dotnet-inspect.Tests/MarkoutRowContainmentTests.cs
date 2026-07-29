using System.Collections;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// Asserts that every serializable row and section the product declares
/// contains the untrusted text it is handed, whether or not any walk can drive
/// the product into producing it (issue #3319).
/// </summary>
/// <remarks>
/// <see cref="LibraryViewShapeDerivedContainmentTests"/> reaches these types the
/// way the product does: it builds a <c>LibraryInspection</c>, fills it, and
/// renders the view. That is the right shape for proving the *rendering* path,
/// and it is why the raw columns it found were real. But its reach is bounded
/// by what a harness can make the model produce, and roughly two dozen
/// projections hang off <c>[Union]</c> <c>FindingInspection&lt;T&gt;</c> values
/// that only the product's own analyzers can populate. Those are declared in
/// its <c>OutOfReach</c> set.
///
/// A reviewer then pointed out what "declared" was quietly costing:
/// <c>IntegrationSignalRow</c> and <c>IntegrationApiSignalRow</c> hang off two
/// of those projections, so deleting <c>LibraryViewText.Contain</c> from their
/// columns left the whole suite green. The columns were contained, and nothing
/// would have noticed if they stopped being.
///
/// The fix is not to reach further -- reconstructing a <c>FindingInspection</c>
/// in the harness would cross the boundary in <c>AGENTS.md</c> that says a test
/// must exercise the product's capability rather than re-implement it. It is to
/// stop asking about reach at all. Containment here is a property of the row
/// type, spelled the same way in every one of them:
///
/// <code>public string Kind { get; init; } = LibraryViewText.Contain(Kind);</code>
///
/// So this walks the *declared types* instead of a reachable object graph. A row
/// no analyzer can currently produce is still a row, and it is checked. That
/// also means a new row type is covered the day it is declared, with no edit
/// here -- the failure mode this issue has hit in every one of nineteen rounds
/// is a rule whose scope is narrower than the set of things that can break it.
///
/// This asserts the row's own contract, not the rendered document; the rendering
/// path is <see cref="LibraryViewShapeDerivedContainmentTests"/>'s job. Both are
/// needed: a contained row rendered through a raw formatter still leaks, and a
/// raw row is a leak wherever it is rendered.
///
/// <para><b>What the pinned set does and does not say.</b> Row-level
/// containment is one of two idioms in this assembly. The other contains at the
/// <i>producer</i>: <c>ResourceTriageRow</c> is built from
/// <c>MarkoutInline.Code(row.Member)</c>, which routes through
/// <c>CSharpIdentifier.ContainRenderedText</c> before the row ever sees the
/// text. Constructing that row directly, as this walk does, bypasses the
/// containment without disproving it.</para>
///
/// <para>So <see cref="NotSelfContaining"/> is not a leak list, and calling it
/// one would be the same overclaim <c>MarkoutInline.Code</c>'s own doc comment
/// had to be corrected for. It is the exact complement of the set that contains
/// <i>in the row</i>. Membership is a fact about which idiom a column uses; some
/// entries are producer-contained and correct as they stand, and the remainder
/// are the residual tracked by issue #3463 -- which this measures at 350 members
/// across 85 types, where the estimate had been "roughly 290."</para>
///
/// <para>Asserting it as a set is what makes the weaker property still bite. A
/// column cannot leave the self-containing set without failing here, which is
/// precisely the hole that was reported: deleting
/// <c>LibraryViewText.Contain</c> from <c>IntegrationSignalRow.Kind</c> moves it
/// into this list and fails. A column also cannot join the set without being
/// removed from the list, so the residual shrinks only by naming, never by
/// drift.</para>
/// </remarks>
public class MarkoutRowContainmentTests
{
    private const string Bidi = "\u202E";
    private const string Hostile = "HOSTILE" + Bidi + "MARKER";

    /// <summary>
    /// Types that carry untrusted text but cannot be constructed generically,
    /// asserted as an exact set so both a new gap and a stale entry fail.
    /// </summary>
    /// <remarks>
    /// Empty today. It exists so that the first type this walk cannot build
    /// has to be named here rather than silently skipped -- which is exactly
    /// how the coverage hole this class was written for came about.
    /// </remarks>
    private static readonly string[] OutOfReach = [];

    /// <summary>
    /// The exact set of string columns that do not contain their own text,
    /// pinned so that leaving the self-containing set is a build failure and
    /// joining it requires deleting a line here.
    /// </summary>
    /// <remarks>
    /// Not a leak list -- see the remarks on
    /// <see cref="MarkoutRowContainmentTests"/>. Membership has three causes and
    /// this set does not distinguish them: a column contained at the producer
    /// (<c>ResourceTriageRow.Member</c>, built from <c>MarkoutInline.Code</c>);
    /// a column whose value the tool composes rather than reads, so there is no
    /// untrusted text to contain (<c>SourceLinkAuditSection.SourceFiles</c>,
    /// which is <c>$"{int}/{int} available"</c>); and the genuine residual
    /// tracked by issue #3463. Deciding which a given entry is takes reading its
    /// producer -- which is the work #3463 exists to do, and is why this pins
    /// the set rather than asserting it empty.
    /// Ordinal sort order, matching the assertion.
    /// </remarks>
    private static readonly string[] NotSelfContaining =
    [
        "AllocationFactRow.AllocatedType",
        "AllocationFactRow.AllocationKind",
        "AllocationFactRow.CountedAsHeap",
        "AllocationFactRow.Escape",
        "AllocationFactRow.Evidence",
        "AllocationFactRow.Frequency",
        "AllocationFactRow.ILOffset",
        "AllocationFactRow.InLoop",
        "AllocationFactRow.Member",
        "AnalysisDiffRow.Delta",
        "AnalysisDiffRow.Evidence",
        "AnalysisDiffRow.Member",
        "AnalysisDiffRow.New",
        "AnalysisDiffRow.Old",
        "AnalysisDiffRow.Shape",
        "AnalysisDiffRow.Signal",
        "AnalysisDiffView.Summary",
        "AnalysisDiffView.Title",
        "AnalysisDiffView.Versions",
        "ApiInspectionFailureRow.Detail",
        "ApiInspectionFailureRow.Kind",
        "ApiInspectionFailureRow.Mechanism",
        "ApiInspectionFailureRow.Operation",
        "ApiInspectionFailureRow.Subject",
        "ApiSurfaceTableRow.Description",
        "ApiSurfaceTableRow.Kind",
        "ApiSurfaceTableRow.Members",
        "ApiSurfaceTableRow.Type",
        "ApiTableRow.Detail",
        "ApiTableRow.Kind",
        "ApiTableRow.Name",
        "ApiTableRow.ReturnType",
        "AppliedTasteRow.Detail",
        "AppliedTasteRow.Fidelity",
        "AppliedTasteRow.Rule",
        "AppliedTasteRow.Subject",
        "AssemblyDependenciesView.AssemblyName",
        "AssemblyDependenciesView.Tfm",
        "AssemblyDependenciesView.Title",
        "AssemblyDependenciesView.Version",
        "AsyncMethodRow.DeclaringType",
        "AsyncMethodRow.Signature",
        "BaseclassRow.Type",
        "CacheCategoryRow.Items",
        "CacheCategoryRow.Name",
        "CacheCategoryRow.Size",
        "CacheInfoView.Location",
        "CacheInfoView.Total",
        "CallSiteRow.CallKind",
        "CallSiteRow.Callee",
        "CallSiteRow.ILOffset",
        "CallSiteRow.Opcode",
        "CallSiteRow.OperandToken",
        "CallSiteRow.ReturnAddress",
        "CalledTypeRow.Assembly",
        "CalledTypeRow.CallKinds",
        "CalledTypeRow.Type",
        "CallerSiteRow.CallKind",
        "CallerSiteRow.Caller",
        "CallerSiteRow.ILOffset",
        "CallerSiteRow.Opcode",
        "CallerSiteRow.OperandToken",
        "CallerSiteRow.ReturnAddress",
        "CallerSiteRow.Source",
        "ClassifiedMethodRow.DeclaringType",
        "ClassifiedMethodRow.Signature",
        "CliApiSurface.Description",
        "CliApiSurface.Library",
        "CliApiSurface.Name",
        "CliApiSurface.Source",
        "CliApiSurface.Tfm",
        "CliApiSurface.Version",
        "CliSchemaView.Description",
        "CliSchemaView.Name",
        "CliSchemaView.Title",
        "CliSchemaView.Version",
        "ConstructorOverloadView.Title",
        "ConstructorParameterRow.Notes",
        "ConstructorParameterRow.Parameter",
        "ConstructorParameterRow.Type",
        "ConstructorSummaryRow.Decode",
        "ConstructorSummaryRow.Name",
        "ConstructorSummaryRow.Overloads",
        "CostFactRow.CostKind",
        "CostFactRow.Evidence",
        "CostFactRow.ILOffset",
        "CostFactRow.InLoop",
        "CostFactRow.Member",
        "CostFactRow.Operation",
        "DiffChangeRow.Message",
        "DiffChangeRow.TypeName",
        "DiffDetailedChangeRow.Change",
        "DiffDetailedChangeRow.Classification",
        "DiffDetailedChangeRow.Detail",
        "DiffDetailedChangeRow.Kind",
        "DiffDetailedChangeRow.Member",
        "DiffDetailedChangeRow.New",
        "DiffDetailedChangeRow.Old",
        "DiffDetailedChangeRow.Type",
        "DiffDetailedChangesView.Summary",
        "DiffDetailedChangesView.Title",
        "DiffDetailedChangesView.Versions",
        "DiffDocumentView.AnalysisDiffNote",
        "DiffDocumentView.AnalysisDiffSummary",
        "DiffDocumentView.ChangesSummary",
        "DiffDocumentView.FindingTransitionsSummary",
        "DiffDocumentView.ImplementationDiffNote",
        "DiffDocumentView.ImplementationDiffSummary",
        "DiffDocumentView.Title",
        "DiffDocumentView.Versions",
        "DiffFullView.Summary",
        "DiffFullView.Title",
        "DiffFullView.Versions",
        "DiffTableRow.Change",
        "DiffTableRow.Detail",
        "DiffTableRow.Type",
        "DiffTableView.Summary",
        "DiffTableView.Title",
        "DiffTableView.Versions",
        "DiscoveryRow.Kind",
        "DiscoveryRow.Name",
        "EmptyDepsView.Description",
        "EmptyDepsView.Title",
        "EnumValueRow.Description",
        "EnumValueRow.Name",
        "EnumValueRow.Value",
        "EventSummaryRow.Name",
        "EventSummaryRow.Type",
        "ExceptionRegionRow.CaughtType",
        "ExceptionRegionRow.Clause",
        "ExceptionRegionRow.FilterRange",
        "ExceptionRegionRow.HandlerRange",
        "ExceptionRegionRow.TryRange",
        "ExtensionCountRow.Extensions",
        "ExtensionRow.Kind",
        "ExtensionsResultView.Description",
        "ExtensionsResultView.Title",
        "FactRow.Anchor",
        "FactRow.Category",
        "FactRow.Conditionality",
        "FactRow.CsLine",
        "FactRow.Detail",
        "FactRow.IL",
        "FactRow.Id",
        "FactRow.Member",
        "FidelityCauseRow.Code",
        "FidelityCauseRow.Discriminator",
        "FidelityCauseRow.Location",
        "FidelityCauseRow.Node",
        "FidelityCauseRow.NodeKind",
        "FidelityCauseRow.Reason",
        "FidelityCauseRow.State",
        "FieldSummaryRow.Decode",
        "FieldSummaryRow.Name",
        "FieldSummaryRow.ReturnType",
        "FindMemberRow.Kind",
        "FindMemberRow.Library",
        "FindMemberRow.Member",
        "FindMemberRow.Pattern",
        "FindMemberRow.Signature",
        "FindMemberRow.Source",
        "FindMemberRow.Type",
        "FindMembersResultView.Description",
        "FindMembersResultView.Title",
        "FindResultView.Description",
        "FindResultView.Title",
        "FindRow.Kind",
        "FindRow.Library",
        "FindRow.Match",
        "FindRow.Namespace",
        "FindRow.Pattern",
        "FindRow.Similarity",
        "FindRow.Source",
        "FindRow.Type",
        "FindingTransitionRow.Finding",
        "FindingTransitionRow.New",
        "FindingTransitionRow.Old",
        "FindingTransitionRow.Transition",
        "FindingTransitionsView.Title",
        "FindingTransitionsView.Versions",
        "ForwarderSummaryRow.TargetLibrary",
        "ForwarderSummaryRow.Types",
        "ILCoordinateBatchRow.Coordinate",
        "ILCoordinateBatchRow.Evidence",
        "ILCoordinateBatchRow.ILOffset",
        "ILCoordinateBatchRow.Label",
        "ILCoordinateBatchRow.Meaning",
        "ILCoordinateBatchRow.Member",
        "ImplementationDiffRow.Change",
        "ImplementationDiffRow.Difference",
        "ImplementationDiffRow.Mechanism",
        "ImplementationDiffView.Summary",
        "ImplementationDiffView.Title",
        "ImplementationDiffView.Versions",
        "ImplementerRow.Kind",
        "ImplementerRow.Relationship",
        "ImplementerRow.Source",
        "ImplementsResultView.Description",
        "ImplementsResultView.Title",
        "InfoView.Cache",
        "InfoView.HTTP",
        "InfoView.Output",
        "InfoView.Readme",
        "InfoView.Time",
        "InspectionFailureRow.Section",
        "InterfaceRow.Interface",
        "LibraryInspectionReport.Title",
        "MemberIndexRow.CanonicalSignature",
        "MemberIndexRow.Decode",
        "MemberIndexRow.Digest",
        "MemberIndexRow.Selector",
        "MemberIndexRow.Stable",
        "MemberRow.Description",
        "MemberRow.Digest",
        "MemberRow.Name",
        "MemberRow.Select",
        "MemberRow.Signature",
        "MemberSignatureRow.CanonicalSignature",
        "MemberSignatureRow.Decode",
        "MemberSignatureRow.Description",
        "MemberSignatureRow.Digest",
        "MemberSignatureRow.Signature",
        "MethodAttributeRow.Name",
        "MethodAttributeRow.Value",
        "MethodSummaryRow.Decode",
        "MethodSummaryRow.Name",
        "MethodSummaryRow.Overloads",
        "MethodSummaryRow.ReturnType",
        "OptimizationOpportunityRow.Allocation",
        "OptimizationOpportunityRow.CachedSites",
        "OptimizationOpportunityRow.CallerLoop",
        "OptimizationOpportunityRow.CallerLoopDepth",
        "OptimizationOpportunityRow.CallerLoopWitness",
        "OptimizationOpportunityRow.Candidate",
        "OptimizationOpportunityRow.ConditionalPaths",
        "OptimizationOpportunityRow.Confidence",
        "OptimizationOpportunityRow.DirectSites",
        "OptimizationOpportunityRow.Evidence",
        "OptimizationOpportunityRow.Finding",
        "OptimizationOpportunityRow.Fix",
        "OptimizationOpportunityRow.IL",
        "OptimizationOpportunityRow.Loop",
        "OptimizationOpportunityRow.Member",
        "OptimizationOpportunityRow.OncePaths",
        "OptimizationOpportunityRow.OpaquePaths",
        "OptimizationOpportunityRow.Operation",
        "OptimizationOpportunityRow.Path",
        "OptimizationOpportunityRow.PathConfidence",
        "OptimizationOpportunityRow.PostDominance",
        "OptimizationOpportunityRow.Provenance",
        "OptimizationOpportunityRow.RepeatedPaths",
        "OptimizationOpportunityRow.RootReach",
        "OptimizationOpportunityRow.Saturated",
        "OptimizationOpportunityRow.Shape",
        "OptimizationOpportunityRow.Token",
        "OptimizationOpportunityRow.UnknownPaths",
        "OptimizationOpportunityRow.Weight",
        "PInvokeMethodRow.DeclaringType",
        "PInvokeMethodRow.Signature",
        "PackageDependenciesView.Title",
        "PackageSearchResultView.Description",
        "PackageSearchResultView.Title",
        "PackageSearchRow.Description",
        "PackageSearchRow.Downloads",
        "PackageSearchRow.Package",
        "PackageSearchRow.Version",
        "PerformanceRow.Allocation",
        "PerformanceRow.Evidence",
        "PerformanceRow.Member",
        "PerformanceRow.Reach",
        "PropertySummaryRow.Accessors",
        "PropertySummaryRow.Decode",
        "PropertySummaryRow.Name",
        "PropertySummaryRow.ReturnType",
        "ReferenceRow.PublicKeyToken",
        "ResourceRow.Size",
        "ResourceRow.Visibility",
        "ResourceTriageRow.AcquireIL",
        "ResourceTriageRow.Boundary",
        "ResourceTriageRow.BoundaryIL",
        "ResourceTriageRow.Candidate",
        "ResourceTriageRow.Member",
        "SafetyFactRow.Evidence",
        "SafetyFactRow.ILOffset",
        "SafetyFactRow.Member",
        "SafetyFactRow.Operation",
        "SafetyFactRow.Requirement",
        "SafetyFactRow.SafetyKind",
        "SampleRow.Description",
        "SampleRow.Type",
        "SampleRow.Url",
        "SourceIntegritySection.CrlfMismatch",
        "SourceIntegritySection.MismatchedFiles",
        "SourceIntegritySection.Status",
        "SourceLinkAuditSection.SourceFiles",
        "SourceLinkAuditSection.Status",
        "SwitchRow.Api",
        "SwitchRow.Kind",
        "SwitchRow.Switch",
        "TopLeverageRow.Callers",
        "TopLeverageRow.Depth",
        "TopLeverageRow.Fanout",
        "TopLeverageRow.Generated",
        "TopLeverageRow.LoopCalls",
        "TopLeverageRow.Member",
        "TopLeverageRow.RootReach",
        "TopLeverageRow.Selector",
        "TopLeverageRow.Stable",
        "TypeExceptionRegionRow.CaughtType",
        "TypeExceptionRegionRow.Clause",
        "TypeExceptionRegionRow.FilterRange",
        "TypeExceptionRegionRow.HandlerRange",
        "TypeExceptionRegionRow.Member",
        "TypeExceptionRegionRow.TryRange",
        "TypeParameterRow.Constraints",
        "TypeParameterRow.Parameter",
        "TypeShapeView.Assembly",
        "TypeShapeView.FullName",
        "TypeShapeView.Kind",
        "TypeShapeView.Modifiers",
        "TypeShapeView.Package",
        "TypeShapeView.Version",
        "TypeSummaryRow.Description",
        "TypeSummaryRow.Kind",
        "TypeSummaryRow.Members",
        "TypeSummaryRow.Type",
        "TypeView.Assembly",
        "TypeView.BaseType",
        "TypeView.Description",
        "TypeView.Implements",
        "TypeView.Kind",
        "TypeView.Modifiers",
        "TypeView.Package",
        "TypeView.SamplesInfo",
        "TypeView.Source",
        "TypeView.SourceUrl",
        "TypeView.Tfm",
        "TypeView.Title",
        "TypeView.TypeParametersInline",
        "TypeView.Version",
        "UnionTypeRow.IUnion",
        "UnsafeMemberRow.Detail",
        "UnsafeMemberRow.IL",
        "UnsafeMemberRow.Member",
        "UnsafeMemberRow.Token",
        "UnsafeOperationRow.Detail",
        "UnsafeOperationRow.IL",
        "UnsafeOperationRow.Kind",
        "UnsafeOperationRow.Reason",
        "UnsafeOperationRow.Token",
    ];

    [Fact]
    public void EverySerializableRow_ContainsTheTextItIsConstructedWith()
    {
        var assembly = typeof(DotnetInspector.Views.LibraryInspectionView).Assembly;

        List<string> leaks = [];
        List<string> declined = [];
        int checkedTypes = 0;
        int checkedProperties = 0;

        foreach (var type in assembly.GetTypes().Where(IsSerializableRow).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!TryConstruct(type, out object? row, out string why))
            {
                declined.Add($"{type.Name}: {why}");
                continue;
            }

            checkedTypes++;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }

                if (property.PropertyType != typeof(string))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(row);
                }
                catch (TargetInvocationException)
                {
                    // A computed projection over members this walk left at
                    // their defaults. It renders nothing, so it carries no
                    // untrusted text of its own.
                    continue;
                }

                if (value is not string text || !text.Contains("HOSTILE", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedProperties++;

                if (text.Contains(Bidi, StringComparison.Ordinal))
                {
                    leaks.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        // Non-vacuity, and the reason it is spelled as two separate floors: a
        // filter bug that matched no types, and one that matched types but no
        // string properties, both produce an empty leak list that reads as a
        // pass. The numbers are floors rather than exact counts so that adding
        // a row does not edit this test, but losing most of them does.
        Assert.True(
            checkedTypes >= 40,
            $"Only {checkedTypes} serializable types were constructed; the type filter is too narrow to prove anything.");
        Assert.True(
            checkedProperties >= 150,
            $"Only {checkedProperties} string columns received the hostile value; the fill is not reaching columns.");

        Assert.Equal(OutOfReach, declined.Order(StringComparer.Ordinal).ToArray());

        string[] observed = [.. leaks.Order(StringComparer.Ordinal)];

        if (!observed.SequenceEqual(NotSelfContaining, StringComparer.Ordinal))
        {
            string[] lost = [.. observed.Except(NotSelfContaining, StringComparer.Ordinal)];
            string[] gained = [.. NotSelfContaining.Except(observed, StringComparer.Ordinal)];

            Assert.Fail(
                "The set of columns that do not contain their own text has changed."
                    + Environment.NewLine
                    + (lost.Length == 0
                        ? string.Empty
                        : "No longer self-containing -- wrap each in LibraryViewText.Contain (or the "
                            + $"view's local equivalent), or contain it at the producer:{Environment.NewLine}"
                            + string.Join(Environment.NewLine, lost) + Environment.NewLine)
                    + (gained.Length == 0
                        ? string.Empty
                        : "Now self-containing -- delete each from NotSelfContaining so the residual "
                            + $"cannot drift back:{Environment.NewLine}"
                            + string.Join(Environment.NewLine, gained)));
        }
    }

    /// <summary>
    /// A type the Markout serializer renders, and so a type whose string
    /// members reach a document.
    /// </summary>
    private static bool IsSerializableRow(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            && type.GetCustomAttributes()
                .Any(a => a.GetType().Name.StartsWith("MarkoutSerializable", StringComparison.Ordinal))
            && type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0);

    /// <summary>
    /// Builds an instance with every string it will accept set to the hostile
    /// value, through the constructor and through every settable property.
    /// </summary>
    private static bool TryConstruct(Type type, out object? row, out string why)
    {
        row = null;
        why = "";

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            why = "no public constructor";
            return false;
        }

        object?[] arguments;
        try
        {
            arguments = [.. constructor.GetParameters().Select(p => MakeArgument(p.ParameterType))];
        }
        catch (Exception ex)
        {
            why = $"could not build arguments ({ex.GetType().Name})";
            return false;
        }

        try
        {
            row = constructor.Invoke(arguments);
        }
        catch (Exception ex)
        {
            why = $"constructor threw ({(ex.InnerException ?? ex).GetType().Name})";
            return false;
        }

        // Sections and rows built from an empty constructor carry their text on
        // settable properties instead, so a constructor-only fill would check
        // the ones that need it least.
        HashSet<string> fromConstructor = new(
            constructor.GetParameters().Select(p => p.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string)
                || !property.CanWrite
                || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            // An init-only property the constructor already supplied has had the
            // hostile value put through its containment; writing it again would
            // replace the contained value with the raw one and report a leak
            // that is not there. One the constructor did *not* supply is the
            // opposite case -- it is still at its default, and the accessor is
            // the only way in.
            if (IsInitOnly(property) && fromConstructor.Contains(property.Name))
            {
                continue;
            }

            try
            {
                property.SetValue(row, Hostile);
            }
            catch (Exception)
            {
                // A validating setter. The constructor already covered it.
            }
        }

        return true;
    }

    /// <summary>
    /// True for <c>{ get; init; }</c>, which reflection can write and C# cannot.
    /// </summary>
    /// <remarks>
    /// This is the difference between a gate and a generator of false leaks.
    /// <c>init</c> is a <c>modreq</c> the compiler enforces at the call site,
    /// not a runtime restriction, so <see cref="PropertyInfo.SetValue"/>
    /// succeeds on one -- and every row here spells its containment as
    /// <c>public string Kind { get; init; } = LibraryViewText.Contain(Kind);</c>.
    /// Writing the property after construction therefore replaces the contained
    /// value with the raw one and then reports the row as leaking.
    ///
    /// The first run of this class did exactly that and accused 479 columns
    /// across 107 types, including ones whose containment had been added and
    /// verified days earlier. A gate that cannot be told apart from a bug in
    /// itself is worse than no gate, because it trains its reader to dismiss it.
    ///
    /// An init-only property the constructor <i>supplied</i> needs no write: the
    /// hostile value already went through the containment being tested.
    ///
    /// The converse is not true, and treating it as true cost the gate a whole
    /// category. A reviewer removed containment from <c>LibraryInfoSection</c>,
    /// which has a parameterless constructor and only <c>init</c> properties
    /// spelled <c>init =&gt; field = LibraryViewText.Contain(value);</c>, and
    /// this class stayed green: nothing was supplied by the constructor and
    /// nothing was written afterwards, so every column read back its default and
    /// none was ever examined. It did not even dent the non-vacuity floors,
    /// because other types kept those satisfied.
    ///
    /// So the skip is conditioned on the constructor having supplied the
    /// property, not on the property being init-only. Where the accessor is the
    /// only way in, reflection goes in through it -- which is exactly the path
    /// that runs the containment.
    /// </remarks>
    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit") == true;

    private static object? MakeArgument(Type type)
    {
        if (type == typeof(string))
        {
            return Hostile;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return null;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 0);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>)
                || definition == typeof(IReadOnlyList<>) || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>))
            {
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
            }
        }

        return null;
    }
}
