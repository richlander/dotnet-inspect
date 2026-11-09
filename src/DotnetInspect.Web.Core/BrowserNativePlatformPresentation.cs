using System.Runtime.Versioning;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserNativePlatformPresentation
{
    internal static string? SelectedTypeName(PortableRetainedSubjectContext context) =>
        context is PortableRetainedSubjectContext.Type selected
            ? selected.TypeIdentity.ToEscapedFullName()
            : null;

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
        AssemblyTypeDeclaration? selectedDeclaration = null;
        CompleteRestorationSubjectResult<InspectionEnvelope<LibraryInspectionOutcome>> result =
            await CompleteRestorationSelectedSubjectOperation.ExecuteAdmittedAsync(
                workspace,
                activation,
                (target, lease, token) =>
                {
                    if (target is CompleteRestorationSelectedSubject.AdmittedType selected
                        && !selected.Declaration.IsPublicSurface)
                    {
                        selectedDeclaration = selected.Declaration;
                    }
                    return ValueTask.FromResult(LibraryInspectionOperation.Execute(
                        new(target.Occurrence.Library,
                            new(BrowserLibraryTypeHierarchy.NamePlan().Types,
                                BrowserApiSurfacePolicy.LibraryBounds)),
                        lease,
                        token));
                },
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
        BrowserTypeSurfaceInfo[] types =
        [
            .. rows.Items.Where(static row =>
                row.DeclarationKind == LibraryTypeDeclarationKind.Definition)
                .Select(row =>
                {
                    string kind = row.DefinitionKind!.Value.ToString().ToLowerInvariant();
                    return TypeSurface(row.Identity, row.DisplayName.ToString(), kind, true);
                }),
            .. selectedDeclaration is { } selectedType
                ? new[] { TypeSurface(selectedType.Name,
                    string.Join(".", selectedType.Name.Segments),
                    selectedType.DefinitionKind switch
                    {
                        AssemblyTypeDefinitionKind.ValueType => "struct",
                        { } kind => kind.ToString().ToLowerInvariant(),
                        _ => throw new InvalidOperationException(
                            "The selected definition has no Type kind."),
                    }, false) }
                : [],
        ];
        var kinds = types.GroupBy(static type => type.Kind)
            .Select(static group => ApiInventoryQuery.DescribeTypeKind(group.Key, group.Count()))
            .OrderBy(static kind => kind.Weight);
        int publicTypes = types.Count(static type => type.AccessibilityId == "public");
        BrowserAssemblySurfaceInfo assembly = new(
            assemblyId, assemblyName, document.Assembly.Version.ToString(),
            document.Assembly.Culture?.ToString(),
            document.Assembly.PublicKeyToken?.ToString(),
            asset, publicTypes, PublicMembers: null, pack);
        BrowserPackageSurfaceInfo surface = new(
            BrowserPlatformIdentity.PackageName, target.Version.Value,
            [framework], framework, Icon: null, assemblyId,
            BrowserCompileLibraryProjection.Selected(framework),
            [assembly], types,
            [.. kinds.Select(BrowserSurfaceProjection.Descriptor)],
            [], [
                new("public", "Public", 100, true, publicTypes),
                .. selectedDeclaration is not null
                    ? new BrowserAccessibilityInfo[] { new("nonpublic", "Non-public", 50, false, 1) }
                    : [],
            ],
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

        BrowserTypeSurfaceInfo TypeSurface(
            MetadataTypeDefinitionName name, string displayName, string kind, bool isPublic)
        {
            string identity = name.ToEscapedFullName();
            string accessibility = isPublic ? "public" : "nonpublic";
            return new(
                $"{assemblyName}:{identity}", identity, identity, identity,
                displayName, displayName, name.Namespace,
                kind, ApiInventoryQuery.DescribeTypeKind(kind, 0).Id, [],
                accessibility, accessibility, asset, assemblyId, assemblyName,
                Members: null, Signature: "", Api: [], PlatformPack: pack);
        }
    }
}
