using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TsJsExport;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = false)]
public sealed class JsExportRootAttribute(Type rootType) : Attribute
{
    public Type RootType { get; } = rootType;
}

[SupportedOSPlatform("browser")]
public static partial class WrongContractExports
{
    [JSExport]
    public static string Echo(string value) => value;
}

[JsExportRoot(typeof(WrongContractExports))]
public sealed class WrongContractContext;
