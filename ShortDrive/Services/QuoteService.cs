using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using ShortDrive.Data;
using ShortDrive.Models;

namespace ShortDrive.Services;

public class DriverFormModel
{
    [Required(ErrorMessage = "First name is required")]
    [MaxLength(50)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Last name is required")]
    [MaxLength(50)]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "Date of birth is required")]
    public DateTime? DateOfBirth { get; set; } = DateTime.Today.AddYears(-25);

    [Required(ErrorMessage = "Licence number is required")]
    [MaxLength(20)]
    public string LicenceNumber { get; set; } = "";

    [Required(ErrorMessage = "Address line 1 is required")]
    [MaxLength(100)]
    public string AddressLine1 { get; set; } = "";

    [MaxLength(100)]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "City is required")]
    [MaxLength(50)]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Postcode is required")]
    [RegularExpression(@"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$",
        ErrorMessage = "Enter a valid UK postcode (e.g. SW1A 1AA)")]
    public string Postcode { get; set; } = "";

    [Range(0, 60, ErrorMessage = "Must be between 0 and 60")]
    public int YearsLicenceHeld { get; set; } = 4;

    [Range(0, 10, ErrorMessage = "Must be between 0 and 10")]
    public int ClaimsLast5Years { get; set; } = 0;

    [Range(0, 10, ErrorMessage = "Must be between 0 and 10")]
    public int ConvictionsLast5Years { get; set; } = 0;
}

public class CoverModel
{
    public int DurationHours { get; set; } = 168;
    public DateTime StartDateLocal { get; set; } = UkTime.Now.AddHours(1);

    public string Usage { get; set; } = "Personal";
    public string VehicleClass { get; set; } = "Car";

    public bool AddOnExcessProtect { get; set; }
    public bool AddOnRoadside { get; set; }
    public bool AddOnLegal { get; set; }
    public bool AddOnComprehensiveUpgrade { get; set; }

    public string DurationLabel => DurationHours switch
    {
        1 => "1 Hour",
        24 => "1 Day",
        < 24 => $"{DurationHours} Hours",
        168 => "7 Days",
        672 => "28 Days",
        _ => $"{DurationHours / 24} Days"
    };
}

public class QuoteResult
{
    public decimal BasePremium { get; set; }
    public decimal DurationMultiplier { get; set; }
    public decimal RiskMultiplier { get; set; }
    public decimal AddOnsTotal { get; set; }
    public decimal TotalPremium { get; set; }
    public string DurationLabel { get; set; } = "";
    public int DurationHours { get; set; }
    public int QuoteId { get; set; }
    public string PolicyNumber { get; set; } = "";
    public string ExcessLabel { get; set; } = "£500";
}

