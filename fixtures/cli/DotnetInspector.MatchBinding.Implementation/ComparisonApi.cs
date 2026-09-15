namespace DotnetInspector.MatchBinding;

public static class ComparisonApi
{
    public static int InvokePayload(int value)
    {
        Payload payload = Identity;
        return payload(value);
    }

    public static int ReturnZero(int value) => 0;

    static int Identity(int value) => value;
}
