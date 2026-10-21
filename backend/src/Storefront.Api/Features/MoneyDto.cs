using Storefront.Api.Common;

namespace Storefront.Api.Features;

/// <summary>Money as it appears on the wire: <c>{ "amountMinor": 4999900, "currency": "GBP" }</c>.</summary>
public sealed record MoneyDto(long AmountMinor, string Currency)
{
    public static MoneyDto From(Money money) => new(money.AmountMinor, money.Currency);
}
