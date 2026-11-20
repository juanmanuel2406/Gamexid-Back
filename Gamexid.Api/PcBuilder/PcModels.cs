namespace Gamexid.Api.PcBuilder;

public record PcSelection(string Slot, string ProductId, int Quantity = 1, string? OfferId = null, string? ConfirmedCurrency = null, bool UseIncludedCooler = false, bool CoolerConfirmed = false);
public record PcRequest(PcSelection[] Items, int? BranchId = null);
public record PcOffer(string Id, string Source, string Title, decimal Amount, string? Currency, string? Url,
    DateTime CheckedUtc, string? ListingId = null);
public record PcPrices(PcOffer[] Offers, string[] Notices, PcCoolingInfo? Cooling = null);
public record PcCheck(string Status, string Title, string Detail);
public record PcLine(string Slot, GamingCityProduct Product, int Quantity, PcOffer? Offer, string? Currency, decimal? Subtotal);
public record PcTotal(string Currency, decimal Amount);
public record PcQuote(PcLine[] Lines, PcCheck[] Checks, PcTotal[] Totals, int MissingPrices, DateTime CheckedUtc, int? BranchId = null);

public record PcStepRequest(string Slot, PcSelection[] Items, int? BranchId = null,
    string? Search = null, int Quantity = 1, bool IncludePending = true);
public record PcOption(GamingCityProduct Product, string Status, PcCheck[] Checks);
public record PcOptions(int BranchId, string BranchName, string Slot, PcOption[] Products,
    bool Truncated, string[] MissingSteps, PcCheck[] SelectionChecks, DateTime CheckedUtc);
