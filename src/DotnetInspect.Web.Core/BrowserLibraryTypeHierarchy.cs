using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

internal static class BrowserLibraryTypeHierarchy
{
    internal static (
        LibraryTypePopulationRequest Types,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> Hierarchy)
        NamePlan()
    {
        LibraryTypeHierarchyPresentationPlan plan =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        return (plan.Types, plan.Hierarchy);
    }

    internal static (string DefinitionId, string? DefinitionKind)
        DeclarationIdentity(LibraryTypeShape declaration) =>
        (
            declaration.Identity.ToEscapedFullName(),
            declaration.DefinitionKind?.ToString()
        );
}
