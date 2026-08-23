namespace Sample.Lib;

public static class Greeter
{
    public static string Greet(string? name = null) => name is null ? "Hello, world!" : $"Hello, {name}!";
}
