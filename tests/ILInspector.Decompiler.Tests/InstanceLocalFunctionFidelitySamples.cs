namespace ILInspector.Decompiler.Tests;

public sealed class InstanceLocalFunctionFidelitySamples
{
    int _state;

    public void RestoreState(int next)
    {
        int previous = _state;
        _state = next;
        Restore();

        void Restore()
        {
            _state = previous;
        }
    }
}
