# Driver registration upload source

Driver uploads and the database can live on different servers. Sharing the database does not share uploaded files.

For an API that reviews registrations uploaded to another API instance, set:

```text
Storage__RegistrationUploads__SourceApiBaseUrl=https://<upload-api-host>
```

This selects the upload API as the authoritative read source for keys beginning with `uploads/drivers/driver-registration/`. Document downloads and registration validation both read the original content through that API's `/api/v1/uploads/content` endpoint. No file is copied to local storage. A failed source request remains a failure, even if a local file with that key exists.

Leave the setting unset on the upload server itself. Setting it to the same API instance would cause recursive requests. Other storage key families and upload/write behavior remain governed by the existing storage configuration.

Use HTTPS for a remote source; HTTP is supported only for loopback. Configure the setting for the reviewing environment, not as a production-wide default. The endpoint and its access rules are provided by the upload API; this setting does not change those rules.
