using Markout;

namespace DotnetInspector;

[MarkoutContext(typeof(InspectionResult))]
[MarkoutContext(typeof(AssemblyAudit))]
[MarkoutContext(typeof(CliApiSurface))]
[MarkoutContext(typeof(ApiTypeView))]
[MarkoutContext(typeof(EnumValueRow))]
[MarkoutContext(typeof(TypeParameterRow))]
[MarkoutContext(typeof(InterfaceRow))]
[MarkoutContext(typeof(BaseclassRow))]
[MarkoutContext(typeof(DependencyGroup))]
[MarkoutContext(typeof(PackageDependency))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(AuditSummary))]
[MarkoutContext(typeof(RidPackageReference))]
public partial class MarkoutContext : MarkoutSerializerContext
{
}
