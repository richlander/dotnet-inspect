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
    nameof(MetadataExports.QueryTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryTypeDocument(
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
        BrowserTypeDocumentInspection inspection =
            await ExecuteTypeDocumentAsync(
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
        return SerializeTypeDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeDocument(
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
        BrowserTypeDocumentInspection inspection =
            await ExecuteTypeDocumentAsync(
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
        return SerializeTypeDocument(inspection);
    }

    [JSExport]
    public static async Task<string>
        QueryUploadedLibraryTypeDocument(
            string declaredName,
            byte[] content,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserTypeDocumentInspection inspection =
            await ExecuteTypeDocumentAsync(
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
        return SerializeTypeDocument(inspection);
    }

    private static async Task<BrowserTypeDocumentInspection>
        ExecuteTypeDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        MetadataTypeDefinitionName type =
            BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity);
        TypeDocumentInspectionPlan plan =
            TypeDocumentInspectionPlans.DeclaredMemberRows(
                type,
                BrowserExactMemberPolicy.Bounds,
                ParseSpelling(spelling),
                ParseAccessibility(accessibility),
                includeHidden: false,
                BrowserExactMemberPolicy.Bounds.MaxMembers);
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<TypeDocumentInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                    materialization,
                    (reference, owner) =>
                        AssemblyContextLibraryInspection.ExecuteOperation(
                            reference,
                            owner,
                            lease =>
                                TypeDocumentInspectionOperation.Execute(
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
                "The exact Library owner could not issue the Type document lease.";
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
        return ProjectTypeDocument(
            envelope.Content,
            envelope.Share,
            diagnostics);
    }

    private static BrowserTypeDocumentInspection ProjectTypeDocument(
        TypeDocumentInspectionOutcome outcome,
        InspectionShare share,
        string[] diagnostics) =>
        outcome switch
        {
            TypeDocumentInspectionOutcome.Available available =>
                ProjectAvailable(available.Document, share, diagnostics),
            TypeDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserTypeDocumentOutcome.Rejected,
                    $"The Type document was rejected ({rejected.Reason}).",
                    null,
                    share,
                    diagnostics),
            TypeDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserTypeDocumentOutcome.Incomplete,
                    $"The Type document reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null,
                    share,
                    diagnostics),
            TypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    $"The Type document failed ({failed.Reason}).",
                    share,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Type document outcome."),
        };

    private static BrowserTypeDocumentInspection ProjectAvailable(
        TypeDocument document,
        InspectionShare share,
        string[] diagnostics) =>
        new(
            BrowserTypeDocumentOutcome.Available,
            null,
            new(
                document.Subject.Type.ToEscapedFullName(),
                document.Subject.TypeDefinitionToken,
                document.Subject.Category.ToString(),
                document.Subject.IsByRefLike,
                [
                    .. document.Subject.Signature.GenericParameters.Select(
                        parameter =>
                            new BrowserTypeDocumentGenericParameter(
                                parameter.Name.ToString(),
                                parameter.MetadataIndex,
                                parameter.DefinitionSegmentIndex,
                                parameter.Attributes.ToString())),
                ],
                ProjectDeclarations(document.Declarations)),
            share,
            diagnostics);

    private static BrowserTypeDocumentDeclarations ProjectDeclarations(
        TypeDocumentDeclarations declarations) =>
        declarations switch
        {
            TypeDocumentDeclarations.NotRequested =>
                new(
                    BrowserTypeDocumentDeclarationsOutcome.NotRequested,
                    null,
                    null),
            TypeDocumentDeclarations.Available available =>
                ProjectAvailableDeclarations(available.Population),
            TypeDocumentDeclarations.Rejected rejected =>
                new(
                    BrowserTypeDocumentDeclarationsOutcome.Rejected,
                    $"The declaration population was rejected ({rejected.Reason}).",
                    null),
            TypeDocumentDeclarations.Incomplete incomplete =>
                new(
                    BrowserTypeDocumentDeclarationsOutcome.Incomplete,
                    $"The declaration population reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null),
            TypeDocumentDeclarations.Failed failed =>
                new(
                    BrowserTypeDocumentDeclarationsOutcome.Failed,
                    $"The declaration population failed ({failed.Reason}).",
                    null),
            _ => throw new InvalidOperationException(
                "Unknown Type document declarations outcome."),
        };

    private static BrowserTypeDocumentDeclarations
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
                BrowserTypeDocumentDeclarationsOutcome.Incomplete,
                "The Type document did not contain the complete requested "
                    + "declaration inventory.",
                null);
        }

        return new(
            BrowserTypeDocumentDeclarationsOutcome.Available,
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
                                    "The Browser Type document requested exact "
                                        + "Member counts."))),
                ]));
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

    private static BrowserTypeDocumentInspection Failed(
        string detail,
        InspectionShare share,
        string[] diagnostics) =>
        new(
            BrowserTypeDocumentOutcome.Failed,
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

    private static string SerializeTypeDocument(
        BrowserTypeDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserTypeDocumentInspection);
}
