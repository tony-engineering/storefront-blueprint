using Storefront.Api.Common;
using Storefront.Api.Features.Orders;
using Xunit;

namespace Storefront.UnitTests.Common;

/// <summary>
/// Money is integer minor units plus a currency (ARCHITECTURE.md §4): totals must be exact,
/// currencies must never be mixed silently, and overflow must fail loudly rather than wrap.
/// </summary>
public sealed class MoneyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    // ---- Construction -------------------------------------------------------------------

    [Fact]
    public void Stores_amount_in_minor_units_and_currency()
    {
        var money = new Money(4999, "GBP");

        Assert.Equal(4999, money.AmountMinor);
        Assert.Equal("GBP", money.Currency);
    }

    [Fact]
    public void FromPence_is_gbp()
    {
        Assert.Equal(new Money(250, Money.Gbp), Money.FromPence(250));
    }

    [Fact]
    public void Zero_has_no_amount_in_the_given_currency()
    {
        var zero = Money.Zero("GBP");

        Assert.Equal(0, zero.AmountMinor);
        Assert.Equal("GBP", zero.Currency);
    }

    [Fact]
    public void Negative_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1, "GBP"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("gbp")]
    [InlineData("GB")]
    [InlineData("GBPX")]
    [InlineData("£")]
    public void Currency_must_be_a_three_letter_upper_case_code(string currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Money(100, currency));
    }

    [Fact]
    public void Equal_amount_and_currency_are_equal_values()
    {
        Assert.Equal(new Money(100, "GBP"), new Money(100, "GBP"));
        Assert.NotEqual(new Money(100, "GBP"), new Money(100, "EUR"));
        Assert.NotEqual(new Money(100, "GBP"), new Money(101, "GBP"));
    }

    // ---- Arithmetic ---------------------------------------------------------------------

    [Fact]
    public void Adding_same_currency_adds_minor_units()
    {
        var sum = Money.FromPence(1999) + Money.FromPence(1);

        Assert.Equal(Money.FromPence(2000), sum);
    }

    [Fact]
    public void Adding_different_currencies_is_rejected()
    {
        var ex = Assert.Throws<CurrencyMismatchException>(() => new Money(100, "GBP").Add(new Money(100, "EUR")));

        Assert.Equal("GBP", ex.Expected);
        Assert.Equal("EUR", ex.Actual);
    }

    [Theory]
    [InlineData(1999, 0, 0)]
    [InlineData(1999, 1, 1999)]
    [InlineData(1999, 3, 5997)]
    [InlineData(4999900, 100, 499990000)]
    public void Multiplying_by_quantity_is_exact(long unitPrice, int quantity, long expected)
    {
        Assert.Equal(Money.FromPence(expected), Money.FromPence(unitPrice) * quantity);
    }

    [Fact]
    public void Multiplying_by_negative_quantity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromPence(100).Multiply(-1));
    }

    [Fact]
    public void Multiplication_overflow_throws_instead_of_wrapping()
    {
        Assert.Throws<OverflowException>(() => Money.FromPence(long.MaxValue / 2 + 1) * 2);
    }

    [Fact]
    public void Addition_overflow_throws_instead_of_wrapping()
    {
        Assert.Throws<OverflowException>(() => Money.FromPence(long.MaxValue) + Money.FromPence(1));
    }

    [Fact]
    public void Sum_of_nothing_is_zero_in_the_given_currency()
    {
        Assert.Equal(Money.Zero("GBP"), Money.Sum([], "GBP"));
    }

    [Fact]
    public void Sum_rejects_an_amount_in_another_currency()
    {
        Assert.Throws<CurrencyMismatchException>(() =>
            Money.Sum([Money.FromPence(100), new Money(100, "EUR")], "GBP"));
    }

    [Fact]
    public void Sum_has_no_rounding_drift()
    {
        // 0.1 + 0.1 + … in floating point does not add up to 100.0; ten thousand 1p amounts must be exactly £100.
        var pennies = Enumerable.Repeat(Money.FromPence(1), 10_000);

        Assert.Equal(Money.FromPence(10_000), Money.Sum(pennies, "GBP"));
    }

    [Fact]
    public void Floating_point_trap_amounts_add_up_exactly()
    {
        // £0.10 + £0.20 is 0.30000000000000004 as a double.
        Assert.Equal(Money.FromPence(30), Money.FromPence(10) + Money.FromPence(20));
    }

    // ---- Line and order totals ----------------------------------------------------------

    [Fact]
    public void Line_total_is_unit_price_times_quantity()
    {
        var line = Line(unitPrice: 1999, quantity: 3);

        Assert.Equal(Money.FromPence(5997), line.LineTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_quantity_must_be_at_least_one(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(unitPrice: 100, quantity: quantity));
    }

    [Fact]
    public void Order_total_is_the_sum_of_line_totals()
    {
        var order = Order.Create(
            [Line(unitPrice: 1999, quantity: 3), Line(unitPrice: 4999900, quantity: 1), Line(unitPrice: 1, quantity: 100)],
            Now);

        Assert.Equal(Money.FromPence(5997 + 4999900 + 100), order.Total);
    }

    [Fact]
    public void Order_total_of_many_lines_has_no_rounding_drift()
    {
        // 50 lines (the validation maximum) of 100 × £0.01.
        var lines = Enumerable.Range(0, 50).Select(_ => Line(unitPrice: 1, quantity: 100)).ToList();

        Assert.Equal(Money.FromPence(5000), Order.Create(lines, Now).Total);
    }

    [Fact]
    public void Order_with_lines_in_different_currencies_is_rejected()
    {
        var gbp = Line(unitPrice: 100, quantity: 1);
        var eur = new OrderLine(Guid.NewGuid(), "Euro thing", new Money(100, "EUR"), 1);

        Assert.Throws<CurrencyMismatchException>(() => Order.Create([gbp, eur], Now));
    }

    [Fact]
    public void Order_without_lines_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Order.Create([], Now));
    }

    [Fact]
    public void Order_total_is_in_the_lines_currency()
    {
        var order = Order.Create([Line(unitPrice: 100, quantity: 2)], Now);

        Assert.Equal("GBP", order.Total.Currency);
    }

    private static OrderLine Line(long unitPrice, int quantity) =>
        new(Guid.NewGuid(), "Product", Money.FromPence(unitPrice), quantity);
}
