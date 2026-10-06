# Examples — Alma.S3Bucket

All example code for this skill lives here. Each example is self-contained and uses neutral
placeholders only. Ordered from basic to full workflow.

## 1. Connect

Open a bucket handle with an explicit access key. The handle is `IDisposable`, so it is bound with
`use!`.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket
open Alma.ServiceIdentification

let bucket =
    BucketName.Instance (
        Create.Instance
            (Domain "domain")
            (Context "context")
            (Purpose "purpose")
            (Version "version")
    )

let configuration =
    Configuration.forAccessKey bucket { Key = "AKIA..."; Secret = "..." }

asyncResult {
    use! client = S3Bucket.connect configuration
    return client
}
```

## 2. Basic put and get

Store a string object and read it back. The object key is `BucketContent.Name`.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket

let storeAndLoad client = asyncResult {
    do! S3Bucket.put client { Name = "demo-key"; Content = "example content" }

    let! loaded = "demo-key" |> S3Bucket.get client
    return loaded
}
```

## 3. Delete

Remove an object by key.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket

let remove client = asyncResult {
    do! "demo-key" |> S3Bucket.delete client
}
```

## 4. Realistic workflow with error handling

Connect once, reuse the handle for several operations, and react to the typed error at the boundary.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket
open Alma.ServiceIdentification

let run () = async {
    let bucket =
        BucketName.Instance (
            Create.Instance
                (Domain "domain") (Context "context")
                (Purpose "purpose") (Version "version")
        )

    let configuration = Configuration.forServiceAccount bucket

    let work = asyncResult {
        use! client = S3Bucket.connect configuration
        do! S3Bucket.put client { Name = "report"; Content = "payload-a" }
        let! reloaded = "report" |> S3Bucket.get client
        do! "report" |> S3Bucket.delete client
        return reloaded
    }

    match! work with
    | Ok content -> return content
    | Error (ConnectionError.RuntimeError ex) -> return failwithf "connect failed: %s" ex.Message
}
```

## 5. S3-compatible endpoint (MinIO)

Override `ServiceUrl` to target a custom endpoint.

```fsharp
open Alma.AWS.S3Bucket

let minioConfiguration bucket accessKey =
    { Configuration.forAccessKey bucket accessKey with
        ServiceUrl = Some "http://localhost:9000" }
```

## 6. Bucket name with a sidecar suffix

Use `InstanceWithSidecar` when one service instance needs more than one bucket.

```fsharp
open Alma.AWS.S3Bucket
open Alma.ServiceIdentification

let instance =
    Create.Instance
        (Domain "domain") (Context "context")
        (Purpose "purpose") (Version "version")

let primary = BucketName.Instance instance
let sidecar = BucketName.InstanceWithSidecar (instance, SidecarSuffix "thumbnails")
```

## 7. Streaming large objects

Upload via multipart from an `AsyncSeq<byte[]>` and download as a chunked stream. `putStream` takes an
`ILogger`; `getStream` takes a chunk size.

```fsharp
open Feather.ErrorHandling
open Microsoft.Extensions.Logging
open FSharp.Control
open Alma.AWS.S3Bucket

let upload (logger: ILogger) client (source: AsyncSeq<byte[]>) = asyncResult {
    do! S3Bucket.putStream logger client { Name = "large-object"; Content = source }
}

let download client = asyncResult {
    let chunkSize = 64 * 1024
    let! chunks = "large-object" |> S3Bucket.getStream chunkSize client
    let! bytes = chunks |> AsyncSeq.concatSeq |> AsyncSeq.toArrayAsync
    return bytes
}
```

## 8. Client-side encryption round-trip

Generate the key once, persist it externally, then use the same key for `put` and `get`. A get with a
different key fails.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket
open Alma.AWS.S3Bucket.ClientSideEncryption

let encryptedRoundTrip client = asyncResult {
    // Generate once, then store in your secrets store and reload for future gets.
    let encryptionKey = EncryptionKey.createAES256 ()

    do! ClientSideEncryption.S3Bucket.put client encryptionKey
            { Name = "secret-key"; Content = "sensitive content" }

    let! decrypted = "secret-key" |> ClientSideEncryption.S3Bucket.get client encryptionKey
    return decrypted
}
```

## 9. Integration test against MinIO

Round-trip against a local S3-compatible server and assert on the `AsyncResult` outcome.

```fsharp
open Feather.ErrorHandling
open Alma.AWS.S3Bucket
open Alma.ServiceIdentification

let putGetRoundTrips () = async {
    let bucket =
        BucketName.Instance (
            Create.Instance
                (Domain "domain") (Context "context")
                (Purpose "purpose") (Version "version")
        )

    let configuration =
        { Configuration.forAccessKey bucket { Key = "minioadmin"; Secret = "minioadmin" } with
            ServiceUrl = Some "http://localhost:9000" }

    let outcome = asyncResult {
        use! client = S3Bucket.connect configuration
        do! S3Bucket.put client { Name = "probe"; Content = "value" }
        return! "probe" |> S3Bucket.get client
    }

    match! outcome with
    | Ok value -> assert (value = "value")
    | Error e -> failwithf "expected Ok, got %A" e
}
```
