namespace ILInspector.Metadata.MethodImplContracts;

public interface IExternalContract
{
    void External();
}

public unsafe interface IFunctionPointerContract
{
    void M<T>(delegate*<T, void> callback);
}
