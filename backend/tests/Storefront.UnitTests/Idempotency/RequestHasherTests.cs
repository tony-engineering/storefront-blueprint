using System.Text.Json;
using Storefront.Api.Features.Orders;
using Storefront.Api.Idempotency;
using Xunit;

namespace Storefront.UnitTests.Idempotency;

/// <summary>
/// The request hash decides whether a repeated Idempotency-Key is the same request (replay the stored
/// response) or a different one (422). ARCHITECTURE.md §6. Getting it wrong either way is costly:
/// too strict and a harmless retry is refused; too loose and a changed basket replays the old order.
/// </summary>
public sealed class RequestHasherTests
{
    private static readonly Guid Keyboard = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mouse = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Monitor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OrderA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrderB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>How ASP.NET Core minimal APIs bind request bodies.</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ---- Shape of the hash --------------------------------------------------------------

    [Fact]
    public void Hash_is_sha256_as_lower_case_hex()
    {
        var hash = RequestHasher.Hash(Basket((Keyboard, 1)));

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_is_deterministic()
    {
        Assert.Equal(RequestHasher.Hash(Basket((Keyboard, 1), (Mouse, 2))), RequestHasher.Hash(Basket((Keyboard, 1), (Mouse, 2))));
    }

    // ---- Equivalent bodies: same hash ---------------------------------------------------

    [Fact]
    public void Line_order_does_not_matter()
    {
        var a = Basket((Keyboard, 1), (Mouse, 2), (Monitor, 3));
        var b = Basket((Monitor, 3), (Keyboard, 1), (Mouse, 2));

        Assert.Equal(RequestHasher.Hash(a), RequestHasher.Hash(b));
    }

    [Fact]
    public void Same_product_split_across_two_lines_is_the_same_request()
    {
        var merged = Basket((Keyboard, 3), (Mouse, 1));
        var split = Basket((Keyboard, 1), (Mouse, 1), (Keyboard, 2));

        Assert.Equal(RequestHasher.Hash(merged), RequestHasher.Hash(split));
    }

    [Fact]
    public void Json_formatting_does_not_matter()
    {
        const string compact =
            """{"lines":[{"productId":"11111111-1111-1111-1111-111111111111","quantity":1}],"paymentToken":"tok_success"}""";
        const string pretty =
            """
            {
              "paymentToken" : "tok_success",
              "lines" : [
                { "quantity" : 1, "productId" : "11111111-1111-1111-1111-111111111111" }
              ]
            }
            """;

        Assert.Equal(RequestHasher.Hash(Parse(compact)), RequestHasher.Hash(Parse(pretty)));
    }

    [Fact]
    public void Property_name_casing_and_guid_casing_do_not_matter()
    {
        // Both bind to the same request, so both are the same request.
        const string lower =
            """{"lines":[{"productId":"aaaaaaaa-0000-0000-0000-000000000001","quantity":2}],"paymentToken":"tok_success"}""";
        const string upper =
            """{"Lines":[{"ProductId":"AAAAAAAA-0000-0000-0000-000000000001","Quantity":2}],"PaymentToken":"tok_success"}""";

        Assert.Equal(RequestHasher.Hash(Parse(lower)), RequestHasher.Hash(Parse(upper)));
    }

    [Fact]
    public void Payment_hash_ignores_formatting()
    {
        var compact = JsonSerializer.Deserialize<RetryPaymentRequest>("""{"paymentToken":"tok_success"}""", Web)!;
        var pretty = JsonSerializer.Deserialize<RetryPaymentRequest>("{\n  \"paymentToken\" : \"tok_success\"\n}", Web)!;

        Assert.Equal(RequestHasher.Hash(OrderA, compact), RequestHasher.Hash(OrderA, pretty));
    }

    // ---- Different bodies: different hash -----------------------------------------------

    [Fact]
    public void Different_quantity_is_a_different_request()
    {
        Assert.NotEqual(RequestHasher.Hash(Basket((Keyboard, 1))), RequestHasher.Hash(Basket((Keyboard, 2))));
    }

    [Fact]
    public void Different_product_is_a_different_request()
    {
        Assert.NotEqual(RequestHasher.Hash(Basket((Keyboard, 1))), RequestHasher.Hash(Basket((Mouse, 1))));
    }

    [Fact]
    public void Extra_line_is_a_different_request()
    {
        Assert.NotEqual(RequestHasher.Hash(Basket((Keyboard, 1))), RequestHasher.Hash(Basket((Keyboard, 1), (Mouse, 1))));
    }

    [Fact]
    public void Different_payment_token_is_a_different_request()
    {
        Assert.NotEqual(
            RequestHasher.Hash(Basket("tok_success", (Keyboard, 1))),
            RequestHasher.Hash(Basket("tok_decline", (Keyboard, 1))));
    }

    [Fact]
    public void Payment_token_is_case_sensitive()
    {
        Assert.NotEqual(
            RequestHasher.Hash(Basket("tok_success", (Keyboard, 1))),
            RequestHasher.Hash(Basket("TOK_SUCCESS", (Keyboard, 1))));
    }

    [Fact]
    public void Moving_quantity_between_products_is_a_different_request()
    {
        // Same products, same total item count: a naive canonical form (e.g. sorting quantities and
        // IDs separately) would collide here.
        Assert.NotEqual(
            RequestHasher.Hash(Basket((Keyboard, 1), (Mouse, 2))),
            RequestHasher.Hash(Basket((Keyboard, 2), (Mouse, 1))));
    }

    [Fact]
    public void Field_boundaries_cannot_be_shifted()
    {
        // A canonical form built by plain concatenation would give "tok_a" + "b…" == "tok_ab" + "…".
        Assert.NotEqual(
            RequestHasher.Hash(Basket("tok_1", (Keyboard, 1))),
            RequestHasher.Hash(Basket("tok_", (Keyboard, 11))));
    }

    [Fact]
    public void Payment_for_another_order_is_a_different_request()
    {
        var body = new RetryPaymentRequest("tok_success");

        Assert.NotEqual(RequestHasher.Hash(OrderA, body), RequestHasher.Hash(OrderB, body));
    }

    [Fact]
    public void Payment_with_another_card_is_a_different_request()
    {
        Assert.NotEqual(
            RequestHasher.Hash(OrderA, new RetryPaymentRequest("tok_success")),
            RequestHasher.Hash(OrderA, new RetryPaymentRequest("tok_decline")));
    }

    [Fact]
    public void Same_key_on_two_endpoints_never_counts_as_a_replay()
    {
        // An order with no lines and a payment with the empty order ID share nothing but the token;
        // the operation name in the canonical form keeps them apart.
        Assert.NotEqual(
            RequestHasher.Hash(new CreateOrderRequest([], "tok_success")),
            RequestHasher.Hash(Guid.Empty, new RetryPaymentRequest("tok_success")));
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static CreateOrderRequest Basket(params (Guid Product, int Quantity)[] lines) =>
        Basket("tok_success", lines);

    private static CreateOrderRequest Basket(string token, params (Guid Product, int Quantity)[] lines) =>
        new(lines.Select(l => new OrderLineRequest(l.Product, l.Quantity)).ToList(), token);

    private static CreateOrderRequest Parse(string json) =>
        JsonSerializer.Deserialize<CreateOrderRequest>(json, Web)
        ?? throw new InvalidOperationException("Body did not bind.");
}
