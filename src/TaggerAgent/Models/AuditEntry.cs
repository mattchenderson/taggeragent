using Azure;
using Azure.Data.Tables;

namespace TaggerAgent.Models;

/// <summary>
/// Represents an audit log entry for tag changes.
/// Stored in Azure Table Storage (TagAuditLog table).
/// </summary>
public sealed class AuditEntry : ITableEntity
{
    /// <summary>
    /// Partition key - set to subscription ID for efficient querying.
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key - unique identifier for this entry ({resourceId}_{tag}_{timestamp}).
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the audit entry.
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Full ARM resource ID.
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Tag key that was applied.
    /// </summary>
    public string TagKey { get; set; } = string.Empty;

    /// <summary>
    /// Tag value that was applied.
    /// </summary>
    public string TagValue { get; set; } = string.Empty;

    /// <summary>
    /// Action taken: "applied" or "pending-review".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Confidence level: "high" or "low".
    /// </summary>
    public string Confidence { get; set; } = string.Empty;

    /// <summary>
    /// Tag key from the rule that triggered this change.
    /// </summary>
    public string RuleTag { get; set; } = string.Empty;

    /// <summary>
    /// Execution mode: "interactive" or "automated".
    /// </summary>
    public string ExecutionMode { get; set; } = string.Empty;
}
