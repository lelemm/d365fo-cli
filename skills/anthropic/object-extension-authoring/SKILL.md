---
name: object-extension-authoring
description: Create a Table / Form / Edt / Enum extension, a new EDT (Extended Data Type), or a new base enum in D365 Finance & Operations. Use when the user asks to "extend a table", "add a field to standard CustTable", "extend an EDT", "add an enum value", "extend a form via FormExtension", "create an EDT", "create a new EDT", "create an enum", "generate edt", or "generate enum".
applies_when: User intent mentions extending a Table / Form / Edt / Enum, adding fields to a standard table, adding controls to a standard form, extending an enum or EDT, creating a new EDT (Extended Data Type), or creating a new base enum.
---
> â›” **NEVER write X++ AOT XML files directly** via PowerShell, terminal file commands (`Set-Content`, `Out-File`, `New-Item`), editor write tools, or any raw text approach. The XML schema (`<AxClass>`, `<AxTable>`, `<AxForm>`, `<Methods>`, `<SourceCode>`) is proprietary â€” LLMs have not been trained on it reliably. **ALWAYS use `d365fo generate â€¦` commands** to produce correct AOT XML. If `d365fo` is unavailable in PATH, stop and ask the user to install it.

# Authoring object extensions (Table / Form / Edt / Enum)

> Object extensions are the **non-intrusive** way to add fields to standard
> tables, controls to standard forms, members to standard enums, and tighten
> standard EDTs. Unlike CoC class extensions they do not wrap method calls â€”
> they merge metadata at compile time.

## When to use which

| Standard object you want to change | Use |
|---|---|
| Add a field/index/relation to `CustTable` | `extension Table CustTable <Suffix>` |
| Add a control / data source / FastTab to a standard form | `extension Form CustTableListPage <Suffix>` |
| Tighten an EDT (e.g. lengthen a string, adjust label) | `extension Edt CustAccount <Suffix>` |
| Add new members to a base enum | `extension Enum NoYes <Suffix>` |
| Add behaviour to a class method | **NOT this** â€” use `coc-extension-authoring` instead |

## Pre-flight

```sh
# 1) Confirm the target object exists
d365fo get {table|form|edt|enum} <Target> --output json

# 2) Discover existing extensions targeting it (avoid duplicate <Suffix>)
d365fo find extensions <Target> --output json
```

If `count > 0`, list the existing extensions to the user. The naming
convention is `<Target>.<Suffix>` (dot-separated; `Suffix` typically is the
model short-name or the feature name).

## Scaffolding

```sh
# Add fields to standard CustTable in the FleetManagement model
d365fo generate extension Table CustTable Fleet --install-to FleetManagement

# Form extension targeting CustTableListPage
d365fo generate extension Form CustTableListPage Fleet --install-to FleetManagement

# Tighten the CustAccount EDT
d365fo generate extension Edt CustAccount Fleet --install-to FleetManagement

# Add an enum member to NoYes
d365fo generate extension Enum NoYes Fleet --install-to FleetManagement
```

The scaffold emits a minimal `<AxXxxExtension>` element with the
`<Name>Target.Suffix</Name>` shape Visual Studio expects. After scaffolding,
hand-edit the XML to add `<Fields>`, `<Controls>`, `<EnumValues>`, etc.
Re-run `d365fo index refresh --model <Model>` so subsequent
`d365fo get` calls reflect the changes.

## Scaffolding new EDTs and enums

When you need a standalone EDT or enum (not an extension of an existing one), use the generate commands directly:

```sh
# New string EDT â€” check for an existing one first
d365fo search edt <NamePart> --output json

d365fo generate edt CustCustomId \
  --base-type String --size 20 --label "@MyModel:CustCustomId" \
  --out c:/AOT/MyModel/AxEdt/CustCustomId.xml

# Derive from an existing EDT (inherits base type and format)
d365fo generate edt CustCustomAccount \
  --extends CustAccount \
  --label "@MyModel:CustCustomAccount" \
  --out c:/AOT/MyModel/AxEdt/CustCustomAccount.xml

# New extensible enum â€” check for existing first
d365fo search enum <NamePart> --output json

d365fo generate enum CustCustomStatus \
  --value "None:0:@SYS000" --value "Active:1:@SYS001" --value "Closed:2:@SYS002" \
  --out c:/AOT/MyModel/AxEnum/CustCustomStatus.xml
```

`--base-type` accepts `String`, `Integer`, `Real`, `Int64`, `Date`, `UtcDateTime`, `Enum`, `Guid`. Enums are `IsExtensible=Yes` by default â€” pass `--no-extensible` to opt out.

## XDS Security Policy scaffolding

When adding row-level security via Extensible Data Security (XDS):

```sh
# Check for existing policies on the same table first
d365fo search security-policy <ConstrainedTableName> --output json

d365fo generate security-policy CustCustomSecurityPolicy \
  --constrained-table CustCustomTable \
  --policy-query CustCustomPolicyQuery \
  --operation Select \
  --context-type RoleName --context-value CustCustomRole \
  --out c:/AOT/MyModel/AxSecurityPolicy/CustCustomSecurityPolicy.xml
```

`--operation` accepts `All`, `Select`, `Insert`, `Update`, `Delete`. The policy query (`--policy-query`) is a separate `AxQuery` AOT object that must already exist or be scaffolded with `d365fo generate query`.

After scaffolding, verify with:
```sh
d365fo get security-policy CustCustomSecurityPolicy --output json
```

## Hard rules

- Never have two extensions with the same `<Target>.<Suffix>` in the same
  model â€” `d365fo find extensions` first.
- Never use `extension` for class behaviour changes â€” that is CoC's job
  (`d365fo generate coc <Class>`).
- Never modify the standard object directly (over-layering) â€” extensions are
  the supported mechanism. Over-layering is reserved for ISVs with explicit
  contractual permission.
- Always pass labels (`@File:Key`) for added fields' captions â€” never
  hardcoded text (BP `BPErrorLabelIsText`).
- Never guess EDT base types â€” `d365fo get edt <Name>` first.
- Always check for existing security policies before adding a new one â€” `d365fo search security-policy` first.
- After scaffolding, run `d365fo build` only on user request.
