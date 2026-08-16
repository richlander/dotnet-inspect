await Task.Yield();
Console.WriteLine(TopLevelAsyncEqual(1, 2));

static bool TopLevelAsyncEqual<T>(T left, T right)
    => left!.Equals(right);
