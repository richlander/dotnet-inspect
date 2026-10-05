using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryTypeMemberPopulation(
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
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
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
        return SerializeTypeMemberPopulation(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeMemberPopulation(
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
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
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
        return SerializeTypeMemberPopulation(inspection);
    }

    [JSExport]
    public static async Task<string>
        QueryUploadedLibraryTypeMemberPopulation(
            string declaredName,
            byte[] content,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
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
        return SerializeTypeMemberPopulation(inspection);
    }

    private static async Task<BrowserTypeMemberPopulationInspection>
        ExecuteTypeMemberPopulationAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        TypeDocumentInspectionPlan plan =
            TypeDocumentInspectionPlans.DeclaredMemberRows(
                BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity),
                BrowserExactMemberPolicy.Bounds,
                ParseSpelling(spelling),
                ParseAccessibility(accessibility),
                includeHidden: false,
                BrowserExactMemberPolicy.Bounds.MaxMembers);
        InspectionEnvelope<TypeDocumentInspectionOutcome> inspection =
            await BrowserTypeDocumentExecution.ExecuteAsync(
                    materialization,
                    plan)
                .ConfigureAwait(false);
        return ProjectTypeMemberPopulation(inspection);
    }

    private static BrowserTypeMemberPopulationInspection
        ProjectTypeMemberPopulation(
            InspectionEnvelope<TypeDocumentInspectionOutcome>
                inspection)
    {
        string[] diagnostics =
        [
            .. inspection.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Summary}"),
        ];
        return inspection.Content switch
        {
            TypeDocumentInspectionOutcome.Available available =>
                ProjectAvailable(available.Document, diagnostics),
            TypeDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserTypeMemberPopulationOutcome.Rejected,
                    $"The Type document was rejected ({rejected.Reason}).",
                    null,
                    diagnostics),
            TypeDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserTypeMemberPopulationOutcome.Incomplete,
                    $"The Type document reached {incomplete.Bound} "
                        + $"({incomplete.Measured} > {incomplete.Limit}).",
                    null,
                    diagnostics),
            TypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    $"The Type document failed ({failed.Reason}).",
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Type document outcome."),
        };
    }

    private static BrowserTypeMemberPopulationInspection ProjectAvailable(
        TypeDocument document,
        string[] diagnostics)
    {
        if (document.Declarations
            is not TypeDocumentDeclarations.Available available)
        {
            return document.Declarations switch
            {
                TypeDocumentDeclarations.Rejected rejected =>
                    new(
                        BrowserTypeMemberPopulationOutcome.Rejected,
                        $"The Type Member population was rejected "
                            + $"({rejected.Reason}).",
                        null,
                        diagnostics),
                TypeDocumentDeclarations.Incomplete incomplete =>
                    new(
                        BrowserTypeMemberPopulationOutcome.Incomplete,
                        $"The Type Member population reached "
                            + $"{incomplete.Bound} "
                            + $"({incomplete.Measured} > "
                            + $"{incomplete.Limit}).",
                        null,
                        diagnostics),
                TypeDocumentDeclarations.Failed failed =>
                    Failed(
                        $"The Type Member population failed "
                            + $"({failed.Reason}).",
                        diagnostics),
                TypeDocumentDeclarations.NotRequested =>
                    Failed(
                        "The Type Member population was not requested.",
                        diagnostics),
                _ => throw new InvalidOperationException(
                    "Unknown Type declaration population outcome."),
            };
        }
        TypeMemberGroupPopulationResult population =
            available.Population;
        if (population.Rows
                is not TypeMemberGroupRowsOutcome.Read rows
            || !rows.IsComplete
            || population.Composition is not { } composition
            || population.SelectorCounts is not { } selectorCounts)
        {
            return population.Rows switch
            {
                TypeMemberGroupRowsOutcome.Rejected rejected =>
                    new(
                        BrowserTypeMemberPopulationOutcome.Rejected,
                        $"The Type Member rows were rejected "
                            + $"({rejected.Reason}).",
                        null,
                        diagnostics),
                TypeMemberGroupRowsOutcome.Incomplete incomplete =>
                    new(
                        BrowserTypeMemberPopulationOutcome.Incomplete,
                        $"The Type Member rows reached "
                            + $"{incomplete.Bound} "
                            + $"({incomplete.Measured} > "
                            + $"{incomplete.Limit}).",
                        null,
                        diagnostics),
                TypeMemberGroupRowsOutcome.Failed failed =>
                    Failed(
                        $"The Type Member rows failed ({failed.Reason}).",
                        diagnostics),
                TypeMemberGroupRowsOutcome.Read =>
                    new(
                        BrowserTypeMemberPopulationOutcome.Incomplete,
                        "The Type Member rows did not contain the complete "
                            + "population.",
                        null,
                        diagnostics),
                _ => Failed(
                    "The Type Member population omitted requested rows or "
                        + "Counts.",
                    diagnostics),
            };
        }

        return new(
            BrowserTypeMemberPopulationOutcome.Available,
            null,
            new(
                document.Subject.Type.ToEscapedFullName(),
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
                    .. rows.Items.Select(row =>
                        new BrowserTypeMemberPopulationGroup(
                            $"{Kind(row.Binding.Category)}:"
                                + row.Binding.Name,
                            row.Binding.Name.ToString(),
                            Kind(row.Binding.Category),
                            row.ExactMemberCount
                                ?? throw new InvalidOperationException(
                                    "The Browser Type Member population "
                                        + "requires nested exact-member "
                                        + "Counts."),
                            Receivers(row.Receivers))),
                ]),
            diagnostics);
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
            _ => throw new InvalidOperationException(
                $"Unknown Member-group category '{category}'."),
        };

    private static string[] Receivers(
        MemberGroupReceiverForms receivers)
    {
        List<string> result = [];
        if ((receivers & MemberGroupReceiverForms.This) != 0)
            result.Add("This");
        if ((receivers & MemberGroupReceiverForms.Static) != 0)
            result.Add("Static");
        if ((receivers & MemberGroupReceiverForms.Extension) != 0)
            result.Add("Extension");
        return [.. result];
    }

    private static BrowserTypeMemberPopulationInspection Failed(
        string detail,
        string[] diagnostics) =>
        new(
            BrowserTypeMemberPopulationOutcome.Failed,
            detail,
            null,
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
            "protected" =>
                TypeMemberGroupAccessibilityFilter.Protected,
            "internal" =>
                TypeMemberGroupAccessibilityFilter.Internal,
            "private" => TypeMemberGroupAccessibilityFilter.Private,
            _ => throw new ArgumentException(
                "Accessibility must be all, public, protected, internal, or private.",
                nameof(accessibility)),
        };

    private static string SerializeTypeMemberPopulation(
        BrowserTypeMemberPopulationInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserTypeMemberPopulationInspection);
}
