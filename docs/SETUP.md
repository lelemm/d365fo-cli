# Setup

> **TL;DR** — clone, build, set three env vars, run `d365fo index build && d365fo index extract`. That's it.

For command examples jump to [EXAMPLES.md](EXAMPLES.md).

---

## Prerequisites

**All platforms:** .NET SDK 10 (pinned by `global.json`), `git` (for `review diff`).

**Windows D365FO VM** *(only for `build` / `sync` / `test` / `bp`):* VS 2022/2026 with the D365FO developer tools + `MSBuild.exe`, `SyncEngine.exe`, `SysTestRunner.exe`, `xppbp.exe` on `PATH`. Off-Windows these commands return `UNSUPPORTED_PLATFORM`; everything else works.

---

## 1. Install

### Option A — Dev mode (your own machine, recommended)

```sh
git clone https://github.com/dynamics365ninja/d365fo-cli.git
cd d365fo-cli
dotnet build d365fo-cli.slnx -c Release
```

Add to your shell profile and you're done — every invocation rebuilds automatically on source changes:

```sh
# bash / zsh (~/.zshrc or ~/.bashrc)
alias d365fo='dotnet run --project /path/to/d365fo-cli/src/D365FO.Cli --'
```

```powershell
# PowerShell ($PROFILE)
function d365fo { dotnet run --project C:\path\to\d365fo-cli\src\D365FO.Cli -- @args }
```

### Option B — Standalone binary (CI, shared VMs, no SDK)

```sh
# Windows
dotnet publish src/D365FO.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=true

# macOS / Linux
dotnet publish src/D365FO.Cli -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true
```

Supported RIDs: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`. Output lands in `src/D365FO.Cli/bin/Release/net10.0/<rid>/publish/`. Rename to `d365fo` (`d365fo.exe` on Windows) and put it on `PATH`.

> Drop `--self-contained` if the target machine already has .NET 10 — output shrinks from ~70 MB to a few MB.

---

## 2. First-time setup

### Step 1 — Set env vars

Three variables matter before you run `index extract`. Set them in your shell profile so they survive restarts.

| Variable | Purpose | Notes |
|---|---|---|
| `D365FO_STANDARD_PACKAGES_PATH` | Primary root of `PackagesLocalDirectory` | **Required** for indexing |
| `D365FO_CUSTOM_PACKAGES_PATH` | Additional `PackagesLocalDirectory` root(s) | Optional. Semicolon- or comma-separated. UDE setups typically need this — see [UDE setup](#ude-unified-developer-experience-setup) below. |
| `D365FO_LABEL_LANGUAGES` | Languages to extract (e.g. `en-us,cs`) | Default: `en-us` only. **Directly controls index size** — each extra language adds significant data. Set this before the first extract. |
| `D365FO_INDEX_DB` | Path to the SQLite index | Defaults to `%LOCALAPPDATA%\d365fo-cli\d365fo-index.sqlite` (`~/.local/share/…` on Linux/macOS) |

All other variables (`D365FO_CUSTOM_MODELS`, `D365FO_BRIDGE_*`, `D365FO_WORKSPACE_PATH`, `D365FO_XREF_CONNECTIONSTRING`) are optional — see [CONFIGURATION.md](CONFIGURATION.md) for details.

> **Tip — JSON config file (recommended, shell-agnostic):** `d365fo` reads settings from a JSON config file at `%LOCALAPPDATA%\d365fo-cli\settings.json` (environment variables always take priority). Running `d365fo init --persist-profile` writes this file automatically. Because it is not tied to any shell profile, it works identically in **Windows PowerShell 5.1, PowerShell 7, VS Developer PowerShell**, and any other host. See [CONFIGURATION.md](CONFIGURATION.md).

#### Visual Studio Developer PowerShell — common pitfall

VS Developer PowerShell is **Windows PowerShell 5.1** (`powershell.exe`), which reads a different `$PROFILE` than PowerShell 7 (`pwsh.exe`):

| Shell host | Profile path |
|---|---|
| Windows PowerShell 5.1 / VS Developer PowerShell | `%USERPROFILE%\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1` |
| PowerShell 7+ (`pwsh`) | `%USERPROFILE%\Documents\PowerShell\Microsoft.PowerShell_profile.ps1` |

If you set env vars only in one profile they will not be visible in the other. The recommended fix is to use the JSON config file:

```powershell
d365fo init --packages K:\AosService\PackagesLocalDirectory --persist-profile
```

If you use UDE dual roots, include extra packages during init so `D365FO_CUSTOM_PACKAGES_PATH` is persisted too:

```powershell
d365fo init --packages K:\AosService\PackagesLocalDirectory --extra-packages C:\LocalMetadata\PackagesLocalDirectory --persist-profile
```

`--persist-profile` now writes **both** profile files and the shell-agnostic JSON config at the same time, so all shell hosts pick up the settings without manual duplication.

Alternatively, set variables as **system-level** (machine-scope) env vars in Windows System Properties — those are inherited by every process:

```powershell
[System.Environment]::SetEnvironmentVariable(
    "D365FO_STANDARD_PACKAGES_PATH",
    "K:\AosService\PackagesLocalDirectory",
    [System.EnvironmentVariableTarget]::Machine)
