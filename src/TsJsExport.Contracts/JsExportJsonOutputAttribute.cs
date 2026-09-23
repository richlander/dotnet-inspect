namespace TsJsExport;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = false)]
public sealed class JsExportJsonOutputAttribute(
    string methodName,
    Type wireType) : Attribute
{
    public string MethodName { get; } = methodName;

    public Type WireType { get; } = wireType;
}
