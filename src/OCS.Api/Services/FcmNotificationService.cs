using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed class FcmNotificationService(
    IDistributedCache cache,
    IHttpClientFactory httpClientFactory,
    IOptions<FirebaseOptions> firebaseOptions,
    ILogger<FcmNotificationService> logger) : IFcmService
{
    private const string GoogleOAuthTokenUrl = "https://oauth2.googleapis.com/token";
    private const string FcmScope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string AccessTokenCacheKey = "fcm:oauth:access_token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FirebaseOptions _options = firebaseOptions.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task RegisterTokenAsync(
        Guid accountId,
        string token,
        string deviceType = "Web",
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var cleanToken = token.Trim();
        var cleanDeviceType = string.IsNullOrWhiteSpace(deviceType) ? "Web" : deviceType.Trim();
        var now = DateTimeOffset.UtcNow;

        var records = await LoadTokensAsync(accountId, cancellationToken);
        var existing = records.FirstOrDefault(r => string.Equals(r.Token, cleanToken, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.DeviceType = cleanDeviceType;
            existing.LastUsedAt = now;
        }
        else
        {
            records.Add(new FcmTokenRecord
            {
                Token = cleanToken,
                DeviceType = cleanDeviceType,
                RegisteredAt = now,
                LastUsedAt = now
            });
        }

        // Keep most recent 10 tokens per account
        if (records.Count > 10)
        {
            records = records
                .OrderByDescending(r => r.LastUsedAt)
                .Take(10)
                .ToList();
        }

        await SaveTokensAsync(accountId, records, cancellationToken);
    }

    public async Task UnregisterTokenAsync(
        Guid accountId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var cleanToken = token.Trim();
        var records = await LoadTokensAsync(accountId, cancellationToken);
        var filtered = records
            .Where(r => !string.Equals(r.Token, cleanToken, StringComparison.Ordinal))
            .ToList();

        if (filtered.Count != records.Count)
        {
            await SaveTokensAsync(accountId, filtered, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<FcmTokenRecord>> GetTokensForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return [];
        }

        return await LoadTokensAsync(accountId, cancellationToken);
    }

    public async Task<FcmDispatchResult> SendPushToUserAsync(
        Guid recipientAccountId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadTokensAsync(recipientAccountId, cancellationToken);
        if (records.Count == 0)
        {
            return new FcmDispatchResult(0, 0, 0, IsConfigured);
        }

        if (!IsConfigured)
        {
            logger.LogInformation(
                "FCM skipped for account {AccountId} ({DeviceCount} device(s)) because Firebase credentials are not configured.",
                recipientAccountId,
                records.Count);
            return new FcmDispatchResult(records.Count, 0, 0, false);
        }

        var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new FcmDispatchResult(records.Count, 0, records.Count, true);
        }

        var client = httpClientFactory.CreateClient("FcmClient");
        var endpoint = $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send";

        int successCount = 0;
        int failureCount = 0;
        var invalidTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var payload = new
                {
                    message = new
                    {
                        token = record.Token,
                        notification = new
                        {
                            title,
                            body
                        },
                        data = data ?? new Dictionary<string, string>()
                    }
                };
                req.Content = JsonContent.Create(payload, options: JsonOptions);

                using var res = await client.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    successCount++;
                    record.LastUsedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    failureCount++;
                    var errorBody = await res.Content.ReadAsStringAsync(cancellationToken);
                    if (res.StatusCode == HttpStatusCode.NotFound ||
                        errorBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase) ||
                        errorBody.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
                    {
                        invalidTokens.Add(record.Token);
                    }

                    logger.LogWarning(
                        "FCM send failed ({StatusCode}) for account {AccountId}: {Error}",
                        (int)res.StatusCode,
                        recipientAccountId,
                        errorBody);
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                logger.LogWarning(ex, "FCM send exception for account {AccountId}", recipientAccountId);
            }
        }

        if (invalidTokens.Count > 0)
        {
            records = records.Where(r => !invalidTokens.Contains(r.Token)).ToList();
        }

        await SaveTokensAsync(recipientAccountId, records, cancellationToken);
        return new FcmDispatchResult(records.Count + invalidTokens.Count, successCount, failureCount, true);
    }

    private async Task<List<FcmTokenRecord>> LoadTokensAsync(Guid accountId, CancellationToken ct)
    {
        var raw = await cache.GetStringAsync(BuildCacheKey(accountId), ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FcmTokenRecord>>(raw, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private Task SaveTokensAsync(Guid accountId, List<FcmTokenRecord> records, CancellationToken ct)
    {
        var raw = JsonSerializer.Serialize(records, JsonOptions);
        return cache.SetStringAsync(
            BuildCacheKey(accountId),
            raw,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
            },
            ct);
    }

    private static string BuildCacheKey(Guid accountId) => $"fcm:tokens:{accountId:N}";

    private async Task<string?> GetGoogleAccessTokenAsync(CancellationToken ct)
    {
        var cached = await cache.GetStringAsync(AccessTokenCacheKey, ct);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = _options.ClientEmail,
                scope = FcmScope,
                aud = GoogleOAuthTokenUrl,
                iat = now.ToUnixTimeSeconds(),
                exp = now.AddMinutes(55).ToUnixTimeSeconds()
            }));

            var unsignedJwt = $"{header}.{claims}";
            using var rsa = RSA.Create();
            var pem = _options.PrivateKey?.Trim() ?? string.Empty;
            if (pem.StartsWith('"') && pem.EndsWith('"') && pem.Length > 1)
            {
                pem = pem[1..^1];
            }
            pem = pem.Replace("\\n", "\n", StringComparison.Ordinal).Trim();
            rsa.ImportFromPem(pem);

            var signatureBytes = rsa.SignData(
                Encoding.ASCII.GetBytes(unsignedJwt),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var signedJwt = $"{unsignedJwt}.{Base64UrlEncode(signatureBytes)}";

            var client = httpClientFactory.CreateClient("FcmClient");
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", signedJwt)
            ]);

            using var res = await client.PostAsync(GoogleOAuthTokenUrl, content, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Failed to acquire Firebase OAuth2 token: {Error}", err);
                return null;
            }

            using var jsonDoc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!jsonDoc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                return null;
            }

            var accessToken = tokenProp.GetString();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                await cache.SetStringAsync(
                    AccessTokenCacheKey,
                    accessToken,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(50) },
                    ct);
            }

            return accessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error generating Firebase OAuth2 token.");
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

