# R2 Password Hashing Deployment Validation

## Source-controlled schema conclusion

R2 does not require a database schema migration. `User.PasswordHash` remains required,
has no configured maximum length, and is mapped by the current EF Core model snapshot as
non-null `nvarchar(max)`. Marking the property as an optimistic-concurrency token changes
EF update predicates but does not add or alter a database column.

No R2 migration should be generated for this release.

## Manual deployment schema validation

Before releasing to each intended environment, an authorized operator must inspect schema
metadata for `dbo.Users.PasswordHash`. Do not use this procedure to select credential data.
The deployed column must be:

- named `PasswordHash` on the `Users` table;
- non-nullable;
- `nvarchar(max)`, matching the source-controlled model, or an explicitly reviewed
  equivalent large enough for ASP.NET Core `PasswordHasher<User>` output; and
- free of a restrictive maximum length that could truncate adaptive hashes.

Run this read-only SQL Server metadata query manually against the intended environment:

```sql
SELECT
    COLUMN_NAME AS ColumnName,
    DATA_TYPE AS DataType,
    CHARACTER_MAXIMUM_LENGTH AS MaximumLength,
    IS_NULLABLE AS IsNullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME = 'Users'
  AND COLUMN_NAME = 'PasswordHash';
```

For the expected `nvarchar(max)` shape, `MaximumLength` is `-1` and `IsNullable` is `NO`.
This check must be performed manually; the application and release process must not
automatically connect to production for this validation.

## Non-sensitive migration counts

Migration progress must be measured with aggregate counts only. Never select, print,
export, or attach stored password hashes, salts, Base64 segments, or parsed credential
payloads.

The strict legacy format consists of exactly one colon, with one 44-character segment on
each side. Both segments must decode as canonical Base64 representations of 32 bytes for
application-side verification to classify the value as legacy. SQL Server pattern and
length checks cannot reliably prove canonical Base64 or cryptographic validity, so SQL
results are candidate counts rather than authoritative classifications.

The following read-only query returns structural candidate counts without returning hash
values:

```sql
WITH CredentialCandidates AS
(
    SELECT
        CASE
            WHEN LEN(PasswordHash) = 89
             AND CHARINDEX(':', PasswordHash) = 45
             AND CHARINDEX(':', PasswordHash, 46) = 0
                THEN 'StrictShapeLegacyCandidate'
            WHEN CHARINDEX(':', PasswordHash) = 0
                THEN 'ModernOrUnknownCandidate'
            ELSE 'MalformedOrUnknownCandidate'
        END AS CandidateClass
    FROM dbo.Users
)
SELECT CandidateClass, COUNT_BIG(*) AS AccountCount
FROM CredentialCandidates
GROUP BY CandidateClass
ORDER BY CandidateClass;
```

`StrictShapeLegacyCandidate` deliberately checks only length and colon placement. It may
include invalid Base64 and therefore may overcount legacy credentials.
`ModernOrUnknownCandidate` includes expected ASP.NET Core payloads but may also contain
unsupported no-colon values. `MalformedOrUnknownCandidate` identifies obvious structural
mismatches. Authentication through the application's password service remains the
authoritative format verification path; database-side counts must not be treated as proof
that credentials are valid.

Malformed or unknown accounts require a known disposition before legacy verification is
removed. They must not simply be counted as migrated. Investigations should use aggregate
counts, account IDs, or other safe administrative metadata only; credential contents must
not be copied into documentation, tickets, logs, or exports.

## Condition for later legacy-verifier removal

Legacy verification may be removed only when all of the following are true:

1. The strict legacy candidate count is zero in every active environment.
2. Every malformed or unknown account has a known disposition.
3. No legacy authentication activity has been observed for the team-approved observation
   period.
4. Backup and restore procedures cannot reintroduce legacy-only credential data without
   compatible verification code.
5. Removal is performed in a later, dedicated change.

Observation period: **TBD by the team and recorded before legacy removal.**

Until those conditions are met, the temporary legacy verifier remains in place. Active
registration and password-change flows use adaptive hashing, and successful legacy login
persists a modern replacement. Legacy SHA-256 password generation is retired from active
account flows. JWT HMAC-SHA256 signing is a separate mechanism and remains unchanged.
