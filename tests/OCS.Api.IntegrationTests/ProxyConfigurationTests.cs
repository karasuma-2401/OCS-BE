using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OCS.Api.Extensions;

namespace OCS.Api.IntegrationTests;

public sealed class ProxyConfigurationTests
{
    [Fact]
    public void ForwardedHeadersAreDisabledWhenNoProxyIsTrusted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOcsApi(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
    }

    [Fact]
    public void ForwardedHeadersUseOnlyConfiguredProxies()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "192.0.2.1"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOcsApi(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.Single(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal("192.0.2.1", options.KnownProxies[0].ToString());
    }
}
