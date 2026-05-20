---
description: Guidance for building or debugging integrations between D365 Finance & Operations and external systems. Invoke when the user asks about OData, custom services, DMF/Data Management Framework, business events, Power Automate connectors, Service Bus, or external system connectivity.
applyTo: '**/AxDataEntityView/**,**/AxService/**,**/AxServiceGroup/**,**/*Entity.xml,**/*Service.xml'
---
> ⛔ **NEVER write X++ AOT XML files directly** via PowerShell, terminal file commands, editor write tools, or any raw text approach. **ALWAYS use `d365fo generate …` commands**. If unavailable in PATH, stop and ask the user to install it.

# D365FO Integration Patterns

| Pattern | Direction | Latency | Volume | Entry point |
|---------|-----------|---------|--------|-------------|
| **OData REST API** | In + Out | Synchronous | Record-level | `data/<EntityName>` |
| **Custom Services** | In | Synchronous | Operation | SOAP + JSON REST |
| **Data Management Framework** | In + Out | Async batch | Bulk | Import/export jobs |
| **Business Events** | Out | Near-real-time | Event-driven | Service Bus, Event Grid |

## Pre-flight (always)

```sh
d365fo analyze integration --output json          # integration readiness check
d365fo report-integrations --output json          # full surface summary
d365fo search entity <namePart> --output json     # find existing entities
d365fo search service <namePart> --output json    # find existing services
d365fo search business-event <namePart> --output json
```

## OData (data entities)

```sh
d365fo get entity <EntityName> --output json
d365fo get entity <EntityName> --odata-metadata  # emit $metadata fragment
d365fo generate entity MyEntity --query MyEntityQuery --install-to MyModel
```

- `PublicEntityName` must be unique (detected by `analyze integration`).
- `PublicCollectionName` sets the OData collection URL segment.
- Staging table is required for DMF import/export.

## Custom Services

```sh
d365fo get service <ServiceName> --output json
d365fo generate custom-service MyService \
  --operation Get:getRecord \
  --install-to MyModel
```

- Every exposed method **must** have `[SysEntryPointAttribute(true)]`.
- REST endpoint: `https://<env>/api/services/<ServiceGroupName>/<ServiceName>/<Method>`.
- Authentication: AAD OAuth2 (client credentials or delegated).

## Data Management Framework

- Entity must have `StagingTable` set — check with `d365fo get entity <Name>`.
- `EnableDataManagementCapabilities` in XML enables DMF support.
- Change tracking: set `ChangeTrackingEnabled` on the entity.
- Run `d365fo analyze integration` to find entities missing their staging table.

## Business Events

```sh
d365fo generate business-event MyEvent \
  --payload "field:EDT" \
  --category "MyCategory" \
  --install-to MyModel
```

Fire after `ttscommit` — never inside a transaction block.
See the `business-events-authoring` skill for full details.

## Hard rules

- OData entity: `PublicEntityName` and `PublicCollectionName` are both required.
- Custom service: `[SysEntryPointAttribute(true)]` on every exposed method.
- DMF entity: `StagingTable` must reference a real table in the AOT.
- Business event: fire after commit; never inside `ttsbegin/ttscommit`.
