# Preferred Patterns — Alma.S3Bucket

Worked code for every pattern below lives in `examples.md`; this file describes the principles and
points to the relevant example by name.

## Core Principles

- Every public operation (`connect`, `put`, `get`, `delete`, `putStream`, `getStream`, and the
  `ClientSideEncryption` variants) returns an `AsyncResult<_, _>`. Treat the `Error` case explicitly.
- The opened `S3Bucket` handle is `IDisposable`. Bind it with `use!` so it is disposed when the
  computation ends.
- The bucket is fixed at connection time — it is part of `Configuration`, not a per-call argument.
  Open one handle per bucket and reuse it for many operations.
- Operations are traced automatically; you do not start or tag spans yourself.

## Recommended API Usage

- Build `Configuration` with the helper module rather than the raw record: `Configuration.forServiceAccount`
  for ambient/STS credentials, `Configuration.forAccessKey` for an explicit `AWSAccessKey`. Override
  `Region` or `ServiceUrl` afterwards only when needed. See `examples.md` → Basic put/get and
  → S3-compatible endpoint (MinIO).
- `Region` defaults to EU-West-1 when left as `None`.
- Use `put`/`get` for string payloads (`BucketContent`); the object key is `BucketContent.Name`.
- Use `delete` to remove an object by key.
- Use `putStream`/`getStream` for byte streams; these chunk through multipart upload / streamed
  download. `putStream` requires an `ILogger` (used for retry warnings) and `getStream` takes a
  `chunkSize`. See `examples.md` → Streaming large objects.

## Error Handling

- Each operation has its own error DU wrapping the underlying exception: `ConnectionError`,
  `BucketPutError`, `BucketGetError`, `BucketDeleteError`, `BucketStreamPutError`,
  `BucketGetStreamError`. Match on these at the boundary where you need to react to failure.
- Inside an `asyncResult` block, `let!` / `do!` short-circuit on the first `Error`, so you rarely
  match mid-pipeline — handle the combined result once at the end. See `examples.md` → Realistic
  workflow with error handling.
- `connect` failures surface as `ConnectionError.RuntimeError`.

## Composition

- Compose multiple operations in a single `asyncResult { }` block; bind the handle once with `use!`
  and chain `do!`/`let!` for the operations. This keeps a single span scope and one error channel.
- Because the handle is reused, do not reconnect per object.

## Integration with Other Libraries

- Bucket names come from `Alma.ServiceIdentification` `Instance` values; wrap them with the
  `BucketName.Instance` case (or `InstanceWithSidecar` plus a `SidecarSuffix` when a single service
  instance needs more than one bucket).
- For S3-compatible servers (e.g. MinIO), set `Configuration.ServiceUrl`; the library enables
  path-style addressing automatically for custom endpoints.
- `putStream` integrates with `Microsoft.Extensions.Logging`; pass the same `ILogger` your host uses.
- `getStream` yields an `FSharp.Control` `AsyncSeq<byte[]>`; consume it with `AsyncSeq` combinators.

## Naming Conventions

- Prefer the `BucketName.Instance` case. The legacy `BucketName.BucketName` case is obsolete (see
  `anti-patterns.md`).
- Object keys are plain strings supplied via `BucketContent.Name` (or the `name` argument of
  `get`/`delete`/`getStream`).

## Testing Recommendations

- Point `Configuration.ServiceUrl` at a local S3-compatible server (e.g. MinIO in a container) for
  integration tests instead of mocking the AWS SDK. See `examples.md` → Integration test against MinIO.
- Assert on the `AsyncResult` outcome (`Ok`/`Error` and the specific error DU case), not on exceptions.
- For client-side encryption, round-trip with the same `EncryptionKey` and assert the decrypted
  content matches; a get with the wrong key must fail.
