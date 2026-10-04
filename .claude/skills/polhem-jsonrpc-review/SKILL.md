---
name: polhem-jsonrpc-review
description: A repeatable methodology for a "full health check" of the Polhem.JsonRpc packages. Read-only review by dimension (package boundaries and dependencies, JSON-RPC 2.0 specification conformance, security and untrusted input, payload wire format stability, public API surface, trim/AOT, performance on the request path, concurrency and async, maintainability and unnecessary types, test quality, documentation drift), scanned by parallel subagents grouped by what they read (exhaustive, not sampled), cross-deduplicated and consolidated into a graded (P0~P4) plan plus a 10-point score per dimension. Includes a checklist per dimension grounded in ADR-001 and ADR-002, the guard mechanisms the next round must confirm, and the methodology lessons carried over from the Polhem framework's health checks. Use it when the user asks for a "health check", "full review", "architecture review", "code review of the whole repository", "score the packages", "is this ready for the next release", "propose a refactoring plan" and similar whole-repository review requests, even if they do not say "health check". **The target is the package code under `src/`** (with `tests/` and `samples/` as evidence); reviewing a single diff is `/code-review`, and reviewing CLAUDE.md or skills is a configuration audit, not this skill. **This skill only reviews, scores and writes the plan; it does not change code.**
---

# Full health check of Polhem.JsonRpc

Run a structured health check over every package under `src/` and produce a **graded plan** and a **10-point score per
dimension**. The method is **read-only scanning by parallel subagents**, followed by cross-deduplication and
consolidation by the main session.

This skill is derived from the Polhem framework's `polhem-framework-review`. The repository is far smaller than the framework,
so the dimensions are fewer and sharper, and the weight moves from layering to the two things this
repository uniquely owns: **the JSON-RPC wire and the payload wire format**, both of which other code (the Polhem framework,
`polhem-connector-js`) depends on byte for byte.

## When to use / what it produces

- **Trigger**: the user asks for a full review / health check / scoring / a refactoring plan / release readiness.
- **Output**: one report per agent plus a graded plan and a score table in the conversation. **Every report goes to a
  durable location the moment it is written**: each subagent writes its report into
  `local/internal/health-<review-date>/<agent>.md`, and the plan goes to `local/plans/`. Never leave reports in the
  scratchpad. `local/` is ignored by git: never commit a report and never `git add -f` it, because it can list unfixed
  security issues.
- **Discipline**: **read-only throughout.** Fixes are a separate step, decided by the user after reading the plan, and
  reach `main` through pull requests as `.claude/CLAUDE.md` describes.

## Ask first (AskUserQuestion)

Each question with explicit options and the recommended one marked:

1. **Extra dimensions** (multi-select): none (recommended) / interoperability with `polhem-connector-js` (read its
   envelope and frame code against ADR-002 decision 2) / the Polhem framework's use of the packages (read its filters,
   object factory and method policy as a consumer).
