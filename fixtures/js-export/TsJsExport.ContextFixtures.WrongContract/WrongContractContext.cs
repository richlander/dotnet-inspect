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

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = false)]
public sealed class JsExportJsonInputAttribute(
    string methodName,
    string parameterName,
    Type wireType) : Attribute
{
    public string MethodName { get; } = methodName;

    public string ParameterName { get; } = parameterName;

    public Type WireType { get; } = wireType;
}

[SupportedOSPlatform("browser")]
[JsExportJsonInput(
    nameof(WrongContractExports.Echo),
    "value",
    typeof(string))]
public static partial class WrongContractExports
{
    [JSExport]
    public static string Echo(string value) => value;
}

[JsExportRoot(typeof(WrongContractExports))]
public sealed class WrongContractContext;
