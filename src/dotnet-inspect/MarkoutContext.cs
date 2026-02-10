using DotnetInspector.Models;
using DotnetInspector.Views;
using DotnetInspector.Packages;
using Markout;

namespace DotnetInspector;

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(InspectionResultView))]
[MarkoutContext(typeof(AssemblyAuditView))]
[MarkoutContext(typeof(AssemblyAuditReport))]
[MarkoutContext(typeof(ReferenceRow))]
[MarkoutContext(typeof(ExtensionMethodRow))]
[MarkoutContext(typeof(CliApiSurface))]
[MarkoutContext(typeof(ApiTypeView))]
[MarkoutContext(typeof(EnumValueRow))]
[MarkoutContext(typeof(TypeParameterRow))]
[MarkoutContext(typeof(InterfaceRow))]
[MarkoutContext(typeof(BaseclassRow))]
[MarkoutContext(typeof(DependencyGroup))]
[MarkoutContext(typeof(PackageDependency))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(RidPackageReferenceView))]
public partial class MarkoutContext : MarkoutSerializerContext
{
}
