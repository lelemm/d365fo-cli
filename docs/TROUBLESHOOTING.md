# Troubleshooting

Common failure modes and their fixes. For installation details see [SETUP.md](SETUP.md).

---

## Package path patterns

The `D365FO_PACKAGES_PATH` environment variable (or `--packages <PATH>`) must point to the root of `PackagesLocalDirectory`. Common locations:

| Environment | Typical path |
|---|---|
| Cloud-hosted dev VM (Azure) | `K:\AosService\PackagesLocalDirectory` |
| Local Hyper-V devbox | `C:\AOSService\PackagesLocalDirectory` |
| Azure Files share (mounted) | `Z:\PackagesLocalDirectory` (drive letter varies) |
| Docker container | `/mnt/packages` (depends on volume mount) |
| UDE (primary — shared drive) | `K:\AosService\PackagesLocalDirectory` |
| UDE (extra — local laptop) | `C:\LocalMetadata\PackagesLocalDirectory` — set in `D365FO_EXTRA_PACKAGES_PATH` |
| Custom override | Set `D365FO_PACKAGES_PATH` to any absolute path |

### UDE — two separate `PackagesLocalDirectory` folders

UDE setups split standard Microsoft metadata (on a shared drive) from your custom model XML (on the local laptop). The CLI supports this via `D365FO_EXTRA_PACKAGES_PATH` or `--extra-packages`:

```powershell
# Environment variables (persist in $PROFILE)
$env:D365FO_PACKAGES_PATH         = 'K:\AosService\PackagesLocalDirectory'
$env:D365FO_EXTRA_PACKAGES_PATH   = 'C:\LocalMetadata\PackagesLocalDirectory'
d365fo index extract

# — or — one-shot CLI flags
d365fo index extract `
    --packages       K:\AosService\PackagesLocalDirectory `
    --extra-packages C:\LocalMetadata\PackagesLocalDirectory
```

`D365FO_EXTRA_PACKAGES_PATH` accepts semicolon- or comma-separated paths. Extra roots that don't exist are silently skipped. See [SETUP.md — UDE setup](SETUP.md#ude-unified-developer-experience-setup) for the full walkthrough.

```sh
# PowerShell — set for the current session
$env:D365FO_PACKAGES_PATH = "K:\AosService\PackagesLocalDirectory"

# Persist across sessions (append to $PROFILE)
d365fo init --persist-profile
```

The CLI also accepts `--packages <PATH>` on every command as a one-shot override without touching the environment.

---

## Common extraction failures

### `PACKAGES_PATH_NOT_FOUND`

```
error: { "code": "PACKAGES_PATH_NOT_FOUND", "hint": "Set D365FO_PACKAGES_PATH or pass --packages <PATH>" }
```

Fix: set `D365FO_PACKAGES_PATH` or pass `--packages`. Verify the path exists: `Test-Path $env:D365FO_PACKAGES_PATH`.

### Unicode characters in the path

Paths containing non-ASCII characters (accented letters, CJK, etc.) can cause the .NET file-system walker to throw `DirectoryNotFoundException` on certain Windows builds. Fix: move the packages directory to an ASCII-only path, or set a junction point:

```powershell
New-Item -ItemType Junction -Path "C:\D365Packages" -Target "K:\AosService\PackagesLocalDirectory"
$env:D365FO_PACKAGES_PATH = "C:\D365Packages"
```

### Locked AOT files during build

If Visual Studio is actively compiling when `index extract` runs, certain `.xml` files may be locked by `MSBuild`. The extractor skips locked files and records a warning in the extraction log. Run `index extract` again after the build completes:

```sh
d365fo index extract --model MyModel
d365fo index history --model MyModel   # confirm no extraction errors
```

### `.NET 4.8 bridge not found` on non-Windows systems

The `D365FO.Bridge` child process requires the .NET Framework 4.8 runtime, which is Windows-only. On macOS or Linux the bridge is unavailable and the CLI falls back to the SQLite index automatically:

```
warning: bridge unavailable on this platform; falling back to index
```

This is expected behaviour — the index still serves `search`, `get`, `find`, and `generate` commands. Only `find refs --xref` (which queries `DYNAMICSXREFDB` directly) requires the bridge.

---

## SQLite WAL-mode locking

The index uses WAL (Write-Ahead Logging) mode for concurrent reads. You may see these files alongside the main database:

```
d365fo-index.sqlite
d365fo-index.sqlite-wal      ← in-progress transactions
d365fo-index.sqlite-shm      ← shared memory for WAL
```

**Symptom:** `d365fo index extract` hangs or returns `DATABASE_LOCKED`.

**Cause:** Two writers accessing the database simultaneously — typically a `daemon` process and a manual `index extract` running at the same time.

**Fix:**

1. Stop any running daemon: `d365fo daemon stop`
2. Stop any running `d365fo-mcp` process.
3. Delete stale WAL sidecars **only when no process is using the database**:
   ```sh
   # PowerShell
   Remove-Item "$env:LOCALAPPDATA\d365fo-cli\d365fo-index.sqlite-wal" -ErrorAction SilentlyContinue
   Remove-Item "$env:LOCALAPPDATA\d365fo-cli\d365fo-index.sqlite-shm" -ErrorAction SilentlyContinue
   ```
