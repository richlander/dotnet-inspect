await Task.Yield();
Console.WriteLine(TopLevelClassicAsyncEqual(1, 2));

static bool TopLevelClassicAsyncEqual<T>(T left, T right)
    => left!.Equals(right);
