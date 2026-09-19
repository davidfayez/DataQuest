using System.Reflection;
using System.Text.Json;
using DataVerification.Application.Features.Content;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// Brings a fresh database up to a demo-able state: the NEN client, Egypt with its currencies, the
/// transaction/authority/service cascade, the permission catalogue, two roles and one
/// SuperAdmin. Every step is keyed on a stable identifier and checks before inserting, so running
/// it repeatedly against the same database is a no-op.
/// </summary>
public sealed class DataSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<DataSeeder> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedClientAsync(cancellationToken);
        await SeedCountriesAndCurrenciesAsync(cancellationToken);
        await SeedAddresseesAsync(cancellationToken);
        await SeedWorldLookupsAsync(cancellationToken);
        // After both currency seeders, so Egypt — mapped to EGP and USD — lands on EGP.
        await CountryDefaultCurrencies.EnsureAsync(_db, null, cancellationToken);
        await SeedTransactionCascadeAsync(cancellationToken);
        await SeedPaymentMethodTypesAsync(cancellationToken);
        await SeedBanksAsync(cancellationToken);
        await SeedTicketCategoriesAsync(cancellationToken);
        await SeedPermissionsAndRolesAsync(cancellationToken);
        await SeedSuperAdminAsync(cancellationToken);
        await SeedLandingContentAsync(cancellationToken);
        await SeedSocialLinksAsync(cancellationToken);

        _logger.LogInformation("Seeding completed.");
    }

    private sealed record WorldData(
        IReadOnlyList<WorldCurrency> Currencies,
        IReadOnlyList<WorldCountry> Countries);

    private sealed record WorldCurrency(string Code, string Symbol, string En, string Ar);

    private sealed record WorldCountry(
        string Code,
        string En,
        string Ar,
        IReadOnlyList<string> Currencies);

    /// <summary>
    /// Seeds the full set of world countries and currencies and maps each country to the currencies
    /// it uses. Additive and idempotent: it matches on the ISO code, so the demo rows already seeded
    /// (Egypt, EGP, USD, with their fixed identifiers) are updated in place rather than duplicated,
    /// and re-running against a populated database inserts only what is missing.
    /// </summary>
    private async Task SeedWorldLookupsAsync(CancellationToken cancellationToken)
    {
        var data = LoadWorldData();
        if (data is null)
        {
            _logger.LogWarning("World lookup seed data was not found; skipping.");
            return;
        }

        // --- currencies -----------------------------------------------------
        var currencyByCode = (await _db.Currencies.ToListAsync(cancellationToken))
            .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var currency in data.Currencies)
        {
            if (currencyByCode.TryGetValue(currency.Code, out var existing))
            {
                existing.NameEn = currency.En;
                existing.NameAr = currency.Ar;
                existing.Symbol = currency.Symbol;
            }
            else
            {
                var created = new Currency
                {
                    Code = currency.Code,
                    NameEn = currency.En,
                    NameAr = currency.Ar,
                    Symbol = currency.Symbol,
                };
                _db.Currencies.Add(created);
                currencyByCode[currency.Code] = created;
            }
        }

        // --- countries ------------------------------------------------------
        var countryByCode = (await _db.Countries.ToListAsync(cancellationToken))
            .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var country in data.Countries)
        {
            if (countryByCode.TryGetValue(country.Code, out var existing))
            {
                existing.NameEn = country.En;
                existing.NameAr = country.Ar;
            }
            else
            {
                var created = new Country
                {
                    Code = country.Code,
                    NameEn = country.En,
                    NameAr = country.Ar,
                };
                _db.Countries.Add(created);
                countryByCode[country.Code] = created;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // --- country ↔ currency mappings ------------------------------------
        var mapped = (await _db.CountryCurrencies
                .Select(cc => new { cc.CountryId, cc.CurrencyId })
                .ToListAsync(cancellationToken))
            .Select(m => (m.CountryId, m.CurrencyId))
            .ToHashSet();

        foreach (var country in data.Countries)
        {
            var countryEntity = countryByCode[country.Code];
            foreach (var code in country.Currencies)
            {
                if (!currencyByCode.TryGetValue(code, out var currency)) continue;
                if (!mapped.Add((countryEntity.Id, currency.Id))) continue;

                _db.CountryCurrencies.Add(new CountryCurrency
                {
                    CountryId = countryEntity.Id,
                    CurrencyId = currency.Id,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "World lookups seeded: {Countries} countries, {Currencies} currencies.",
            data.Countries.Count,
            data.Currencies.Count);
    }

    private static readonly JsonSerializerOptions WorldDataJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static WorldData? LoadWorldData()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("world-lookups.json");
        if (stream is null) return null;

        return JsonSerializer.Deserialize<WorldData>(stream, WorldDataJsonOptions);
    }

    private async Task SeedClientAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Clients.AnyAsync(c => c.Code == Client.DefaultCode, cancellationToken))
        {
            _db.Clients.Add(new Client
            {
                Id = Ids.NenClient,
                Code = Client.DefaultCode,
                Name = "NEN",
                IsLocalOrder = true,
            });

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded default client {ClientCode}.", Client.DefaultCode);
        }

        await EnsureLocalOrderClientAsync(cancellationToken);
    }

    /// <summary>
    /// Guarantees the one local-order client the platform is supposed to have.
    ///
    /// A database that predates the flag has it on nobody, and the admin panel cannot fix that by
    /// itself: clearing the flag is refused, but there is nothing to clear, and the platform would
    /// sit with none until an operator noticed. The default client gets it, or the oldest one if
    /// this deployment has retired the default.
    /// </summary>
    private async Task EnsureLocalOrderClientAsync(CancellationToken cancellationToken)
    {
        if (await _db.Clients.AnyAsync(c => c.IsLocalOrder, cancellationToken))
        {
            return;
        }

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Code == Client.DefaultCode, cancellationToken)
            ?? await _db.Clients.OrderBy(c => c.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);

        if (client is null) return;

        client.IsLocalOrder = true;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Local-order client set to {ClientCode}.", client.Code);
    }

    /// <summary>
    /// A starting set for the "Addressed to" list, so a fresh install offers something rather than
    /// an empty dropdown. Seeded once by name: an operator who renames or removes one is not
    /// overruled on the next boot.
    /// </summary>
    private async Task SeedAddresseesAsync(CancellationToken cancellationToken)
    {
        if (await _db.Addressees.AnyAsync(cancellationToken)) return;

        _db.Addressees.AddRange(
            new Addressee { NameEn = "Ministry of Foreign Affairs", NameAr = "وزارة الخارجية", SortOrder = 10 },
            new Addressee { NameEn = "Ministry of Higher Education", NameAr = "وزارة التعليم العالي", SortOrder = 20 },
            new Addressee { NameEn = "Ministry of Health", NameAr = "وزارة الصحة", SortOrder = 30 },
            new Addressee { NameEn = "Embassy", NameAr = "السفارة", SortOrder = 40 },
            new Addressee { NameEn = "Consulate", NameAr = "القنصلية", SortOrder = 50 },
            new Addressee { NameEn = "Employer", NameAr = "جهة العمل", SortOrder = 60 },
            new Addressee { NameEn = "University", NameAr = "الجامعة", SortOrder = 70 });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded the default addressee list.");
    }

    private async Task SeedCountriesAndCurrenciesAsync(CancellationToken cancellationToken)
    {
        var egypt = await _db.Countries.FirstOrDefaultAsync(c => c.Id == Ids.Egypt, cancellationToken);
        if (egypt is null)
        {
            _db.Countries.Add(new Country
            {
                Id = Ids.Egypt,
                Code = "EG",
                PhoneCode = "+20",
                NameAr = "مصر",
                NameEn = "Egypt",
            });
        }
        else if (string.IsNullOrWhiteSpace(egypt.PhoneCode))
        {
            egypt.PhoneCode = "+20";
        }

        if (!await _db.Currencies.AnyAsync(c => c.Id == Ids.Egp, cancellationToken))
        {
            _db.Currencies.Add(new Currency
            {
                Id = Ids.Egp,
                Code = "EGP",
                NameAr = "جنيه مصري",
                NameEn = "Egyptian Pound",
                Symbol = "ج.م",
            });
        }

        if (!await _db.Currencies.AnyAsync(c => c.Id == Ids.Usd, cancellationToken))
        {
            _db.Currencies.Add(new Currency
            {
                Id = Ids.Usd,
                Code = "USD",
                NameAr = "دولار أمريكي",
                NameEn = "US Dollar",
                Symbol = "$",
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var currencyId in new[] { Ids.Egp, Ids.Usd })
        {
            var alreadyMapped = await _db.CountryCurrencies
                .AnyAsync(cc => cc.CountryId == Ids.Egypt && cc.CurrencyId == currencyId, cancellationToken);

            if (!alreadyMapped)
            {
                _db.CountryCurrencies.Add(new CountryCurrency
                {
                    CountryId = Ids.Egypt,
                    CurrencyId = currencyId,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds the payment-type catalogue: the providers an operator would otherwise have to describe
    /// by hand before they could configure a single method. Only the flags are seeded — the methods
    /// themselves, with their countries, currencies and receiving numbers, are the admin's to write.
    ///
    /// Keyed on stable identifiers and skipped per row, so an operator who has since retuned a
    /// type's flags keeps their version.
    /// </summary>
    private async Task SeedPaymentMethodTypesAsync(CancellationToken cancellationToken)
    {
        // Every requirement is a choice now, so every one is listed: what a provider needs is
        // configuration rather than something the kind decides.
        var types = new (Guid Id, string Ar, string En, PaymentMethodKind Kind,
            bool Account, bool Barcode, bool Bank, bool Url,
            bool Proof, bool Reference, int Order)[]
        {
            (Ids.PayTypeVodafoneCash, "فودافون كاش", "Vodafone Cash",
                PaymentMethodKind.Transfer,
                Account: true, Barcode: false, Bank: false, Url: false,
                Proof: true, Reference: true, Order: 0),

            (Ids.PayTypeInstapay, "إنستاباي", "InstaPay",
                PaymentMethodKind.Transfer,
                Account: true, Barcode: true, Bank: false, Url: false,
                Proof: true, Reference: true, Order: 1),

            (Ids.PayTypeBankTransfer, "تحويل بنكي", "Bank Transfer",
                PaymentMethodKind.Transfer,
                Account: true, Barcode: false, Bank: true, Url: false,
                Proof: true, Reference: true, Order: 2),

            // Still a transfer: the applicant is sent somewhere to pay and tells us they did.
            // The link is the one thing it needs, and that is now a switch rather than a kind.
            (Ids.PayTypeExternalLink, "رابط دفع خارجي", "External Payment Link",
                PaymentMethodKind.Transfer,
                Account: false, Barcode: false, Bank: false, Url: true,
                Proof: false, Reference: false, Order: 3),

            (Ids.PayTypePayPal, "باي بال", "PayPal",
                PaymentMethodKind.PayPal,
                Account: false, Barcode: false, Bank: false, Url: false,
                Proof: false, Reference: false, Order: 4),
        };

        foreach (var (id, ar, en, kind, account, barcode, bank, url, proof, reference, order) in types)
        {
            if (await _db.PaymentMethodTypes.AnyAsync(t => t.Id == id, cancellationToken))
            {
                continue;
            }

            _db.PaymentMethodTypes.Add(new PaymentMethodType
            {
                Id = id,
                NameAr = ar,
                NameEn = en,
                Kind = kind,
                RequiresAccountNumber = account,
                RequiresBarcode = barcode,
                RequiresBank = bank,
                RequiresExternalUrl = url,
                RequiresProofDocument = proof,
                RequiresReferenceNumber = reference,
                SortOrder = order,
                IsActive = true,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A starting set of contact-form categories.
    ///
    /// Idempotent by id, and only ever inserts what is missing: the list is a starting point, not
    /// an authority. Support reshapes their own intake from the admin page, and a rename made there
    /// must survive the next restart.
    /// </summary>
    private async Task SeedTicketCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = new (Guid Id, string Ar, string En, int Order)[]
        {
            (Ids.TicketCategoryGeneral, "استفسار عام", "General enquiry", 0),
            (Ids.TicketCategoryApplication, "استفسار عن طلب", "Question about an application", 1),
            (Ids.TicketCategoryPayment, "الدفع والمحفظة", "Payments and wallet", 2),
            (Ids.TicketCategoryTechnical, "مشكلة تقنية", "Technical problem", 3),
            (Ids.TicketCategoryComplaint, "شكوى", "Complaint", 4),
        };

        foreach (var (id, ar, en, order) in categories)
        {
            if (await _db.TicketCategories.AnyAsync(c => c.Id == id, cancellationToken))
            {
                continue;
            }

            _db.TicketCategories.Add(new TicketCategory
            {
                Id = id,
                NameAr = ar,
                NameEn = en,
                SortOrder = order,
                IsActive = true,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record BankSeedFile(string CountryCode, IReadOnlyList<BankSeed> Banks);

    private sealed record BankSeed(string En, string Ar, string? Swift);

    /// <summary>
    /// Seeds the bank catalogue for the country named in the seed file.
    ///
    /// Additive and idempotent: matched on the English name within the country, so an operator who
    /// has since corrected a name, a SWIFT code or an ordering keeps their version, and only banks
    /// that are missing get inserted. The list is a starting point, not an authority — the whole
    /// point of the lookup is that corrections are an edit rather than a deployment.
    /// </summary>
    private async Task SeedBanksAsync(CancellationToken cancellationToken)
    {
        var data = LoadEmbedded<BankSeedFile>("egypt-banks.json");
        if (data is null)
        {
            _logger.LogWarning("Bank seed data was not found; skipping.");
            return;
        }

        var country = await _db.Countries
            .FirstOrDefaultAsync(c => c.Code == data.CountryCode, cancellationToken);

        if (country is null)
        {
            _logger.LogWarning(
                "Country {Code} is not seeded yet; skipping its banks.", data.CountryCode);
            return;
        }

        var existing = await _db.Banks
            .Where(bank => bank.CountryId == country.Id)
            .Select(bank => bank.NameEn)
            .ToListAsync(cancellationToken);

        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inserted = 0;

        for (var index = 0; index < data.Banks.Count; index++)
        {
            var bank = data.Banks[index];
            if (known.Contains(bank.En))
            {
                continue;
            }

            _db.Banks.Add(new Bank
            {
                CountryId = country.Id,
                NameEn = bank.En,
                NameAr = bank.Ar,
                SwiftCode = string.IsNullOrWhiteSpace(bank.Swift) ? null : bank.Swift,
                SortOrder = index,
                IsActive = true,
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded {Count} banks for {Country}.", inserted, data.CountryCode);
        }
    }

    /// <summary>Reads one of the embedded seed files, or null when it is not present.</summary>
    private static T? LoadEmbedded<T>(string resourceName)
        where T : class
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private async Task SeedTransactionCascadeAsync(CancellationToken cancellationToken)
    {
        var transactionTypes = new (Guid Id, string Ar, string En)[]
        {
            (Ids.TxEducational, "التحقق من الشهادات الدراسية", "Educational Certificate Verification"),
            (Ids.TxProfessional, "التحقق من الشهادات المهنية", "Professional Certificate Verification"),
            (Ids.TxSecurity, "التحقق من الشهادات الأمنية", "Security Certificate Verification"),
        };

        foreach (var (id, ar, en) in transactionTypes)
        {
            var transactionType = await _db.TransactionTypes
                .Include(type => type.CountryLinks)
                .FirstOrDefaultAsync(type => type.Id == id, cancellationToken);

            if (transactionType is null)
            {
                transactionType = new TransactionType
                {
                    Id = id,
                    Code = Ids.Codes[id],
                    NameAr = ar,
                    NameEn = en,
                };
                _db.TransactionTypes.Add(transactionType);
            }

            if (transactionType.CountryLinks.All(link => link.CountryId != Ids.Egypt))
            {
                transactionType.CountryLinks.Add(new TransactionTypeCountry
                {
                    TransactionTypeId = id,
                    CountryId = Ids.Egypt,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var subTypes = new (Guid Id, Guid ParentId, string Ar, string En)[]
        {
            (Ids.SubBachelor, Ids.TxEducational, "بكالوريوس", "Bachelor's Degree"),
            (Ids.SubMaster, Ids.TxEducational, "ماجستير", "Master's Degree"),
            (Ids.SubDoctorate, Ids.TxEducational, "دكتوراه", "Doctorate"),
            (Ids.SubExperience, Ids.TxProfessional, "شهادة الخبرة", "Experience Certificate"),
            (Ids.SubCriminalRecord, Ids.TxSecurity, "فيش جنائي", "Criminal Record Check"),
            (Ids.SubMilitaryService, Ids.TxSecurity, "شهادة الخدمة العسكرية", "Military Service Certificate"),
        };

        foreach (var (id, parentId, ar, en) in subTypes)
        {
            if (await _db.SubTransactionTypes.AnyAsync(s => s.Id == id, cancellationToken))
            {
                continue;
            }

            _db.SubTransactionTypes.Add(new SubTransactionType
            {
                Id = id,
                Code = Ids.Codes[id],
                TransactionTypeId = parentId,
                NameAr = ar,
                NameEn = en,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (!await _db.VerificationAuthorities.AnyAsync(a => a.Id == Ids.AuthoritySupremeCouncil, cancellationToken))
        {
            _db.VerificationAuthorities.Add(new VerificationAuthority
            {
                Id = Ids.AuthoritySupremeCouncil,
                Code = Ids.Codes[Ids.AuthoritySupremeCouncil],
                CountryId = Ids.Egypt,
                NameAr = "المجلس الأعلى للجامعات",
                NameEn = "Supreme Council of Universities",
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        // The seeded authority handles the three educational sub-types.
        foreach (var subTypeId in new[] { Ids.SubBachelor, Ids.SubMaster, Ids.SubDoctorate })
        {
            var mapped = await _db.AuthoritySubTransactionTypes.AnyAsync(
                link => link.VerificationAuthorityId == Ids.AuthoritySupremeCouncil
                        && link.SubTransactionTypeId == subTypeId,
                cancellationToken);

            if (!mapped)
            {
                _db.AuthoritySubTransactionTypes.Add(new AuthoritySubTransactionType
                {
                    VerificationAuthorityId = Ids.AuthoritySupremeCouncil,
                    SubTransactionTypeId = subTypeId,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await SeedServiceTypeAsync(
            Ids.ServiceStandardVerification,
            Ids.SubBachelor,
            "التحقق القياسي من الشهادة",
            "Standard Certificate Verification",
            "التحقق من صحة الشهادة الدراسية لدى الجهة المصدرة.",
            "Verification of an academic certificate's authenticity with the issuing authority.",
            executionTimeDays: 14,
            cost: 750m,
            enableExpress: false,
            expressCost: 0m,
            requiredFiles:
            [
                (Ids.FileStandardCertificate, "صورة الشهادة", "Certificate copy", true),
                (Ids.FileStandardIdCopy, "صورة إثبات الهوية", "Identity document copy", true),
            ],
            cancellationToken);

        await SeedServiceTypeAsync(
            Ids.ServiceAttestedVerification,
            Ids.SubBachelor,
            "التحقق مع التصديق",
            "Attested Verification",
            "التحقق من الشهادة مع إصدار إفادة مصدقة، مع إمكانية التنفيذ المستعجل.",
            "Certificate verification with an official attested statement issued, with express processing available.",
            executionTimeDays: 21,
            cost: 1500m,
            enableExpress: true,
            expressCost: 600m,
            requiredFiles:
            [
                (Ids.FileAttestedCertificate, "صورة الشهادة", "Certificate copy", true),
                (Ids.FileAttestedTranscript, "بيان الدرجات", "Academic transcript", false),
            ],
            cancellationToken);
    }

    private async Task SeedServiceTypeAsync(
        Guid id,
        Guid subTransactionTypeId,
        string nameAr,
        string nameEn,
        string descriptionAr,
        string descriptionEn,
        int executionTimeDays,
        decimal cost,
        bool enableExpress,
        decimal expressCost,
        (Guid Id, string Ar, string En, bool Mandatory)[] requiredFiles,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ServiceTypes
            .Include(s => s.Costs)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        // Backfill the bilingual descriptions on rows seeded before the description was split into
        // Arabic/English, without clobbering copy an admin has since edited.
        if (existing is not null)
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(existing.DescriptionAr)) { existing.DescriptionAr = descriptionAr; changed = true; }
            if (string.IsNullOrWhiteSpace(existing.DescriptionEn)) { existing.DescriptionEn = descriptionEn; changed = true; }
            if (existing.SubTransactionTypeId == Guid.Empty)
            {
                existing.SubTransactionTypeId = subTransactionTypeId;
                changed = true;
            }
            if (changed) await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _db.ServiceTypes.Add(new ServiceType
            {
                Id = id,
                Code = Ids.Codes[id],
                VerificationAuthorityId = Ids.AuthoritySupremeCouncil,
                SubTransactionTypeId = subTransactionTypeId,
                NameAr = nameAr,
                NameEn = nameEn,
                DescriptionAr = descriptionAr,
                DescriptionEn = descriptionEn,
                ExecutionTimeDays = executionTimeDays,
                Cost = cost,
                EnableExpress = enableExpress,
                ExpressCost = expressCost,
            });

            await _db.SaveChangesAsync(cancellationToken);
            existing = await _db.ServiceTypes
                .Include(s => s.Costs)
                .FirstAsync(s => s.Id == id, cancellationToken);
        }

        foreach (var currencyId in new[] { Ids.Egp, Ids.Usd })
        {
            if (existing!.Costs.Any(c => c.CurrencyId == currencyId)) continue;

            existing.Costs.Add(new ServiceTypeCost
            {
                ServiceTypeId = id,
                CurrencyId = currencyId,
                Cost = currencyId == Ids.Usd ? Math.Round(cost / 50m, 2) : cost,
                ExpressCost = enableExpress
                    ? (currencyId == Ids.Usd ? Math.Round(expressCost / 50m, 2) : expressCost)
                    : 0m,
            });
        }

        foreach (var (fileId, ar, en, mandatory) in requiredFiles)
        {
            if (await _db.ServiceTypeRequiredFiles.AnyAsync(f => f.Id == fileId, cancellationToken))
            {
                continue;
            }

            _db.ServiceTypeRequiredFiles.Add(new ServiceTypeRequiredFile
            {
                Id = fileId,
                ServiceTypeId = id,
                NameAr = ar,
                NameEn = en,
                IsMandatory = mandatory,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedPermissionsAndRolesAsync(CancellationToken cancellationToken)
    {
        // The catalogue itself is reconciled on every boot, in every environment, by
        // DatabaseInitializer. Seeding runs it again because a fresh development database is
        // created and seeded in the same pass, and it is cheap when there is nothing to do.
        await PermissionCatalogue.SyncAsync(_db, cancellationToken);

        var superAdmin = await EnsureRoleAsync(
            Ids.SuperAdminRole,
            SystemRoles.SuperAdmin,
            "Full access to every module.",
            cancellationToken);

        var reviewer = await EnsureRoleAsync(
            Ids.ReviewerRole,
            SystemRoles.Reviewer,
            "Reviews applications and communicates with applicants.",
            cancellationToken);

        // SuperAdmin holds every permission; Reviewer only what the review workflow needs: read the
        // lookups the queue references, and work applications and their orders — but no create,
        // update or delete on any lookup, and nothing under Administration.
        await AssignPermissionsAsync(superAdmin, Permissions.Names.ToArray(), cancellationToken);
        await AssignPermissionsAsync(
            reviewer,
            [
                Permissions.DashboardView,
                Permissions.ApplicationsView,
                Permissions.ApplicationsReview,
                Permissions.ApplicationsAttachResults,
                Permissions.OrdersView,
                .. Permissions.AllLookupViews,
            ],
            cancellationToken);
    }

    private async Task<Role> EnsureRoleAsync(
        Guid id,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (role is not null)
        {
            return role;
        }

        role = new Role { Id = id, Name = name, Description = description, IsSystemRole = true };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken);
        return role;
    }

    private async Task AssignPermissionsAsync(
        Role role,
        IReadOnlyCollection<string> permissionNames,
        CancellationToken cancellationToken)
    {
        var permissions = await _db.Permissions
            .Where(p => permissionNames.Contains(p.Name))
            .ToListAsync(cancellationToken);

        var alreadyAssigned = await _db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);

        var assignedSet = alreadyAssigned.ToHashSet();

        foreach (var permission in permissions.Where(p => !assignedSet.Contains(p.Id)))
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var email = _configuration["Seed:SuperAdmin:Email"] ?? "admin@dataverification.local";
        var username = (_configuration["Seed:SuperAdmin:Username"] ?? "admin").ToLowerInvariant();
        var password = _configuration["Seed:SuperAdmin:Password"] ?? "Admin#12345";
        var hasher = new PasswordHasher<AdminUser>();

        // Repair rather than skip. The previous version returned early whenever a row with this
        // email already existed, so a SuperAdmin left in a bad state by an earlier run was never
        // fixed: a drifted or empty password rejected the documented credentials forever, and a
        // crash between the two original saves could leave the account with no role — able to sign
        // in but seeing an empty panel (no Lookups, no Services, nothing).
        //
        // Seeding runs in Development only (see Program.cs), so restoring the demo SuperAdmin to a
        // known-good state on every boot is safe and is what makes the documented login reliable.
        var admin = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        var isNew = admin is null;

        if (admin is null)
        {
            admin = new AdminUser
            {
                Id = Ids.SuperAdminUser,
                Email = email,
                Username = username,
                FullName = "Platform Administrator",
                PasswordHash = string.Empty,
            };
            _db.AdminUsers.Add(admin);
        }

        // Always restore the account to a usable state: correct password, active, not locked out.
        admin.Username = username;
        admin.PasswordHash = hasher.HashPassword(admin, password);
        admin.IsActive = true;
        admin.FailedLoginAttempts = 0;
        admin.LockoutEndsAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);

        // Direct grants are the authorization source of truth. Always restore SuperAdmin's full
        // catalogue so a drifted demo account cannot sign in with an empty sidebar.
        await AssignUserPermissionsAsync(admin, Permissions.Names.ToArray(), cancellationToken);

        _logger.LogWarning(
            "{Action} SuperAdmin {Email}. Change this password before deploying anywhere real.",
            isNew ? "Seeded" : "Restored",
            email);
    }

    private async Task AssignUserPermissionsAsync(
        AdminUser user,
        IReadOnlyCollection<string> permissionNames,
        CancellationToken cancellationToken)
    {
        var permissions = await _db.Permissions
            .Where(p => permissionNames.Contains(p.Name))
            .ToListAsync(cancellationToken);

        var alreadyAssigned = await _db.AdminUserPermissions
            .Where(up => up.AdminUserId == user.Id)
            .Select(up => up.PermissionId)
            .ToListAsync(cancellationToken);

        var assignedSet = alreadyAssigned.ToHashSet();

        foreach (var permission in permissions.Where(p => !assignedSet.Contains(p.Id)))
        {
            _db.AdminUserPermissions.Add(new AdminUserPermission
            {
                AdminUserId = user.Id,
                PermissionId = permission.Id,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds the landing "features" section so the site is populated out of the box. Additive and
    /// idempotent: each card is keyed on a fixed identifier and the heading on its setting key, so
    /// re-running never duplicates and never overwrites copy an admin has since edited.
    /// </summary>
    private async Task SeedLandingContentAsync(CancellationToken cancellationToken)
    {
        var features = new (Guid Id, string Icon, int Order,
            string TitleEn, string BodyEn, string TitleAr, string BodyAr)[]
        {
            (Ids.LandingFeatureTimeline, "timeline", 0,
                "Live status timeline",
                "Every comment, status change, and file event in one activity feed. Nothing happens behind your back.",
                "سجل الحالة المباشر",
                "كل تعليق وتغيير حالة وحدث ملف في سجل نشاط واحد. لا شيء يحدث دون علمك."),
            (Ids.LandingFeatureWallet, "wallet", 1,
                "Wallet with a full ledger",
                "Every top-up, payment, and refund is a ledger entry you can audit. Refund pending applications in one tap.",
                "محفظة بسجل كامل",
                "كل عملية شحن ودفع واسترداد هي قيد في السجل يمكنك مراجعته. استرد الطلبات المعلّقة بنقرة واحدة."),
            (Ids.LandingFeatureLanguage, "language", 2,
                "Multi-language, RTL native",
                "Ten languages including Arabic with true right-to-left layouts across the wizard, tables, and timeline.",
                "متعدد اللغات ويدعم الاتجاه من اليمين لليسار",
                "عشر لغات من بينها العربية بتخطيط كامل من اليمين لليسار عبر المعالج والجداول وسجل النشاط."),
            (Ids.LandingFeatureSecurity, "security", 3,
                "Bank-grade credentials",
                "Cryptographically generated order numbers and passwords. Your order is scoped, isolated, and rate-limited.",
                "بيانات اعتماد بمستوى بنكي",
                "أرقام طلبات وكلمات مرور مُولّدة تشفيريًا. طلبك معزول ومحدود المعدل."),
        };

        foreach (var (id, icon, order, titleEn, bodyEn, titleAr, bodyAr) in features)
        {
            var existing = await _db.LandingFeatures
                .Include(f => f.Translations)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

            if (existing is null)
            {
                _db.LandingFeatures.Add(new LandingFeature
                {
                    Id = id,
                    Icon = icon,
                    SortOrder = order,
                    IsPublished = true,
                    Translations =
                    {
                        new LandingFeatureTranslation { LanguageCode = "en", Title = titleEn, Body = bodyEn },
                        new LandingFeatureTranslation { LanguageCode = "ar", Title = titleAr, Body = bodyAr },
                    },
                });

                continue;
            }

            // Backfill a seeded language only when it is missing, so admin-edited copy is never lost.
            foreach (var (lang, cardTitle, cardBody) in new[] { ("en", titleEn, bodyEn), ("ar", titleAr, bodyAr) })
            {
                if (!existing.Translations.Any(x =>
                        string.Equals(x.LanguageCode, lang, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Translations.Add(new LandingFeatureTranslation
                    {
                        LanguageCode = lang,
                        Title = cardTitle,
                        Body = cardBody,
                    });
                }
            }
        }

        var headings = new (string Key, string Value)[]
        {
            (LandingContentDefaults.EyebrowKey("en"), LandingContentDefaults.Eyebrow),
            (LandingContentDefaults.TitleKey("en"), LandingContentDefaults.Title),
            (LandingContentDefaults.EyebrowKey("ar"), "صُمّم لراحة البال"),
            (LandingContentDefaults.TitleKey("ar"), "كل ما يخص عملية التحقق، بكل شفافية"),
            (LandingContentDefaults.TrustTitleKey("en"), LandingContentDefaults.TrustTitle),
            (LandingContentDefaults.TrustTitleKey("ar"), "موثوق للتحقق مع"),
            (LandingContentDefaults.HowEyebrowKey("en"), LandingContentDefaults.HowEyebrow),
            (LandingContentDefaults.HowEyebrowKey("ar"), "كيف تتم العملية"),
            (LandingContentDefaults.HowTitleKey("en"), LandingContentDefaults.HowTitle),
            (LandingContentDefaults.HowTitleKey("ar"), "من البريد الإلكتروني إلى مستند موثق في ثلاث خطوات"),
            (FooterContentDefaults.SubtitleKey("en"), FooterContentDefaults.Subtitle),
            (FooterContentDefaults.SubtitleKey("ar"), "التحقق الرسمي من المستندات عبر جهات حكومية موثوقة."),
            (FooterContentDefaults.ExploreHeadingKey("en"), FooterContentDefaults.ExploreHeading),
            (FooterContentDefaults.ExploreHeadingKey("ar"), "استكشف"),
            (FooterContentDefaults.AccountHeadingKey("en"), FooterContentDefaults.AccountHeading),
            (FooterContentDefaults.AccountHeadingKey("ar"), "الحساب"),
            (FooterContentDefaults.OrganisationHeadingKey("en"), FooterContentDefaults.OrganisationHeading),
            (FooterContentDefaults.OrganisationHeadingKey("ar"), "المؤسسة"),
            (CoverageContentDefaults.TitleKey("en"), CoverageContentDefaults.Title),
            (CoverageContentDefaults.TitleKey("ar"), "أين نتحقق"),
            (CoverageContentDefaults.SubtitleKey("en"), CoverageContentDefaults.Subtitle),
            (CoverageContentDefaults.SubtitleKey("ar"),
                "نعمل مباشرة مع الجهات المُصدِرة في هذه الدول، ونضيف المزيد."),
            (LandingContentDefaults.CtaTitleKey("en"), LandingContentDefaults.CtaTitle),
            (LandingContentDefaults.CtaTitleKey("ar"), "مستنداتك، موثّقة من المصدر."),
            (LandingContentDefaults.CtaBodyKey("en"), LandingContentDefaults.CtaBody),
            (LandingContentDefaults.CtaBodyKey("ar"),
                "ابدأ ببريدك الإلكتروني فقط. ستصلك بيانات الدخول إلى طلبك خلال دقيقة."),
            (LandingContentDefaults.CtaButtonKey("en"), LandingContentDefaults.CtaButton),
            (LandingContentDefaults.CtaButtonKey("ar"), "ابدأ التحقق الآن"),
            (LandingContentDefaults.CtaLinkKey, LandingContentDefaults.CtaLink),
        };

        // The wizard's step headings are seeded the same way, so the panel that edits them opens on
        // the copy the applicant actually sees rather than on empty boxes.
        foreach (var (key, value) in headings.Concat(WizardContentDefaults.SeedRows()))
        {
            if (await _db.SiteSettings.AnyAsync(s => s.Key == key, cancellationToken))
            {
                continue;
            }

            _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }

        await SeedLandingStatsAsync(cancellationToken);
        await SeedLandingTrustEntriesAsync(cancellationToken);
        await SeedLandingStepsAsync(cancellationToken);
        await SeedFooterLinksAsync(cancellationToken);
        await SeedHeaderLinksAsync(cancellationToken);
        await SeedCoverageAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Landing content seeded.");
    }

    /// <summary>
    /// Seeds the footer's three columns with the links the site carried hard-coded, so moving them
    /// into the database does not empty the footer on first deployment.
    ///
    /// The Account rows keep their visibility: a stranger sees Register and Sign in, someone signed
    /// in sees their own pages. Flattening them into one list would have shown everyone both.
    ///
    /// Runs only when the table is empty, so an operator's edits and deletions survive a restart.
    /// </summary>
    private async Task SeedFooterLinksAsync(CancellationToken cancellationToken)
    {
        if (await _db.FooterLinks.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaults = new (FooterColumn Column, FooterLinkVisibility Visibility, string Url, string En, string Ar, int Sort)[]
        {
            (FooterColumn.Explore, FooterLinkVisibility.Everyone, "#how", "How it works", "كيف تتم العملية", 0),
            (FooterColumn.Explore, FooterLinkVisibility.Everyone, "#services", "Services", "الخدمات", 1),
            (FooterColumn.Explore, FooterLinkVisibility.Everyone, "#coverage", "Coverage", "التغطية", 2),
            (FooterColumn.Explore, FooterLinkVisibility.Everyone, "/tools", "Tool", "الأدوات", 3),
            (FooterColumn.Explore, FooterLinkVisibility.Everyone, "/contact", "Contact us", "اتصل بنا", 4),

            (FooterColumn.Account, FooterLinkVisibility.SignedOut, "/register", "Create an order", "إنشاء طلب", 0),
            (FooterColumn.Account, FooterLinkVisibility.SignedOut, "/login", "Follow order", "متابعة الطلب", 1),
            (FooterColumn.Account, FooterLinkVisibility.SignedIn, "/applications", "My applications", "طلباتي", 2),
            (FooterColumn.Account, FooterLinkVisibility.SignedIn, "/applications/new", "New Order", "طلب جديد", 3),
            (FooterColumn.Account, FooterLinkVisibility.SignedIn, "/wallet", "Wallet", "المحفظة", 4),

            (FooterColumn.Organisation, FooterLinkVisibility.Everyone, "https://nen-global.org", "NEN Global", "NEN Global", 0),
            (FooterColumn.Organisation, FooterLinkVisibility.Everyone, "https://itep.nen-global.org", "iTEP Uzbekistan", "iTEP Uzbekistan", 1),
            (FooterColumn.Organisation, FooterLinkVisibility.Everyone, "https://www.itepexam.com", "iTEP International", "iTEP International", 2),
            (FooterColumn.Organisation, FooterLinkVisibility.Everyone, "https://www.itepexam.com/products/", "iTEP Products", "iTEP Products", 3),
            (FooterColumn.Organisation, FooterLinkVisibility.Everyone, "https://www.iteptest.com/test_mod/verify/test_verification.php", "Score Verification", "التحقق من الدرجات", 4),
        };

        foreach (var (column, visibility, url, en, ar, sort) in defaults)
        {
            _db.FooterLinks.Add(new FooterLink
            {
                Column = column,
                Visibility = visibility,
                Url = url,
                SortOrder = sort,
                IsActive = true,
                Translations =
                [
                    new FooterLinkTranslation { LanguageCode = "en", Label = en },
                    new FooterLinkTranslation { LanguageCode = "ar", Label = ar },
                ],
            });
        }

        _logger.LogInformation("Footer links seeded: {Count} rows.", defaults.Length);
    }

    /// <summary>
    /// Seeds the header with the entries the site ships with, in the order it ships them.
    ///
    /// No labels: every one of these is already translated inside the web app, and copying the
    /// English wording in here would replace nine other languages with it. A label written in the
    /// panel later overrides the app's, one language at a time.
    ///
    /// Runs only when the table is empty, so an operator's arrangement survives a restart.
    /// </summary>
    private async Task SeedHeaderLinksAsync(CancellationToken cancellationToken)
    {
        if (await _db.HeaderLinks.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var (key, visibility, url, sort) in HeaderContentDefaults.Rows())
        {
            _db.HeaderLinks.Add(new HeaderLink
            {
                Key = key,
                Visibility = visibility,
                Url = url,
                SortOrder = sort,
                IsActive = true,
            });
        }

        _logger.LogInformation("Header links seeded: {Count} rows.", HeaderContentDefaults.Rows().Count);
    }

    /// <summary>
    /// Seeds the "how it works" steps with the copy the landing page carried hard-coded, so moving
    /// them into the database does not empty the section on first deployment.
    ///
    /// Runs only when the table is empty, so an operator's edits and deletions survive a restart.
    /// </summary>
    private async Task SeedLandingStepsAsync(CancellationToken cancellationToken)
    {
        if (await _db.LandingSteps.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaults = new (string Icon, string TitleEn, string BodyEn, string TitleAr, string BodyAr, int Sort)[]
        {
            ("mail",
                "Register with email only",
                "No forms, no passwords to invent. We send you a secure order number and access key instantly.",
                "أنشئ طلبًا",
                "أدخل بريدك الإلكتروني، وسنرسل إليك رقم الطلب وكلمة المرور على الفور.",
                0),
            ("file",
                "Build your application",
                "A guided wizard walks you through personal details, document type, authority, and required files.",
                "قدّم طلب التحقق",
                "اختر جهة التحقق ونوع الخدمة، ثم ارفع المستندات المطلوبة.",
                1),
            ("search",
                "Track and download results",
                "Follow every status change in a live timeline, chat with reviewers, and download stamped results.",
                "تابع واستلم",
                "تابع سير العمل، وأجب عن استفسارات المراجعين، ثم حمّل ملفاتك بعد التحقق.",
                2),
        };

        foreach (var (icon, titleEn, bodyEn, titleAr, bodyAr, sort) in defaults)
        {
            _db.LandingSteps.Add(new LandingStep
            {
                Icon = icon,
                SortOrder = sort,
                IsPublished = true,
                Translations =
                [
                    new LandingStepTranslation { LanguageCode = "en", Title = titleEn, Body = bodyEn },
                    new LandingStepTranslation { LanguageCode = "ar", Title = titleAr, Body = bodyAr },
                ],
            });
        }

        _logger.LogInformation("Landing steps seeded: {Count} steps.", defaults.Length);
    }

    /// <summary>
    /// Seeds the "trusted for verification with" strip with the names the landing page carried
    /// hard-coded, so moving them into the database does not empty the strip on first deployment.
    ///
    /// Runs only when the table is empty, so an operator's edits and deletions survive a restart.
    /// </summary>
    /// <summary>
    /// Puts the countries the platform actually verifies in on the coverage map, so the section is
    /// not an empty world on first deployment.
    ///
    /// Matched by ISO code against the seeded country lookup rather than created here: a coverage
    /// row is a pointer at a country, and inventing one would give the wizard a country nobody
    /// meant to offer. A code missing from the lookup is skipped rather than failing the seed.
    ///
    /// Runs only when the table is empty, so an operator's edits survive a restart.
    /// </summary>
    private async Task SeedCoverageAsync(CancellationToken cancellationToken)
    {
        if (await _db.CoverageEntries.AnyAsync(cancellationToken))
        {
            return;
        }

        string[] codes = ["EG", "AE", "SA", "UZ", "JO", "KW", "QA", "OM"];

        var countries = await _db.Countries
            .Where(c => codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, cancellationToken);

        for (var i = 0; i < codes.Length; i++)
        {
            if (!countries.TryGetValue(codes[i], out var country)) continue;

            _db.CoverageEntries.Add(new CoverageEntry
            {
                CountryId = country.Id,
                SortOrder = i,
                IsPublished = true,
            });
        }

        _logger.LogInformation("Coverage countries seeded: {Count} rows.", countries.Count);
    }

    private async Task SeedLandingTrustEntriesAsync(CancellationToken cancellationToken)
    {
        if (await _db.LandingTrustEntries.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaults = new (string NameEn, string NameAr, int Sort)[]
        {
            ("Supreme Council of Universities", "المجلس الأعلى للجامعات", 0),
            ("Ministry of Interior", "وزارة الداخلية", 1),
            ("Ministry of Higher Education", "وزارة التعليم العالي", 2),
            ("Chamber of Commerce", "الغرفة التجارية", 3),
            ("KHDA Dubai", "هيئة المعرفة والتنمية البشرية بدبي", 4),
        };

        foreach (var (nameEn, nameAr, sort) in defaults)
        {
            _db.LandingTrustEntries.Add(new LandingTrustEntry
            {
                Icon = "authority",
                SortOrder = sort,
                IsPublished = true,
                Translations =
                [
                    new LandingTrustEntryTranslation { LanguageCode = "en", Name = nameEn },
                    new LandingTrustEntryTranslation { LanguageCode = "ar", Name = nameAr },
                ],
            });
        }

        _logger.LogInformation("Landing trust strip seeded: {Count} entries.", defaults.Length);
    }

    /// <summary>
    /// Seeds the statistics strip with the figures the landing page carried hard-coded, so moving
    /// them into the database does not empty the strip on the first deployment.
    ///
    /// Runs only when the table is empty. An operator who deletes a figure keeps it deleted, and
    /// one who edits the numbers does not find them reset on the next restart.
    /// </summary>
    private async Task SeedLandingStatsAsync(CancellationToken cancellationToken)
    {
        if (await _db.LandingStats.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaults = new (string Icon, string ValueEn, string LabelEn, string ValueAr, string LabelAr, int Sort)[]
        {
            ("document", "48,000+", "documents verified", "+٤٨٬٠٠٠", "مستند تم التحقق منه", 0),
            ("authority", "120+", "verification authorities", "+١٢٠", "جهة تحقق", 1),
            ("turnaround", "7 days", "average turnaround", "٧ أيام", "متوسط مدة الإنجاز", 2),
        };

        foreach (var (icon, valueEn, labelEn, valueAr, labelAr, sort) in defaults)
        {
            _db.LandingStats.Add(new LandingStat
            {
                Icon = icon,
                SortOrder = sort,
                IsPublished = true,
                Translations =
                [
                    new LandingStatTranslation { LanguageCode = "en", Value = valueEn, Label = labelEn },
                    new LandingStatTranslation { LanguageCode = "ar", Value = valueAr, Label = labelAr },
                ],
            });
        }

        _logger.LogInformation("Landing statistics seeded: {Count} figures.", defaults.Length);
    }

    /// <summary>
    /// Seeds the footer's channels with the addresses the site shipped hard-coded, so moving them
    /// into the database does not empty the footer on the first deployment.
    ///
    /// Additive and idempotent: it matches on placement and platform, and skips any row already
    /// there. An operator who edits or deletes a link keeps their edit — this never writes over an
    /// existing row, so a channel deliberately removed does not come back on the next start.
    /// </summary>
    private async Task SeedSocialLinksAsync(CancellationToken cancellationToken)
    {
        var defaults = new (SocialLinkPlacement Placement, SocialPlatform Platform, string Url, int Sort)[]
        {
            (SocialLinkPlacement.FollowUs, SocialPlatform.Facebook, "https://www.facebook.com/iTEPSLATE", 0),
            (SocialLinkPlacement.FollowUs, SocialPlatform.Telegram, "https://t.me/itep_nen", 1),
            (SocialLinkPlacement.FollowUs, SocialPlatform.Instagram, "https://www.instagram.com/itep_exam", 2),
            (SocialLinkPlacement.FollowUs, SocialPlatform.LinkedIn, "https://www.linkedin.com/company/itep-international", 3),
            (SocialLinkPlacement.FollowUs, SocialPlatform.YouTube, "https://www.youtube.com/user/TheITEPexam", 4),
            (SocialLinkPlacement.FollowUs, SocialPlatform.Vk, "https://vk.com/nenglobal", 5),

            (SocialLinkPlacement.MessageUs, SocialPlatform.WhatsApp, "https://wa.me/+998908227561", 0),
            (SocialLinkPlacement.MessageUs, SocialPlatform.Telegram, "https://t.me/+998908227561", 1),
            (SocialLinkPlacement.MessageUs, SocialPlatform.Messenger, "https://m.me/nenglobal", 2),
        };

        var added = 0;

        foreach (var (placement, platform, url, sort) in defaults)
        {
            var exists = await _db.SocialLinks.AnyAsync(
                link => link.Placement == placement && link.Platform == platform,
                cancellationToken);

            if (exists)
            {
                continue;
            }

            _db.SocialLinks.Add(new SocialLink
            {
                Placement = placement,
                Platform = platform,
                Url = url,
                SortOrder = sort,
                IsActive = true,
            });

            added++;
        }

        if (added == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Footer channels seeded: {Count} added.", added);
    }

    /// <summary>
    /// Stable identifiers for seeded rows. Fixed GUIDs keep the seeder idempotent and let
    /// integration tests reference the demo data without querying for it first.
    /// </summary>
    public static class Ids
    {
        public static readonly Guid NenClient = new("11111111-0000-0000-0000-000000000001");

        public static readonly Guid Egypt = new("22222222-0000-0000-0000-000000000001");
        public static readonly Guid Egp = new("22222222-0000-0000-0000-000000000002");
        public static readonly Guid Usd = new("22222222-0000-0000-0000-000000000003");


        public static readonly Guid TxEducational = new("44444444-0000-0000-0000-000000000001");
        public static readonly Guid TxProfessional = new("44444444-0000-0000-0000-000000000002");
        public static readonly Guid TxSecurity = new("44444444-0000-0000-0000-000000000003");

        public static readonly Guid SubBachelor = new("55555555-0000-0000-0000-000000000001");
        public static readonly Guid SubMaster = new("55555555-0000-0000-0000-000000000002");
        public static readonly Guid SubDoctorate = new("55555555-0000-0000-0000-000000000003");
        public static readonly Guid SubExperience = new("55555555-0000-0000-0000-000000000004");
        public static readonly Guid SubCriminalRecord = new("55555555-0000-0000-0000-000000000005");
        public static readonly Guid SubMilitaryService = new("55555555-0000-0000-0000-000000000006");

        public static readonly Guid AuthoritySupremeCouncil = new("66666666-0000-0000-0000-000000000001");

        public static readonly Guid PayTypeVodafoneCash = new("aaaaaaaa-1111-0000-0000-000000000001");
        public static readonly Guid PayTypeInstapay = new("aaaaaaaa-1111-0000-0000-000000000002");
        public static readonly Guid PayTypeBankTransfer = new("aaaaaaaa-1111-0000-0000-000000000003");
        public static readonly Guid PayTypeExternalLink = new("aaaaaaaa-1111-0000-0000-000000000004");

        public static readonly Guid PayTypePayPal = new("aaaaaaaa-1111-0000-0000-000000000005");

        public static readonly Guid TicketCategoryGeneral = new("bbbbbbbb-1111-0000-0000-000000000001");
        public static readonly Guid TicketCategoryApplication = new("bbbbbbbb-1111-0000-0000-000000000002");
        public static readonly Guid TicketCategoryPayment = new("bbbbbbbb-1111-0000-0000-000000000003");
        public static readonly Guid TicketCategoryTechnical = new("bbbbbbbb-1111-0000-0000-000000000004");
        public static readonly Guid TicketCategoryComplaint = new("bbbbbbbb-1111-0000-0000-000000000005");

        public static readonly Guid ServiceStandardVerification = new("77777777-0000-0000-0000-000000000001");
        public static readonly Guid ServiceAttestedVerification = new("77777777-0000-0000-0000-000000000002");

        /// <summary>
        /// Codes for the seeded catalogue. Declared after the ids it keys on: static fields initialise
        /// in textual order, so placing this above them would key every entry by an empty Guid.
        /// The AddLookupCodes migration backfills existing databases with these same values.
        /// </summary>
        public static readonly IReadOnlyDictionary<Guid, string> Codes = new Dictionary<Guid, string>
        {
            [TxEducational] = "EDU",
            [TxProfessional] = "PRO",
            [TxSecurity] = "SEC",
            [SubBachelor] = "EDU-BA",
            [SubMaster] = "EDU-MA",
            [SubDoctorate] = "EDU-PHD",
            [SubExperience] = "PRO-EXP",
            [SubCriminalRecord] = "SEC-CRIM",
            [SubMilitaryService] = "SEC-MIL",
            [AuthoritySupremeCouncil] = "EG-SCU",
            [ServiceStandardVerification] = "SVC-STD",
            [ServiceAttestedVerification] = "SVC-ATT",
        };

        public static readonly Guid FileStandardCertificate = new("88888888-0000-0000-0000-000000000001");
        public static readonly Guid FileStandardIdCopy = new("88888888-0000-0000-0000-000000000002");
        public static readonly Guid FileAttestedCertificate = new("88888888-0000-0000-0000-000000000003");
        public static readonly Guid FileAttestedTranscript = new("88888888-0000-0000-0000-000000000004");

        public static readonly Guid SuperAdminRole = new("99999999-0000-0000-0000-000000000001");
        public static readonly Guid ReviewerRole = new("99999999-0000-0000-0000-000000000002");
        public static readonly Guid SuperAdminUser = new("99999999-0000-0000-0000-000000000003");

        public static readonly Guid LandingFeatureTimeline = new("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid LandingFeatureWallet = new("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid LandingFeatureLanguage = new("aaaaaaaa-0000-0000-0000-000000000003");
        public static readonly Guid LandingFeatureSecurity = new("aaaaaaaa-0000-0000-0000-000000000004");
    }
}
