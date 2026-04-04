namespace Alma.AWS.S3Bucket

// This file itself should be in a separate library, for Alma.AWS, but it is currently not needed anywhere else, so for now, it is just here.

open System
open Feather.ErrorHandling
open Alma.ServiceIdentification
open Alma.Tracing
open FSharp.Control

//
// Errors
//

type ConnectionError =
    | RuntimeError of exn

type BucketPutError =
    | BucketPutExn of exn

type BucketGetError =
    | BucketGetExn of exn

type BucketStreamPutError =
    | BucketStreamPutExn of exn
    | BucketStreamAbortExn of exn

type BucketGetStreamError =
    | BucketGetStreamExn of exn

type BucketDeleteError =
    | BucketDeleteExn of exn

//
// Types
//

type AWSAccessKey = {
    Key: string
    Secret: string
}

type Credentials =
    | ServiceAccount
    | AccessKey of AWSAccessKey

type BucketName = BucketName of Instance

[<RequireQualifiedAccess>]
module BucketName =
    let value (BucketName bucket) = bucket |> Instance.concat "-" |> fun s -> s.ToLowerInvariant()

type Configuration = {
    Region: Amazon.RegionEndpoint option
    Credentials: Credentials
    Bucket: BucketName
}

type AWSClient =
    internal AWSClient of Amazon.S3.AmazonS3Client

    with
        member this.Dispose() =
            let (AWSClient client) = this
            client.Dispose()

        interface IDisposable with
            member this.Dispose() = this.Dispose()

type S3Bucket =
    internal {
        Client: AWSClient
        Bucket: BucketName
    }

    with
        member this.Dispose() =
            this.Client.Dispose()

        interface IDisposable with
            member this.Dispose() = this.Dispose()

type BucketContent = {
    Name: string
    Content: string
}

type BucketStreamedContent = {
    Name: string
    Content: AsyncSeq<byte[]>
}

