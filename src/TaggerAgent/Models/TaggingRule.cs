namespace TaggerAgent.Models;

/// <summary>
/// Represents a tagging rule loaded from the rules JSON file.
/// Rules define how to assess resources for tagging - they are inputs.
/// Confidence is an output from the agent's assessment, not stored in rules.
/// </summary>
/// <param name="Tag">Tag key to apply (e.g., "Environment", "CostCenter").</param>
/// <param name="Assessment">Natural language assessment criteria for applying this tag.</param>
/// <param name="AutoApply">Whether the agent can automatically apply this tag in automated mode when confidence is high.</param>
public sealed record TaggingRule(
    string Tag,
    string Assessment,
    bool AutoApply
);
