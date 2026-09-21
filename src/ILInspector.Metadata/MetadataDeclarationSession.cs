namespace ILInspector.Metadata;

internal sealed class MetadataDeclarationSession : IDisposable
{
    AssemblyInspectionSession? _assemblySession;
    MetadataOperationContext? _operationContext;
    MetadataImageAdmissionResult? _imageAdmission;
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
        _operationContext = null;
        _assemblySession = null;
    }
}
