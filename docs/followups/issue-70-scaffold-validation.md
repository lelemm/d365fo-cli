# Follow-ups — scaffold validation (issue #70)

Context: issue #70 surfaced that `XppScaffolder.Enum` emitted invalid AOT XML
(missing `xmlns:i` namespace + `Yes`/`No` written into the CLR-bool
`IsExtensible`), which Visual Studio's metadata reader refused to open. The
specific generator was fixed on branch
`fix/enum-scaffold-namespace-isextensible` (commit `62f44be`). These two
follow-ups harden the *paths around* the generator so the same class of bug
can't slip through again.

---

## 1. Value-shape validation in `ScaffoldFileWriter`

**Goal:** extend the scaffold writer so it catches value-shape mistakes, not
just abstract root elements — runtime-free, so generation keeps working
offline / in CI / in Copilot sessions.

**Where:** `src/D365FO.Core/Scaffolding/XppScaffolder.cs`, the
`ScaffoldFileWriter` class (~lines 831–887). It already has
`EnsureConcreteAxRoot` rejecting abstract roots (`AxEdt`, `AxEdtExtension`) via
the `_abstractAxRoots` set. Add an analogous guard that runs in **both**
`Write(XDocument, …)` and `Write(string xml, …)` overloads:

- Require `xmlns:i="http://www.w3.org/2001/XMLSchema-instance"` on the root of
  AOT documents that need it (at minimum `AxEnum`; confirm whether
  `AxEdt*`/`AxTable` also require it against the real D365FO file format — note
  `tests/Samples/MiniAot/.../FmVehicle.xml` is a simplified hand-authored
  fixture without it, so it is **not** authoritative).
- Reject known-bad primitive encodings — specifically `Yes`/`No` in elements
  that map to CLR `bool` properties such as `IsExtensible`. Throw a clear
  `InvalidOperationException` mirroring the existing abstract-root message
  style, naming the expected `true`/`false`.

**Constraints:** no Metadata API / `D365FO.Bridge` dependency.

**Tests:** follow the abstract-root rejection tests in
`tests/D365FO.Cli.Tests/ScaffoldingSnapshotTests.cs` (~lines 80–103). Verify
with `dotnet test tests/D365FO.Cli.Tests` and `tests/D365FO.Core.Tests`.

---

## 2. Opt-in bridge verification of scaffolded files

**Goal:** when the D365FO Metadata API runtime is available, optionally re-read
a just-written file through `D365FO.Bridge` as a belt-and-suspenders check —
the same way Visual Studio would read it. **Strictly opt-in**; never blocks
offline generation.

**Where:** generate commands such as
`src/D365FO.Cli/Commands/Generate/GenerateEnumCommand.cs` (scaffold via
`XppScaffolder`, persist via `ScaffoldFileWriter.Write`). Bridge plumbing
already exists: `MetadataBootstrap.TryInitialize()`,
`MetadataBootstrap.Diagnostics()`, and handlers in
`src/D365FO.Bridge/Handlers.cs`.

**Design constraints:**
- Add a `--verify` flag (or a shared post-write hook). Only runs when the flag
  is set **and** the bridge reports available.
- When the runtime is absent, skip silently (or note it in verbose/JSON
  output) — must never fail or no-op real generation.
- Reuse the existing runtime-availability check rather than adding a new one.
- Coordinate with follow-up #1 so the writer-side shape check and the
  bridge-side check don't overlap confusingly.

**Why not always-on:** generation must work with no Metadata API runtime loaded
(Copilot/agent sessions, CI, machines without VS metadata assemblies). Making
generation depend on the bridge would break that.
