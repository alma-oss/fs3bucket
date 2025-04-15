namespace Alma.AWS.S3Bucket

// This file itself should be in a separate library, for Alma.AWS, but it is currently not needed anywhere else, so for now, it is just here.

open System
open Alma.ErrorHandling
open Alma.ServiceIdentification
open Alma.Tracing

//
// Errors
//

type ConnectionError =
    | RuntimeError of exn

type BucketPutError =
    | BucketPutExn of exn

type BucketGetError =
    | BucketGetExn of exn

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

    let put { Client = client; Bucket = bucket } file = asyncResult {
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