[<RequireQualifiedAccess>]
module S3Bucket =
    open System
    open System.IO
    open Amazon.S3
    open Amazon.S3.Model
    open Microsoft.Extensions.Logging

    [<RequireQualifiedAccess>]
    module Configuration =
        let forServiceAccount bucket =
            {
                Region = None
                Credentials = ServiceAccount
                Bucket = bucket
            }

        let forAccessKey bucket accessKey =
            {
                Region = None
                Credentials = AccessKey accessKey
                Bucket = bucket
            }

    let internal trace name bucket =
        sprintf "[S3Bucket] %s" name
        |> Trace.ChildOf.continueOrStart Trace.Active.current
        |> Trace.addTags [
            "component", (sprintf "fS3Bucket (%s)" AssemblyVersionInformation.AssemblyVersion)
            "peer.service", "S3Bucket"
            "db.instance", bucket |> BucketName.value
            "db.type", "S3Bucket"
            "span.kind", "client"
        ]

    let internal traceError trace error =
        trace
        |> Trace.addError (TracedError.ofError (sprintf "%A") error)
        |> ignore

    [<RequireQualifiedAccess>]
    module internal Multipart =
        // AWS S3 (and MinIO) require each part except the last to be at least 5 MB.
        // https://docs.aws.amazon.com/AmazonS3/latest/userguide/qfacts.html
        [<Literal>]
        let MinPartSize = 5 * 1024 * 1024

        type ETag = private ETag of string

        type Upload = {
            BucketName: BucketName
            Key: string
            UploadId: string
        }

        let create (AWSClient client) bucket key = asyncResult {
            let bucketName = bucket |> BucketName.value
            let request = new InitiateMultipartUploadRequest(BucketName = bucketName, Key = key)
            let! (response: InitiateMultipartUploadResponse) =
                client.InitiateMultipartUploadAsync(request)
                |> AsyncResult.ofTaskCatch BucketStreamPutExn

            return {
                BucketName = bucket
                Key = key
                UploadId = response.UploadId
            }
        }

        let uploadPart (AWSClient client) (upload: Upload) partNumber (data: byte[]) = asyncResult {
            use stream = new MemoryStream(data)
            let request =
                new UploadPartRequest(
                    BucketName = (upload.BucketName |> BucketName.value),
                    Key = upload.Key,
                    UploadId = upload.UploadId,
                    PartNumber = Nullable(partNumber),
                    InputStream = stream
                )

            let! (response: UploadPartResponse) =
                client.UploadPartAsync(request)
                |> AsyncResult.ofTaskCatch BucketStreamPutExn

            return partNumber, ETag response.ETag
        }

        let complete (AWSClient client) (upload: Upload) (parts: (int * ETag) list) = asyncResult {
            let partETags =
                parts
                |> List.map (fun (pn, ETag etag) ->
                    new PartETag(PartNumber = Nullable(pn), ETag = etag)
                )

            let request =
                new CompleteMultipartUploadRequest(
                    BucketName = (upload.BucketName |> BucketName.value),
                    Key = upload.Key,
                    UploadId = upload.UploadId,
                    PartETags = ResizeArray(partETags)
                )

            let! _response =
                client.CompleteMultipartUploadAsync(request)
                |> AsyncResult.ofTaskCatch BucketStreamPutExn

            return ()
        }

        let abort (AWSClient client) (upload: Upload) = asyncResult {
            let request =
                new AbortMultipartUploadRequest(
                    BucketName = (upload.BucketName |> BucketName.value),
                    Key = upload.Key,
                    UploadId = upload.UploadId
                )

            let! _response =
                client.AbortMultipartUploadAsync(request)
                |> AsyncResult.ofTaskCatch BucketStreamAbortExn

            return ()
        }

        let readInChunks chunkSize (stream: Stream) = asyncSeq {
            let buffer = Array.zeroCreate<byte> chunkSize
            let rec loop () = asyncSeq {
                let! n = stream.ReadAsync(buffer, 0, chunkSize) |> Async.AwaitTask
                if n > 0 then
                    yield Array.sub buffer 0 n
                    yield! loop ()
            }

            yield! loop ()
        }

        // Accumulates small chunks into buffers of at least minSize bytes, yielding
        // each full buffer as a single byte[]. The final buffer may be smaller.
        let bufferToMinSize minSize (source: AsyncSeq<byte[]>) : AsyncSeq<byte[]> = asyncSeq {
            use buffer = new MemoryStream()
            for chunk in source do
                buffer.Write(chunk, 0, chunk.Length)
                if buffer.Length >= int64 minSize then
                    yield buffer.ToArray()
                    buffer.SetLength(0L)
            if buffer.Length > 0L then
                yield buffer.ToArray()
        }

    let connect (configuration: Configuration) = asyncResult {
        use trace = trace "Connect" configuration.Bucket
        let defaultRegion = Amazon.RegionEndpoint.EUWest1
        let region = configuration.Region |> Option.defaultValue defaultRegion

        try
            let client: AmazonS3Client =
                match configuration.Credentials with
                | ServiceAccount -> new AmazonS3Client(region)
                | AccessKey { Key = key; Secret = secret } -> new AmazonS3Client(key, secret, region)

            return {
                Client = AWSClient client
                Bucket = configuration.Bucket
            }
        with e ->
            trace
            |> Trace.addError (TracedError.ofExn e)
            |> ignore

            return! Error (ConnectionError.RuntimeError e)
    }

    let internal putRequest (AWSClient client) request = asyncResult {
        let! _response = client.PutObjectAsync(request) |> AsyncResult.ofTaskCatch BucketPutExn

        return ()
    }

    let put { Client = client; Bucket = bucket } (file: BucketContent) = asyncResult {
        use trace = trace "Put Item" bucket
        let traceError = traceError trace

        let bucket = bucket |> BucketName.value
        let request =
            new PutObjectRequest(
                BucketName = bucket,
                Key = file.Name,
                ContentBody = file.Content
            )

        return!
            request
            |> putRequest client
            |> AsyncResult.teeError traceError
    }

    let putStream (logger: ILogger) { Client = client; Bucket = bucket } (file: BucketStreamedContent) = asyncResult {
        use trace = trace "Put Stream" bucket
        let traceError = traceError trace

        let! upload =
            file.Name
            |> Multipart.create client bucket
            |> AsyncResult.teeError traceError

        let uploadParts (): AsyncResult<_, BucketStreamPutError> =
            file.Content
            |> Multipart.bufferToMinSize Multipart.MinPartSize
            |> AsyncSeq.indexed
            |> AsyncSeq.foldAsync (fun state (i, data) -> async {
                match state with
                | Error _ -> return state
                | Ok parts ->
                    let partNumber = int i + 1
                    let! uploadResult =
                        data
                        |> Multipart.uploadPart client upload partNumber
                        |> AsyncResult.retryWith (logger.LogWarning) 500 3

                    return
                        match uploadResult with
                        | Ok part -> Ok (part :: parts)
                        | Error e -> Error e
            }) (Ok [])

        let! uploadedParts = async {
            match! uploadParts () with
            | Ok parts -> return Ok (parts |> List.rev) // reverse to get correct part order
            | Error uploadErr ->
                match! Multipart.abort client upload with
                | Ok () -> return Error uploadErr
                | Error abortErr -> return Error abortErr
        }

        return!
            uploadedParts
            |> Multipart.complete client upload
            |> AsyncResult.teeError traceError
    }

    let internal getRequest (AWSClient client) request = asyncResult {
        use! response = client.GetObjectAsync(request) |> AsyncResult.ofTaskCatch BucketGetExn

        let readContent (response: GetObjectResponse) =
            use reader = new StreamReader(response.ResponseStream)
            reader.ReadToEnd()

        return readContent response
    }

    let get { Client = client; Bucket = bucket } name = asyncResult {
        use trace =
            trace "Get Item" bucket
            |> Trace.addTags [ "db.statement", sprintf "Key = %s" name ]
        let traceError = traceError trace

        let bucket = bucket |> BucketName.value
        let request = new GetObjectRequest(BucketName = bucket, Key = name)

        return!
            request
            |> getRequest client
            |> AsyncResult.teeError traceError
    }

    let getStream chunkSize { Client = (AWSClient client); Bucket = bucket } name = asyncResult {
        use trace =
            trace "Get Stream" bucket
            |> Trace.addTags [ "db.statement", sprintf "Key = %s" name ]
        let traceError = traceError trace

        let bucketName = bucket |> BucketName.value
        let request = new GetObjectRequest(BucketName = bucketName, Key = name)

        let! (response: GetObjectResponse) =
            client.GetObjectAsync(request)
            |> AsyncResult.ofTaskCatch BucketGetStreamExn
            |> AsyncResult.teeError traceError

        return asyncSeq {
            use r = response
            yield! r.ResponseStream |> Multipart.readInChunks chunkSize
        }
    }

    let delete { Client = AWSClient client; Bucket = bucket } name = asyncResult {
        use trace =
            trace "Delete Item" bucket
            |> Trace.addTags [ "db.statement", sprintf "Key = %s" name ]
        let traceError = traceError trace

        let bucketName = bucket |> BucketName.value
        let request = new DeleteObjectRequest(BucketName = bucketName, Key = name)

        let! _response =
            client.DeleteObjectAsync(request)
            |> AsyncResult.ofTaskCatch BucketDeleteExn
            |> AsyncResult.teeError traceError

        return ()
    }