2. **Scope** (single): `src/` with `tests/` and `samples/` as evidence (recommended) / `src/` only.
3. **Mode** (single): read-only review + plan document (recommended) / verbal report only / multi-agent workflow deep scan
   (requires the user's explicit consent to large-scale orchestration).

## The dimensions

| # | Dimension | Core question |
|---|-----------|---------------|
| 1 | Package boundaries and dependencies | Does every package reference only what ADR-001 decision 1 and ADR-002 decision 1 allow, in one direction |
| 2 | Specification conformance | Does every request, response, batch, notification and error behave as JSON-RPC 2.0 defines it |
| 3 | Security and untrusted input | Is everything reachable before the application's code bounded, fail-closed and free of leaks |
| 4 | Payload wire format stability | Are the envelope, frame, encryption and compression byte-for-byte what ADR-002 decision 2 specifies |
| 5 | Public API surface | Is the shipped surface minimal, documented, and changed only compatibly |
| 6 | Trim / AOT | Do the packages that promise AOT keep the promise on every path, and do the others say they do not |
| 7 | Performance on the request path | Is per-request work free of uncached reflection, rebuilt options and needless copies |
| 8 | Concurrency and async | Is shared state safe, bounded, and are tasks awaited correctly with cancellation flowing |
| 9 | Maintainability and unnecessary types | Naming, one type per file, identifier comparison, dead code, facades, large files |
| 10 | Test quality | Are the tests asserting what their names claim, and do the gates actually bite |
| 11 | Documentation drift | Do READMEs, CHANGELOG, samples and ADRs still describe the code |

## Method: parallel agents, grouped by what they read

The code base is small enough that one agent per dimension would mostly re-read the same files. Dispatch **one
`general-purpose` subagent per row** below, in the background and in parallel, so each reads one coherent part:

| Agent | Dimensions | Reads mostly |
|-------|-----------|--------------|
| `wire` | 2, 4 | `src/Polhem.JsonRpc`, `src/Polhem.JsonRpc.Payload`, the serializer, specification and vector tests |
| `security` | 3 | the dispatcher, the HTTP handler, the payload filter, the encryptor, the replay window |
| `surface` | 1, 5, 6 | every `.csproj`, `Directory.Build.*`, `PublicAPI.*.txt`, the AOT smoke test, `build-ci.yml` |
| `runtime` | 7, 8, 9 | the dispatcher, the connector, the transports, the replay store, the options types |
| `evidence` | 10, 11 | `tests/`, `samples/`, every README, the CHANGELOGs, `maintainers/adr/` |

Each agent:

- Is **strictly read-only on the repository**. The prompt says "you must not modify any repository file; the only files
  you write are your own report and any probe project, both under `local/`", and names the report path.
- Receives **its dimensions' checklists below and the relevant passages of `.claude/CLAUDE.md` and the ADRs**, quoted in
  the prompt rather than "go and read everything".
- Reports in one format: P0~P4, each item with `path:line`, the problem (WHY) and a recommendation, plus a list of what it
  scanned and found clean.
- Scans **exhaustively** with grep and glob and lists the **complete set**; "there are some" is not accepted.
- Uses `git log` / `git show` to say when each finding was introduced, so a regression is told apart from an old problem
  seen for the first time.

After all reports are in, the main session **cross-deduplicates** and **settles severity itself by reading the source**: a
finding reported by two agents is more certainly real, not more certainly severe.

> This is ordinary subagent delegation, not billed workflow orchestration. Switch to the Workflow tool only if the user
> chose the deep scan.

## What the build already enforces (do not rescan)

A green `build-ci.yml` already proves these, so the review does not repeat them:

- Formatting and the analyzer rules in `.editorconfig` and `src/.editorconfig`, under `TreatWarningsAsErrors` and
  `EnforceCodeStyleInBuild`; this includes **CA2007** (`ConfigureAwait(false)` on every await in `src/`).
- Nullable warnings, XML documentation warnings (`GenerateDocumentationFile`), public API declared in `PublicAPI.*.txt`
  (`PublicApiAnalyzers`).
- The dependency gate `JSONRPC9001` (`src/Directory.Build.targets`) and the trim/AOT analyzers on the projects that
  enable them.
- Native AOT publishing of `tests/Polhem.JsonRpc.AotSmoke` (the `aot` job) and what SonarCloud reports.

The review checks what these **cannot** see: whether a declared API is right, whether a gate is vacuous, which paths the
AOT smoke test never runs, and every semantic rule.

## Checklists (paste the relevant ones into each agent's prompt)

### 1. Package boundaries and dependencies (ADR-001 decision 1, ADR-002 decision 1)

- Extract every `<ProjectReference>`, `<PackageReference>` and `<FrameworkReference>` under `src/`. Allowed edges:
  `.Server → Polhem.JsonRpc`; `.AspNetCore → .Server` + ASP.NET Core; `.Client → Polhem.JsonRpc`;
  `.Payload → Polhem.JsonRpc`; `.Payload.Server → .Payload, .Server`. Anything else is a finding.
- Hard constraints: the client never references the server; the core packages never reference the payload packages;
  nothing references Polhem; no Newtonsoft.Json or MessagePack anywhere in `src/`.
- Check the gate covers what it claims: `JSONRPC9001` sees direct package and framework references, and
  `DependencyGateTests` sees referenced assemblies. Is every package in its `TheoryData` (a new package missing from it is
  unguarded)?

### 2. Specification conformance (JSON-RPC 2.0)

Read the specification sections against `JsonRpcSerializer`, `JsonRpcDispatcher` and the HTTP handler, and check each
case has a test (`SpecificationExampleTests`, `SerializerTests`, `DispatcherTests`, `HttpHandlerTests`):

- `jsonrpc` must be exactly `"2.0"`; a missing or wrong value is `-32600`.
- `id`: string, number or null; numbers with fractions and very large numbers; a request with `"id": null` is a request,
  not a notification; the response echoes the id in the same JSON kind.
- Notifications get no response, inside and outside a batch; a batch of only notifications returns nothing (and over
  HTTP, no body).
- Batches: an empty array is a single `-32600`; non-object elements each get `-32600`; `MaxBatchSize` is enforced and
  its error is well-formed; response order is not relied on.
- Invalid JSON is `-32700` with `id: null`; a valid JSON that is not a request is `-32600`.
- `params` by name only; positional `params` is `-32602` (ADR-001 decision 2, step 6); a missing `params` for a method
  that takes one.
- A response carries `result` **or** `error`, never both; `error.data` is optional; members the specification does not
  define are ignored on read (ADR-001 decision 6, amended).
- Error codes: the reserved range `-32768..-32000` is used only as the specification assigns it; `JsonRpcErrorCodes`
  matches the specification names and values.

### 3. Security and untrusted input

Everything up to the method's own code runs on attacker-controlled bytes, often before authentication. For each item
name the bound and the test that pins it.

- **Method reachability** (fail-closed): `JsonRpcMethod.IsResolvableAction` (public, non-generic, instance, one
  parameter, not an accessor, not declared by `object`); ambiguity is not resolved; `JsonRpcNamingConventionPolicy`
  admits only `{Action}Request` → `{Action}Response`. Look for anything reachable that the ADRs say is not: inherited
  methods, explicit interface implementations, `Task<T>` unwrapping, methods on base types, `ProgId` / action grammar and
  length limits, case sensitivity.
- **Order**: method policy before filters, so a payload is only decrypted for a call that may happen (ADR-001 decision 2,
  step 5; ADR-002 decision 4). Confirm in the code, not the ADR.
- **Transport identity** comes only from the transport, never from headers or `params` (ADR-001 decision 4,
  `HttpHandlerTests.Post_HeaderClaimsInProcess_StillMarkedHttp`).
- **Resource bounds**: `JsonRpcHttpOptions.MaxRequestBodySize` (both with and without `Content-Length`, chunked bodies);
  `JsonRpcServerOptions.MaxBatchSize`; JSON nesting depth (`JsonDocumentOptions.MaxDepth`, `JsonSerializerOptions.MaxDepth`)
  on the envelope **and** on `params`; the gzip decompressed-size limit; Base64 length before decoding; the replay
  store's growth under a flood of unique sequence numbers or scopes.
- **Payload cryptography** (ADR-002 decisions 2 and 6): AES-256-CBC + HMAC-SHA256, encrypt-then-MAC, HMAC verified with
  `CryptographicOperations.FixedTimeEquals` **before** decryption, IV from `RandomNumberGenerator` every time, length
  fields validated before slicing (no out-of-range on a truncated body), key length checked, padding errors not
  distinguishable from MAC errors.
- **Frame and replay**: version byte checked; timestamp tolerance; sequence uniqueness per scope; "frame required" is a
  deployment setting never read from the request (ADR-002 decision 7, downgrade).
- **Type names** (ADR-002 decision 5): no `Type.GetType` on a received name anywhere; where the reader chose the type,
  the name is only compared; the allow-list screens the whole assembly-qualified name, generic arguments included.
- `NoPayloadEncryptor` refused unless `PayloadOptions.AllowNoEncryption`; built-in codecs cannot be replaced by name.
- **Leaks**: error messages and `error.data` never carry exception messages of unexpected exceptions, stack traces,
  types, paths, keys or tokens; the internal-error path maps to `-32603` without detail.
- Exceptions: no `catch (Exception)` that swallows, no empty catch, no `throw ex;`; `OperationCanceledException` from the
  client's abort is not turned into `-32603`.

### 4. Payload wire format stability (ADR-002 decision 2)

- Envelope members `format` / `value` / `type` / `codec`; `format` as a number; `codec` omitted when blank; Base64 for
  encoded and encrypted bodies.
- Encoding order serialize → compress → frame → encrypt, decoding the exact reverse, no step skippable.
- Frame: 17 bytes, version 1, big-endian timestamp and sequence; ciphertext layout with little-endian lengths.
- `PayloadWireVectorTests` holds vectors produced by the Polhem implementation **before** the extraction. Check that the
  vectors are compared byte for byte (not round-tripped through the same code, which would pass with any format) and
  that every format × codec × compressor combination the ADR names has one.
- Any diff to the vectors or to the code that writes them since the last release is a **protocol change**: it must be in
  the CHANGELOG as breaking and arranged with the Polhem framework and `polhem-connector-js`.

### 5. Public API surface

- Read `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` of every package. For each public type ask whether a
  consumer needs it; implementation types that are public but should be internal are findings (cheap before the next
  major, expensive after).
- Unshipped changes: is each binary compatible (adding an optional parameter to an existing public method, or a member to
  a public interface, is not)? Is each in the CHANGELOG?
- Mutability: options types read once at construction (`JsonRpcDispatcher` copies `MaxBatchSize`) versus read on every
  request; a setter that has no effect after start is a trap unless documented.
- Exposed concrete collections where `IReadOnlyList` / `IReadOnlyDictionary` would do; public mutable fields.
- Extension points (`IJsonRpcObjectFactory`, `IJsonRpcMethodPolicy`, `IJsonRpcFilter`, `IJsonRpcParameterBinder`,
  `IJsonRpcClientInterceptor`, `IJsonRpcTransport`, the payload interfaces): each has a reason in an ADR, a default, and a
  test that replaces it.
- XML docs: English, `<see cref>` for types in this solution, no inventory counts, and every absolute claim ("never",
  "always") names what enforces it.

### 6. Trim / AOT (ADR-001 decision 7, ADR-002 decision 1)

- `Polhem.JsonRpc`, `.Client` and `.Payload` are `IsAotCompatible`; `.Server`, `.AspNetCore` and `.Payload.Server`
  enable the trim analyzer and annotate reflection with `RequiresUnreferencedCode` / `RequiresDynamicCode`. Look for an
  annotation that is missing, or one that is suppressed with `UnconditionalSuppressMessage` without a justification that
  holds.
- **Which paths `tests/Polhem.JsonRpc.AotSmoke` runs**, and which public paths of the three AOT packages it never runs
  (batches, notifications, interceptors, each payload format and codec, error mapping). The analyzer passes on code that
  is never rooted; only running proves it.
- Parameters and results go through the application's `JsonSerializerOptions`; look for any internal serialization that
  uses reflection-based `JsonSerializer` overloads without a `JsonTypeInfo`.

### 7. Performance on the request path

**Identify the hot paths first and prove each finding is on one** (trace the callers). An O(n²) at startup is P4.
Hot paths: HTTP read → parse → resolve → policy → filters → bind → invoke → serialize → write; per element of a batch;
the client's serialize → send → parse; the payload encode/decode per call.

- Method resolution is cached (`JsonRpcDispatcher._actions`); look for reflection that is **not** behind it (policy
  checks, parameter type lookups, `Task<T>` result extraction via reflection per call).
- `JsonSerializerOptions` and `JsonTypeInfo` never built per call (a per-call `new` defeats STJ's metadata cache).
- Body handling: the request read once, not buffered twice; `params` not re-serialized to text and parsed again on the
  way to the binder or a filter; `JsonDocument` disposed.
- Client: one `HttpClient` per transport, not per call; batches sent as one request.
- Payload: gzip on tiny bodies (fixed overhead), `ArrayPool` / stack use around the frame and encryption.
- No `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on any path.

### 8. Concurrency and async

**Each finding must prove concurrent reachability**: state set once before the first request and read-only afterwards
is safe.

- Shared state: `JsonRpcDispatcher._actions`, `PayloadTypeRegistry`, `PayloadOptions` (its codec lock),
  `MemoryPayloadReplayStore`, `ReplayWindow`. For each: what writes it, can a request write it, is the check-then-act
  atomic.
- **Unbounded growth** in the replay store and any cache keyed by request data: who evicts, and can an unauthenticated
  caller make it grow.
- Cancellation: the HTTP abort token reaches the method and the transport; the client honours its token and does not
  leak the response on cancel.
- `IJsonRpcObjectFactory.ReleaseObjectAsync` runs on success, failure and cancellation (ADR-001 decision 2, step 7).
- `async void`, fire-and-forget, sync-over-async, `IDisposable` / `IAsyncDisposable` not disposed, DI lifetimes in
  `JsonRpcServiceCollectionExtensions` (a singleton capturing a scoped service).

### 9. Maintainability and unnecessary types

- Identifier comparison (method names, ProgIds, codec names, type names, header names) always `Ordinal` /
  `OrdinalIgnoreCase` as the meaning requires; grep `CurrentCulture`, `ToLower()`, `ToUpper()`, `string.Compare` without a
  comparison.
- One type per file; folders match namespaces; files over 500 lines split by concern (the largest file today is the
  serializer; measure, do not assume).
- Pure facades and 1-line wrappers; non-public members with zero callers; duplicate logic (the same envelope or frame
  parsing in two places); `*Helper` / `*Func` names.
- Comments: WHY not WHAT, no commented-out code, absolute claims backed by a named mechanism.

### 10. Test quality

- Tests that assert nothing (S2699), assert only `NotNull`, or round-trip through the code under test where a fixed
  expected value is needed (wire vectors, serializer output).
- Every test has a `DisplayName` on its `[Fact]` / `[Theory]` and is named `<Method>_<Scenario>_<Expected>`;
  `TestConventionTests` enforces both, so check that it still covers every test rather than re-listing names.
- Coverage of security logic: every rejection in dimension 3 has a test that sends the hostile input and checks the exact
  error.
- The sample tests (`QuickStartSampleTests`, `PayloadQuickStartSampleTests`) exercise what the sample READMEs tell a
  reader to do.
- Gates are not vacuous: `DependencyGateTests` lists every package; the vectors cannot pass with a changed format;
  the AOT smoke test exits non-zero on a wrong result, not only on a crash.
- Wall-clock dependence (replay window, timestamp tolerance) uses an injectable time source, not `DateTime.UtcNow`.

### 11. Documentation drift

Public documents: the root `README.md` / `README.zh-TW.md`, `CHANGELOG.md` / `CHANGELOG.zh-TW.md`, each sample's README
pair, and each package's README (English only, the nuget.org page). `maintainers/` and the ADRs are checked for
distorted design records only.

- **Compilability first**: every type, method, option and extension method named in a README or sample README exists
  with that exact name and signature. Copy the README snippets into a probe project under `local/` and build it.
- Bilingual pairs have the same sections and the same content; the language switch line is on both.
- CHANGELOG: every commit since the last tag that changes public API or wire format is recorded; breaking changes are
  marked.
- ADRs: each mechanism and test an ADR names still exists (ADR-001 and ADR-002 name tests by method name); amendments
  are marked in place.
- `./check-md-links.sh` is clean (the Docs Check workflow runs it; confirm it still does).

## Consolidation and output

### Grades (P0~P4)

| Grade | Meaning |
|-------|---------|
| **P0** | Wrong behaviour on the wire: specification violation, payload format drift, a security hole, a false-green gate |
| **P1** | Security hardening and unbounded resources; public API that must change before it is harder to change |
| **P2** | Structural: performance on a proven hot path, concurrency, large files, dead code |
| **P3** | Documentation drift |
| **P4** | Observations and decisions for the user |

Each item: `path:line`, the problem (WHY), a recommendation, the grade. At the end, a list of **items scanned as clean**
(the baseline for the next round) and a **recommended execution order**, with batch boundaries drawn by file ownership.

### Scores (10 per dimension)

One score per dimension, the main deductions mapped to finding numbers, and an average.
**9+**: nothing to fix but wording. **7~8.5**: solid, with fixable consistency gaps. **6~7**: pulled down by concrete
correctness bugs. Say which P0/P1 fixes would raise each score and to what. Break each change from the previous round
into "the code improved" and "the scan went deeper", so a rising score is not mistaken for fewer problems.

### Plan document

Follow the plan convention of `.claude/CLAUDE.md`: a file in `local/plans/`, a status line at the top, a phase table
(P0~P4), and a link to the file in the reply.

## Methodology lessons (carried over from the Polhem framework's rounds)

- **Write each report to `local/` the moment it exists.** A scratchpad cleared before consolidation once lost every
  report and every probe project of a round.
- **Re-verify every item the previous round marked fixed** before scanning anything new. Status markers have claimed
  fixes whose commit did not touch the lines in question.
- **A finding is a pointer, not a specification.** Whoever fixes it re-reads the code first and reports "already fixed"
  as a result.
- **Ask what the scan unit hides.** Scanning "does this type have callers" misses attributes; scanning "is this method
  tested" misses which inputs were sent.
- **A guard guards "do not change", not "was right".** A baseline ratified on the day it is built ratifies whatever was
  wrong that day; check correctness separately when establishing one.
- **P0 findings are worth measuring.** Build a probe project under `local/` that references the packages and goes through
  the public API; the failure mode (thrown, silent, wrong value) decides the fix.
- **Cross-check any "this is dead code" or "this protection does nothing" conclusion** by reading the source; a redundant
  half-condition is easily mistaken for a dead mechanism.

### Lessons of this repository's own rounds

- **A fix PR is reviewed adversarially before it merges.** In three rounds running, the previous round's fixes brought
  in a P1 that nobody had looked at from an attacker's side or from how the Polhem framework calls the code. What stopped
  it: before auto-merge is enabled, one agent reads only the PR's diff, as an attacker and as a downstream user.
- **Mutate the code a claim is about, not only the code a test names.** A sentence in an XML doc, the CHANGELOG or an
  ADR that says "always", "only", "once" or "before" is a claim; break the code it describes and see whether a test
  fails. Most findings of the later rounds were such claims with nothing behind them.
- **Restore a mutated file with a newer timestamp** (`touch`) and rebuild with `--no-incremental` before trusting a
  green run: a file put back with its old timestamp leaves the mutant in the binaries. A mutant that does not compile
  looks like a passing one unless the build output is checked for errors.
- **Ask what a mechanism does not cover.** The payload HMAC was correct for what it covered; the method and direction
  it did not cover made the replay protection bypassable (ADR-003). List the fields and paths outside each protection.
- **Read a release as a dependency graph.** Open version ranges let one package of the family upgrade alone; a binary
  break then reaches code nobody rebuilt (ADR-001, decision 4, amended).
- **Stop full rounds when the agents agree they add nothing.** After that, a fix PR with its adversarial review and a
  rerun of the round's mutation scripts before a release are enough; run a full round again when the dispatcher, a
  transport, the payload filter or the wire format changes.

## Baseline

Earlier rounds left their reports in `local/internal/health-<date>[-rN]/` and their plans in `local/plans/`
(`plan-health-*.md`). Read the latest plan before dispatching: its score table, its lists of items scanned as clean,
and its decisions to keep something that looks removable, with the reason, so they are not listed again. Each plan
records:

- the score table and its date;
- the **items scanned as clean**, as concrete lists, never as "dead code 0";
- the **guard mechanisms** confirmed to exist, run in CI and bite;
- decisions to keep something that looks removable, with the reason.

The guard mechanisms believed to exist today: `JSONRPC9001` and `DependencyGateTests`; `PublicApiAnalyzers` with the
`PublicAPI.*.txt` baselines, which also hold the numeric value of every error code; `PayloadWireVectorTests`, including
the ADR-003 binding vectors; `SpecificationExampleTests`; `TestConventionTests`; `ReadmeSnippetTests` with the compiled
`tests/Polhem.JsonRpc.ReadmeSnippets`; CA2007; the required checks `build`, `docs` and `aot` (the `aot` job runs
`tests/Polhem.JsonRpc.AotSmoke` published with Native AOT), with `.github/scripts/detect-docs-only.sh` skipping steps
inside each job of `build-ci.yml`; `./check-md-links.sh` in the `docs` job; and, in `nuget-publish.yml`, the checks that the tag matches
the version, that the public API is shipped, that new package IDs are confirmed, and the AOT smoke test again. Read the
list from the code at review time rather than trusting this one.

Compute every quantity from the code at review time; never compare with a number carried over from an earlier round.
