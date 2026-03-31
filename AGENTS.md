# AGENTS.md — Alma.S3Bucket (fs3bucket)

## Project Purpose

F# library (`Alma.S3Bucket`) for accessing AWS S3 Bucket storage. Provides a typed, traced API for connecting to S3 buckets, putting/getting objects as string content, and optional client-side AES-256 encryption. Published as a NuGet package.

## Tech Stack

| Component | Detail |
|---|---|
| Language | F# on .NET 10.0 |
| SDK | `global.json` pins .NET SDK 10.0.x (`rollForward: latestMinor`) |
| AWS SDK | `AWSSDK.S3 ~> 4.0`, `AWSSDK.Core ~> 4.0`, `AWSSDK.SecurityToken ~> 4.0` |
| Error handling | `Feather.ErrorHandling` (`asyncResult` CE) |
| Service identification | `Alma.ServiceIdentification` — `Instance` types for bucket naming |
| Tracing | `Alma.Tracing` — OpenTracing-style spans |
| Build system | FAKE (F# Make) v1.3.0 via `build/` project |
| Package manager | Paket |
| Lint | fsharplint (`fsharplint.json`) |

## Commands

```bash
# Restore dependencies
dotnet paket install

# Build
./build.sh build

# Run tests
./build.sh -t tests

# Publish to NuGet (CI only — requires NUGET_API_KEY)
./build.sh -t publish
```

Build options:
- `no-clean` — skip cleaning output dirs (required on CI)
- `no-lint` — run lint but ignore failures

## Project Structure

```
fs3bucket/
├── S3Bucket.fsproj             # Library project (PackageId: Alma.S3Bucket, v2.0.0)
├── AssemblyInfo.fs             # Auto-generated assembly metadata
├── src/
│   ├── S3Bucket.fs             # Core module: connect, put, get + Configuration helpers
│   └── ClientSideEncryption.fs # Client-side AES-256 encryption module
├── build/
│   ├── Build.fs                # FAKE build entry point
│   ├── Targets.fs              # FAKE target definitions
│   ├── SafeBuildHelpers.fs     # SAFE Stack helpers (unused by this library)
│   └── Utils.fs                # Build utilities
├── paket.dependencies          # Dependency definitions
├── paket.references            # Package references
├── fsharplint.json             # Lint configuration
├── global.json                 # .NET SDK version pin
├── CHANGELOG.md                # Release history
└── .github/workflows/
    ├── tests.yaml              # Tests on PRs + nightly
    ├── pr-check.yaml           # Blocks fixup commits + ShellCheck
    └── publish.yaml            # Publishes on version tags
```

## Architecture & Key Concepts

### Module: `Alma.AWS.S3Bucket.S3Bucket`

- `Configuration.forServiceAccount` / `Configuration.forAccessKey`: Helper functions for creating config.
- `connect`: Creates an `S3Bucket` handle (wraps `AmazonS3Client`). Default region: EU-West-1. Returns `AsyncResult<S3Bucket, ConnectionError>`.
- `put`: Writes a `BucketContent` (`Name` + `Content` as string) to S3.
- `get`: Reads an object by name from S3, returns content as string.
- The `S3Bucket` type is `IDisposable` — use `use!` in `asyncResult` CEs.

### Module: `Alma.AWS.S3Bucket.ClientSideEncryption.S3Bucket`

- `put`: Puts with SSE-C (AES-256 customer-provided key).
- `get`: Gets with SSE-C decryption.
- `EncryptionKey.createAES256()`: Generates a new AES-256 key (base64-encoded). Caller is responsible for persisting it.

### Bucket Naming Convention

Buckets are named using `Alma.ServiceIdentification.Instance` (`{domain}-{context}-{purpose}-{version}`), lowercased.

### Error Types

- `ConnectionError.RuntimeError of exn`
- `BucketPutError.BucketPutExn of exn`
- `BucketGetError.BucketGetExn of exn`

### Tracing

Every S3 operation creates a child trace span tagged with `component`, `peer.service`, `db.instance`, `db.type=S3Bucket`, `span.kind=client`.

## Key Dependencies

| Package | Role |
|---|---|
| `AWSSDK.S3` | AWS S3 client |
| `AWSSDK.Core` | AWS core SDK |
| `AWSSDK.SecurityToken` | AWS STS for service-account credential resolution |
| `Feather.ErrorHandling` | `asyncResult` CE and `AsyncResult` combinators |
| `Alma.ServiceIdentification` | `Instance` types for bucket naming |
| `Alma.Tracing` | Distributed tracing |

## Conventions

- **Namespace**: `Alma.AWS.S3Bucket` for the core module, `Alma.AWS.S3Bucket.ClientSideEncryption` for encryption.
- **Single-case DU wrappers**: `BucketName of Instance`, `AWSClient of AmazonS3Client`, `EncryptionKey of string`.
- **`IDisposable` resources**: `S3Bucket` and `AWSClient` implement `IDisposable`. Always use `use!` when connecting.
- **Railway-oriented error handling**: All public functions return `AsyncResult`. Use `asyncResult { }` CE.
- **Tracing via `use`**: Trace spans are `IDisposable`.
- **Internal visibility**: `putRequest`, `getRequest`, `trace`, `traceError` are `internal` — used by both plain and encrypted modules.

## CI/CD

| Workflow | Trigger | What it does |
|---|---|---|
| `tests.yaml` | PRs + nightly cron | Runs `./build.sh -t tests` on ubuntu-latest with .NET 10.x |
| `pr-check.yaml` | PRs | Blocks fixup commits + ShellCheck |
| `publish.yaml` | Git tags `[0-9]+.[0-9]+.[0-9]+` | Publishes to NuGet.org |

## Release Process

1. Increment `<Version>` in `S3Bucket.fsproj`
2. Update `CHANGELOG.md`
3. Commit and create a git tag matching the version (e.g., `2.0.0`)
4. Push tag — CI publishes automatically

## Pitfalls

- **No tests directory**: This library has no test project. Any changes require manual verification.
- **No docker-compose**: Library project — no local services needed.
- **Content is string-only**: `put` and `get` transfer content as `string`. Binary content is not supported.
- **Region hardcoded**: EU-West-1 is the default. Can be overridden via `Configuration.Region`.
- **Encryption key management**: `EncryptionKey.createAES256()` creates a key, but the library does NOT persist it. Callers must store the key externally. Losing the key = losing the data.
- **`build/` is shared boilerplate**: FAKE build files are shared across Alma libraries. Do not modify `Targets.fs` or `SafeBuildHelpers.fs` without understanding cross-project impact.
