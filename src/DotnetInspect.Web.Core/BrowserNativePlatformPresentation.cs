using System.Runtime.Versioning;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserNativePlatformPresentation
{
    internal static CompleteRestorationExecutionOptions BindAdmission(
        CompleteRestorationExecutionOptions options,
        CompleteRestorationRecipe recipe)
    {
        PlatformLibraryDemand demand = recipe switch
        {
            CompleteRestorationRecipe.Version6 native =>
                ExactDemand(native.Definitions.View!.States[1].Context switch
                {
                    PortableRetainedSubjectContext.Library library =>
                        library.LibraryIdentity,
                    PortableRetainedSubjectContext.Type type =>
                        type.LibraryIdentity,
                    _ => throw new InvalidOperationException(
                        "Native restoration requires exact Library context."),
                }),
            CompleteRestorationRecipe.LegacyPlatformUpgrade legacy =>
                new PlatformLibraryDemand.AssemblyName(
                    Path.GetFileNameWithoutExtension(legacy.AssetFileName)),
            _ => throw new InvalidOperationException(
                "The Browser native adopter requires native restoration."),
        };
        return options with
        {
            AdmitPlatform = (workspace, target, token) =>
                RuntimePlatformLibraryAdmission.AdmitAsync(
                    workspace, target, demand, options.ContextLoad, token),
        };
    }

    private static PlatformLibraryDemand ExactDemand(PortableLibraryIdentity identity) =>
        new PlatformLibraryDemand.Assembly(new ILInspector.Metadata.AssemblyReferenceIdentity(
            identity.Name, Version.Parse(identity.Version),
            identity.Culture, identity.PublicKeyToken));

    internal static async ValueTask<BrowserRetainedWorkspacePostingDraft> CreateAsync(
        BrowserRetainedWorkspaceActivationRequest request,
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        CompleteRestorationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        CompleteRestorationSubjectResult<InspectionEnvelope<LibraryInspectionOutcome>> result =
            await CompleteRestorationSelectedSubjectOperation.ExecuteAdmittedAsync(
                workspace,
                activation,
                (target, lease, token) => ValueTask.FromResult(
                    LibraryInspectionOperation.Execute(
                        new(target.Occurrence.Library,
                            new(BrowserLibraryTypeHierarchy.NamePlan().Types,
                                BrowserApiSurfacePolicy.LibraryBounds)),
                        lease,
                        token)),
                cancellationToken).ConfigureAwait(false);
        if (result is not CompleteRestorationSubjectResult<
            InspectionEnvelope<LibraryInspectionOutcome>>.Completed completed
            || completed.Content.Content is not LibraryInspectionOutcome.Available available
            || available.Document.Types?.Rows is not LibraryTypePopulationRowsOutcome.Read
            {
                Continuation: null,
            } rows)
        {
            throw new InvalidOperationException(
                "The retained Runtime Library has no completed Name presentation.");
        }
        WorkspaceDeclarationContext context = activation.SelectedContext
            ?? throw new InvalidOperationException(
                "Native restoration did not retain its exact declaration context.");
        var definition = ((CompleteRestorationResolvedState.Version6)
            activation.Snapshot.Resolved).Definitions;
        var target = ((WorkspaceDeclarationRequest.PlatformPopulation)
            context.Receipt.Request).Target;
        string framework = target.TargetFramework.ToString();
        string pack = BrowserPlatformWorkspace.Pack(BrowserPlatformWorkspace.RuntimeFamily);
        LibraryDocument document = available.Document;
        string assemblyName = document.Assembly.Name.ToString();
        string asset = $"{assemblyName}.dll";
        string assemblyId = assemblyName;
        var kinds = rows.Items
            .Where(static row =>
                row.DeclarationKind == LibraryTypeDeclarationKind.Definition)
            .GroupBy(static row =>
                row.DefinitionKind!.Value.ToString().ToLowerInvariant())
            .ToDictionary(static group => group.Key,
                static group => ApiInventoryQuery.DescribeTypeKind(
                    group.Key, group.Count()), StringComparer.Ordinal);
        BrowserTypeSurfaceInfo[] types =
        [
            .. rows.Items.Where(static row =>
                row.DeclarationKind == LibraryTypeDeclarationKind.Definition)
                .Select(row =>
                {
                    string identity = row.Identity.ToEscapedFullName();
                    string kind = row.DefinitionKind!.Value.ToString().ToLowerInvariant();
                    return new BrowserTypeSurfaceInfo(
                        $"{assemblyName}:{identity}", identity, identity, identity,
                        row.DisplayName.ToString(), row.DisplayName.ToString(),
                        row.Namespace.ToString(), kind, kinds[kind].Id, [],
                        "public", "public", asset, assemblyId, assemblyName,
                        Members: null, Signature: "", Api: [], PlatformPack: pack);
                }),
        ];
        BrowserAssemblySurfaceInfo assembly = new(
            assemblyId, assemblyName, document.Assembly.Version.ToString(),
            document.Assembly.Culture?.ToString(),
            document.Assembly.PublicKeyToken?.ToString(),
            asset, types.Length, PublicMembers: null, pack);
        BrowserPackageSurfaceInfo surface = new(
            BrowserPlatformIdentity.PackageName, target.Version.Value,
            [framework], framework, Icon: null, assemblyId,
            BrowserCompileLibraryProjection.Selected(framework),
            [assembly], types,
            [.. kinds.Values.OrderBy(static kind => kind.Weight)
                .Select(BrowserSurfaceProjection.Descriptor)],
            [], [new("public", "Public", 100, true, types.Length)],
            TotalMembers: null,
            Documents: [],
            InspectionErrors: [.. completed.Content.Diagnostics.Select(
                static diagnostic => diagnostic.Summary.ToString())],
            InspectionError: null);
        return new(
            request.RetainedDefinitionId, request.Label, request.CanonicalLocation,
            new BrowserRetainedWorkspaceOrigin.Restored(
                activation.Request, activation.Projection, definition),
            activation.Snapshot.Navigation, [],
            [new("t0", context.Receipt.Order, BrowserPlatformWorkspace.RuntimeFamily,
                RuntimeIdentifier: null, surface, activation)],
            BrowserRetainedNavigationPreparation.Create(options));
    }
}
