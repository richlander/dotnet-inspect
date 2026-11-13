using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>
/// Metadata-owned compatibility characteristics used by relation producers.
/// </summary>
public static class CompatibilityInspectionGraphCatalog
{
    public static InspectionGraphCharacteristicDescriptor
        RequiresUnreferencedCode
    { get; } =
        BooleanMemberCharacteristic("member.requires-unreferenced-code");

    public static InspectionGraphCharacteristicDescriptor RequiresDynamicCode
    { get; } =
        BooleanMemberCharacteristic("member.requires-dynamic-code");

    public static InspectionGraphCharacteristicDescriptor RequiresAssemblyFiles
    { get; } =
        BooleanMemberCharacteristic("member.requires-assembly-files");

    static InspectionGraphCharacteristicDescriptor BooleanMemberCharacteristic(
        string id) =>
        new(
            id,
            InspectionGraphOwner.Metadata,
            InspectionGraphValueCatalog.Boolean,
            [InspectionGraphTargetKind.Node],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);
}

/// <summary>Exact metadata receipt for one direct compatibility attribute.</summary>
public sealed record CompatibilityAttributeEvidence(
    MethodIdentity Method,
    int CustomAttributeToken,
    string AttributeTypeName);
