using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Runtime.Maf;
using Xunit;

namespace SharpAgent.Runtime.Maf.Tests;

public sealed class MafRuntimeRegistrationTests
{
    [Fact]
    public void AddMafRuntime_registers_the_runtime_adapter_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddMafRuntime();

        var descriptor = Assert.Single(services, static item => item.ServiceType == typeof(IAgentRuntime));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(MafAgentRuntime), descriptor.ImplementationType);
    }
}
