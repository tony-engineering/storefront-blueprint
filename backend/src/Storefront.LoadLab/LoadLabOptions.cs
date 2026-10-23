namespace Storefront.LoadLab;

public sealed class LoadLabOptions
{
    public const string SectionName = "LoadLab";

    /// <summary>Turns the lab off: tests cannot be started.</summary>
    public bool Enabled { get; init; } = true;

    public string ApiBaseUrl { get; init; } = "http://api:8080";

    /// <summary>Pause after a run before the next one may start.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a request keeps trying, with the same idempotency key, after a failure the browser would also
    /// retry: no answer at all (the API is restarting) or a 5xx. After this it gives up and the audit says so.
    /// </summary>
    public TimeSpan RetryFor { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Longest wait for orders to settle in the Finalizing phase.</summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromMinutes(4);
}
