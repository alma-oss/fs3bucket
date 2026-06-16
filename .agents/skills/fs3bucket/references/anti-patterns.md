# Anti-Patterns — Alma.S3Bucket

Each entry is **mistake → why → fix**. Code for the correct approach lives in `examples.md`.

## Using the obsolete `BucketName.BucketName` case

- **Why it's wrong:** `BucketName.BucketName` is marked `[<Obsolete>]` and will be removed in the next
  major version.
- **Fix:** Use `BucketName.Instance` (or `BucketName.InstanceWithSidecar` when a service instance needs
  more than one bucket).

## Discarding or regenerating the encryption key

- **Why it's wrong:** Client-side encryption uses a customer-provided key (SSE-C). The library never
  persists it. A new key from `EncryptionKey.createAES256` cannot decrypt previously written objects —
  losing the key means losing the data.
- **Fix:** Generate the key once, store it in your secrets store, and load the same key for every
  subsequent `get`. See `examples.md` → Client-side encryption round-trip.

## Not disposing the connection (`let!` instead of `use!`)

- **Why it's wrong:** The `S3Bucket` handle is `IDisposable`; binding it with `let!` leaks the
  underlying `AmazonS3Client`.
- **Fix:** Bind with `use!` inside the `asyncResult` block so it is disposed at scope end.

## Sending binary data through `put`/`get`

- **Why it's wrong:** `BucketContent.Content` is a `string`; `put`/`get` transfer text only. Binary
  bytes get corrupted by string encoding.
- **Fix:** Use `putStream`/`getStream`, which work with `AsyncSeq<byte[]>`.

## Ignoring the `AsyncResult` error channel

- **Why it's wrong:** Operations return `AsyncResult`; treating the call as if it always succeeds hides
  connection and I/O failures (`ConnectionError`, `BucketPutError`, etc.).
- **Fix:** Bind with `do!`/`let!` inside `asyncResult` (which short-circuits on `Error`) and handle the
  final result, matching the specific error DU case where you need to react.

## Reconnecting for every object

- **Why it's wrong:** The bucket is fixed in `Configuration` and `connect` opens a real client; calling
  it per object is wasteful and creates many spans/handles.
- **Fix:** Connect once per bucket, then reuse the handle for all `put`/`get`/`delete` calls in the same
  `asyncResult` block.

## Hand-building `Configuration` when a helper exists

- **Why it's wrong:** Manually constructing the record is error-prone and easy to leave fields stale.
- **Fix:** Start from `Configuration.forServiceAccount` or `Configuration.forAccessKey`, then override
  `Region`/`ServiceUrl` only if required.

## Expecting the AWS SDK's ambient default region

- **Why it's wrong:** Leaving `Region` as `None` does not fall back to the AWS SDK's ambient default
  (see the library default in `preferred-patterns.md` → Recommended API Usage).
- **Fix:** Set `Configuration.Region` explicitly when you need a specific region.

## Pointing at MinIO without setting `ServiceUrl`

- **Why it's wrong:** Custom S3-compatible endpoints need their URL set explicitly; relying on region
  alone fails to reach the server.
- **Fix:** Set `Configuration.ServiceUrl` (see `preferred-patterns.md` → Integration and
  `examples.md` → S3-compatible endpoint).
