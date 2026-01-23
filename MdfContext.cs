using MarkdownData;

namespace DotnetInspector;

[MdfContext(typeof(InspectionResult))]
[MdfContext(typeof(AssemblyAudit))]
[MdfContext(typeof(AssemblyInfo))]
[MdfContext(typeof(ApiSurface))]
[MdfContext(typeof(ApiType))]
[MdfContext(typeof(ApiMember))]
[MdfContext(typeof(DependencyGroup))]
[MdfContext(typeof(PackageDependency))]
[MdfContext(typeof(FlatDependency))]
[MdfContext(typeof(AuditSummary))]
[MdfContext(typeof(RidPackageReference))]
public partial class MdfContext : MdfSerializerContext
{
}
