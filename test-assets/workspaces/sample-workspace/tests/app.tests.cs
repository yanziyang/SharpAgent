using Sample.Lib;
using Xunit;

namespace Sample.Tests;

public class GreeterTests
{
    [Fact]
    public void Greets_the_world_by_default() => Assert.Equal("Hello, world!", Greeter.Greet());
}
