---
name: "functions-hosting-plan"
description: "Choosing the right Azure Functions hosting plan for azd-deployed projects"
domain: "infrastructure"
confidence: "high"
source: "earned — debugging Flex Consumption deployment failures"
---

## Context

When deploying Azure Functions via `azd`, the hosting plan choice directly
affects deployment reliability. Not all plans use the same deployment mechanism,
and mismatches cause failures.

## Patterns

1. **Standard/Dedicated plans (B1, S1, P1v3)** use **zip deploy** — the
   default and most reliable path with `azd`. Always works.
2. **Standard Consumption (Y1)** uses **zip deploy** — also reliable with
   `azd`. Best for sporadic/low-frequency workloads (free tier eligible).
3. **Flex Consumption (FC1)** requires **OneDeploy** with blob-based storage
   deployment — `azd` support is evolving and unreliable as of mid-2026.
4. For timer triggers on dedicated plans, set `alwaysOn: true` to avoid cold
   start delays.
5. For Windows function apps: `kind: 'functionapp'`, no `reserved: true`.
6. For Linux function apps: `kind: 'functionapp,linux'`, `reserved: true`.
7. Standard plans require `FUNCTIONS_EXTENSION_VERSION` and
   `FUNCTIONS_WORKER_RUNTIME` app settings (Flex Consumption uses
   `functionAppConfig.runtime` instead).

## Examples

Standard S1 Windows hosting plan (Bicep):

```bicep
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'S1'
    tier: 'Standard'
  }
  properties: {}
}
```

Required app settings for non-Flex plans:

```bicep
{ name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
{ name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
```

## Anti-Patterns

- **Do not use Flex Consumption (FC1) with `azd`** unless you have verified
  azd support. The KuduSpecializer restart and OneDeploy requirement cause
  deployment failures.
- **Do not include `functionAppConfig`** block when using Standard, Consumption,
  or Premium plans — it is Flex Consumption-only and will cause errors.
- **Do not omit `FUNCTIONS_EXTENSION_VERSION`** on non-Flex plans — the
  Functions runtime won't start without it.
- **Do not set `reserved: true`** for Windows plans — it's Linux-only and
  may cause provisioning errors.
