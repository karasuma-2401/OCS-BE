using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OCS.Api.Configuration;

namespace OCS.Api.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    private static (IConfiguration, IHostEnvironment) CreateEnv(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var defaults = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=ocs;Username=user;Password=pass",
            ["Jwt:Issuer"] = "ocs-api",
            ["Jwt:Audience"] = "ocs-clients",
            ["Jwt:Key"] = "at-least-32-bytes-long-super-secret-key-12345",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Email:Provider"] = "GmailSmtp",
            ["Email:SmtpHost"] = "smtp.gmail.com",
            ["Email:Username"] = "user@example.com",
            ["Email:AppPassword"] = "app-secret-pwd",
            ["Email:FromAddress"] = "user@example.com"
        };

        foreach (var (k, v) in overrides)
        {
            defaults[k] = v;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
        var env = new TestHostEnvironment { EnvironmentName = environmentName };
        return (config, env);
    }

    [Fact]
    public void ProductionValidatesSuccessfullyWithCompleteConfiguration()
    {
        var (config, env) = CreateEnv(Environments.Production, []);
        ProductionConfiguration.Validate(config, env);
    }

    [Theory]
    [InlineData("TestSink")]
    [InlineData("Fake")]
    [InlineData("testsink")]
    public void ProductionRejectsFakeOrTestSinkEmailSender(string provider)
    {
        var (config, env) = CreateEnv(Environments.Production, new() { ["Email:Provider"] = provider });
        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, env));
        Assert.Contains("fake or test sink", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Email:SmtpHost")]
    [InlineData("Email:Username")]
    [InlineData("Email:AppPassword")]
    [InlineData("Email:FromAddress")]
    public void ProductionRequiresAllMandatoryEmailSettings(string missingSetting)
    {
        var (config, env) = CreateEnv(Environments.Production, new() { [missingSetting] = "" });
        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, env));
        Assert.Contains(missingSetting, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentDoesNotEnforceProductionEmailSettings()
    {
        var (config, env) = CreateEnv(Environments.Development, new()
        {
            ["Email:Provider"] = "TestSink",
            ["Email:SmtpHost"] = "",
            ["Email:AppPassword"] = ""
        });
        ProductionConfiguration.Validate(config, env);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "OCS.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

