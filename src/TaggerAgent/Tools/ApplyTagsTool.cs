using Microsoft.Extensions.Logging;
using TaggerAgent.Models;
using TaggerAgent.Services;

namespace TaggerAgent.Tools;

/// <summary>
/// Tool implementation for applying tags to Azure resources.
/// </summary>
public sealed class ApplyTagsTool(
    TaggingService taggingService,
    AuditService auditService,
    ILogger<ApplyTagsTool> logger)
{
    /// <summary>
    /// Applies a list of tag changes to resources.
    /// </summary>
    /// <param name="changes">List of tag changes to apply</param>
    /// <param name="executionMode">Execution mode: "interactive" or "automated"</param>
    /// <returns>Summary of applied changes</returns>
    public async Task<object> ExecuteAsync(List<TagChange> changes, string executionMode = "interactive")
    {
        logger.LogInformation("Applying {ChangeCount} tag changes in {ExecutionMode} mode", changes.Count, executionMode);

        var appliedChanges = new List<TagChange>();
        var failedChanges = new List<(TagChange change, string error)>();

        foreach (var change in changes)
        {
            try
            {
                await taggingService.ApplyTagAsync(change.ResourceId, change.TagKey, change.TagValue);

                // Log to audit table
                await auditService.LogChangeAsync(new AuditEntry
                {
                    ResourceId = change.ResourceId,
                    TagKey = change.TagKey,
                    TagValue = change.TagValue,
                    Action = "applied",
                    Confidence = change.Confidence,
                    RuleTag = change.TagKey, // Simplified - could track which rule triggered this
                    ExecutionMode = executionMode
                });

                appliedChanges.Add(change);
                logger.LogInformation("Applied tag {TagKey}={TagValue} to {ResourceId}", 
                    change.TagKey, change.TagValue, change.ResourceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply tag {TagKey}={TagValue} to {ResourceId}",
                    change.TagKey, change.TagValue, change.ResourceId);
                failedChanges.Add((change, ex.Message));
            }
        }

        return new
        {
            Applied = appliedChanges.Count,
            Failed = failedChanges.Count,
            AppliedChanges = appliedChanges,
            FailedChanges = failedChanges.Select(f => new { f.change, f.error })
        };
    }
}
