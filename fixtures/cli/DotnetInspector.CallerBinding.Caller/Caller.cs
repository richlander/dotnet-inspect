using DotnetInspector.Services.RouteLearning;

namespace CallerBinding;

public static class Caller
{
    public static Middle Create() => new();

    public static int Unrelated() => 42;
}
