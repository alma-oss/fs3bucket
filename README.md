F-S3Bucket
==========

Library for accessing a AWS S3 Bucket storage.

## Install

Add following into `paket.dependencies`
```
source https://nuget.pkg.github.com/almacareer/index.json username: "%PRIVATE_FEED_USER%" password: "%PRIVATE_FEED_PASS%"
# LMC Nuget dependencies:
nuget Alma.S3Bucket
```

NOTE: For local development, you have to create ENV variables with your github personal access token.
```sh
export PRIVATE_FEED_USER='{GITHUB USERNANME}'
export PRIVATE_FEED_PASS='{TOKEN}'	# with permissions: read:packages
```

Add following into `paket.references`
```
Alma.AWS.S3Bucket
```

## Use

### Connect to a S3 Bucket
> BucketName is part of the configuration since it should be paired with specific credentials (and its policies)

```fs
open Alma.AWS.S3Bucket

let configuration = {
    Region = None // eu-west-1 is a default
    Credentials = AccessKey {
        Key = "..."
        Secret = "..."
    }
    Bucket = BucketName <| Create.Instance (
        Domain "domain"
        Context "context"
        Purpose "purpose"
        Version "version"
    )
}

asyncResult {
    use! s3client = S3Bucket.connect configuration
}
```

TIP: use `S3Bucket.Configuration.forXXX` function for easier configuration creation

### Put item to S3Bucket
```fs
open Alma.AWS.S3Bucket
open Alma.ErrorHandling

asyncResult {
    do! S3Bucket.put s3client {
        Name = "Movie"
        Content = "Lord of the Rings"
    }
}
```

### Get item from S3Bucket
```fs
open Alma.AWS.S3Bucket

asyncResult {
    let! lotrMovie = "Movie" |> S3Bucket.get s3client

    return lotrMovie
}
```

### Use client-side encryption

```fs
open Alma.ErrorHandling
open Alma.AWS.S3Bucket
open Alma.AWS.S3Bucket.ClientSideEncryption

asyncResult {
    let encryptionKey = EncryptionKey "...Base64 encoded AES256..."

    // put encrypted file
    do! S3Bucket.put s3client encryptionKey {
        Name = "Movie"
        Content = "Lord of the Rings"
    }

    // get encrypted file
    let! lotrMovie = "Movie" |> S3Bucket.get s3client encryptionKey
}
```

NOTE: 
There is a `EncryptionKey.createAES256()` function, which can be used to create a valid `EncryptionKey`, but keep in mind, that you need the key, for getting the item back.
So you should persist the key to your secrets store.

## Release
1. Increment version in `S3Bucket.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)

### Build
```bash
./build.sh build
```

### Tests
```bash
./build.sh -t tests
```
