namespace Storefront.Api.Common;

/// <summary>
/// An amount of money in integer minor units (pence for GBP) plus an ISO 4217 currency code.
/// No floating point anywhere, so totals never drift (ARCHITECTURE.md §4).
/// </summary>
/// <remarks>
/// Contract only; the implementation is still to be written (test-first, see Storefront.UnitTests).
/// Rules the implementation must satisfy:
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

    public Money(long amountMinor, string currency) =>
        throw new NotImplementedException();

    public long AmountMinor { get; }

    public string Currency { get; }

    public static Money Zero(string currency) =>
        throw new NotImplementedException();

    /// <summary>Shorthand for GBP, the only currency the storefront sells in.</summary>
    public static Money FromPence(long amountMinor) =>
        throw new NotImplementedException();

    public Money Add(Money other) =>
        throw new NotImplementedException();

    /// <summary>Unit price × quantity. The quantity must not be negative.</summary>
    public Money Multiply(int quantity) =>
        throw new NotImplementedException();

    /// <summary>Sums the amounts; an empty sequence gives zero in <paramref name="currency"/>.</summary>
    public static Money Sum(IEnumerable<Money> amounts, string currency) =>
        throw new NotImplementedException();

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator *(Money money, int quantity) => money.Multiply(quantity);
}

public sealed class CurrencyMismatchException(string expected, string actual)
    : InvalidOperationException($"Cannot combine {expected} with {actual}.")
{
    public string Expected { get; } = expected;

    public string Actual { get; } = actual;
}
