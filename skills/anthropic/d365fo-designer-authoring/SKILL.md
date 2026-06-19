---
name: d365fo-designer-authoring
description: Use the bridge-backed `d365fo designer` CLI to discover and run D365FO metadata-designer context-menu actions. Invoke when adding or modifying AOT metadata child nodes such as table fields/indexes/relations, form data sources/controls, query data sources, data entity nodes, security privilege entry points, duty/role references, or whenever a task would otherwise require hand-authoring Ax* XML nodes.
---

# D365FO Designer Authoring

## Rule

Never hand-author partial Ax* metadata node XML first. Use the designer CLI to create metadata through Microsoft D365FO metadata assemblies, then modify the result if needed.

Use raw full-content XML only as a fallback after the designer CLI fails or does not support the needed action. When falling back, record the exact failed designer command and error in the response.

## Workflow

1. Discover valid parent kinds and child paths:

```sh
d365fo designer kinds --full
```

2. Narrow the catalog for the parent object kind:

```sh
d365fo designer catalog --parent-kind <kind> --output json
d365fo designer catalog --parent-kind <kind> --node <path> --output json
```

3. Run the action against a real parent object:

```sh
d365fo designer run <action-id> \
  --parent-kind <kind> \
  --parent <ParentObjectName> \
  --model <ModelName> \
  --set name=<NewNodeName> \
  --output json
```

Use `--file <AxObject.xml> --out <updated.xml>` for file-based edits, dry runs, tests, or repo-local patches:

```sh
d365fo designer run new-entry-point \
  --parent-kind privilege \
  --parent MyPrivilege \
  --file ./AxSecurityPrivilege/MyPrivilege.xml \
  --set name=MyMenuItem \
  --set objectName=MyMenuItem \
  --set objectType=MenuItemDisplay \
  --out ./AxSecurityPrivilege/MyPrivilege.xml \
  --overwrite \
  --output json
```

4. Continue from the returned taxonomy:

The `designer run` response includes `createdKind`, `createdPath`, and `nextCatalogKind`. Use those values for the next discovery step instead of guessing nested paths.

## Top-Level Objects

For whole AOT objects or composite generators, use `d365fo generate ... --backend bridge` first. Examples include new tables, forms, classes, EDTs, enums, menu items, queries, data entities, workflows, and services.

After the top-level object exists, use `d365fo designer` for child-node additions such as fields, indexes, relations, data sources, controls, entry points, and references.

## Fallback Discipline

Only write full XML content manually when:

- `d365fo designer catalog` shows no applicable action, or
- `d365fo designer run` fails for the required action, or
- `d365fo generate --backend bridge` fails for whole-object creation.

When falling back:

- Write or replace the complete AOT XML file, not isolated fragments.
- Prefer existing project examples and Microsoft serializer-shaped output.
- Validate with the strongest available command, such as `d365fo validate xpp`, `d365fo get <kind>`, or `d365fo index refresh`.
- Include the failed designer/generate command and error in the final response.

## Common Commands

```sh
# List discoverable kind tree and action outputs
d365fo designer kinds --full

# Table child nodes
d365fo designer catalog --parent-kind table --output json
d365fo designer run new-field --parent-kind table --parent MyTable --model MyModel --set name=Description --set type=string --output json
d365fo designer run new-index --parent-kind table --parent MyTable --model MyModel --set name=IdxDescription --output json

# Form child nodes
d365fo designer catalog --parent-kind form --node Design/Controls --output json
d365fo designer run new-control --parent-kind form --parent MyForm --model MyModel --node Design/Controls --set name=MyButton --set controlType=button --output json

# Security child nodes
d365fo designer catalog --parent-kind privilege --output json
d365fo designer run new-entry-point --parent-kind privilege --parent MyPrivilege --model MyModel --set name=MyMenuItem --set objectName=MyMenuItem --set objectType=MenuItemDisplay --output json
```
