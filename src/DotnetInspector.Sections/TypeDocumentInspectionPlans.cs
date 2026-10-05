using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static class TypeDocumentInspectionPlans
{
    public static TypeDocumentInspectionPlan DeclaredMemberRows(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        bool includeHidden,
        int maximumRows) =>
        new(
            type,
            bounds,
            new(
                count: null,
                rows: new(
                    maximumRows,
                    includeExactMemberCount: true),
                composition: new(),
                selectorCounts: new(),
                spelling,
                accessibility,
                TypeMemberGroupReceiverFilter.All,
                includeHidden));

    public static TypeDocumentInspectionPlan DeclaredMemberCount(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        bool includeHidden) =>
        new(
            type,
            bounds,
            new(
                count: null,
                rows: null,
                composition: new(),
                spelling: spelling,
                accessibility: accessibility,
                receiver: TypeMemberGroupReceiverFilter.All,
                includeHidden: includeHidden));
}
