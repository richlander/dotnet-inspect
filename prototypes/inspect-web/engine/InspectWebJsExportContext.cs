using TsJsExport;

namespace InspectWeb.Engine;

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
[JsExportRoot(typeof(global::InspectionEngine))]
[JsExportRoot(typeof(global::PackageExports))]
[JsExportRoot(typeof(global::MetadataExports))]
[JsExportRoot(typeof(global::AnalysisExports))]
[JsExportRoot(typeof(global::SourceExports))]
[JsExportRoot(typeof(global::CallGraphExports))]
[JsExportRoot(typeof(global::CatalogExports))]
internal sealed class InspectWebJsExportContext;
