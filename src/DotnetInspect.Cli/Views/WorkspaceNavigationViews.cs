using DotnetInspector.Queries;
using Markout;

using static DotnetInspect.Cli.Views.WorkspaceNavigationViewText;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description))]
public sealed class WorkspaceNavigationView
{
    [MarkoutIgnore]
    public string Title => "Workspace";

    [MarkoutIgnore]
    public string? Description
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutSection(Name = "Navigation")]
    public List<WorkspaceNavigationSummaryRow> Navigation { get; init; } = [];

    [MarkoutSection(Name = "Packages")]
    public List<WorkspaceNavigationPackageRow> Packages { get; init; } = [];

    [MarkoutSection(Name = "Hierarchy")]
    public List<WorkspaceNavigationHierarchyRow> Hierarchy { get; init; } = [];

    [MarkoutSection(Name = "Libraries")]
    public List<WorkspaceNavigationLibraryRow> Libraries { get; init; } = [];

    [MarkoutSection(Name = "Types")]
    public List<WorkspaceNavigationTypeRow> Types { get; init; } = [];

    [MarkoutSection(Name = "Members")]
    public List<WorkspaceNavigationMemberRow> Members { get; init; } = [];

    [MarkoutSection(Name = "Lenses")]
    public List<WorkspaceNavigationLensRow> Lenses { get; init; } = [];

    [MarkoutSection(Name = "Diagnostics")]
    public List<WorkspaceNavigationDiagnosticRow> Diagnostics { get; init; } = [];
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationSummaryRow
{
    public WorkspaceNavigationSummaryRow(
        string activeSubject,
        string activeKind,
        string lens,
        string lensState,
        string typeInventoryContext)
    {
        ActiveSubject = Contain(activeSubject);
        ActiveKind = Contain(activeKind);
        Lens = Contain(lens);
        LensState = Contain(lensState);
        TypeInventoryContext = Contain(typeInventoryContext);
    }

    public string ActiveSubject { get; }
    public string ActiveKind { get; }
    public string Lens { get; }
    public string LensState { get; }
    public string TypeInventoryContext { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationPackageRow
{
    public WorkspaceNavigationPackageRow(
        int order,
        string package,
        string version,
        string framework,
        string state,
        bool active)
    {
        Order = order;
        Package = Contain(package);
        Version = Contain(version);
        Framework = Contain(framework);
        State = Contain(state);
        Active = active;
    }

    public int Order { get; }
    public string Package { get; }
    public string Version { get; }
    public string Framework { get; }
    public string State { get; }
    public bool Active { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationHierarchyRow
{
    public WorkspaceNavigationHierarchyRow(
        string level,
        string subject,
        string state,
        bool active)
    {
        Level = Contain(level);
        Subject = Contain(subject);
        State = Contain(state);
        Active = active;
    }

    public string Level { get; }
    public string Subject { get; }
    public string State { get; }
    public bool Active { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationLibraryRow
{
    public WorkspaceNavigationLibraryRow(
        string library,
        string assetId,
        string asset,
        string state,
        bool primary,
        bool active,
        bool retained)
    {
        Library = Contain(library);
        AssetId = Contain(assetId);
        Asset = Contain(asset);
        State = Contain(state);
        Primary = primary;
        Active = active;
        Retained = retained;
    }

    public string Library { get; }
    public string AssetId { get; }
    public string Asset { get; }
    public string State { get; }
    public bool Primary { get; }
    public bool Active { get; }
    public bool Retained { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationTypeRow
{
    public WorkspaceNavigationTypeRow(
        string type,
        string library,
        string libraryAssetId,
        string accessibility,
        string state,
        bool active,
        bool retained)
    {
        Type = Contain(type);
        Library = Contain(library);
        LibraryAssetId = Contain(libraryAssetId);
        Accessibility = Contain(accessibility);
        State = Contain(state);
        Active = active;
        Retained = retained;
    }

    public string Type { get; }
    public string Library { get; }
    public string LibraryAssetId { get; }
    public string Accessibility { get; }
    public string State { get; }
    public bool Active { get; }
    public bool Retained { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationMemberRow
{
    public WorkspaceNavigationMemberRow(
        string member,
        string containingType,
        string declaringType,
        string libraryAssetId,
        string selector,
        string canonicalSignature,
        string state,
        bool active,
        bool retained)
    {
        Member = Contain(member);
        ContainingType = Contain(containingType);
        DeclaringType = Contain(declaringType);
        LibraryAssetId = Contain(libraryAssetId);
        Selector = Contain(selector);
        CanonicalSignature = Contain(canonicalSignature);
        State = Contain(state);
        Active = active;
        Retained = retained;
    }

    public string Member { get; }
    public string ContainingType { get; }
    public string DeclaringType { get; }
    public string LibraryAssetId { get; }
    public string Selector { get; }
    public string CanonicalSignature { get; }
    public string State { get; }
    public bool Active { get; }
    public bool Retained { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationLensRow
{
    public WorkspaceNavigationLensRow(
        string facet,
        string title,
        string availability,
        bool effective,
        string diagnostic)
    {
        Facet = Contain(facet);
        Title = Contain(title);
        Availability = Contain(availability);
        Effective = effective;
        Diagnostic = Contain(diagnostic);
    }

    public string Facet { get; }
    public string Title { get; }
    public string Availability { get; }
    public bool Effective { get; }
    public string Diagnostic { get; }
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationDiagnosticRow
{
    public WorkspaceNavigationDiagnosticRow(
        string scope,
        string code,
        string message)
    {
        Scope = Contain(scope);
        Code = Contain(code);
        Message = Contain(message);
    }

    public string Scope { get; }
    public string Code { get; }
    public string Message { get; }
}

[MarkoutSerializable]
public sealed class WorkspaceNavigationStreamView
{
    [MarkoutSection(Headless = true)]
    public List<WorkspaceNavigationStreamRow> Rows { get; init; } = [];
}

[MarkoutSerializable]
public sealed record WorkspaceNavigationStreamRow
{
    public WorkspaceNavigationStreamRow(
        string record,
        string kind,
        string subject,
        string parent,
        string state,
        string detail,
        bool active,
        string libraryAssetId,
        string containingType,
        string declaringType)
    {
        Record = Contain(record);
        Kind = Contain(kind);
        Subject = Contain(subject);
        Parent = Contain(parent);
        State = Contain(state);
        Detail = Contain(detail);
        Active = active;
        LibraryAssetId = Contain(libraryAssetId);
        ContainingType = Contain(containingType);
        DeclaringType = Contain(declaringType);
    }

    public string Record { get; }
    public string Kind { get; }
    public string Subject { get; }
    public string Parent { get; }
    public string State { get; }
    public string Detail { get; }
    public bool Active { get; }
    public string LibraryAssetId { get; }
    public string ContainingType { get; }
    public string DeclaringType { get; }
}

internal static class WorkspaceNavigationViewText
{
    internal static string Contain(string value) =>
        LibraryViewText.Contain(value) ?? "";
}

[MarkoutContext(typeof(WorkspaceNavigationView))]
[MarkoutContext(typeof(WorkspaceNavigationStreamView))]
public partial class WorkspaceNavigationViewContext :
    MarkoutSerializerContext;