```

### UDE (Unified Developer Experience) setup

The UDE developer topology provides **two separate `PackagesLocalDirectory` folders**:

| Folder | Contents |
|---|---|
| Shared / UDE drive (e.g. `K:\AosService\PackagesLocalDirectory`) | Standard Microsoft metadata (ApplicationSuite, Foundation, …) |
| Local laptop folder (e.g. `C:\LocalMetadata\PackagesLocalDirectory`) | Your custom model XML (edited in VS 2022 locally) |

Visual Studio merges both transparently, but `d365fo-cli` needs to know about both to build a complete index.

**PowerShell (one session):**

```powershell
$env:D365FO_STANDARD_PACKAGES_PATH         = 'K:\AosService\PackagesLocalDirectory'
$env:D365FO_CUSTOM_PACKAGES_PATH   = 'C:\LocalMetadata\PackagesLocalDirectory'
d365fo index extract
```

**Persist in `$PROFILE`:**

```powershell
Add-Content -Path $PROFILE -Value @"

# d365fo-cli — UDE dual-packages config
`$env:D365FO_STANDARD_PACKAGES_PATH         = 'K:\AosService\PackagesLocalDirectory'
`$env:D365FO_CUSTOM_PACKAGES_PATH   = 'C:\LocalMetadata\PackagesLocalDirectory'
"@
. $PROFILE
```

**CLI flags (one-shot, no env vars):**

```powershell
d365fo index extract `
    --packages       K:\AosService\PackagesLocalDirectory `
    --extra-packages C:\LocalMetadata\PackagesLocalDirectory
```

> `--extra-packages` is repeatable. Multiple extra roots can also be given as a single semicolon-separated string in `D365FO_CUSTOM_PACKAGES_PATH`. Missing extra roots are silently skipped (the primary root still produces an error if absent). Duplicate model directories across roots are deduplicated automatically.

`d365fo index refresh` supports the same `--extra-packages` / `D365FO_CUSTOM_PACKAGES_PATH` mechanism.

### Step 2 — Build and populate the index

```sh
d365fo index build      # create / migrate the SQLite schema
d365fo index extract    # ingest metadata from PACKAGES_PATH
d365fo doctor           # confirm everything is green
```

`index extract` is idempotent — safe to re-run any time, replaces rows per model. Scope it to save time:

```sh
d365fo index extract --model MyCustomModel   # seconds
d365fo index extract --model ApplicationSuite  # minutes, parallelised per file
```

### Step 3 — Quickstart scripts

Copy-paste to go from a fresh clone to a working index in one shot.

**PowerShell:**

```powershell
# Edit these three lines, then run the rest as-is.
$Repo  = "C:\source\d365fo-cli"
$Pkg   = "K:\AosService\PackagesLocalDirectory"
$Langs = "en-us"          # add languages you actually use, e.g. "en-us,cs,de"

# 1. Build
Push-Location $Repo
dotnet build d365fo-cli.slnx -c Release
Pop-Location

