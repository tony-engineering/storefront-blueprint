namespace Storefront.Api.Features.Lab;

/// <summary>The <c>Lab</c> configuration section.</summary>
public sealed class LabOptions
{
    public const string SectionName = "Lab";

    /// <summary>Lab orders older than this are deleted by <see cref="LabOrderJanitor"/>.</summary>
    public TimeSpan RetainFor { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How often the janitor runs. The integration tests switch it off with <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan JanitorInterval { get; init; } = TimeSpan.FromHours(1);
}
