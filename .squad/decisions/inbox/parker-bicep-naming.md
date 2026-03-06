# Decision: Resource Naming with uniqueString Token

**Date:** 2025-01-06  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented

## Context

Resource names in our Bicep templates were purely deterministic based on `environmentName` (e.g., `${environmentName}-foundry`). This caused deployment conflicts:
- "Resource already exists" errors when redeploying to different environments in the same subscription
- Conflicts with soft-deleted resources (30-90 day retention period)
- Issues with globally unique resource types (storage accounts, ACR, Foundry accounts)

## Decision

Adopted the standard azd pattern of using `uniqueString()` to generate a deterministic resource token:

```bicep
var resourceToken = uniqueString(subscription().subscriptionId, environmentName, location)
```

**Naming pattern:**
- Readable resources: `${environmentName}-${take(resourceToken, 6)}-{suffix}`
- Storage accounts: `take(toLower(replace('${environmentName}${resourceToken}st', '-', '')), 24)` (24-char alphanumeric limit)
- ACR: `take(replace('${environmentName}${resourceToken}acr', '-', ''), 50)` (50-char alphanumeric limit)

## Rationale

1. **Uniqueness:** The token is deterministic per subscription/environment/location but unique across different contexts
2. **Reproducibility:** Same inputs always generate the same token (idempotent deployments)
3. **Readability:** Using `take(resourceToken, 6)` keeps names human-readable while ensuring uniqueness
4. **Standard practice:** Follows azd template conventions and Azure best practices
5. **Subscription-scoped compatibility:** Uses only subscription-level inputs (can't reference `resourceGroup().id` at subscription scope)

## Impact

All resource names now include a 6-character unique suffix. Examples:
- Before: `dev-foundry`
- After: `dev-abc123-foundry`

This prevents naming collisions across environments and enables clean redeployment workflows.

## Files Changed

- `infra/main.bicep`
- `infra/modules/foundry.bicep`
- `infra/modules/storage.bicep`
- `infra/modules/identity.bicep`
- `infra/modules/monitoring.bicep`
- `infra/modules/function-app.bicep`

## Related

- Fixed missing `location` property on Foundry project resource (same commit)
- Addresses "LocationRequired" and "ResourceAlreadyExists" deployment errors