# 2. Persist function + env in $PROFILE
Add-Content -Path $PROFILE -Value @"

# d365fo-cli
function d365fo { dotnet run --project $Repo\src\D365FO.Cli -- @args }
`$env:D365FO_STANDARD_PACKAGES_PATH   = "$Pkg"
`$env:D365FO_LABEL_LANGUAGES = "$Langs"
`$env:D365FO_INDEX_DB        = "`$env:LOCALAPPDATA\d365fo-cli\index.sqlite"
"@
. $PROFILE

# 3. Build the index
d365fo index build
d365fo index extract
d365fo doctor
```

**bash / zsh:**

```sh
# Edit these three lines, then run the rest as-is.
REPO=$HOME/source/d365fo-cli
PKG=/mnt/d365fo/PackagesLocalDirectory
LANGS="en-us"   # add languages you actually use, e.g. "en-us,cs,de"

cd "$REPO" && dotnet build d365fo-cli.slnx -c Release

{
  echo ""
  echo "# d365fo-cli"
  echo "alias d365fo='dotnet run --project $REPO/src/D365FO.Cli --'"
  echo "export D365FO_STANDARD_PACKAGES_PATH=\"$PKG\""
  echo "export D365FO_LABEL_LANGUAGES=\"$LANGS\""
  echo "export D365FO_INDEX_DB=\"\$HOME/.d365fo/index.sqlite\""
} >> "$HOME/.zshrc"
source "$HOME/.zshrc"

