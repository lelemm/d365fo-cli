# Configuration

`d365fo` resolves settings in the following priority order — first match wins:

1. **Explicit CLI flags** (`--packages`, `--db`, …) — highest priority
2. **Process environment variables** (`D365FO_PACKAGES_PATH`, …)
3. **JSON config file** (`%LOCALAPPDATA%\d365fo-cli\settings.json` on Windows, `~/.local/share/d365fo-cli/settings.json` on Linux/macOS)
4. **Built-in defaults** (e.g. `en-us` language, default SQLite path)

---

## Environment variables

| Variable | Purpose | Default |
|---|---|---|
| `D365FO_PACKAGES_PATH` | Primary `PackagesLocalDirectory` root | *(required for indexing)* |
| `D365FO_EXTRA_PACKAGES_PATH` | Additional `PackagesLocalDirectory` roots (semicolon/comma separated) | — |
| `D365FO_LABEL_LANGUAGES` | Label languages to extract, e.g. `en-us,cs,de` | `en-us` |
| `D365FO_INDEX_DB` | Path to the SQLite index file | `%LOCALAPPDATA%\d365fo-cli\d365fo-index.sqlite` |
| `D365FO_WORKSPACE_PATH` | Root of your X++ solution (enables scaffold output) | — |
| `D365FO_CUSTOM_MODELS` | Comma-separated list of custom model names | — |
| `D365FO_BRIDGE_*` | Bridge server connection settings (see ARCHITECTURE.md) | — |
| `D365FO_XREF_CONNECTIONSTRING` | Cross-reference DB connection string | — |

---

## JSON config file

The JSON config file is the **recommended** way to persist settings when running `d365fo` from multiple shell hosts (Windows PowerShell 5.1, PowerShell 7, Visual Studio Developer PowerShell, CI, etc.) because it is not tied to any shell profile.

### Location

| OS | Default path |
|---|---|
| Windows | `%LOCALAPPDATA%\d365fo-cli\settings.json` |
| Linux | `~/.local/share/d365fo-cli/settings.json` |
| macOS | `~/Library/Application Support/d365fo-cli/settings.json` |

### Format

A flat JSON object mapping variable names to string values:

```json
{
  "D365FO_PACKAGES_PATH": "K:\\AosService\\PackagesLocalDirectory",
  "D365FO_INDEX_DB": "C:\\Users\\you\\AppData\\Local\\d365fo-cli\\d365fo-index.sqlite",
  "D365FO_LABEL_LANGUAGES": "en-us,cs"
}
```

### Creating / updating the file

Run `d365fo init --persist-profile` — this writes (or updates) both the JSON config file and the shell profiles for all PowerShell versions found on the machine.

To write the file manually, create it at the path above. Only the keys you need to override have to be present; missing keys fall back to environment variables and then built-in defaults.

---

## Developer PowerShell in Visual Studio

VS Developer PowerShell is **Windows PowerShell 5.1** (`powershell.exe`), which reads a **different** `$PROFILE` than PowerShell 7 (`pwsh.exe`):

| Shell | Profile |
|---|---|
| Windows PowerShell 5.1 (VS Developer PowerShell) | `%USERPROFILE%\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1` |
| PowerShell 7+ (`pwsh`) | `%USERPROFILE%\Documents\PowerShell\Microsoft.PowerShell_profile.ps1` |

If you only set env vars in one profile, `d365fo doctor` will report different results from the other shell. **Use the JSON config file** (via `d365fo init --persist-profile`) to avoid this.

Alternatively, set variables at **machine scope** so they are inherited by every process:

```powershell
[System.Environment]::SetEnvironmentVariable(
    "D365FO_PACKAGES_PATH",
    "K:\AosService\PackagesLocalDirectory",
    [System.EnvironmentVariableTarget]::Machine)
```
