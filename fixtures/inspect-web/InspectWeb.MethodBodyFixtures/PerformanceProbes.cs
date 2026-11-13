namespace InspectWeb.MethodBodyFixtures;

public static class PerformanceProbes
{
    public static object PerformanceBoxingProbe(int value) => value;

    private static object PerformancePrivateBoxingProbe(int value) => value;

    public static object PerformanceBoxingProperty => 42;

    public static class PerformanceNestedProbe
    {
        public static object Box(int value) => value;
    }
}
