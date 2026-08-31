namespace ILInspector.Metadata.MemorySafetyFixtures;

public static class MemorySafetyFixtures
{
    public static unsafe int MethodContract() => 42;

    public static void PointerOnly(int* value)
    {
        unsafe
        {
            _ = *value;
        }
    }

    public static unsafe int PropertyContract { get; set; }

    public static unsafe event Action? EventContract;

    public static void Raise()
    {
        unsafe
        {
            EventContract?.Invoke();
        }
    }
}
