# Decision: agent.yaml Structure for azd Extension

**Date:** 2026-03-05  
**Author:** Parker (Infra/DevOps)  
**Status:** Resolved

## Context

The `azd ai agent` extension (version 0.1.13-preview) was rejecting our `agent.yaml` with validation error:
```
template.kind must be one of: [prompt hosted workflow], got ''
```

## Investigation

Tested multiple hypotheses:
1. ❌ Line endings (CRLF vs LF)
2. ❌ File encoding (BOM, UTF-8 variants)
3. ❌ YAML formatting/indentation
4. ❌ Case sensitivity of `kind` value
5. ❌ Quoted vs unquoted strings
6. ✅ **Schema structure mismatch**

The breakthrough came when testing `kind: Agent` at the root - the error changed to `got 'Agent'`, proving the extension reads from root, not from a nested `template` field.

## Root Cause

The AgentSchema specification defines two structures:
- **AgentManifest**: Has `template` field containing an `AgentDefinition`
- **AgentDefinition**: The actual agent (PromptAgent, ContainerAgent, Workflow)

We were using AgentManifest structure, but the azd extension expects a bare AgentDefinition.

## Decision

Use `ContainerAgent` structure directly at root in `agent.yaml`:

```yaml
kind: hosted                    # At root, not under template:
name: tagger-agent
displayName: "Tagger Agent"
description: "..."
metadata:
  authors: [...]
  tags: [...]
protocols:
  - protocol: responses
    version: v1
environment_variables:
  - name: VAR_NAME
    value: ${VAR_VALUE}
resources:
  - name: chat
    kind: model
    id: gpt-4o
```

**Key constraints:**
- `kind` must be at root level
- Valid values: `prompt`, `hosted`, `workflow` (lowercase)
- No `template:` wrapper
- Extension version: 0.1.13-preview

## Impact

- Fixed `azd up` validation failure
- Agent deployment can now proceed (subject to subscription permissions)

## Notes for Future

- azd extension schema differs from full AgentSchema spec
- Extension is in preview - schema may change in future versions
- If validation fails again after extension upgrade, check if AgentManifest structure is now required
