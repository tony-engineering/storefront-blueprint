using Storefront.Api.Common;
using Storefront.Api.Features.Orders;
using Xunit;

namespace Storefront.UnitTests.Orders;

/// <summary>
/// The order state machine (ARCHITECTURE.md §4), as a table over every (from, to) pair of statuses:
/// each legal transition succeeds and each illegal one throws and leaves the order untouched.
/// Through the API only a few states can be set up on demand, so this is where the full table is checked.
/// </summary>
public sealed class OrderStateMachineTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);

    private const string DeclineCode = "insufficient_funds";

    /// <summary>The diagram in ARCHITECTURE.md §4, edge by edge. Anything not listed is illegal.</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Legal =
    [
        (OrderStatus.Pending, OrderStatus.Paid),
        (OrderStatus.Pending, OrderStatus.PaymentFailed),
        (OrderStatus.Pending, OrderStatus.PaymentRetrying),
        (OrderStatus.Pending, OrderStatus.PaymentUnknown),

        (OrderStatus.PaymentRetrying, OrderStatus.Paid),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentFailed),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentUnknown),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentUnavailable),

        (OrderStatus.PaymentUnknown, OrderStatus.Paid),
        (OrderStatus.PaymentUnknown, OrderStatus.PaymentFailed),
        (OrderStatus.PaymentUnknown, OrderStatus.NeedsAttention),

        (OrderStatus.PaymentFailed, OrderStatus.Pending),
        (OrderStatus.PaymentUnavailable, OrderStatus.Pending),
    ];

    public static TheoryData<OrderStatus, OrderStatus> LegalTransitions()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var (from, to) in AllPairs().Where(Legal.Contains))
            data.Add(from, to);
        return data;
    }

    public static TheoryData<OrderStatus, OrderStatus> IllegalTransitions()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var (from, to) in AllPairs().Where(pair => !Legal.Contains(pair)))
            data.Add(from, to);
        return data;
    }

    // ---- The table ----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void Legal_transition_succeeds(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);

        Transition(order, to);

        Assert.Equal(to, order.Status);
        Assert.Equal(Later, order.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void Illegal_transition_throws_and_leaves_the_order_unchanged(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);
        var reasonBefore = order.FailureReason;
        var updatedBefore = order.UpdatedAt;

        var ex = Assert.Throws<InvalidOrderTransitionException>(() => Transition(order, to));

        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
        Assert.Equal(from, order.Status);
        Assert.Equal(reasonBefore, order.FailureReason);
        Assert.Equal(updatedBefore, order.UpdatedAt);
    }

    [Fact]
    public void Table_covers_every_status()
    {
        // Adding a status without deciding its transitions must break this test, not slip through.
        var statuses = Enum.GetValues<OrderStatus>();
        Assert.Equal(7, statuses.Length);
        Assert.Equal(statuses.Length * statuses.Length, LegalTransitions().Count + IllegalTransitions().Count);
        Assert.All(statuses, status => Assert.Equal(status, OrderIn(status).Status));
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.NeedsAttention)]
    public void Terminal_states_have_no_way_out(OrderStatus terminal)
    {
        Assert.DoesNotContain(Legal, pair => pair.From == terminal);
    }

    // ---- What each transition records ---------------------------------------------------

    [Fact]
    public void New_order_is_pending_without_failure_reason()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.FailureReason);
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(Created, order.CreatedAt);
        Assert.Equal(Created, order.UpdatedAt);
    }

    [Fact]
    public void Each_new_order_gets_its_own_id()
    {
        Assert.NotEqual(NewOrder().Id, NewOrder().Id);
    }

    [Fact]
    public void Failing_records_the_decline_code()
    {
        var order = NewOrder();

        order.MarkFailed(DeclineCode, Later);

        Assert.Equal(DeclineCode, order.FailureReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Failing_requires_a_reason(string reason)
    {
        var order = NewOrder();

        Assert.ThrowsAny<ArgumentException>(() => order.MarkFailed(reason, Later));
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Unavailable_records_the_provider_unavailable_reason()
    {
        var order = OrderIn(OrderStatus.PaymentRetrying);

        order.MarkUnavailable(Later);

        Assert.Equal("payment_provider_unavailable", order.FailureReason);
        Assert.Equal(Order.ProviderUnavailableReason, order.FailureReason);
    }

    [Theory]
    [InlineData(OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.PaymentUnavailable)]
    public void Starting_a_new_payment_clears_the_failure_and_keeps_the_total(OrderStatus failed)
    {
        var order = OrderIn(failed);
        var total = order.Total;

        order.StartNewPayment(Later);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.FailureReason);
        Assert.Equal(total, order.Total);
    }

    [Fact]
    public void Paying_after_a_decline_and_a_new_attempt_clears_the_reason()
    {
        var order = NewOrder();
        order.MarkFailed(DeclineCode, Later);
        order.StartNewPayment(Later);

        order.MarkPaid(Later.AddSeconds(1));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.FailureReason);
    }

    [Fact]
    public void Paid_order_cannot_be_marked_failed()
    {
        // The case the brief cares about most: once the customer is told "paid", nothing may undo it.
        var order = OrderIn(OrderStatus.Paid);

        Assert.Throws<InvalidOrderTransitionException>(() => order.MarkFailed(DeclineCode, Later));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.FailureReason);
    }

    [Fact]
    public void Unknown_payment_cannot_start_a_new_attempt()
    {
        // The first charge may have gone through; letting the customer pay again could charge twice.
        var order = OrderIn(OrderStatus.PaymentUnknown);

        Assert.Throws<InvalidOrderTransitionException>(() => order.StartNewPayment(Later));
    }

    [Fact]
    public void Unknown_payment_cannot_go_back_to_retrying()
    {
        // Retrying means "safe to charge again"; after a timeout it is not.
        var order = OrderIn(OrderStatus.PaymentUnknown);

        Assert.Throws<InvalidOrderTransitionException>(() => order.MarkRetrying(Later));
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static IEnumerable<(OrderStatus From, OrderStatus To)> AllPairs() =>
        from source in Enum.GetValues<OrderStatus>()
        from target in Enum.GetValues<OrderStatus>()
        select (source, target);

    private static Order NewOrder() =>
        Order.Create([new OrderLine(Guid.NewGuid(), "Product", Money.FromPence(1999), 2)], Created);

    /// <summary>
    /// An order in <paramref name="status"/>, reached through legal transitions only (no back door into
    /// the entity), all stamped at <see cref="Created"/> so a later transition is visible in UpdatedAt.
    /// </summary>
    private static Order OrderIn(OrderStatus status)
    {
        var order = NewOrder();
        switch (status)
        {
            case OrderStatus.Pending:
                break;
            case OrderStatus.Paid:
                order.MarkPaid(Created);
                break;
            case OrderStatus.PaymentFailed:
                order.MarkFailed(DeclineCode, Created);
                break;
            case OrderStatus.PaymentRetrying:
                order.MarkRetrying(Created);
                break;
            case OrderStatus.PaymentUnknown:
                order.MarkUnknown(Created);
                break;
            case OrderStatus.PaymentUnavailable:
                order.MarkRetrying(Created);
                order.MarkUnavailable(Created);
                break;
            case OrderStatus.NeedsAttention:
                order.MarkUnknown(Created);
                order.MarkNeedsAttention(Created);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Add a path to this status.");
        }

        return order;
    }

    /// <summary>The one method that moves an order into <paramref name="to"/>.</summary>
    private static void Transition(Order order, OrderStatus to)
    {
        switch (to)
        {
            case OrderStatus.Pending:
                order.StartNewPayment(Later);
                break;
            case OrderStatus.Paid:
                order.MarkPaid(Later);
                break;
            case OrderStatus.PaymentFailed:
                order.MarkFailed(DeclineCode, Later);
                break;
            case OrderStatus.PaymentRetrying:
                order.MarkRetrying(Later);
                break;
            case OrderStatus.PaymentUnknown:
                order.MarkUnknown(Later);
                break;
            case OrderStatus.PaymentUnavailable:
                order.MarkUnavailable(Later);
                break;
            case OrderStatus.NeedsAttention:
                order.MarkNeedsAttention(Later);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(to), to, "Add the transition method for this status.");
        }
    }
}
