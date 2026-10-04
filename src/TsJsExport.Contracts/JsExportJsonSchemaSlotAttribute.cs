namespace TsJsExport;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class JsExportJsonSchemaSlotAttribute(
    int order,
    string nodeIdentity,
    string vocabulary,
    string term) : Attribute
{
    public int Order { get; } = order;

    public string NodeIdentity { get; } = nodeIdentity;

    public string Vocabulary { get; } = vocabulary;

    public string Term { get; } = term;

    public bool Displayable { get; init; } = true;
}
