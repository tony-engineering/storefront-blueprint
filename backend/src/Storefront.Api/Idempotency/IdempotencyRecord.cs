namespace Storefront.Api.Idempotency;

public enum IdempotencyState
{
    /// <summary>The first request with this key is still being processed.</summary>
    InProgress,

    /// <summary>The final response is stored and is replayed for every repeat of the key.</summary>
    Completed,
}

/// <summary>
/// What the API remembers about one <c>Idempotency-Key</c> (ARCHITECTURE.md §6). The key is the primary key,
/// so the database, not an in-process lock, decides which of several concurrent requests is the first.
/// </summary>
public sealed class IdempotencyRecord
{
    // For EF Core.
    private IdempotencyRecord()
    {
        RequestHash = null!;
    }

    public Guid Key { get; private set; }

    /// <summary>Fingerprint of the first request's body (<see cref="RequestHasher"/>).</summary>
    public string RequestHash { get; private set; }

    public IdempotencyState State { get; private set; }

    /// <summary>The order this request created or paid for, once known.</summary>
    public Guid? OrderId { get; set; }

    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? Location { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static IdempotencyRecord Begin(Guid key, string requestHash, DateTimeOffset now) => new()
    {
        Key = key,
        RequestHash = requestHash,
        State = IdempotencyState.InProgress,
        CreatedAt = now,
    };
}
