namespace Alma.AWS.S3Bucket

module ClientSideEncryption =
    type EncryptionKey =
        /// Base64 encoded AES256 encryption key
        EncryptionKey of string

    [<RequireQualifiedAccess>]
    module EncryptionKey =
        open System
        open System.Security.Cryptography

        let createAES256 () =
            let aes = Aes.Create()
            aes.KeySize <- 256
            aes.GenerateKey()

            Convert.ToBase64String(aes.Key)
            |> EncryptionKey

    [<RequireQualifiedAccess>]
    module S3Bucket =
        open System
        open System.IO
        open Amazon.S3
        open Amazon.S3.Model
        open Microsoft.Extensions.Logging
        open Feather.ErrorHandling
        open Alma.Tracing
        open Alma.AWS.S3Bucket

        let put { Client = client; Bucket = bucket } (EncryptionKey encryptionKey) (file: BucketContent) = asyncResult {
            use trace = S3Bucket.trace "Put Encrypted Item" bucket
            let traceError = S3Bucket.traceError trace

            let bucket = bucket |> BucketName.value
            let request =
                new PutObjectRequest(
                    BucketName = bucket,
                    Key = file.Name,
                    ContentBody = file.Content,
                    ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
                    ServerSideEncryptionCustomerProvidedKey = encryptionKey
                )

            return!
                request
                |> S3Bucket.putRequest client
                |> AsyncResult.teeError traceError
        }

        let get { Client = client; Bucket = bucket } (EncryptionKey encryptionKey) name = asyncResult {
            use trace =
                S3Bucket.trace "Get Encrypted Item" bucket
                |> Trace.addTags [ "db.statement", sprintf "Key = %s" name ]
            let traceError = S3Bucket.traceError trace

            let bucket = bucket |> BucketName.value
            let request =
                new GetObjectRequest(
                    BucketName = bucket,
                    Key = name,
                    ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
                    ServerSideEncryptionCustomerProvidedKey = encryptionKey
                )

            return!
                request
                |> S3Bucket.getRequest client
                |> AsyncResult.teeError traceError
        }
