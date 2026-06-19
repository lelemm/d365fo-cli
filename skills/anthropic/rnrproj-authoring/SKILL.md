---
name: rnrproj-authoring
description: Author and validate Microsoft Dynamics 365 Finance and Operations Visual Studio .rnrproj project membership. Use when Codex creates, modifies, reviews, or validates D365FO AOT metadata objects that must be linked to a .rnrproj file, including AxClass, AxTable, AxEdt, AxEnum, AxForm, menu items, security, workflow, queries, labels, and label resource files.
---

# RNRProj Authoring

Use this skill whenever a D365FO implementation requires new or touched AOT artifacts to be linked into a Visual Studio `.rnrproj` project.

For metadata creation itself, prefer the bridge-backed CLI first: use `d365fo designer` for child-node edits and `d365fo generate ... --backend bridge` for top-level or composite artifacts. Only write full AOT XML manually after the designer/generate command fails or has no supported action, and record the failed command/error.

## Workflow

1. Inspect the target `.rnrproj` before editing it.
2. Identify the target package, model, project file, object names, label file ID, and language from the actual artifacts or existing project entries. Do not assume file names, prefixes, package names, model names, or label IDs from another customer/project.
3. Scan nearby projects for local conventions when unsure:

```powershell
Get-ChildItem -Path "<PackagesDir>\<Package>\VSProjects" -Recurse -Filter "*.rnrproj"
```

4. Build an explicit expected object list from generated and touched artifacts.
5. Add folder entries once per logical UI folder.
6. Add one `Content` entry for every expected AOT artifact.
7. Validate membership before continuing with more implementation work.

## Content Entry Rules

Use project-relative AOT paths without `.xml`:

```xml
<Content Include="AxClass\ObjectName">
  <SubType>Content</SubType>
  <Name>ObjectName</Name>
  <Link>Classes\ObjectName</Link>
</Content>
```

Map common artifact families to stable link folders:

- `AxClass` -> `Classes`
- `AxTable` -> `Tables`
- `AxEdt` -> `EDTs`
- `AxEnum` -> `Enums`
- `AxForm` -> `Forms`
- `AxMenuItemDisplay`, `AxMenuItemAction`, `AxMenuItemOutput` -> `Menu Items`
- `AxSecurityPrivilege`, `AxSecurityDuty`, `AxSecurityRole` -> `Security`
- `AxWorkflowTemplate`, `AxWorkflowApproval`, `AxWorkflowTask` -> `Workflow`
- `AxQuery` -> `Queries`
- `AxLabelFile` -> `Label Files`

Add matching folder entries if they are missing:

```xml
<Folder Include="Classes\" />
<Folder Include="Tables\" />
```

## Labels

For a label file object, include the AOT label file. Use the concrete label file ID and culture from the model, commonly `{LabelFileId}_{culture}`:

```xml
<Content Include="AxLabelFile\{LabelFileId}_{culture}">
  <SubType>Content</SubType>
  <Name>{LabelFileId}_{culture}</Name>
  <Link>Label Files\{LabelFileId}_{culture}</Link>
</Content>
```

For the label resource text file, follow the local convention when present. A common pattern is `{LabelFileId}.{culture}.label.txt`:

```xml
<Content Include="{LabelFileId}.{culture}.label.txt">
  <SubType>Content</SubType>
  <Name>{LabelFileId}.{culture}.label.txt</Name>
  <DependentUpon>AxLabelFile\{LabelFileId}_{culture}</DependentUpon>
</Content>
```

Discover label names with the model's `AxLabelFile` folder and existing `.label.txt` files before choosing project entries.

## Validation

Run the bundled validator after patching a project file:

```powershell
& "{skill-dir}\scripts\Validate-RnrprojMembership.ps1" `
  -ProjectPath "{path-to-project.rnrproj}" `
  -ModelRoot "{path-to-model-folder}" `
  -Prefix "{FeatureOrObjectPrefix}" `
  -Include "AxClass\TouchedExistingObject","AxLabelFile\{LabelFileId}_{culture}","{LabelFileId}.{culture}.label.txt" `
  -Exclude "AxClass\OlderObjectWithSamePrefix"
```

Use `-Include` for touched objects that do not match the prefix, especially existing extensions and label resources.
Use the narrowest safe `-Prefix`; use `-Exclude` when a feature prefix collides with older objects.

The validation gate passes only when every expected object has a matching `Content Include`.

## Project Membership Is Not Metadata Health

A correct `.rnrproj` entry only proves Visual Studio can find the artifact. It
does not prove the referenced `Ax*` XML can deserialize. If Visual Studio says
"There was an error reading metadata", inspect the referenced file first,
especially for forms, tables, queries, and enums.

After adding or repairing project membership for generated metadata, pair the
rnrproj membership check with object-level readback:

```powershell
d365fo validate xpp <AxObject.xml> --code-type xml-any --output table
d365fo index refresh --model <Model> --force
d365fo get <object-kind> <ObjectName> --output json
```

Common causes are abstract metadata nodes such as bare `AxTableField`, malformed
query relation collections, missing form control `i:type` values, or enum values
that Visual Studio cannot display because their value nodes lack local-model
properties such as labels.

## D365FO Pathing Reminder

When generating AOT files with `d365fo.exe`, run from the packages directory and pass package/model-relative paths:

```powershell
Set-Location "{PackagesDir}"
& "{PathToD365FoExe}" generate table "{TableName}" --out "{Package}\{Model}\AxTable\{TableName}.xml"
```

Avoid absolute output paths with `d365fo.exe`; its write guard may reject them. Prefer package/model-relative `--out` values rooted at the current packages directory.
