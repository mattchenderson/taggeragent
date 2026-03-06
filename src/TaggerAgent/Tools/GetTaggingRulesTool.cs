using Microsoft.Extensions.Logging;
using TaggerAgent.Models;
using TaggerAgent.Services;

namespace TaggerAgent.Tools;

/// <summary>
/// Tool implementation for loading tagging rules from Blob Storage.
/// </summary>
public sealed class GetTaggingRulesTool(
    RulesService rulesService,
    ILogger<GetTaggingRulesTool> logger)
{
    /// <summary>
    /// Loads the current tagging rules from Blob Storage.
    /// </summary>
    /// <returns>List of tagging rules</returns>
    public async Task<List<TaggingRule>> ExecuteAsync()
    {
        logger.LogInformation("Loading tagging rules from Blob Storage");

        var rules = await rulesService.LoadRulesAsync();

        logger.LogInformation("Loaded {RuleCount} tagging rules", rules.Count);
        return rules;
    }
}
