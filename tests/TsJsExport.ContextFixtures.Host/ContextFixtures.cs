extern alias WrongContractFixture;

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using TsJsExport.ContextFixtures.Alpha;
using TsJsExport.ContextFixtures.Beta;

namespace TsJsExport.ContextFixtures.Host;

[SupportedOSPlatform("browser")]
public static partial class HostExports
{
    [JSExport]
    public static string IdentifyHost(string value) => $"host:{value}";
}

[JsExportRoot(typeof(HostExports))]
[JsExportRoot(typeof(AlphaExports))]
[JsExportRoot(typeof(BetaExports))]
public sealed class MultiAssemblyContext;

[JsExportRoot(typeof(AlphaExports))]
public sealed class AlphaOnlyContext;

[JsExportRoot(typeof(AlphaExports))]
[JsExportRoot(typeof(AlphaSecondaryAnchor))]
public sealed class DuplicateAssemblyContext;

[JsExportRoot(typeof(JsExportRootAttribute))]
public sealed class EmptySurfaceContext;

public sealed class GenericAnchor<T>;

[JsExportRoot(typeof(GenericAnchor<>))]
public sealed class GenericRootContext;

[WrongContractFixture::TsJsExport.JsExportRoot(typeof(HostExports))]
public sealed class WrongContractIdentityContext;
