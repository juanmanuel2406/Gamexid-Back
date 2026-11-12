namespace Gamexid.Api.PcBuilder;

public record PcSelection(string Slot, string ProductId, int Quantity = 1, string? OfferId = null, string? ConfirmedCurrency = null);
public record PcRequest(PcSelection[] Items);
public record PcOffer(string Id, string Source, string Title, decimal Amount, string? Currency, string? Url,
    DateTime CheckedUtc, string? ListingId = null);
public record PcPrices(PcOffer[] Offers, string[] Notices);
public record PcCheck(string Status, string Title, string Detail);
public record PcLine(string Slot, GamingCityProduct Product, int Quantity, PcOffer? Offer, string? Currency, decimal? Subtotal);
public record PcTotal(string Currency, decimal Amount);
public record PcQuote(PcLine[] Lines, PcCheck[] Checks, PcTotal[] Totals, int MissingPrices, DateTime CheckedUtc);
