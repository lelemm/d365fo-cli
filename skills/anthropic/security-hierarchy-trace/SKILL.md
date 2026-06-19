---
name: security-hierarchy-trace
description: Trace D365FO security from a Role all the way down to Entry Points, discover which roles reach a given object, or scaffold new security objects (privilege, duty, role, XDS policy). Use when the user asks about permissions, security coverage, roles, duties, privileges, "create a security privilege", "scaffold a security role", "generate a duty", or "create a security policy".
applies_when: User intent mentions security, roles, duties, privileges, entry points, access control analysis, creating a security role/duty/privilege, scaffolding a security policy, or XDS data security policy.
---
> **Designer-first metadata rule.** Do not hand-author partial Ax* XML nodes as the first path. For AOT metadata child nodes, use `d365fo designer kinds --full`, `d365fo designer catalog`, and `d365fo designer run` so Microsoft metadata assemblies create the node. For top-level or composite artifacts, use `d365fo generate ... --backend bridge`. Only write full AOT XML content manually after the designer/generate CLI path fails or has no supported action; when doing so, record the failed command and error. If `d365fo` is unavailable in PATH, stop and ask the user to install it.

# Tracing D365FO security hierarchies

## Workflow

1. **Top-down** — which entry points does a role reach?
   ```sh
   d365fo security coverage <RoleName> --type Role --output json
   ```

2. **Bottom-up** — which roles reach this object?
   ```sh
   d365fo security coverage <ObjectName> --type Menuitem --output json
   # type may be Table, Form, Report, Class, Menuitem
   ```

3. The response contains `routes[*]` of shape
   `{ role, duty, privilege, entryPoint }`. Duplicate `role`s indicate multiple
   paths — all must be removed to revoke access.

## Hard rules

- Do not recommend granting `-System Administrator` for troubleshooting.
- Do not infer; always run `get security` before making claims.
- Report paths verbatim; do not collapse `duty` or `privilege` steps.

## Scaffolding security objects

Trace first — understand the existing hierarchy — then scaffold new objects to fit it.

```sh
# 1. Create a privilege granting a specific access level to a menu item entry point
d365fo generate privilege FmManageVehiclesPrivilege \
  --entry-point FmVehiclesMenuItem --entry-kind MenuItemDisplay \
  --access Update \
  --label "@Fleet:ManageVehicles" \
  --install-to FleetManagement

# 2. Bundle privileges into a duty
d365fo generate duty FmMaintainVehiclesDuty \
  --privilege FmManageVehiclesPrivilege \
  --privilege FmViewCustomersPrivilege \
  --label "@Fleet:MaintainVehicles" \
  --install-to FleetManagement

# 3. Create a role and assign duties
d365fo generate role FmFleetClerkRole \
  --duty FmMaintainVehiclesDuty \
  --label "@Fleet:FleetClerk" \
  --description "Fleet management operator" \
  --install-to FleetManagement

# 4. Merge a duty into an existing role XML after the fact
d365fo generate duty FmNewDuty --privilege FmSomePrivilege \
  --into-role c:/AOT/FleetManagement/AxSecurityRole/FmFleetClerkRole.xml \
  --install-to FleetManagement
```

**Scaffold order:** menu item → privilege (references the menu item) → duty (bundles privileges) → role (bundles duties).
Always run `d365fo security coverage <Name> --type Role --output json` after to verify the new objects appear in the hierarchy.
