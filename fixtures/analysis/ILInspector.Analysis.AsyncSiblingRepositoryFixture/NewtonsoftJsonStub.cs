namespace Newtonsoft.Json;

public static class JsonConvert
{
    public static T DeserializeObject<T>(string value)
        where T : new() =>
        new();
}
