---
description: Build or extend a D365FO custom service (JSON/SOAP REST endpoint). Invoke when the user asks to "create a custom service", "expose an X++ method as REST", "build a service class", or "register a service group".
applyTo: '**/AxService/**,**/AxServiceGroup/**,**/*Service.xml,**/*ServiceGroup.xml'
---
> ⛔ **NEVER write X++ AOT XML files directly** via PowerShell, terminal file commands, editor write tools, or any raw text approach. **ALWAYS use `d365fo generate …` commands**. If unavailable in PATH, stop and ask the user to install it.

# Custom Service Authoring in D365FO

Three artifacts required: **service class** + **AxService XML** + **AxServiceGroup XML**.

## Pre-flight

```sh
d365fo search service <namePart> --output json   # avoid duplicate
d365fo get service <Existing> --output json      # inspect reference
d365fo report-integrations --model <M> --output json
```

## Scaffolding

```sh
d365fo generate custom-service VendorLookupService \
  --operation Lookup:lookupVendor \
  --install-to MyModel
```

## Service class skeleton

```xpp
[ServiceAttribute]
public class VendorLookupService
{
    [SysEntryPointAttribute(true)]  // REQUIRED on every exposed method
    public VendorLookupResponse lookupVendor(VendorLookupRequest _request)
    {
        // business logic
    }
}
```

## Request/response contracts

```xpp
[DataContractAttribute]
public class VendorLookupRequest
{
    private AccountNum accountNum;
    [DataMemberAttribute('AccountNum')]
    public AccountNum parmAccountNum(AccountNum _v = accountNum) { accountNum = _v; return accountNum; }
}
```

## REST endpoint format

```
POST https://<env>/api/services/<ServiceGroupName>/<ServiceName>/<OperationName>
Authorization: Bearer <AAD token>
Content-Type: application/json
```

## Hard rules

- `[SysEntryPointAttribute(true)]` on **every exposed method** — missing it causes `Method not found`.
- Request/response types must be `[DataContractAttribute]` classes or primitives.
- `[DataMemberAttribute]` on every `parmXxx()` accessor.
- Service group name is the URL path segment — pick a stable name; renaming breaks callers.
- Use EDTs for parameter types, not raw `str` / `int`.
