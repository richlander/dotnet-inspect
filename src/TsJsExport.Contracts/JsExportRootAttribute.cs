namespace TsJsExport;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = false)]
public sealed class JsExportRootAttribute(Type rootType) : Attribute
{
    public Type RootType { get; } = rootType;
}
