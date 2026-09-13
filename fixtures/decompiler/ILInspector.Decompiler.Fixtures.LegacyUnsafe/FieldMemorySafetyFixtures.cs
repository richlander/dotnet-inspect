namespace ILInspector.Decompiler.Fixtures.LegacyUnsafe;

public static class FieldMemorySafetyFixtures
{
    public static unsafe int* LegacyPointerField;

    public static unsafe int* ReadLegacyPointerField()
        => LegacyPointerField;
}
