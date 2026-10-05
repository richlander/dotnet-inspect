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
    typeof(BrowserTypeOverviewDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeOverviewDocument),
    typeof(BrowserTypeOverviewDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeOverviewDocument),
    typeof(BrowserTypeOverviewDocumentInspection))]
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
        BrowserTypeOverviewDocumentInspection inspection =
            await ExecuteTypeOverviewDocumentAsync(
                    scope.UseSurfaceParticipant(
                        participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.ApiOnly,
                                BrowserExactMemberPolicy
                                    .MaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeOverviewDocument(inspection);
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
                    CancellationToken.None)
                .ConfigureAwait(false);
        BrowserTypeOverviewDocumentInspection inspection =
            await ExecuteTypeOverviewDocumentAsync(
                    resolution.Scope.UseParticipant(
                        resolution.Participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.Implementation,
                                BrowserExactMemberPolicy
                                    .MaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeOverviewDocument(inspection);
    }

    [JSExport]
    public static async Task<string>
        QueryUploadedLibraryTypeOverviewDocument(
            string declaredName,
            byte[] content,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserTypeOverviewDocumentInspection inspection =
            await ExecuteTypeOverviewDocumentAsync(
                    EmbeddedLibraryInspection.MaterializeAsync(
                        declaredName,
                        ImmutableArray.CreateRange(content),
                        AssemblyContextLibraryRole.Implementation,
                        BrowserExactMemberPolicy
                            .MaterializationLimits),
                    typeIdentity,
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeOverviewDocument(inspection);
    }

    private static async Task<BrowserTypeOverviewDocumentInspection>
        ExecuteTypeOverviewDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        MetadataTypeDefinitionName type =
            BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity);
        TypeOverviewDocumentInspectionPlan plan =
            TypeOverviewDocumentInspectionPlans.DeclaredMemberRows(
                type,
                BrowserExactMemberPolicy.Bounds,
                ParseSpelling(spelling),
                ParseAccessibility(accessibility),
                includeHidden: false,
                BrowserExactMemberPolicy.Bounds.MaxMembers);
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
                                    lease)))
                .ConfigureAwait(false);
        if (run.Failure is { } failure)
        {
            return Failed(
                failure,
                new InspectionShare.NonProjectable(
                    "type-document/materialization",
                    failure),
                [.. run.CleanupFailures]);
        }
        if (run.Result is not { } envelope)
        {
            const string detail =
                "The exact Library owner could not issue the Type overview document lease.";
            return Failed(
                detail,
                new InspectionShare.NonProjectable(
                    "type-document/lease",
                    detail),
                [.. run.CleanupFailures]);
        }

        string[] diagnostics =
        [
            .. envelope.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Summary}"),
            .. run.CleanupFailures,
        ];
        return ProjectTypeOverviewDocument(
            envelope.Content,
            envelope.Share,
            diagnostics);
    }

    private static BrowserTypeOverviewDocumentInspection ProjectTypeOverviewDocument(
        TypeOverviewDocumentInspectionOutcome outcome,
        InspectionShare share,
        string[] diagnostics) =>
        outcome switch
        {
            TypeOverviewDocumentInspectionOutcome.Available available =>
                ProjectAvailable(available.Document, share, diagnostics),
            TypeOverviewDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserTypeOverviewDocumentOutcome.Rejected,
                    $"The Type overview document was rejected ({rejected.Reason}).",
                    null,
                    share,
                    diagnostics),
            TypeOverviewDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserTypeOverviewDocumentOutcome.Incomplete,
                    $"The Type overview document reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null,
                    share,
                    diagnostics),
            TypeOverviewDocumentInspectionOutcome.Failed failed =>
                Failed(
                    $"The Type overview document failed ({failed.Reason}).",
                    share,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Type overview document outcome."),
        };

    private static BrowserTypeOverviewDocumentInspection ProjectAvailable(
        TypeOverviewDocument document,
        InspectionShare share,
        string[] diagnostics) =>
        new(
            BrowserTypeOverviewDocumentOutcome.Available,
            null,
            new(
                document.Subject.Type.ToEscapedFullName(),
                document.Subject.TypeDefinitionToken,
                document.Subject.Category.ToString(),
                document.Subject.IsByRefLike,
                [
                    .. document.Subject.Signature.GenericParameters.Select(
                        parameter =>
                            new BrowserTypeOverviewDocumentGenericParameter(
                                parameter.Name.ToString(),
                                parameter.MetadataIndex,
                                parameter.DefinitionSegmentIndex,
                                parameter.Attributes.ToString())),
                ],
                ProjectDeclarations(document.Declarations)),
            share,
            diagnostics);

    private static BrowserTypeOverviewDocumentDeclarations ProjectDeclarations(
        TypeOverviewDocumentDeclarations declarations) =>
        declarations switch
        {
            TypeOverviewDocumentDeclarations.NotRequested =>
                new(
                    BrowserTypeOverviewDocumentDeclarationsOutcome.NotRequested,
                    null,
                    null),
            TypeOverviewDocumentDeclarations.Available available =>
                ProjectAvailableDeclarations(available.Population),
            TypeOverviewDocumentDeclarations.Rejected rejected =>
                new(
                    BrowserTypeOverviewDocumentDeclarationsOutcome.Rejected,
                    $"The declaration population was rejected ({rejected.Reason}).",
                    null),
            TypeOverviewDocumentDeclarations.Incomplete incomplete =>
                new(
                    BrowserTypeOverviewDocumentDeclarationsOutcome.Incomplete,
                    $"The declaration population reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null),
            TypeOverviewDocumentDeclarations.Failed failed =>
                new(
                    BrowserTypeOverviewDocumentDeclarationsOutcome.Failed,
                    $"The declaration population failed ({failed.Reason}).",
                    null),
            _ => throw new InvalidOperationException(
                "Unknown Type overview document declarations outcome."),
        };

    private static BrowserTypeOverviewDocumentDeclarations
        ProjectAvailableDeclarations(
            TypeMemberGroupPopulationResult population)
    {
        if (population.Rows is not TypeMemberGroupRowsOutcome.Read
            {
                Continuation: null,
            } rows
            || population.Composition is not { } composition
            || population.SelectorCounts is not { } selectorCounts)
        {
            return new(
                BrowserTypeOverviewDocumentDeclarationsOutcome.Incomplete,
                "The Type overview document did not contain the complete requested "
                    + "declaration inventory.",
                null);
        }

        return new(
            BrowserTypeOverviewDocumentDeclarationsOutcome.Available,
            null,
            new(
                population.Binding.Spelling.ToString(),
                population.Binding.Accessibility.ToString(),
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
                                Kind(count.Kind),
                                count.Count)),
                    ],
                    new(
                        selectorCounts.Traits.All,
                        selectorCounts.Traits.Static,
                        selectorCounts.Traits.Instance,
                        selectorCounts.Traits.Virtual,
                        selectorCounts.Traits.Interface,
                        selectorCounts.Traits.Extensions)),
                [
                    .. rows.Items.Select(group =>
                        new BrowserTypeMemberPopulationGroup(
                            $"{Kind(group.Binding.Category)}:"
                                + group.Binding.Name,
                            group.Binding.Name.ToString(),
                            DisplayName(group),
                            Kind(group.Binding.Category),
                            group.BaselineOrdinal,
                            Receivers(group.Receivers),
                            new(
                                group.Traits.All,
                                group.Traits.Static,
                                group.Traits.Instance,
                                group.Traits.Virtual,
                                group.Traits.Interface,
                                group.Traits.Extensions),
                            group.ExactMemberCount
                                ?? throw new InvalidOperationException(
                                    "The Browser Type overview document requested exact "
                                        + "Member counts."))),
                ]));
    }

    private static string DisplayName(TypeMemberGroupShape group)
    {
        string name = group.Binding.Name.ToString();
        return group.SharedGenericParameters is { Length: > 0 } parameters
            ? $"{name}<{string.Join(
                ", ",
                parameters.Select(parameter => parameter.ToString()))}>"
            : name;
    }

    private static string Kind(MemberGroupCategory category) =>
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

    private static string[] Receivers(MemberGroupReceiverForms receivers) =>
    [
        .. new[]
        {
            (MemberGroupReceiverForms.Static, "static"),
            (MemberGroupReceiverForms.This, "this"),
            (MemberGroupReceiverForms.Extension, "extension"),
        }
        .Where(candidate => receivers.HasFlag(candidate.Item1))
        .Select(candidate => candidate.Item2),
    ];

    private static BrowserTypeOverviewDocumentInspection Failed(
        string detail,
        InspectionShare share,
        string[] diagnostics) =>
        new(
            BrowserTypeOverviewDocumentOutcome.Failed,
            detail,
            null,
            share,
            diagnostics);

    private static TypeMemberGroupSpelling ParseSpelling(string spelling) =>
        spelling.Trim().ToLowerInvariant() switch
        {
            "csharp" or "c#" => TypeMemberGroupSpelling.CSharp,
            "metadata" => TypeMemberGroupSpelling.Metadata,
            _ => throw new ArgumentException(
                "Spelling must be 'csharp' or 'metadata'.",
                nameof(spelling)),
        };

    private static TypeMemberGroupAccessibilityFilter ParseAccessibility(
        string accessibility) =>
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

    private static string SerializeTypeOverviewDocument(
        BrowserTypeOverviewDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserTypeOverviewDocumentInspection);
}
