using Markout;

namespace DotnetInspector;

[MarkoutContext(typeof(InspectionResult))]
[MarkoutContext(typeof(AssemblyAudit))]
[MarkoutContext(typeof(AssemblyInfo))]
[MarkoutContext(typeof(ApiSurface))]
[MarkoutContext(typeof(ApiType))]
[MarkoutContext(typeof(ApiMember))]
[MarkoutContext(typeof(DependencyGroup))]
[MarkoutContext(typeof(PackageDependency))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(AuditSummary))]
[MarkoutContext(typeof(RidPackageReference))]
public partial class MarkoutContext : MarkoutSerializerContext
{
}