4. Re-run extraction, then optimise: `d365fo index optimize`

`index optimize` runs `PRAGMA wal_checkpoint(FULL)` + `PRAGMA optimize` which compacts the WAL and reclaims space. Schedule it periodically in CI.

---

## Bridge child process issues

**Symptom:** Commands that normally use the bridge (`get table`, `find refs --xref`) return stale or incomplete data, or `_source: "index"` when you expect `"bridge"`.

**Checklist:**

| Check | Command |
|---|---|
| Is the bridge enabled? | `echo $env:D365FO_BRIDGE_ENABLED` — must be `1` or `true` |
| Is `D365FO_BIN_PATH` set? | Must point to the D365FO binaries folder (contains `Microsoft.Dynamics.Ax.Xpp.Support.dll`) |
| Is the bridge executable present? | `Test-Path "$env:D365FO_BRIDGE_PATH"` or auto-discovered at `<CLI root>/bin/D365FO.Bridge.exe` |
| Is the target platform Windows? | Bridge is Windows-only; non-Windows always falls back |

Bridge startup errors are written to stderr. Capture them:

```powershell
d365fo get table CustTable --output json 2>bridge-stderr.txt
```

Common message: `Assembly not found: Microsoft.Dynamics.Ax.Xpp.Support`. Fix: ensure `D365FO_BIN_PATH` points to the correct version of the D365FO binaries.

---

## Label language detection

### Expected language code format

Label files follow the pattern `<File>.<lang-tag>.label.txt` (e.g. `ApplicationCommon.en-US.label.txt`, `ApplicationCommon.cs-CZ.label.txt`). Language codes use IETF BCP 47 format (`en-US`, `cs-CZ`, `de-DE`, not `en`, `cs`, `de`).

### `--languages` flag

When running `index extract`, the extractor indexes all `.label.txt` files it finds. To limit extraction to specific languages (faster, smaller index):

```sh
d365fo index extract --languages en-US,cs-CZ
```

### Label not found after extraction

If `d365fo resolve label @SYS12345 --lang cs-CZ` returns `LABEL_NOT_FOUND`:

1. Check the language code is correct: `d365fo index status --output json` lists indexed languages under `data.languages`.
2. Re-run extraction with the language included: `d365fo index extract --languages cs-CZ --force`.
3. Verify the label file exists on disk: `Get-ChildItem $env:D365FO_PACKAGES_PATH -Recurse -Filter "*.cs-CZ.label.txt"`.

---

## Schema migration

### When the index was built with an older schema version

The CLI auto-migrates the schema on first connection via `EnsureSchema()`. This is safe and additive — it never drops existing data.

The migration is applied automatically on first connection and is always additive:

- New columns are added via `ALTER TABLE … ADD COLUMN` with safe defaults (`NULL` or `0`).
- New tables (e.g. `BusinessEvents`, `SecurityPolicies`, `ConfigurationKeys`, `Tiles`) are created empty.
- Lint flag columns (`HasInsertInLoop`, `HasNestedSelect`, etc.) default to `0` in existing rows.

After migration, newly added columns are empty until you re-extract:

```sh
d365fo index extract --force    # re-populate all models with new columns
```

### When to use `index build` vs `index extract`

| Command | What it does | When to use |
|---|---|---|
| `index build` | Creates the SQLite schema (no data) | First time only, or after `index drop` |
| `index extract` | Walks AOT XML and populates the database | After build, after `--force`, or to pick up new objects |
| `index refresh` | Incremental: re-extracts only changed models | Routine use — after editing XML files |
| `index optimize` | WAL checkpoint + ANALYZE | Periodically, or after a large extraction |

If you see `NO_INDEX` errors, run `index build` then `index extract`. If data is stale but the schema is intact, run `index refresh` or `index refresh --force`.

---

## MCP tool count and token budget

### How many tools are exposed

The MCP adapter (`d365fo-mcp`) exposes approximately 70 tools covering all CLI search, get, find, generate, and analyze commands. Each tool definition is included in the model context on every turn.

### Reducing token usage

The primary way to reduce token cost is to use the CLI directly instead of the MCP adapter:

| Approach | Cost per turn |
|---|---|
| MCP adapter (~70 tools) | ~3,500 tokens injected into every turn |
| CLI + lazy-loaded Skills | ~100 tokens — one shell tool definition |

See [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) for the full analysis.

### Using targeted search instead of `search any`

`search any` UNIONs every indexed kind, which returns many results and uses more tokens. Prefer targeted commands when you know the object type:

```sh
# Broad — returns hits across all kinds
d365fo search any CustCustom --output json

# Targeted — only tables; fewer results, cheaper
d365fo search table CustCustom --output json

# Batch — multiple targeted lookups in one process call
d365fo search batch CustTable SalesTable CustAccount --output json
```

`--limit N` (default 25) caps result set size on every search command.
