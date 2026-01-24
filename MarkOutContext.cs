using MarkOut;

namespace DotnetInspector;

[MarkOutContext(typeof(InspectionResult))]
[MarkOutContext(typeof(AssemblyAudit))]
[MarkOutContext(typeof(AssemblyInfo))]
[MarkOutContext(typeof(ApiSurface))]
[MarkOutContext(typeof(ApiType))]
[MarkOutContext(typeof(ApiMember))]
[MarkOutContext(typeof(DependencyGroup))]
[MarkOutContext(typeof(PackageDependency))]
[MarkOutContext(typeof(FlatDependency))]
[MarkOutContext(typeof(AuditSummary))]
[MarkOutContext(typeof(RidPackageReference))]
public partial class MarkOutContext : MarkOutSerializerContext
{
}
