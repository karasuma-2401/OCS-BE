using System.Text;

namespace OCS.Api.Configuration;

internal static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // AUTH TODO: Once Gmail SMTP is wired, also validate the Email options required by the selected
        // provider in Production without echoing secret values. Missing/revoked secrets must fail safe.
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
    }
}
