using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryTypeOverviewDocument),
    typeof(BrowserTypeOverviewInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeOverviewDocument),
    typeof(BrowserTypeOverviewInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeOverviewDocument),
    typeof(BrowserTypeOverviewInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryTypeOverviewDocument(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string spelling,
        string accessibility)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework).ConfigureAwait(false);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        if (!coordinate.Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{packageId} {version} has no selected compile Library "
                    + $"({coordinate.Selection.Status}).");
        }
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(
                coordinate,
                coordinate.CompileAsset(assemblyName));
        return SerializeOverview(await ExecuteOverviewAsync(
            scope.UseSurfaceParticipant(
                participant,
                (group, member) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        member,
                        AssemblyContextLibraryRole.ApiOnly,
                        BrowserExactMemberPolicy.MaterializationLimits,
                        CancellationToken.None)),
            typeIdentity,
            spelling,
            accessibility).ConfigureAwait(false));
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeOverviewDocument(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string spelling,
        string accessibility)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                CancellationToken.None).ConfigureAwait(false);
        return SerializeOverview(await ExecuteOverviewAsync(
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, member) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        member,
                        AssemblyContextLibraryRole.Implementation,
                        BrowserExactMemberPolicy.MaterializationLimits,
                        CancellationToken.None)),
            typeIdentity,
            spelling,
            accessibility).ConfigureAwait(false));
    }

    [JSExport]
    public static async Task<string> QueryUploadedLibraryTypeOverviewDocument(
        string declaredName,
        byte[] content,
        string typeIdentity,
        string spelling,
        string accessibility)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SerializeOverview(await ExecuteOverviewAsync(
            EmbeddedLibraryInspection.MaterializeAsync(
                declaredName,
                ImmutableArray.CreateRange(content),
                AssemblyContextLibraryRole.Implementation,
                BrowserExactMemberPolicy.MaterializationLimits),
            typeIdentity,
            spelling,
            accessibility).ConfigureAwait(false));
    }

    private static async Task<BrowserTypeOverviewInspection>
        ExecuteOverviewAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        TypeOverviewDocumentInspectionPlan plan = new(
            BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity),
            new(
                BrowserExactMemberPolicy.Bounds.MaxMembers,
                includeExactMemberCount: true),
            BrowserExactMemberPolicy.Bounds,
            ParseOverviewSpelling(spelling),
            ParseOverviewAccessibility(accessibility),
            includeHidden: false,
            includeComposition: true,
            includeSelectorCounts: true);
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                    materialization,
                    (reference, owner) =>
                        AssemblyContextLibraryInspection.ExecuteOperation(
                            reference,
                            owner,
                            lease =>
                                TypeOverviewDocumentInspectionOperation.Execute(
                                    new(reference, plan),
                                    lease))).ConfigureAwait(false);
        if (run.Failure is { } failure)
            return Failure(failure, "type-overview/materialization",
                [.. run.CleanupFailures.Select(static detail =>
                    new BrowserMemberGroupDocumentDiagnostic(
                        "type-overview.library-retirement", "Warning",
                        detail, null))]);
        if (run.Result is not { } envelope)
        {
            const string detail =
                "The exact Library owner could not issue the Type overview lease.";
            return Failure(detail, "type-overview/lease",
                [.. run.CleanupFailures.Select(static failure =>
                    new BrowserMemberGroupDocumentDiagnostic(
                        "type-overview.library-retirement", "Warning",
                        failure, null))]);
        }
        BrowserMemberGroupDocumentDiagnostic[] diagnostics =
        [
            .. envelope.Diagnostics.Select(static diagnostic =>
                new BrowserMemberGroupDocumentDiagnostic(
                    diagnostic.Code,
                    diagnostic.Severity.ToString(),
                    diagnostic.Summary.ToString(),
                    diagnostic.Correspondence?.ToString())),
            .. run.CleanupFailures.Select(static detail =>
                new BrowserMemberGroupDocumentDiagnostic(
                    "type-overview.library-retirement",
                    "Warning",
                    detail,
                    null)),
        ];
        return envelope.Content switch
        {
            TypeOverviewDocumentInspectionOutcome.Available available =>
                ProjectAvailable(available.Document, envelope.Share,
                    diagnostics),
            TypeOverviewDocumentInspectionOutcome.Rejected rejected =>
                new(BrowserTypeOverviewOutcome.Rejected,
                    $"The Type overview was rejected ({rejected.Reason}).",
                    null, envelope.Share, diagnostics),
            TypeOverviewDocumentInspectionOutcome.Incomplete incomplete =>
                new(BrowserTypeOverviewOutcome.Incomplete,
                    $"The Type overview reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null, envelope.Share, diagnostics),
            TypeOverviewDocumentInspectionOutcome.Failed failed =>
                new(BrowserTypeOverviewOutcome.Failed,
                    $"The Type overview failed ({failed.Reason}).",
                    null, envelope.Share, diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Type overview document outcome."),
        };
    }

    private static BrowserTypeOverviewInspection ProjectAvailable(
        TypeOverviewDocument document,
        InspectionShare share,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics)
    {
        TypeMemberGroupPopulationResult population = document.Members;
        if (population.Rows is not TypeMemberGroupRowsOutcome.Read
            { Continuation: null } rows
            || population.Composition is not { } composition
            || population.SelectorCounts is not { } selectorCounts)
        {
            return new(
                BrowserTypeOverviewOutcome.Incomplete,
                "The Type overview did not contain the complete requested declaration inventory.",
                null,
                share,
                diagnostics);
        }
        return new(
            BrowserTypeOverviewOutcome.Available,
            null,
            new(
                new(
                    document.Subject.Assembly.Name.ToString(),
                    document.Subject.Assembly.Version.ToString(),
                    document.Subject.Assembly.Culture?.ToString(),
                    document.Subject.Assembly.PublicKeyToken?.ToString()),
                document.Subject.ModuleVersionId,
                document.Subject.Type.ToEscapedFullName(),
                document.Subject.TypeDefinitionToken,
                document.Subject.Category.ToString(),
                document.Subject.IsByRefLike,
                [
                    .. document.Subject.Signature.GenericParameters.Select(
                        parameter => new BrowserTypeOverviewGenericParameter(
                            parameter.Name.ToString(),
                            parameter.MetadataIndex,
                            parameter.DefinitionSegmentIndex,
                            parameter.Attributes.ToString())),
                ],
                new(
                    population.Binding.Spelling.ToString(),
                    population.Binding.Accessibility.ToString(),
                    population.Binding.Receiver.ToString(),
                    population.Binding.Ordering.ToString(),
                    population.Binding.IncludeHidden,
                    new(
                        composition.Public,
                        composition.Protected,
                        composition.Internal,
                        composition.Private,
                        composition.Static,
                        composition.This,
                        composition.Extension),
                    new(
                        [
                            .. selectorCounts.Kinds.Select(count =>
                                new BrowserTypeMemberFacetCount(
                                    OverviewKind(count.Kind),
                                    count.Count)),
                        ],
                        ProjectOverviewTraits(selectorCounts.Traits)),
                    [
                        .. rows.Items.Select(group =>
                            new BrowserTypeOverviewGroup(
                                $"{OverviewKind(group.Binding.Category)}:"
                                    + group.Binding.Name,
                                group.Binding.Name.ToString(),
                                OverviewDisplayName(group),
                                OverviewKind(group.Binding.Category),
                                group.BaselineOrdinal,
                                group.ExactMemberCount
                                    ?? throw new InvalidOperationException(
                                        "Exact group counts were requested."),
                                OverviewReceivers(group.Receivers),
                                ProjectOverviewTraits(group.Traits
                                    ?? throw new InvalidOperationException(
                                        "Group trait counts were requested.")))),
                    ])),
            share,
            diagnostics);
    }

    private static string OverviewDisplayName(TypeMemberGroupShape group)
    {
        string name = group.Binding.Name.ToString();
        return group.SharedGenericParameters is { Length: > 0 } parameters
            ? $"{name}<{string.Join(", ",
                parameters.Select(parameter => parameter.ToString()))}>"
            : name;
    }

    private static BrowserTypeMemberTraitCounts ProjectOverviewTraits(
        TypeMemberTraitCounts traits) =>
        new(traits.All, traits.BodyBacked, traits.Static, traits.Instance,
            traits.Virtual, traits.Interface, traits.Extensions);

    private static string OverviewKind(MemberGroupCategory category) =>
        category switch
        {
            MemberGroupCategory.Method => "method",
            MemberGroupCategory.Constructor => "constructor",
            MemberGroupCategory.Operator => "operator",
            MemberGroupCategory.Finalizer => "finalizer",
            MemberGroupCategory.ExplicitInterfaceImplementation =>
                "explicit-interface-implementation",
            MemberGroupCategory.Property => "property",
            MemberGroupCategory.Field => "field",
            MemberGroupCategory.Event => "event",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private static string[] OverviewReceivers(MemberGroupReceiverForms forms) =>
    [
        .. new[]
        {
            (MemberGroupReceiverForms.Static, "static"),
            (MemberGroupReceiverForms.This, "this"),
            (MemberGroupReceiverForms.Extension, "extension"),
        }.Where(candidate => forms.HasFlag(candidate.Item1))
            .Select(candidate => candidate.Item2),
    ];

    private static BrowserTypeOverviewInspection Failure(
        string detail,
        string boundary,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics) =>
        new(BrowserTypeOverviewOutcome.Failed, detail, null,
            new InspectionShare.NonProjectable(boundary, detail),
            diagnostics);

    private static TypeMemberGroupSpelling ParseOverviewSpelling(
        string spelling) =>
        spelling.Trim().ToLowerInvariant() switch
        {
            "csharp" or "c#" => TypeMemberGroupSpelling.CSharp,
            "metadata" => TypeMemberGroupSpelling.Metadata,
            _ => throw new ArgumentException(
                "Spelling must be 'csharp' or 'metadata'.", nameof(spelling)),
        };

    private static TypeMemberGroupAccessibilityFilter
        ParseOverviewAccessibility(string accessibility) =>
        accessibility.Trim().ToLowerInvariant() switch
        {
            "all" => TypeMemberGroupAccessibilityFilter.All,
            "public" => TypeMemberGroupAccessibilityFilter.Public,
            "protected" => TypeMemberGroupAccessibilityFilter.Protected,
            "internal" => TypeMemberGroupAccessibilityFilter.Internal,
            "private" => TypeMemberGroupAccessibilityFilter.Private,
            _ => throw new ArgumentException(
                "Accessibility must be all, public, protected, internal, or private.",
                nameof(accessibility)),
        };

    private static string SerializeOverview(BrowserTypeOverviewInspection value) =>
        JsonSerializer.Serialize(
            value,
            BrowserMetadataJsonContext.Default.BrowserTypeOverviewInspection);
}