public class QuoteService
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuoteService> _logger;

    public QuoteService(AppDbContext db, ILogger<QuoteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PricingSettings> GetPricingSettingsAsync(CancellationToken ct = default)
    {
        var settings = await _db.PricingSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new PricingSettings();
            _db.PricingSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }
        return settings;
    }

    public async Task UpdatePricingSettingsAsync(PricingSettings updated, CancellationToken ct = default)
    {
        var existing = await GetPricingSettingsAsync(ct);
        existing.OneHourRate = updated.OneHourRate;
        existing.OneDayRate = updated.OneDayRate;
        existing.SevenDayRate = updated.SevenDayRate;
        existing.TwentyEightDayRate = updated.TwentyEightDayRate;
        existing.CarMultiplier = updated.CarMultiplier;
        existing.VanMultiplier = updated.VanMultiplier;
        existing.BikeMultiplier = updated.BikeMultiplier;
        existing.CamperMultiplier = updated.CamperMultiplier;
        existing.PersonalMultiplier = updated.PersonalMultiplier;
        existing.BusinessMultiplier = updated.BusinessMultiplier;
        existing.ExcessProtectPrice = updated.ExcessProtectPrice;
        existing.RoadsideRecoveryPrice = updated.RoadsideRecoveryPrice;
        existing.LegalExpensesPrice = updated.LegalExpensesPrice;
        existing.ComprehensiveUpgradePrice = updated.ComprehensiveUpgradePrice;
        existing.UnderwriterName = updated.UnderwriterName;
        existing.FcaFirmReference = updated.FcaFirmReference;
        existing.StripeSecretKey = updated.StripeSecretKey;
        existing.StripePublishableKey = updated.StripePublishableKey;
        existing.StripeWebhookSecret = updated.StripeWebhookSecret;
        existing.StripeTestMode = updated.StripeTestMode;

        // DVSA / DVLA API credentials
        existing.DvsaClientId = updated.DvsaClientId;
        existing.DvsaClientSecret = updated.DvsaClientSecret;
        existing.DvsaApiKey = updated.DvsaApiKey;

        // SMTP email configuration
        existing.SmtpHost = updated.SmtpHost;
        existing.SmtpPort = updated.SmtpPort;
        existing.SmtpUsername = updated.SmtpUsername;
        existing.SmtpPassword = updated.SmtpPassword;
        existing.SmtpFromEmail = updated.SmtpFromEmail;
        existing.SmtpFromName = updated.SmtpFromName;
        existing.SmtpUseSsl = updated.SmtpUseSsl;

        // Admin password hash (persists password changes to the store)
        existing.AdminPasswordHash = updated.AdminPasswordHash;

        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Pricing settings updated by admin");
    }

    public async Task<QuoteResult> CalculateAndSaveAsync(
        VehicleDto vehicle,
        DriverFormModel driver,
        CoverModel cover,
        string? customerEmail,
        CancellationToken ct = default)
    {
        var settings = await GetPricingSettingsAsync(ct);

        // Single source of truth for pricing — shared with the live quote display.
        var pricing = ComputePremium(cover, driver, settings);
        var basePremium = pricing.BasePremium;
        var classMultiplier = pricing.ClassMultiplier;
        var usageMultiplier = pricing.UsageMultiplier;
        var riskMultiplier = pricing.EffectiveMultiplier;
        var addOns = pricing.AddOnsTotal;
        var total = pricing.Total;

        var cleanReg = vehicle.Registration.Replace(" ", "").ToUpperInvariant();
        var dbVehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Registration == cleanReg, ct)
            ?? new Vehicle();

        dbVehicle.Registration = cleanReg;
        dbVehicle.Make = vehicle.Make;
        dbVehicle.Model = vehicle.Model ?? "";
        dbVehicle.Colour = vehicle.Colour;
        dbVehicle.FuelType = vehicle.FuelType;
        dbVehicle.YearOfManufacture = vehicle.YearOfManufacture;
        dbVehicle.EngineCapacityCc = vehicle.EngineCapacityCc;
        dbVehicle.FirstRegistered = vehicle.FirstRegistered;
        dbVehicle.BodyType = vehicle.BodyType;
        dbVehicle.Co2Emissions = vehicle.Co2Emissions;
        dbVehicle.EuroClass = vehicle.EuroClass;
        dbVehicle.TypeApproval = vehicle.TypeApproval;
        dbVehicle.TaxStatus = vehicle.TaxStatus;
        dbVehicle.TaxDueDate = vehicle.TaxDueDate;
        dbVehicle.MotStatus = vehicle.MotStatus;
        dbVehicle.MotExpiryDate = vehicle.MotExpiryDate;
        dbVehicle.LookedUpAt = DateTime.UtcNow;

        if (dbVehicle.Id == 0) _db.Vehicles.Add(dbVehicle);

        var dob = driver.DateOfBirth.HasValue
            ? DateOnly.FromDateTime(driver.DateOfBirth.Value)
            : DateOnly.FromDateTime(DateTime.Today.AddYears(-25));

        var dbDriver = new Driver
        {
            FirstName = driver.FirstName,
            LastName = driver.LastName,
            DateOfBirth = dob,
            LicenceNumber = driver.LicenceNumber,
            AddressLine1 = driver.AddressLine1,
            AddressLine2 = driver.AddressLine2,
            City = driver.City,
            Postcode = driver.Postcode.ToUpperInvariant(),
            YearsLicenceHeld = driver.YearsLicenceHeld,
            ClaimsLast5Years = driver.ClaimsLast5Years,
            ConvictionsLast5Years = driver.ConvictionsLast5Years
        };
        _db.Drivers.Add(dbDriver);
        await _db.SaveChangesAsync(ct);

        var policyNum = $"DF-{DateTime.UtcNow.Year}-{Random.Shared.Next(10000, 99999)}";

        var quote = new Quote
        {
            VehicleId = dbVehicle.Id,
            DriverId = dbDriver.Id,
            Usage = cover.Usage,
            VehicleClass = cover.VehicleClass,
            CoverStartUtc = UkTime.ToUtc(cover.StartDateLocal),
            DurationHours = cover.DurationHours,
            BaseRate = basePremium,
            DurationMultiplier = classMultiplier * usageMultiplier,
            RiskMultiplier = riskMultiplier,
            AddOnsTotal = addOns,
            TotalPremium = total,
            AddOnExcessProtect = cover.AddOnExcessProtect,
            AddOnRoadside = cover.AddOnRoadside,
            AddOnLegal = cover.AddOnLegal,
            AddOnComprehensiveUpgrade = cover.AddOnComprehensiveUpgrade,
            PolicyNumber = policyNum,
            CustomerEmail = customerEmail,
            Status = QuoteStatus.Quoted
        };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Quote {QuoteId} created for {Reg}: £{Total}",
            quote.Id, vehicle.Registration, total);

        string excess = "£500";
        if (cover.AddOnExcessProtect) excess = "£0 (Excess Protected)";
        else if (cover.AddOnComprehensiveUpgrade) excess = "£250";

        return new QuoteResult
        {
            BasePremium = basePremium,
            DurationMultiplier = classMultiplier * usageMultiplier,
            RiskMultiplier = riskMultiplier,
            AddOnsTotal = addOns,
            TotalPremium = total,
            DurationLabel = cover.DurationLabel,
            DurationHours = cover.DurationHours,
            QuoteId = quote.Id,
            PolicyNumber = policyNum,
            ExcessLabel = excess
        };
    }

    public async Task<Quote?> GetQuoteWithDetailsAsync(int quoteId, CancellationToken ct = default)
    {
        return await _db.Quotes
            .Include(q => q.Vehicle)
            .Include(q => q.Driver)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
    }

    public async Task<bool> MarkAsPurchasedAsync(int quoteId, string stripeSessionId, CancellationToken ct = default)
    {
        var q = await _db.Quotes
            .Include(q => q.Vehicle)
            .Include(q => q.Driver)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);

        if (q is null) return false;

        if (q.Status == QuoteStatus.Purchased)
            return true;

        q.Status = QuoteStatus.Purchased;
        q.StripeSessionId = stripeSessionId;
        q.CertificateIssuedAt = DateTime.UtcNow;
        q.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Quote {QuoteId} marked Purchased with policy {Policy}", quoteId, q.PolicyNumber);
        return true;
    }

    public async Task<List<Quote>> GetAllQuotesAsync(CancellationToken ct = default)
    {
        return await _db.Quotes
            .Include(q => q.Vehicle)
            .Include(q => q.Driver)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(ct);
    }

    // ---- Admin-editable content pages (About / Privacy / Terms / Contact) ----

    public async Task<SitePage?> GetPageAsync(string slug, CancellationToken ct = default)
        => await _db.SitePages.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public async Task<List<SitePage>> GetAllPagesAsync(CancellationToken ct = default)
        => await _db.SitePages.OrderBy(p => p.Slug).ToListAsync(ct);

    public async Task UpdatePageAsync(SitePage page, CancellationToken ct = default)
    {
        var existing = await _db.SitePages.FirstOrDefaultAsync(p => p.Id == page.Id, ct);
        if (existing is null) return;
        existing.Title = page.Title;
        existing.ContentHtml = page.ContentHtml;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static decimal CalculateRiskMultiplier(DriverFormModel d)
    {
        var m = 1.0m;
        m += Math.Max(0, d.ClaimsLast5Years) * 0.15m;
        m += Math.Max(0, d.ConvictionsLast5Years) * 0.25m;
        if (d.YearsLicenceHeld < 2) m += 0.25m;
        return m;
    }

    /// <summary>
    /// Central pricing engine — the single source of truth used by both the live quote
    /// display in the wizard and the persisted quote. Keeping one implementation prevents
    /// the frontend estimate and the amount actually charged from drifting apart.
    /// </summary>
    public static PremiumBreakdown ComputePremium(CoverModel cover, DriverFormModel driver, PricingSettings s)
    {
        var basePremium = ComputeBaseRate(cover.DurationHours, s);

        var classMultiplier = (cover.VehicleClass ?? "Car").ToLowerInvariant() switch
        {
            "van" => s.VanMultiplier,
            "bike" => s.BikeMultiplier,
            "camper" => s.CamperMultiplier,
            _ => s.CarMultiplier
        };
        if (classMultiplier <= 0) classMultiplier = 1m;

        var usageMultiplier = string.Equals(cover.Usage, "Business", StringComparison.OrdinalIgnoreCase)
            ? s.BusinessMultiplier
            : s.PersonalMultiplier;
        if (usageMultiplier <= 0) usageMultiplier = 1m;

        var riskMultiplier = CalculateRiskMultiplier(driver);
        var effectiveMultiplier = riskMultiplier * classMultiplier * usageMultiplier;

        var core = Math.Round(basePremium * effectiveMultiplier, 2, MidpointRounding.AwayFromZero);

        decimal addOns = 0m;
        if (cover.AddOnExcessProtect) addOns += Math.Max(0, s.ExcessProtectPrice);
        if (cover.AddOnRoadside) addOns += Math.Max(0, s.RoadsideRecoveryPrice);
        if (cover.AddOnLegal) addOns += Math.Max(0, s.LegalExpensesPrice);
        if (cover.AddOnComprehensiveUpgrade) addOns += Math.Max(0, s.ComprehensiveUpgradePrice);
        addOns = Math.Round(addOns, 2, MidpointRounding.AwayFromZero);

        var total = Math.Round(core + addOns, 2, MidpointRounding.AwayFromZero);

        var excess = cover.AddOnExcessProtect ? "£0"
            : cover.AddOnComprehensiveUpgrade ? "£250"
            : "£500";

        return new PremiumBreakdown(
            basePremium, classMultiplier, usageMultiplier, riskMultiplier,
            effectiveMultiplier, core, addOns, total, excess);
    }

    /// <summary>
    /// Duration base rate. The four configured tiers (1h, 1 day, 7 days, 28 days) are the
    /// anchor points; every duration in between is interpolated on a per-day basis so pricing
    /// is continuous and strictly increasing with duration. Previously the 2–7 day band all
    /// mapped to the flat 7-day rate, so a 3-day cover was charged the same as 7 days.
    /// </summary>
    public static decimal ComputeBaseRate(int durationHours, PricingSettings s)
    {
        var oneHour = Math.Max(0m, s.OneHourRate);
        var oneDay = Math.Max(0m, s.OneDayRate);
        var sevenDay = s.SevenDayRate > 0 ? s.SevenDayRate : oneDay * 7m;
        var twentyEight = s.TwentyEightDayRate > 0 ? s.TwentyEightDayRate : sevenDay * 4m;

        if (durationHours <= 1) return oneHour;
        if (durationHours <= 24) return oneDay;

        var days = (int)Math.Ceiling(durationHours / 24.0);

        if (days <= 7)
        {
            var perExtraDay = (sevenDay - oneDay) / 6m;
            return Math.Round(oneDay + perExtraDay * (days - 1), 2, MidpointRounding.AwayFromZero);
        }

        if (days >= 28) return twentyEight;

        var perExtraDayLong = (twentyEight - sevenDay) / 21m;
        return Math.Round(sevenDay + perExtraDayLong * (days - 7), 2, MidpointRounding.AwayFromZero);
    }
}

public record PremiumBreakdown(
    decimal BasePremium,
    decimal ClassMultiplier,
    decimal UsageMultiplier,
    decimal RiskMultiplier,
    decimal EffectiveMultiplier,
    decimal CorePremium,
    decimal AddOnsTotal,
    decimal Total,
    string ExcessLabel);
