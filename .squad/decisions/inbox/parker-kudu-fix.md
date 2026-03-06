# KuduSpecializer Fix — Identity-Based AzureWebJobsStorage

**Date:** 2026-03-06
**Author:** Parker (Infra/DevOps)
**Status:** Implemented and verified

## Context

`azd deploy` for the `functions` service failed with `[KuduSpecializer] Kudu has been restarted after package deployed`. Root cause: missing `AzureWebJobsStorage` configuration for the Functions runtime.

## Decision

Configure identity-based `AzureWebJobsStorage` using the user-assigned managed identity (same identity used for deployment storage). Upgrade storage RBAC to include Blob Data Owner, Table Data Contributor, and Queue Data Contributor — the minimum roles required for the Functions runtime.

## Changes

- `infra/modules/function-app.bicep` — Added `AzureWebJobsStorage__accountName`, `__credential`, `__clientId` app settings
- `infra/modules/storage-roles.bicep` — Upgraded function identity roles (Blob Owner, Table Contributor, Queue Contributor)

## Rationale

- Storage account enforces `allowSharedKeyAccess: false` (managed identity only) — requires identity-based connection format
- Flex Consumption deployment storage and runtime storage are separate concerns; both need explicit configuration
- Blob Data Owner (not Contributor) is required because Functions runtime uses blob leases for singleton/orchestration patterns
