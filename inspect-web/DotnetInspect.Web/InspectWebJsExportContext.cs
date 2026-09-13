using TsJsExport;

namespace DotnetInspect.Web;

/// <summary>
/// The compiled production facade recipe. Its roots are the exact seven managed export assemblies
/// the browser application composes; <c>ts-jsexport --context</c> executes it once and emits one
/// TypeScript module per root.
/// </summary>
/// <remarks>
/// Each type is an assembly anchor under the generator-owned root meaning; it does not filter that
/// assembly's export surface. The host already references every capability export assembly, so the
/// context adds no reverse or sibling dependency. Attribute order is explanatory only.
/// <c>ProductionFacadeContext_DeclaresExactAssemblySet</c> gates the declared set.
/// </remarks>
[JsExportRoot(typeof(InspectionEngine))]
[JsExportRoot(typeof(Interop.Package.PackageExports))]
[JsExportRoot(typeof(Interop.Metadata.MetadataExports))]
[JsExportRoot(typeof(Interop.Analysis.AnalysisExports))]
[JsExportRoot(typeof(Interop.Source.SourceExports))]
[JsExportRoot(typeof(Interop.CallGraph.CallGraphExports))]
[JsExportRoot(typeof(Interop.Catalog.CatalogExports))]
internal sealed class InspectWebJsExportContext;
