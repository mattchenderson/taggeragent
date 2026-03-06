namespace TaggerAgent.Models;

/// <summary>
/// Represents a proposed tag change to be applied to a resource.
/// </summary>
/// <param name="ResourceId">Full ARM resource ID to tag.</param>
/// <param name="TagKey">Tag key to apply.</param>
/// <param name="TagValue">Tag value to apply.</param>
/// <param name="Confidence">Confidence level: "high" or "low".</param>
/// <param name="Action">Action to take: "auto" or "needs-review".</param>
public sealed record TagChange(
    string ResourceId,
    string TagKey,
    string TagValue,
    string Confidence,  // "high" or "low"
    string Action       // "auto" or "needs-review"
);
