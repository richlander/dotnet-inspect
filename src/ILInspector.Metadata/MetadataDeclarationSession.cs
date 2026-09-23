using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public sealed class MetadataDeclarationSession : IDisposable
{
    AssemblyInspectionSession? _assemblySession;
    MetadataOperationContext? _operationContext;
    MetadataImageAdmissionResult? _imageAdmission;
    MetadataTypeDefinitionIndex? _typeDefinitionIndex;
    bool _disposed;

    internal MetadataDeclarationSession(
        AssemblyInspectionSession assemblySession,
        MetadataOperationContext operationContext)
    {
        ArgumentNullException.ThrowIfNull(assemblySession);
        ArgumentNullException.ThrowIfNull(operationContext);

        operationContext.Attach(assemblySession);
        _imageAdmission = operationContext.AdmitImage(
            assemblySession.GetMetadataReaderForDeclarationSession());
        _assemblySession = assemblySession;
        _operationContext = operationContext;
    }

    public MetadataImageAdmissionResult ImageAdmission
    {
        get
        {
            EnsureAccess();
            return _imageAdmission!;
        }
    }

    public MetadataMethodDeclarationResult PostMethodDeclaration(
        MetadataTypeDefinitionAddress type,
        ILInspector.MetadataPrimitives.MetadataMethodAddress method,
        CancellationToken token = default)
    {
        EnsureAccess();
        token.ThrowIfCancellationRequested();
        MetadataOperationContext operation = _operationContext!;
        if (_imageAdmission is MetadataImageAdmissionResult.Rejected rejected)
        {
            return new MetadataMethodDeclarationResult.Rejected(
                new MetadataMethodDeclarationFailure(
                    new(type, method),
                    MetadataMethodDeclarationFailureReason.BudgetExceeded,
                    MetadataMethodDeclarationStage.RequestValidation,
                    MetadataMethodDeclarationMechanism.ImageAdmission,
                    "The metadata image was not admitted.",
                    default,
                    MetadataOperationDimension.MetadataRows,
                    rejected.Failure.MaxMetadataRows,
                    rejected.Failure.ImageMetadataRows),
                operation.Counters);
        }

        return new MetadataMethodDeclarationEvidenceOperation(
            _assemblySession!.GetMetadataReaderForDeclarationSession(),
            operation,
            GetOrCreateTypeDefinitionIndex)
            .Post(type, method, token);
    }

    public MetadataMethodImplementationResult Relate(
        MetadataTypeDefinitionAddress type,
        ILInspector.MetadataPrimitives.MetadataMethodAddress body,
        CancellationToken token = default)
    {
        EnsureAccess();
        token.ThrowIfCancellationRequested();
        MetadataOperationContext operationContext = _operationContext!;
        if (_imageAdmission is MetadataImageAdmissionResult.Rejected rejected)
        {
            var request =
                new MetadataMethodImplementationRequest(type, body);
            return new MetadataMethodImplementationResult.Rejected(
                new MetadataMethodImplementationFailure(
                    request,
                    MetadataMethodImplementationFailureReason.BudgetExceeded,
                    MetadataMethodImplementationStage.RequestValidation,
                    MetadataMethodImplementationMechanism.ImageAdmission,
                    "The metadata image was not admitted by the operation policy.",
                    RelevantRow: null,
                    RelevantHandle: default,
                    BudgetDimension:
                        MetadataOperationDimension.MetadataRows,
                    BudgetLimit:
                        rejected.Failure.MaxMetadataRows,
                    AttemptedCharge:
                        rejected.Failure.ImageMetadataRows),
                operationContext.Counters);
        }

        MetadataReader reader =
            _assemblySession!.GetMetadataReaderForDeclarationSession();
        return new MetadataMethodImplementationEvidenceOperation(
            reader,
            operationContext,
            GetOrCreateTypeDefinitionIndex)
            .Relate(type, body, token);
    }

    public MetadataInterfaceImplementationResult Relate(
        MetadataTypeDefinitionAddress type,
        MetadataTypeIdentity interfaceType,
        CancellationToken token = default)
    {
        EnsureAccess();
        token.ThrowIfCancellationRequested();
        MetadataOperationContext operationContext = _operationContext!;
        if (_imageAdmission is MetadataImageAdmissionResult.Rejected rejected)
        {
            var request =
                new MetadataInterfaceImplementationRequest(
                    type,
                    interfaceType);
            return new MetadataInterfaceImplementationResult.Rejected(
                new MetadataInterfaceImplementationFailure(
                    request,
                    MetadataInterfaceImplementationFailureReason
                        .BudgetExceeded,
                    MetadataInterfaceImplementationStage
                        .RequestValidation,
                    MetadataInterfaceImplementationMechanism
                        .ImageAdmission,
                    "The metadata image was not admitted by the operation policy.",
                    RelevantRow: null,
                    RelevantHandle: default,
                    BudgetDimension:
                        MetadataOperationDimension.MetadataRows,
                    BudgetLimit:
                        rejected.Failure.MaxMetadataRows,
                    AttemptedCharge:
                        rejected.Failure.ImageMetadataRows),
                operationContext.Counters);
        }

        MetadataReader reader =
            _assemblySession!.GetMetadataReaderForDeclarationSession();
        return new MetadataInterfaceImplementationEvidenceOperation(
            reader,
            operationContext,
            GetOrCreateTypeDefinitionIndex)
            .Relate(type, interfaceType, token);
    }

    MetadataTypeDefinitionIndex GetOrCreateTypeDefinitionIndex(
        Action beforeAccess,
        Action<TypeDefinitionHandle> beforeRelationshipFollow,
        Action beforeCreateNode,
        Action<int> beforeRetainText)
    {
        ArgumentNullException.ThrowIfNull(beforeAccess);
        ArgumentNullException.ThrowIfNull(beforeRelationshipFollow);
        ArgumentNullException.ThrowIfNull(beforeCreateNode);
        ArgumentNullException.ThrowIfNull(beforeRetainText);
        EnsureAccess();
        beforeAccess();
        return _typeDefinitionIndex ??=
            MetadataTypeDefinitionIndex.Create(
                _assemblySession!
                    .GetMetadataReaderForDeclarationSession(),
                definitionVisited: null,
                beforeRelationshipFollow,
                beforeMaterialize: null,
                beforeCreateNode,
                beforeRetainText);
    }

    void EnsureAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _operationContext!.EnsureAlive();
        _assemblySession!.EnsureAliveForDeclarationSession();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _imageAdmission = null;
        _typeDefinitionIndex = null;
        _operationContext = null;
        _assemblySession = null;
    }
}
