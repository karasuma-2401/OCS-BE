using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Configuration;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed class FcmNotificationService(
    AppDbContext db,
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

        if (db.Database.IsNpgsql())
        {
            // The upsert transfers ownership atomically when accounts register concurrently.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FcmDeviceTokens" ("Token", "AccountId", "DeviceType", "RegisteredAt", "LastUsedAt")
                VALUES ({cleanToken}, {accountId}, {cleanDeviceType}, {now}, {now})
                ON CONFLICT ("Token") DO UPDATE SET
                    "AccountId" = EXCLUDED."AccountId", "DeviceType" = EXCLUDED."DeviceType",
                    "RegisteredAt" = EXCLUDED."RegisteredAt", "LastUsedAt" = EXCLUDED."LastUsedAt"
                """, cancellationToken);
        }
        else
        {
            var existing = await db.FcmDeviceTokens.SingleOrDefaultAsync(t => t.Token == cleanToken, cancellationToken);
            if (existing is null)
            {
                existing = new FcmDeviceToken { Token = cleanToken };
                db.FcmDeviceTokens.Add(existing);
            }
            existing.AccountId = accountId;
            existing.DeviceType = cleanDeviceType;
            existing.RegisteredAt = now;
            existing.LastUsedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var staleTokens = await db.FcmDeviceTokens.Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.LastUsedAt).ThenBy(t => t.Token).Skip(10)
            .Select(t => t.Token).ToListAsync(cancellationToken);
        await DeleteTokensAsync(accountId, staleTokens, cancellationToken);
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

        await DeleteTokensAsync(accountId, [token.Trim()], cancellationToken);
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
            if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation(
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

                var payloadData = data is not null
                    ? new Dictionary<string, string>(data)
                    : new Dictionary<string, string>();

                if (!payloadData.ContainsKey("title")) payloadData["title"] = title;
                if (!payloadData.ContainsKey("body")) payloadData["body"] = body;

                var targetLink = payloadData.TryGetValue("link", out var linkVal) && !string.IsNullOrWhiteSpace(linkVal)
                    ? linkVal
                    : (payloadData.TryGetValue("url", out var urlVal) && !string.IsNullOrWhiteSpace(urlVal) ? urlVal : "/");

                payloadData["link"] = targetLink;
                payloadData["url"] = targetLink;

                var webPush = new Dictionary<string, object>
                {
                    ["headers"] = new Dictionary<string, string> { ["Urgency"] = "high", ["TTL"] = "86400" },
                    ["notification"] = new { title, body, icon = "/mascot-logo.png", badge = "/mascot-logo.png" }
                };
                // Relative application routes are handled by our worker. FCM links require HTTPS.
                if (Uri.TryCreate(targetLink, UriKind.Absolute, out var absoluteLink) && absoluteLink.Scheme == Uri.UriSchemeHttps)
                    webPush["fcm_options"] = new { link = targetLink };

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
                        data = payloadData,
                        webpush = webPush,
                        android = new
                        {
                            priority = "high",
                            notification = new
                            {
                                title,
                                body,
                                sound = "default",
                                channel_id = "ocs_notifications"
                            }
                        }
                    }
                };
                req.Content = JsonContent.Create(payload, options: JsonOptions);

                using var res = await client.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                    var errorBody = await res.Content.ReadAsStringAsync(cancellationToken);
                    if (IsUnregisteredToken(errorBody))
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
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failureCount++;
                logger.LogWarning(ex, "FCM send exception for account {AccountId}", recipientAccountId);
            }
        }

        await DeleteTokensAsync(recipientAccountId, invalidTokens, cancellationToken);
        return new FcmDispatchResult(records.Count, successCount, failureCount, true);
    }

    private async Task<List<FcmTokenRecord>> LoadTokensAsync(Guid accountId, CancellationToken ct)
    {
        return await db.FcmDeviceTokens.AsNoTracking().Where(t => t.AccountId == accountId)
            .Select(t => new FcmTokenRecord
            {
                Token = t.Token, DeviceType = t.DeviceType,
                RegisteredAt = t.RegisteredAt, LastUsedAt = t.LastUsedAt
            }).ToListAsync(ct);
    }

    private async Task DeleteTokensAsync(Guid accountId, IReadOnlyCollection<string> tokens, CancellationToken ct)
    {
        if (tokens.Count == 0) return;
        var query = db.FcmDeviceTokens.Where(t => t.AccountId == accountId && tokens.Contains(t.Token));
        if (db.Database.IsRelational())
            await query.ExecuteDeleteAsync(ct);
        else
        {
            db.FcmDeviceTokens.RemoveRange(await query.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
    }

    private static bool IsUnregisteredToken(string errorBody)
    {
        try
        {
            using var document = JsonDocument.Parse(errorBody);
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                return false;
            return details.EnumerateArray().Any(detail =>
                detail.TryGetProperty("@type", out var type) &&
                type.GetString() == "type.googleapis.com/google.firebase.fcm.v1.FcmError" &&
                detail.TryGetProperty("errorCode", out var code) && code.GetString() == "UNREGISTERED");
        }
        catch (JsonException) { return false; }
    }

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
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

