namespace TsJsExport;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class JsExportJsonSchemaAttribute(
    string contractIdentity,
    string direction,
    string vocabularyCatalog,
    string vocabularySnapshotIdentity) : Attribute
{
    public string ContractIdentity { get; } = contractIdentity;

    public string Direction { get; } = direction;

    public string VocabularyCatalog { get; } = vocabularyCatalog;

    public string VocabularySnapshotIdentity { get; } =
        vocabularySnapshotIdentity;
}
