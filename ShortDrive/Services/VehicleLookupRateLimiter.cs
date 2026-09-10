using Microsoft.Extensions.Caching.Memory;

namespace ShortDrive.Services;

/// <summary>
/// In-memory per-IP rate limiter for DVSA vehicle lookups (5 req/min).
/// Used because Blazor Server runs on SignalR, not raw HTTP, so
/// ASP.NET Core RateLimiting middleware cannot target it directly.
/// </summary>
public class VehicleLookupRateLimiter
{
    private readonly IMemoryCache _cache;
    private const int Max = 5;
    private const int WindowMinutes = 1;

    public VehicleLookupRateLimiter(IMemoryCache cache) => _cache = cache;

    public bool IsAllowed(string clientKey)
    {
        var key = $"rl:vehicle:{clientKey}";
        _cache.TryGetValue<int>(key, out var count);
        if (count >= Max) return false;
        _cache.Set(key, count + 1, TimeSpan.FromMinutes(WindowMinutes));
        return true;
    }
}
