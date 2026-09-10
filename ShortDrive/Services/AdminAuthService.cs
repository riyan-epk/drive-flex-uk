using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShortDrive.Data;

namespace ShortDrive.Services;

/// <summary>
/// Admin authentication for the control panel. Stored passwords are held as salted
/// PBKDF2 hashes; the credential configured via <c>Admin:Password</c> is compared in
/// constant time. A shared failed-attempt counter provides basic brute-force protection.
/// </summary>
public class AdminAuthService
{
    private const string HashPrefix = "pbkdf2$";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(5);
    private const string LockoutKey = "admin_login_failures";

    private readonly IMemoryCache _cache;

    // Either a PBKDF2 hash (preferred, loaded from the store) or a plaintext value
    // configured via Admin:Password. Empty means admin login is disabled.
    private string _credential;

    public bool IsAuthenticated { get; private set; }

    public AdminAuthService(IConfiguration config, IServiceProvider sp, IMemoryCache cache)
    {
        _cache = cache;
        _credential = config["Admin:Password"] ?? "";

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = db.PricingSettings.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(settings?.AdminPasswordHash))
            _credential = settings.AdminPasswordHash;
    }

    public bool IsLockedOut => _cache.TryGetValue<int>(LockoutKey, out var n) && n >= MaxAttempts;

    public bool Login(string password)
    {
        if (IsLockedOut)
            return false;

        if (VerifyPassword(password))
        {
            _cache.Remove(LockoutKey);
            IsAuthenticated = true;
            return true;
        }

        RegisterFailure();
        IsAuthenticated = false;
        return false;
    }

    public bool ValidatePassword(string password) => VerifyPassword(password);

    /// <summary>
    /// Restores the authenticated state for a circuit that was re-established (e.g. after a
    /// full-page refresh), based on the encrypted per-session marker written at login time.
    /// </summary>
    public void RestoreAuthenticated() => IsAuthenticated = true;

    /// <summary>Updates the in-memory credential for the current session to the new password.</summary>
    public void SetNewPassword(string newPassword) => _credential = HashPassword(newPassword);

    /// <summary>Produces a salted hash suitable for persisting in the settings store.</summary>
    public static string HashPasswordForStorage(string newPassword) => HashPassword(newPassword);

    public void Logout() => IsAuthenticated = false;

    private void RegisterFailure()
    {
        _cache.TryGetValue<int>(LockoutKey, out var n);
        _cache.Set(LockoutKey, n + 1, LockoutWindow);
    }

    private bool VerifyPassword(string? password)
    {
        if (string.IsNullOrEmpty(_credential) || string.IsNullOrEmpty(password))
            return false;

        if (_credential.StartsWith(HashPrefix, StringComparison.Ordinal))
            return VerifyHash(password, _credential);

        // Plaintext credential from configuration — constant-time comparison.
        return FixedTimeEquals(password, _credential);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{HashPrefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    private static bool VerifyHash(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$'); // pbkdf2 | iterations | salt | key
            if (parts.Length != 4) return false;
            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        // Hash to a fixed length first so the comparison does not leak length information.
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
