# Model Default: Switch to gpt-4o-mini

**Date:** 2025-01-05  
**Author:** Ripley  
**Status:** Implemented  
**Requested by:** Matthew Henderson

## Context

The project currently hardcodes `gpt-4o` as the default model in `azure.yaml` and `agent.yaml`. This causes deployment failures on subscriptions that don't have access to gpt-4o.

**Specific issue:**
- In westus2, user's subscription only had `gpt-oss-120b` available
- In northcentralus, gpt-4o deployed successfully
- Question: Should we change the DEFAULT model to something more widely available, or keep gpt-4o and document that users may need to adjust?

## Decision

**Switch the default model from `gpt-4o` to `gpt-4o-mini`.**

## Rationale

1. **Wider availability** — gpt-4o-mini is available on most Azure subscriptions, including basic/free tiers. This reduces friction for new users and ensures `azd up` succeeds out of the box.

2. **Sufficient capability** — The TaggerAgent performs structured, tool-heavy workflows:
   - Resource Graph queries (via `ScanResources`)
   - Tag operations (via `ApplyTags`)
   - Rule evaluation against natural language criteria (via `GetTaggingRules`)
   - Rule management (via `SaveTaggingRules`, `CopyTaggingRules`)
   
   These tasks don't require gpt-4o's advanced reasoning capabilities. They need reliable function calling and competent text generation — both of which gpt-4o-mini handles excellently.

3. **Cost efficiency** — gpt-4o-mini is ~60x cheaper than gpt-4o (pricing as of 2025-01). For automated daily timer scans across large subscriptions, this matters. A subscription with 500 resources scanned daily would incur significantly lower costs with -mini.

4. **Function calling support** — gpt-4o-mini has excellent function calling support, which is critical for this agent (5 tools registered).

5. **Production pattern** — Many production AI agents use -mini models for structured, tool-based workflows. The -mini suffix doesn't mean "toy model" — it means "optimized for structured tasks, not creative writing."

6. **Easy override** — Users who want gpt-4o (or gpt-35-turbo, or any other model) can easily edit `azure.yaml` and redeploy. The default should optimize for "works out of the box," not "maximum capability."

## Implementation

**Files changed:**
1. `azure.yaml` — `services.tagger-agent.config.deployments[0]`
   - model.name: `gpt-4o` → `gpt-4o-mini`
   - model.version: `2024-08-06` → `2024-07-18`
   - deployment name: `gpt-4o` → `gpt-4o-mini`

2. `src/TaggerAgent/agent.yaml` — `resources[0]`
   - id: `gpt-4o` → `gpt-4o-mini`

3. `README.md` — Technology Stack section
   - Updated model reference: "gpt-4o model" → "gpt-4o-mini model, configurable"
   - Added **Model Configuration** subsection under "Configuration" with override instructions

4. `docs/architecture.md` — Overview diagram
   - Updated model name in ASCII diagram: `gpt-4o` → `gpt-4o-mini`

## Alternative Considered (and rejected)

**Keep gpt-4o as default, add documentation about changing models:**

- **Pro:** gpt-4o is more capable
- **Con:** Fails deployment on many subscriptions (especially free/basic tiers)
- **Con:** Unnecessary cost for this workload
- **Con:** Bad first-run experience ("azd up failed, now what?")

The "more capable" argument doesn't hold for this agent. It's not doing creative writing, complex reasoning, or multi-step planning. It's executing structured queries and applying tags based on rules.

## Verification

After this change, users should be able to:
1. Run `azd up` successfully on subscriptions that don't have gpt-4o access
2. The agent should perform all tagging operations correctly with gpt-4o-mini
3. Users who want gpt-4o can edit `azure.yaml` and `agent.yaml`, then run `azd up` again

## Risks

**Minimal.** gpt-4o-mini has been production-tested extensively for function calling workflows. The only risk is if the model produces lower-quality natural language assessments for rules — but given the structured nature of tagging decisions (resource type, name patterns, existing tags), this is unlikely to be an issue.

If users report quality issues with gpt-4o-mini, they can override to gpt-4o per the instructions in README.md.

## Related Decisions

- Architecture decision: Microsoft Agent Framework adoption (2025-07-18)
- Rules v2: Natural language assessment criteria (2025-07-18)
