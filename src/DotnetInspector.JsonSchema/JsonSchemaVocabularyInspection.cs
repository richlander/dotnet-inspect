using DotnetInspector.JsonSchema;
using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace DotnetInspector.JsonSchema;

public sealed record JsonSchemaVocabularyInspectionRequest(
    JsonSchemaContractIdentity Contract,
    JsonWireDirection Direction,
    VocabularyCatalogIdentity VocabularyCatalog,
    VocabularySnapshotIdentity VocabularySnapshotIdentity);

public sealed class JsonSchemaVocabularyInspection
{
    readonly IReadOnlyList<DeclaredJsonSchemaVocabularyDescriptor>
        _declarations;

    public JsonSchemaVocabularyInspection(
        JsExportSurface surface,
        JsonWireDeclarationPlan declarationPlan,
        ApiAssemblyIdentity jsonContractIdentity,
        VocabularySnapshotReference vocabularySnapshot)
    {
        _declarations =
            DeclaredJsonSchemaVocabularyDescriptorBuilder.Build(
                surface,
                declarationPlan,
                jsonContractIdentity,
                vocabularySnapshot);
        Requests = [
            .. _declarations.Select(declaration =>
                new JsonSchemaVocabularyInspectionRequest(
                    declaration.Descriptor.Contract,
                    declaration.Descriptor.Direction,
                    declaration.Descriptor.VocabularyCatalog,
                    declaration.Descriptor.VocabularySnapshotIdentity)),
        ];
    }

    public IReadOnlyList<JsonSchemaVocabularyInspectionRequest> Requests
        { get; }

    public InspectionEnvelope<JsonSchemaVocabularyDescriptor> Inspect(
        JsonSchemaVocabularyInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeclaredJsonSchemaVocabularyDescriptor? declaration =
            _declarations.SingleOrDefault(candidate =>
                candidate.Descriptor.Contract == request.Contract
                && candidate.Descriptor.Direction == request.Direction);
        if (declaration is null)
        {
            throw new JsonSchemaVocabularyException(
                request.Contract.Value,
                "no authenticated declaration matches the requested "
                    + "contract and direction");
        }

        JsonSchemaVocabularyDescriptor descriptor =
            declaration.Descriptor;
        if (descriptor.VocabularyCatalog
                != request.VocabularyCatalog)
        {
            throw new JsonSchemaVocabularyException(
                request.Contract.Value,
                "the requested vocabulary catalog does not match the "
                    + "authenticated declaration");
        }
        if (descriptor.VocabularySnapshotIdentity
                != request.VocabularySnapshotIdentity)
        {
            throw new JsonSchemaVocabularyException(
                request.Contract.Value,
                "the requested vocabulary snapshot does not match the "
                    + "authenticated declaration");
        }

        string direction = request.Direction switch
        {
            JsonWireDirection.Serialize => "serialize",
            JsonWireDirection.Deserialize => "deserialize",
            _ => throw new JsonSchemaVocabularyException(
                request.Contract.Value,
                "the requested direction must be serialize or deserialize"),
        };
        return new(
            descriptor,
            new InspectionShare.NonProjectable(
                $"json-schema-vocabulary/{request.Contract.Value}/{direction}",
                "Build-time schema descriptors do not have a portable "
                    + "Workspace projection."));
    }
}
