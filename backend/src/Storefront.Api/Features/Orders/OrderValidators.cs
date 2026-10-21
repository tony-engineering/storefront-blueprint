using FluentValidation;

namespace Storefront.Api.Features.Orders;

/// <summary>Rules for <c>POST /api/orders</c> (ARCHITECTURE.md §9): 1–50 lines, quantity 1–100, no product twice.</summary>
public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxLines = 50;
    public const int MaxQuantity = 100;

    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Lines are required.")
            .Must(lines => lines.Count is >= 1 and <= MaxLines).WithMessage($"An order needs between 1 and {MaxLines} lines.")
            .Must(lines => lines.All(line => line is not null)).WithMessage("A line must not be empty.")
            .Must(lines => lines.Select(line => line.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("Each product may appear on an order only once; raise the quantity instead.");

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage("A product ID is required.");
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, MaxQuantity)
                .WithMessage($"Quantity must be between 1 and {MaxQuantity}.");
        });

        RuleFor(r => r.PaymentToken).SetValidator(new PaymentTokenValidator());
    }
}

public sealed class RetryPaymentRequestValidator : AbstractValidator<RetryPaymentRequest>
{
    public RetryPaymentRequestValidator() => RuleFor(r => r.PaymentToken).SetValidator(new PaymentTokenValidator());
}

internal sealed class PaymentTokenValidator : AbstractValidator<string>
{
    public PaymentTokenValidator() =>
        RuleFor(token => token)
            .NotEmpty().WithMessage("A payment token is required.")
            .MaximumLength(100).WithMessage("A payment token is at most 100 characters.");
}
