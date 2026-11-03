using System.Text;

namespace OCS.Api.Configuration;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        var required = new[] { "ConnectionStrings:DefaultConnection", "Jwt:Issuer", "Jwt:Audience", "Jwt:Key" };
        foreach (var name in required)
        {
            if (string.IsNullOrWhiteSpace(configuration[name]))
            {
                throw new InvalidOperationException($"Production requires configuration '{name}'.");
            }
        }

        if (Encoding.UTF8.GetByteCount(configuration["Jwt:Key"]!) < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes in Production.");
        }

        var provider = configuration["Email:Provider"];
        if (string.Equals(provider, "TestSink", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Production cannot use a fake or test sink email sender.");
        }

        var requiredEmail = new[] { "Email:SmtpHost", "Email:Username", "Email:AppPassword", "Email:FromAddress" };
        foreach (var name in requiredEmail)
        {
            if (string.IsNullOrWhiteSpace(configuration[name]))
            {
                throw new InvalidOperationException($"Production requires configuration '{name}'.");
            }
        }
    }
}
