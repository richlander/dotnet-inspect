namespace TsJsExport;

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
