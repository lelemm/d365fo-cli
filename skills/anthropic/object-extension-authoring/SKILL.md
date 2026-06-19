---
name: object-extension-authoring
description: Create a Table / Form / Edt / Enum extension, a new EDT (Extended Data Type), or a new base enum in D365 Finance & Operations. Use when the user asks to "extend a table", "add a field to standard CustTable", "extend an EDT", "add an enum value", "extend a form via FormExtension", "create an EDT", "create a new EDT", "create an enum", "generate edt", or "generate enum".
applies_when: User intent mentions extending a Table / Form / Edt / Enum, adding fields to a standard table, adding controls to a standard form, extending an enum or EDT, creating a new EDT (Extended Data Type), or creating a new base enum.
---
> **Designer-first metadata rule.** Do not hand-author partial Ax* XML nodes as the first path. For AOT metadata child nodes, use `d365fo designer kinds --full`, `d365fo designer catalog`, and `d365fo designer run` so Microsoft metadata assemblies create the node. For top-level or composite artifacts, use `d365fo generate ... --backend bridge`. Only write full AOT XML content manually after the designer/generate CLI path fails or has no supported action; when doing so, record the failed command and error. If `d365fo` is unavailable in PATH, stop and ask the user to install it.

# Authoring object extensions (Table / Form / Edt / Enum)

> Object extensions are the **non-intrusive** way to add fields to standard
> tables, controls to standard forms, members to standard enums, and tighten
> standard EDTs. Unlike CoC class extensions they do not wrap method calls —
> they merge metadata at compile time.

## When to use which

| Standard object you want to change | Use |
|---|---|
| Add a field/index/relation to `CustTable` | `extension Table CustTable <Suffix>` |
| Add a control / data source / FastTab to a standard form | `extension Form CustTableListPage <Suffix>` |
| Tighten an EDT (e.g. lengthen a string, adjust label) | `extension Edt CustAccount <Suffix>` |
| Add new members to a base enum | `extension Enum NoYes <Suffix>` |
| Add behaviour to a class method | **NOT this** — use `coc-extension-authoring` instead |

## Pre-flight (ONE call)

```sh
d365fo prepare change <Target> --goal "<why>" --output json
```

Returns the resolved object type, existing extensions/CoC, the recommended
strategy, and a **grounding token** for `d365fo generate extension …
--grounding-token <token>`.

Fallback (prepare unavailable):

```sh
# 1) Confirm the target object exists
d365fo get {table|form|edt|enum} <Target> --output json

# 2) Discover existing extensions targeting it (avoid duplicate <Suffix>)
d365fo find extensions <Target> --output json
```

If `count > 0`, list the existing extensions to the user. The naming
convention is `<Target>.<Suffix>` (dot-separated; `Suffix` typically is the
model short-name or the feature name).

## Reuse before creating

`D365FO_CUSTOM_MODELS` constrains the model, not the feature suffix. If the
variable contains multiple models, resolve the active target model first from
the artifact named by the user, the model that already contains the related
extension, or the model currently being edited. If more than one custom model
could own the change, stop and ask. The extension suffix is separate from the
model name: extract `<ExistingSuffix>` from existing related extensions in the
active model, such as `<Target>.<ExistingSuffix>` or
`<Target>_<ExistingSuffix>_Extension`. If no suffix can be derived and the user
did not provide one, stop and ask for the suffix. Do not create a feature-named
extension such as `<Target>.<Feature>` or a class such as
`<Target>_<Feature>_Extension` merely because the request mentions a feature,
ticket, integration, customer name, or model name.

Before scaffolding a new extension, inspect the existing result from
`d365fo find extensions <Target> --output json`:

- If an extension already exists in the target model and suffix family, modify
  that existing extension.
- If multiple extensions exist, stop and ask which artifact should own the
  change unless the user has named one explicitly.
- Create a new extension only when no existing extension owns that target/model
  concern or when the user explicitly asks for isolation.

## Scaffolding

```sh
# Add fields to the resolved target table in the resolved target model
d365fo generate extension Table {TableName} {Suffix} --install-to {Model}

# Form extension targeting the resolved target form
d365fo generate extension Form {FormName} {Suffix} --install-to {Model}

# Tighten the resolved target EDT
d365fo generate extension Edt {EdtName} {Suffix} --install-to {Model}

# Add enum members to the resolved target enum
d365fo generate extension Enum {EnumName} {Suffix} --install-to {Model}
```

Substitute `{Model}`, `{Suffix}`, and target object names from pre-flight
results, existing related extensions, or explicit user input. Do not use demo
model names, customer names, ticket names, or feature names as defaults.

The scaffold emits a minimal `<AxXxxExtension>` element with the
`<Name>Target.Suffix</Name>` shape Visual Studio expects. After scaffolding,
hand-edit the XML to add `<Fields>`, `<Controls>`, `<EnumValues>`, etc.
Re-run `d365fo index refresh --model <Model>` so subsequent
`d365fo get` calls reflect the changes.

