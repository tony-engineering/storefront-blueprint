namespace Storefront.Api.Features;

/// <summary>
/// An amount in integer minor units (pence) plus an ISO 4217 currency code.
/// No floating point anywhere, so totals never pick up rounding errors (ARCHITECTURE.md §4).
/// </summary>
public sealed record Money
{
    /// <summary>The only currency the store sells in for now.</summary>
    public const string DefaultCurrency = "GBP";

    public Money(long amountMinor, string currency = DefaultCurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Currency must be a three-letter upper-case ISO 4217 code.", nameof(currency));
        }

        AmountMinor = amountMinor;
        Currency = currency;
    }

    public long AmountMinor { get; private init; }

    public string Currency { get; private init; }

    public static Money Gbp(long amountMinor) => new(amountMinor, DefaultCurrency);
}

/// <summary>Money as it appears on the wire: <c>{ "amountMinor": 4999900, "currency": "GBP" }</c>.</summary>
public sealed record MoneyDto(long AmountMinor, string Currency)
{
    public static MoneyDto From(Money money) => new(money.AmountMinor, money.Currency);
}
