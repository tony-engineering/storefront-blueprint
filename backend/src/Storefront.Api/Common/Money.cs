namespace Storefront.Api.Common;

/// <summary>
/// An amount of money in integer minor units (pence for GBP) plus an ISO 4217 currency code.
/// No floating point anywhere, so totals never drift (ARCHITECTURE.md §4).
/// </summary>
/// <remarks>
/// - the currency is a three-letter upper-case code; anything else throws <see cref="ArgumentException"/>;
/// - amounts are never negative (there are no refunds in this system); a negative amount throws
///   <see cref="ArgumentOutOfRangeException"/>;
/// - arithmetic is checked: overflowing a <see cref="long"/> throws <see cref="OverflowException"/>
///   instead of wrapping round to a wrong (possibly negative) total;
/// - combining two currencies throws <see cref="CurrencyMismatchException"/>.
/// </remarks>
public readonly record struct Money
{
    public const string Gbp = "GBP";

    public Money(long amountMinor, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Currency must be a three-letter upper-case ISO 4217 code.", nameof(currency));
        }

        AmountMinor = amountMinor;
        Currency = currency;
    }

    public long AmountMinor { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);

    /// <summary>Shorthand for GBP, the only currency the storefront sells in.</summary>
    public static Money FromPence(long amountMinor) => new(amountMinor, Gbp);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }

        return new Money(checked(AmountMinor + other.AmountMinor), Currency);
    }

    /// <summary>Unit price × quantity. The quantity must not be negative.</summary>
    public Money Multiply(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        return new Money(checked(AmountMinor * quantity), Currency);
    }

    /// <summary>Sums the amounts; an empty sequence gives zero in <paramref name="currency"/>.</summary>
    public static Money Sum(IEnumerable<Money> amounts, string currency)
    {
        var total = Zero(currency);
        foreach (var amount in amounts)
        {
            total = total.Add(amount);
        }

        return total;
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator *(Money money, int quantity) => money.Multiply(quantity);
}

public sealed class CurrencyMismatchException(string expected, string actual)
    : InvalidOperationException($"Cannot combine {expected} with {actual}.")
{
    public string Expected { get; } = expected;

    public string Actual { get; } = actual;
}
