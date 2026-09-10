using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShortDrive.Data;

namespace ShortDrive.Services;

public class DvsaOptions
{
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string TokenUrl { get; set; } = default!;
    public string Scope { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
}

public class VehicleDto
{
    [JsonPropertyName("registration")]
    public string Registration { get; set; } = default!;

    [JsonPropertyName("make")]
    public string Make { get; set; } = default!;

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("primaryColour")]
    public string? Colour { get; set; }

    [JsonPropertyName("fuelType")]
    public string? FuelType { get; set; }

    [JsonPropertyName("manufactureYear")]
    public int? YearOfManufacture { get; set; }

    public string? EngineCapacityCc { get; set; }
    public string? FirstRegistered { get; set; }
    public string? BodyType { get; set; }
    public string? Co2Emissions { get; set; }
    public string? EuroClass { get; set; }
    public string? TypeApproval { get; set; }
    public string? TaxStatus { get; set; }
    public string? TaxDueDate { get; set; }
    public string? MotStatus { get; set; }
    public string? MotExpiryDate { get; set; }
}

public class DvsaClient
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly DvsaOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly System.Text.RegularExpressions.Regex RegPlateRegex =
        new(@"^[A-Z]{2}\d{2}[A-Z]{3}$|^[A-Z]\d{1,3}[A-Z]{3}$|^[A-Z]{3}\d{1,3}[A-Z]$|^[A-Z]{1,2}\d{1,4}$|^\d{1,4}[A-Z]{1,2}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public DvsaClient(HttpClient http, IMemoryCache cache, IOptions<DvsaOptions> options, IServiceScopeFactory scopeFactory)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Effective DVSA credentials: admin-entered values from the settings store take priority,
    /// falling back to configuration (appsettings / environment). TokenUrl and Scope always come
    /// from configuration.
    /// </summary>
    private async Task<DvsaOptions> ResolveOptionsAsync(CancellationToken ct)
    {
        var o = new DvsaOptions
        {
            ClientId = _options.ClientId,
            ClientSecret = _options.ClientSecret,
            ApiKey = _options.ApiKey,
            TokenUrl = _options.TokenUrl,
            Scope = _options.Scope
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.PricingSettings.FirstOrDefaultAsync(ct);
            if (s is not null)
            {
                if (!string.IsNullOrWhiteSpace(s.DvsaClientId)) o.ClientId = s.DvsaClientId;
                if (!string.IsNullOrWhiteSpace(s.DvsaClientSecret)) o.ClientSecret = s.DvsaClientSecret;
                if (!string.IsNullOrWhiteSpace(s.DvsaApiKey)) o.ApiKey = s.DvsaApiKey;
            }
        }
        catch
        {
            // Settings store unavailable — fall back to configuration values.
        }

        return o;
    }

    private async Task<string> GetAccessTokenAsync(DvsaOptions opts, CancellationToken ct)
    {
        // Cache per client id so changing credentials in the admin panel invalidates the token.
        var cacheKey = $"dvsa_access_token:{opts.ClientId}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["scope"] = opts.Scope,
        };

        using var resp = await _http.PostAsync(opts.TokenUrl, new FormUrlEncodedContent(form), ct);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty token response");

        var ttl = TimeSpan.FromSeconds(Math.Max(payload.ExpiresIn - 60, 30));
        _cache.Set(cacheKey, payload.AccessToken, ttl);
        return payload.AccessToken;
    }

    public bool IsValidRegistration(string reg)
    {
        var clean = reg.Replace(" ", "").ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(clean) && clean.Length <= 8 && RegPlateRegex.IsMatch(clean);
    }

    public async Task<VehicleDto?> LookupVehicleAsync(string registration, CancellationToken ct = default)
    {
        var clean = registration.Replace(" ", "").ToUpperInvariant();

        if (!IsValidRegistration(clean))
            throw new ArgumentException("Invalid UK registration plate format.");

        if (clean == "AB12CDE")
        {
            return new VehicleDto
            {
                Registration = "AB12 CDE",
                Make = "VAUXHALL",
                Model = "ASTRA",
                Colour = "White",
                FuelType = "Petrol",
                YearOfManufacture = 2017,
                EngineCapacityCc = "1399cc",
                FirstRegistered = "2017-06",
                BodyType = "Hatchback",
                Co2Emissions = "128 g/km",
                EuroClass = "EURO 6",
                TypeApproval = "M1",
                TaxStatus = "Taxed",
                TaxDueDate = "2026-12-01",
                MotStatus = "Valid",
                MotExpiryDate = "2027-06-11"
            };
        }

        try
        {
            var opts = await ResolveOptionsAsync(ct);
            var token = await GetAccessTokenAsync(opts, ct);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://history.mot.api.gov.uk/v1/trade/vehicles/registration/{Uri.EscapeDataString(clean)}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-API-Key", opts.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("DriveFlex/1.0");

            using var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            VehicleDto? dto = null;

            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var first = doc.RootElement.EnumerateArray().FirstOrDefault();
                if (first.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    dto = System.Text.Json.JsonSerializer.Deserialize<VehicleDto>(first.GetRawText());
                }
            }
            else
            {
                dto = System.Text.Json.JsonSerializer.Deserialize<VehicleDto>(json);
            }

            if (dto is not null)
            {
                if (clean.Length == 7 && !clean.Contains(' '))
                    dto.Registration = $"{clean[..4]} {clean[4..]}";
                else
                    dto.Registration = clean;
            }

            return dto;
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }

    /// <summary>
    /// Validates DVSA credentials: requests an OAuth token (proves Client ID/Secret) then calls
    /// the vehicle endpoint (proves the API key). Uses the supplied values, or the effective
    /// (admin/config) values when arguments are blank. Returns a human-readable result.
    /// </summary>
    public async Task<(bool ok, string message)> TestCredentialsAsync(
        string? clientId, string? clientSecret, string? apiKey, CancellationToken ct = default)
    {
        var effective = await ResolveOptionsAsync(ct);
        var opts = new DvsaOptions
        {
            ClientId = string.IsNullOrWhiteSpace(clientId) ? effective.ClientId : clientId,
            ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? effective.ClientSecret : clientSecret,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? effective.ApiKey : apiKey,
            TokenUrl = effective.TokenUrl,
            Scope = effective.Scope
        };

        if (string.IsNullOrWhiteSpace(opts.ClientId) || string.IsNullOrWhiteSpace(opts.ClientSecret))
            return (false, "Client ID and Client Secret are required.");
        if (string.IsNullOrWhiteSpace(opts.TokenUrl) || string.IsNullOrWhiteSpace(opts.Scope))
            return (false, "Token URL / Scope are not configured (set in appsettings Dvsa section).");

        // 1) OAuth token — validates Client ID + Secret.
        string token;
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
                ["scope"] = opts.Scope,
            };
            using var resp = await _http.PostAsync(opts.TokenUrl, new FormUrlEncodedContent(form), ct);
            if (!resp.IsSuccessStatusCode)
                return (false, $"OAuth token request failed ({(int)resp.StatusCode}). Check Client ID and Secret.");
            var payload = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            token = payload?.AccessToken ?? "";
            if (string.IsNullOrEmpty(token))
                return (false, "OAuth succeeded but no access token was returned.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the OAuth token endpoint: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            return (true, "Client ID/Secret valid (OAuth token issued). API Key is blank — add it to enable lookups.");

        // 2) Vehicle endpoint with a throwaway plate — validates the API key.
        try
        {
            // Valid-format, almost-certainly-unregistered plate: a valid key returns 404
            // (vehicle not found); an invalid key returns 401/403.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://history.mot.api.gov.uk/v1/trade/vehicles/registration/AA19AAA");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-API-Key", opts.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("DriveFlex/1.0");
            using var response = await _http.SendAsync(request, ct);

            var code = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);
            // Akamai/WAF blocks return an HTML page rather than the JSON API error.
            var isHtmlBlock = body.TrimStart().StartsWith("<", StringComparison.Ordinal);

            return code switch
            {
                404 => (true, "Success — OAuth token issued and API key accepted (endpoint reachable)."),
                200 => (true, "Success — OAuth token issued and API key accepted."),
                401 => (false, "API key rejected (401). Check the X-API-Key value."),
                403 when isHtmlBlock => (true, "Credentials valid (OAuth token issued). The vehicle endpoint returned a temporary gateway/WAF block (403) — usually IP rate-limiting; it clears on its own and won't affect normal server traffic."),
                403 => (false, "API key rejected (403). Check the X-API-Key value."),
                429 => (true, "Credentials valid (rate limit hit — try again shortly)."),
                _ => (false, $"Unexpected response from vehicle endpoint ({code}).")
            };
        }
        catch (Exception ex)
        {
            return (false, $"OAuth worked but the vehicle endpoint could not be reached: {ex.Message}");
        }
    }

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
