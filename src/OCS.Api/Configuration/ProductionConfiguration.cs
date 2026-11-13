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

        if (string.Equals(configuration["Jwt:Key"], "ocs-super-secret-key-that-is-at-least-32-bytes-long", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Production cannot use the default placeholder Jwt:Key.");
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

        if (string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"]))
            throw new InvalidOperationException("Production requires Redis:ConnectionString for distributed rate limits.");

        // Firebase Cloud Messaging configuration validation in Production
        var firebaseProjectId = configuration["Firebase:ProjectId"];
        var firebaseClientEmail = configuration["Firebase:ClientEmail"];
        var firebasePrivateKey = configuration["Firebase:PrivateKey"];

        if (string.IsNullOrWhiteSpace(firebaseProjectId))
        {
            throw new InvalidOperationException("Production requires 'Firebase:ProjectId' for Firebase Cloud Messaging.");
        }

        if (string.IsNullOrWhiteSpace(firebaseClientEmail))
        {
            throw new InvalidOperationException("Production requires 'Firebase:ClientEmail' for FCM HTTP v1 Service Account authentication.");
        }

        if (string.IsNullOrWhiteSpace(firebasePrivateKey))
        {
            throw new InvalidOperationException("Production requires 'Firebase:PrivateKey' containing the RSA private key PEM.");
        }

        // Validate RSA PEM parsing at startup to fail-fast before serving requests
        try
        {
            var pem = firebasePrivateKey.Trim();
            if (pem.StartsWith('"') && pem.EndsWith('"') && pem.Length > 1)
            {
                pem = pem[1..^1];
            }
            pem = pem.Replace("\\n", "\n", StringComparison.Ordinal).Trim();

            using var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Firebase:PrivateKey format is invalid or cannot be parsed as an RSA private key PEM: {ex.Message}", ex);
        }
    }
}