## Scaffolding new EDTs and enums

When you need a standalone EDT or enum (not an extension of an existing one), use the generate commands directly:

```sh
# New string EDT — check for an existing one first
d365fo search edt <NamePart> --output json

d365fo generate edt {NewEdtName} \
  --base-type String --size {Length} --label "{LabelReference}" \
  --out "{ModelRoot}/AxEdt/{NewEdtName}.xml"

# Derive from an existing EDT (inherits base type and format)
d365fo generate edt {NewEdtName} \
  --extends {BaseEdtName} \
  --label "{LabelReference}" \
  --out "{ModelRoot}/AxEdt/{NewEdtName}.xml"

# New extensible enum — check for existing first
d365fo search enum <NamePart> --output json

d365fo generate enum {NewEnumName} \
  --value "{ValueName}:{Ordinal}:{LabelReference}" \
  --out "{ModelRoot}/AxEnum/{NewEnumName}.xml"
```

Resolve `{ModelRoot}` from the actual package/model folder. Reuse the model's
label file and label-key conventions; create labels before referencing them.
Use a concrete `{LabelReference}` accepted by the CLI, commonly `@File:Key`.

`--base-type` accepts `String`, `Integer`, `Real`, `Int64`, `Date`, `UtcDateTime`, `Enum`, `Guid`. Enums are `IsExtensible=Yes` by default — pass `--no-extensible` to opt out.

## XDS Security Policy scaffolding

When adding row-level security via Extensible Data Security (XDS):

```sh
# Check for existing policies on the same table first
d365fo search security-policy <ConstrainedTableName> --output json

d365fo generate security-policy {SecurityPolicyName} \
  --constrained-table {ConstrainedTableName} \
  --policy-query {PolicyQueryName} \
  --operation Select \
  --context-type RoleName --context-value {SecurityRoleName} \
  --out "{ModelRoot}/AxSecurityPolicy/{SecurityPolicyName}.xml"
```

`--operation` accepts `All`, `Select`, `Insert`, `Update`, `Delete`. The policy query (`--policy-query`) is a separate `AxQuery` AOT object that must already exist or be scaffolded with `d365fo generate query`.

After scaffolding, verify with:
```sh
d365fo get security-policy {SecurityPolicyName} --output json
```

## Hard rules

- Never have two extensions with the same `<Target>.<Suffix>` in the same
  model — `d365fo find extensions` first.
- Never create a feature-specific extension when a model-level extension for
  the same target already exists and is the natural owner of the change.
- Never rewrite an existing extension XML file wholesale. Preserve unrelated
  nodes and only add the requested structural element(s).
- After changing extension XML, validate XML well-formedness, run
  `d365fo validate xpp --file <file> --code-type xml-any --output json`, run
  `d365fo index refresh --model <Model>`, and re-read the target metadata.
- Never use `extension` for class behaviour changes — that is CoC's job
  (`d365fo generate coc <Class>`).
- Never modify the standard object directly (over-layering) — extensions are
  the supported mechanism. Over-layering is reserved for ISVs with explicit
  contractual permission.
- Always pass labels (`@File:Key`) for added fields' captions — never
  hardcoded text (BP `BPErrorLabelIsText`).
- Never guess EDT base types — `d365fo get edt <Name>` first.
- Always check for existing security policies before adding a new one — `d365fo search security-policy` first.
- After scaffolding, run `d365fo build` only on user request.

## Enum serializer compatibility lessons

Visual Studio may display a generated enum but omit its value items if the
value collection is not shaped like local model metadata. For standalone
AxEnum XML, match nearby working enums before finalizing:

```xml
<AxEnum xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
  <Name>MyEnum</Name>
  <Label>@MyFile:MyEnum</Label>
  <UseEnumValue>No</UseEnumValue>
  <EnumValues>
    <AxEnumValue>
      <Name>MyValue</Name>
      <Label>@MyFile:MyValue</Label>
      <Value>0</Value>
    </AxEnumValue>
  </EnumValues>
  <IsExtensible>true</IsExtensible>
</AxEnum>
```

Rules:

- Give every `AxEnumValue` a `Label`; create or reuse labels first.
- Keep `EnumValues` before `IsExtensible` when that is the local convention.
- Add `UseEnumValue` consistently with nearby enums in the same model. Do not
  assume the value from another customer/project.
- Do not trust XML well-formedness alone. Run:

```powershell
d365fo validate xpp <AxEnum.xml> --code-type xml-any --output table
d365fo index refresh --model <Model> --force
d365fo get enum <EnumName> --output json
d365fo analyze completeness <ProjectOrModelPath> --resolve-labels --output table
```

If `d365fo get enum` sees values but Visual Studio does not, compare property
ordering and value labels against existing enums in the same model.