mkdir -p "$HOME/.d365fo"
d365fo index build
d365fo index extract
d365fo doctor
```

> A first-class `d365fo init` command is available — see [SETUP.md #2 First-time setup](#2-first-time-setup) for usage.

---

## 3. Day-to-day maintenance

| Situation | Command |
|---|---|
| You edited XML in a custom model | `d365fo index refresh --model <Model>` |
| New PU / hotfix metadata landed | `d365fo index extract` (re-runs only changed models) |
| `git pull` changed the index schema | `d365fo index build` (migrates in place, safe to re-run) |
| Results look stale or wrong | `d365fo doctor` → `d365fo index status` |

---

## 4. Visual Studio integration

Wires up `d365fo` as a Tools menu shortcut and deploys the Copilot Skills to your X++ project so Copilot has the full X++ rule canon in scope.

### Prerequisites

- Visual Studio 2022 or 2026 with the **Dynamics 365 Finance and Operations** workload installed.
- [GitHub Copilot extension](https://marketplace.visualstudio.com/items?itemName=GitHub.copilotvs) installed in Visual Studio (supports VS 2022 17.10+ and VS 2026).
- `d365fo` reachable on `PATH` (Option A alias or Option B binary from section 1 above).

### Register `d365fo` as an External Tool

External Tools let you run any CLI command from the **Tools** menu without leaving Visual Studio.

1. Open **Tools → External Tools…**
2. Click **Add** and fill in:

   | Field | Value |
   |---|---|
   | Title | `d365fo: index status` |
   | Command | `d365fo` |
   | Arguments | `index status --output json` |
   | Initial directory | `$(SolutionDir)` |
   | ☑ Use Output window | checked |

3. Repeat for any commands you want one-click access to (e.g. `index refresh --model $(ProjectName)`, `lint --output json`).
4. Click **OK**.

> **Tip.** Add a second entry with **Arguments** = `doctor --output json` to run a health check straight from the menu.

### How Copilot works in Visual Studio and VS Code

In **agent mode** Copilot has a terminal tool and executes `d365fo` commands autonomously — the Skills in `.github/instructions/` tell it exactly what to run and in what order. No copy-paste required.

| Environment | How Copilot runs d365fo | Token cost |
|---|---|---|
| **VS 2022 / VS 2026 agent mode** | Built-in terminal tool → `d365fo` CLI | ~100 tokens |
| **VS Code agent mode** | `run_in_terminal` → `d365fo` CLI | ~100 tokens |
| **VS Chat mode** (no agent tools) | Asks user to run commands, pastes JSON | collaborative |

To activate agent mode in Visual Studio: open the Copilot Chat pane, click the mode dropdown (top-right), and select **Agent**. No additional configuration is needed — `d365fo` must simply be on `PATH`.

> ⚠️ **Never** use the built-in VS code search or `@workspace` on an X++ / AOT project. It always fails on AOT XML files. Copilot must use `d365fo` commands exclusively for all codebase queries.

#### Agent mode — example session ("Add a new table")

1. **You ask** in Copilot Chat (agent mode):
   ```
   Add a new table MyVendorRating to model MyCustomModel with fields VendAccount (string), Rating (int), RatingDate (date).
   ```

2. **Copilot** runs pre-flight checks autonomously:
   ```sh
   d365fo doctor --output json
   d365fo models list --output json
   ```

3. **Copilot** confirms the index is healthy and model exists, then scaffolds directly:
   ```sh
   d365fo generate table MyVendorRating --install-to MyCustomModel \
     --field "VendAccount:VendAccountNum" --field "Rating:int" --field "RatingDate:date" \
     --output json
   d365fo index refresh --model MyCustomModel --output json
   ```

No copy-paste. The skill `table-scaffolding.instructions.md` guides the exact field/pattern choices.

#### Chat mode — step-by-step workflow (no agent tools)

When agent mode is not available the interaction is **collaborative**: Copilot tells you which `d365fo` commands to run, you run them in **Developer PowerShell**, and you paste the JSON output back.

**Is Copilot smart enough to ask on its own?**  
Yes — *if* `copilot-instructions.md` is deployed to your X++ project's `.github/` folder (via `Install-D365FoCopilotSkills.ps1`). If Copilot jumps straight to generating X++ code without requesting `d365fo` output first, the file is either missing or not being picked up — re-run the install script and restart Visual Studio.

**Example (same task, chat mode):**

1. **You ask:** `Add a new table MyVendorRating to model MyCustomModel …`

2. **Copilot responds:**
   ```
   Please run these commands in Developer PowerShell and paste the output:

   d365fo doctor --output json
   d365fo models list --output json
   ```

3. **You run** them and paste the JSON.

4. **Copilot** reviews the output, then asks:
   ```
   d365fo generate table MyVendorRating --install-to MyCustomModel --output json
   ```

5. **You run** it and paste back. Copilot confirms and tells you any follow-up steps.

**Key principle:** you are the hands, Copilot is the brain. You never type `d365fo` commands unprompted — wait for Copilot to tell you exactly what to run.

### Copy Skills and Copilot instructions to your X++ project

GitHub Copilot in Visual Studio reads `.github/copilot-instructions.md` and `.github/instructions/*.instructions.md` from the root of your solution (repository). Deploying these files gives Copilot the full X++ rule canon — D365FO table/method names, CoC rules, BP rules, label rules — without burning context tokens.

**Automated script** — run once per X++ project (re-run after `d365fo-cli` updates to pick up new Skills):

```powershell
# Install-D365FoCopilotSkills.ps1
# Usage:
#   .\Install-D365FoCopilotSkills.ps1 -CliRepo C:\source\d365fo-cli -XppRepo K:\D365FO\MyProject
#
# Parameters:
#   -CliRepo   Path to your d365fo-cli clone (source of skills + copilot-instructions.md)
#   -XppRepo   Root of your X++ project / solution repository (target)

param(
    [Parameter(Mandatory)][string] $CliRepo,
    [Parameter(Mandatory)][string] $XppRepo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$src      = Join-Path $CliRepo 'skills\copilot'
$canon    = Join-Path $CliRepo '.github\copilot-instructions.md'
$dstRoot  = Join-Path $XppRepo '.github'
$dstInstr = Join-Path $dstRoot 'instructions'

# Create target directories if absent
New-Item -ItemType Directory -Force -Path $dstRoot  | Out-Null
New-Item -ItemType Directory -Force -Path $dstInstr | Out-Null

# Copy the main X++ rule canon
if (Test-Path $canon) {
    Copy-Item -Path $canon -Destination $dstRoot -Force
    Write-Host "[OK] copilot-instructions.md  →  $dstRoot"
} else {
    Write-Warning "copilot-instructions.md not found at: $canon"
}

# Copy all 15 Skills (.instructions.md)
$skills = Get-ChildItem -Path $src -Filter '*.instructions.md'
if ($skills.Count -eq 0) {
    Write-Warning "No *.instructions.md files found in: $src"
    Write-Warning "Run 'python scripts/emit-skills.py' in the d365fo-cli repo first."
} else {
    foreach ($f in $skills) {
        Copy-Item -Path $f.FullName -Destination $dstInstr -Force
        Write-Host "[OK] $($f.Name)  →  $dstInstr"
    }
}

Write-Host ""
Write-Host "Done. $($skills.Count) skill(s) + copilot-instructions.md deployed to $XppRepo"
Write-Host "Restart Visual Studio (or reload the solution) to apply."
```

**Example invocation:**

```powershell
.\Install-D365FoCopilotSkills.ps1 `
    -CliRepo  "C:\source\d365fo-cli" `
    -XppRepo  "K:\D365FO\MyProject"
```

After the script runs your X++ repository will have:

```
.github/
  copilot-instructions.md          ← full X++ / CoC / BP rule canon
  instructions/
    coc-extension-authoring.instructions.md
    data-entity-scaffolding.instructions.md
    event-handler-authoring.instructions.md
    form-pattern-scaffolding.instructions.md
    label-translation.instructions.md
    model-dependency-and-coupling.instructions.md
    object-extension-authoring.instructions.md
    review-and-checkpoint-workflow.instructions.md
    security-hierarchy-trace.instructions.md
    table-scaffolding.instructions.md
    x++-class-authoring.instructions.md
    xpp-best-practice-rules.instructions.md
    xpp-class-and-method-rules.instructions.md
    xpp-database-queries.instructions.md
    xpp-statement-and-type-rules.instructions.md
```

Commit these files so every developer on the project gets the same Copilot context automatically.

### Keep Skills up to date

When you pull a new version of `d365fo-cli`, re-emit the Skills and re-run the install script:

```powershell
# In the d365fo-cli clone
cd C:\source\d365fo-cli
git pull
python scripts/emit-skills.py

# Re-deploy to your X++ project
.\Install-D365FoCopilotSkills.ps1 `
    -CliRepo "C:\source\d365fo-cli" `
    -XppRepo "K:\D365FO\MyProject"

# Then commit the updated files in your X++ project
cd K:\D365FO\MyProject
git add .github/
git commit -m "chore: update d365fo Copilot skills"
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PACKAGES_PATH_NOT_FOUND` | Set `D365FO_STANDARD_PACKAGES_PATH` or pass `--packages <PATH>`. |
| `UNSUPPORTED_PLATFORM` | `build` / `sync` / `test` / `bp` require Windows + a D365FO dev VM. Run them there. |
| Index file appears locked | Stop any running `d365fo daemon` or `d365fo-mcp` process. WAL sidecar files (`-wal`, `-shm`) are normal. |
| Copilot Chat in VS gives "There was an error executing code search" then generic X++ advice | VS Copilot Chat cannot search AOT XML (it always errors). The `.github/copilot-instructions.md` should prevent the code-search attempt — re-run `Install-D365FoCopilotSkills.ps1` to deploy the latest instructions. For full agent capabilities (auto-running `d365fo`), switch Copilot Chat to **Agent** mode (mode dropdown, top-right of the Chat pane). |
| Extract missed a package | Confirm the `<root>/<Package>/<Model>/AxTable/…` layout and point `--packages` at the real `PackagesLocalDirectory`. |
| Label values contain junk | `search label` / `get label` strip control characters by default — pass `--raw-text` to see the unfiltered value. |
| Self-contained binary won't start on Linux | `chmod +x d365fo` after copying out of the publish folder. |

---

## Next steps

- [EXAMPLES.md](EXAMPLES.md) — one worked example per command.
- [ARCHITECTURE.md](ARCHITECTURE.md) — index schema, guardrails, bridge.
- [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) — why CLI + Skills beats MCP on token cost.
- [MIGRATION_FROM_MCP.md](MIGRATION_FROM_MCP.md) — switching from `d365fo-mcp-server`.
