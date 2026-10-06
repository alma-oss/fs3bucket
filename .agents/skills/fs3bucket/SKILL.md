---
name: fs3bucket
description: >-
  Use whenever generating or reviewing F# code that connects to or reads/writes AWS S3
  (or S3-compatible storage like MinIO) through the Alma.S3Bucket library. Trigger on
  `open Alma.AWS.S3Bucket`, on calls to `S3Bucket.connect`, `S3Bucket.put`, `S3Bucket.get`,
  `S3Bucket.delete`, `S3Bucket.putStream`, `S3Bucket.getStream`, on
  `S3Bucket.Configuration.forServiceAccount` / `forAccessKey`, on `BucketContent` /
  `BucketStreamedContent`, on `BucketName` / `SidecarSuffix`, on client-side encryption via
  `Alma.AWS.S3Bucket.ClientSideEncryption` (`EncryptionKey`, `EncryptionKey.createAES256`),
  and whenever composing these inside an `asyncResult` computation expression or handling
  their `AsyncResult` errors (`ConnectionError`, `BucketPutError`, `BucketGetError`,
  `BucketDeleteError`, `BucketStreamPutError`, `BucketGetStreamError`).
---

# F-S3Bucket

Library: [alma-oss/fs3bucket](https://github.com/alma-oss/fs3bucket)
NuGet: `Alma.S3Bucket`

## Purpose

`Alma.S3Bucket` is an F# library that wraps `AWSSDK.S3` with a typed, traced, railway-oriented
API for AWS S3 and S3-compatible stores (e.g. MinIO). It exposes a small surface for connecting
to a bucket and putting/getting/deleting objects as string content or byte streams, with optional
client-side AES-256 (SSE-C) encryption. Every public operation returns an `AsyncResult` and emits
an `Alma.Tracing` span.

## When to Use

- Code needs to store, fetch, or delete objects in an S3 (or S3-compatible) bucket from F#.
- You want typed errors and tracing instead of calling `AmazonS3Client` directly.
- You need streamed (multipart) upload/download for large objects.
- You need client-side, customer-provided-key encryption (SSE-C).

## When NOT to Use

- You need S3 operations not exposed here (listing keys, presigned URLs, bucket lifecycle, tagging,
  ACLs) — use `AWSSDK.S3` directly for those.
- You need to transfer binary content via the string API — only `putStream`/`getStream` handle bytes;
  `put`/`get` are string-only.
- You are not on the Alma stack (these depend on `Feather.ErrorHandling`, `Alma.ServiceIdentification`,
  `Alma.Tracing`).

## Main Concepts

- `Configuration` — record describing how to connect: `Region`, `ServiceUrl`, `Credentials`, `Bucket`.
- `Credentials` — `ServiceAccount` (resolved via AWS/STS) or `AccessKey of AWSAccessKey`.
- `AWSAccessKey` — `{ Key; Secret }` static key pair.
- `BucketName` — bucket identity from an `Alma.ServiceIdentification` `Instance`; cases `Instance` and `InstanceWithSidecar`.
- `SidecarSuffix` — extra suffix appended to a bucket name for an `InstanceWithSidecar`.
- `S3Bucket` — opened, `IDisposable` connection handle returned by `connect`.
- `BucketContent` — `{ Name; Content }` string payload for `put`/`get`.
- `BucketStreamedContent` — `{ Name; Content: AsyncSeq<byte[]> }` payload for `putStream`.
- `S3Bucket.Configuration` — helper module (`forServiceAccount`, `forAccessKey`) for building `Configuration`.
- `ClientSideEncryption` — sub-namespace with its own `S3Bucket.put`/`get` for SSE-C.
- `EncryptionKey` — base64 AES-256 key wrapper; `EncryptionKey.createAES256` generates one.
- Error DUs — `ConnectionError`, `BucketPutError`, `BucketGetError`, `BucketDeleteError`,
  `BucketStreamPutError`, `BucketGetStreamError`; each wraps the underlying exception.

## Related Libraries

- `Feather.ErrorHandling` — provides the `asyncResult` CE and `AsyncResult` combinators used by every operation.
- `Alma.ServiceIdentification` — provides `Instance`, the source of bucket names.
- `Alma.Tracing` — every operation opens a child span.
- `AWSSDK.S3` — the underlying client this library wraps.
- `Microsoft.Extensions.Logging` — `ILogger` is required by `putStream` for retry warnings.

## Keywords for Search

Alma.S3Bucket, S3Bucket, S3, AWS S3, MinIO, S3-compatible, ServiceUrl, ForcePathStyle, connect,
put, get, delete, putStream, getStream, BucketContent, BucketStreamedContent, BucketName,
SidecarSuffix, Instance, forServiceAccount, forAccessKey, ServiceAccount, AccessKey, AWSAccessKey,
ClientSideEncryption, EncryptionKey, createAES256, SSE-C, AES256, multipart upload, AsyncSeq,
asyncResult, AsyncResult, F# S3.

## Reference Files

- For composition principles, recommended API usage, error handling, integration and testing
  guidance, read `references/preferred-patterns.md`.
- For known pitfalls, incorrect assumptions, obsolete API, and the correct replacements, read
  `references/anti-patterns.md`.
- For worked, copy-ready code examples (connect, put/get, delete, streaming, encryption, tests),
  read `references/examples.md`.
