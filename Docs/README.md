# Vocabulary Building Application - Implementation Summary

## Overview
A comprehensive vocabulary building application built with .NET 8.0 Web API backend, featuring user management, word lookup, external dictionary integration, and JWT authentication.

## Architecture
- **Backend**: .NET 8.0 Web API with Entity Framework Core
- **Database**: SQL Server LocalDB (VocabularyAppDb_Dev)
- **Authentication**: JWT Bearer tokens with secure password hashing
- **API Documentation**: Swagger/OpenAPI with JWT support
- **External Integration**: Canonical database lookup with an external dictionary provider boundary.

## Project Structure
```
VocabularyApp/
├── VocabularyApp.Data/              # Data layer
│   ├── Models/                      # Entity Framework models
│   └── ApplicationDbContext.cs      # Database context with configurations
├── VocabularyApp.WebApi/            # API layer
│   ├── Controllers/                 # REST API endpoints
│   ├── Services/                    # Business logic services
│   ├── DTOs/                       # Data transfer objects
│   ├── Helpers/                    # Utility classes
│   └── Program.cs                  # Application configuration
└── test-api.http                   # API testing file
```

## Features Implemented

### 🔐 User Management
- **User Registration**: Secure account creation with ASP.NET Core adaptive password hashing
- **User Authentication**: JWT-based login system with configurable token expiration
- **Profile Access**: Read authenticated profile; no profile-update endpoint exists.
- **Password Security**: Adaptive password hashing for new and changed passwords, with temporary legacy verification and transparent login migration

### 📚 Word Management
- **Word Lookup**: Canonical database-first lookup; vocabulary search is a separate authenticated action.
- **External Dictionary**: Genuine misses are 404; unavailable/untrustworthy upstream data is 503. Provider details are not public contracts.
- **Parts of Speech**: Proper categorization (Noun, Verb, Adjective, etc.)
- **Sample Sentences**: Support for contextual word usage examples

### 🏗️ Database Schema
- **Users**: User accounts with secure authentication
- **Words**: Canonical word storage with metadata
- **WordDefinitions**: Multiple definitions per word with part of speech
- **UserWords**: Personal vocabulary collections
- **SampleSentences**: Contextual usage examples
- **QuizResults**: Learning progress tracking
- **ChatHistory**: Chatbot interaction storage
- **PartsOfSpeech**: Standardized grammatical categories

### 🔧 Technical Features
- **Clean Architecture**: Separation of concerns with interfaces and dependency injection
- **Error Handling**: Comprehensive error responses with proper HTTP status codes
- **API Documentation**: Interactive Swagger UI with JWT authorization support
- **External API Integration**: HttpClient-based dictionary API consumption
- **Database Seeding**: Pre-populated parts of speech data
- **CORS Ready**: Prepared for frontend integration

## API Endpoints

The current 14 actions are documented in the [R7 API contract reference](Updates/R7-api-contract-reference.md), including typed responses, nullable fields, validation media types, error codes and Angular consumers. R7 status: **IMPLEMENTED AND EXECUTION VERIFIED** locally. See [Phase 8 results](Updates/R7-api-contracts-phase-8-verification-results.md) for executed totals, remediation and warnings.

| Method | Route | Authentication |
|---|---|---|
| POST | `/api/users/register` | Anonymous |
| POST | `/api/users/login` | Anonymous |
| GET | `/api/users/profile` | Bearer |
| POST | `/api/users/change-password` | Bearer |
| GET | `/api/users/validate-token` | Bearer |
| GET | `/api/words/lookup/{word}` | Bearer fallback |
| POST | `/api/words/vocabulary/add` | Bearer |
| GET | `/api/words/vocabulary` | Bearer |
| GET | `/api/words/vocabulary/search` | Bearer |
| PUT | `/api/words/vocabulary/{userWordId:int}/favorite` | Bearer |
| PUT | `/api/words/vocabulary/{userWordId:int}/preferred-definition` | Bearer |
| POST | `/api/quiz/start` | Bearer |
| POST | `/api/quiz/submit` | Bearer |
| GET | `/api/quiz/history` | Bearer |

Application errors use six-field JSON; binding/validation uses extended ValidationProblemDetails; framework Bearer challenges may be empty. Vocabulary identity is `(UserId, WordId)`. Duplicate add succeeds. Favorite requires explicit isFavorite (false valid); quiz answers require explicit selectedOptionId (zero valid). Ignored definition/example/pronunciation add fields remain accepted and deprecated; POS remains an active selection fallback.

## Configuration

### Database Connection
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=VocabularyAppDb_Dev;Trusted_Connection=true;MultipleActiveResultSets=true"
  }
}
```

### JWT Configuration

The JWT signing key is required external configuration and must not be stored in
`appsettings.json`, `appsettings.Development.json`, or other source-controlled files.
It must contain at least 32 bytes for HS256.

For local development, store a generated local-only key with ASP.NET Core User Secrets:

```powershell
dotnet user-secrets set "JwtSettings:SecretKey" "<generated-local-secret>" --project .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj
```

Each developer should generate their own secret and must not commit or share it.

For production on SmarterASP.NET, configure the signing key outside the deployed files
as an environment variable named `JwtSettings__SecretKey`. Do not place its value in
`web.config`, a publish profile, deployment scripts, or source control. After changing
the production key, restart the API; tokens signed with the previous key will no longer
be valid and users will need to sign in again.

## Security Features
- **JWT Authentication**: Secure token-based authentication
- **Password Hashing**: Registration and password changes use ASP.NET Core `PasswordHasher<User>`. Existing salted SHA-256 credentials are temporarily accepted through a strict legacy verifier and are upgraded after a successful login; active account flows cannot generate the legacy format.
- **Legacy Retirement**: Legacy verification remains temporary and may be removed only after the operational conditions in [R2 Password Hashing Deployment Validation](Updates/R2-Password-Hashing-Deployment-Validation.md) are satisfied.
- **JWT Signing**: JWT HMAC-SHA256 signing is separate from password storage and is unchanged by the password-hashing migration.
- **Authorization**: Protect sensitive endpoints with JWT requirements
- **HTTPS (PSH-1 Release A)**: Production complete as verified September 18, 2026. The source-controlled root IIS configuration redirects safe HTTP navigation to canonical HTTPS and rejects insecure API/unsafe requests with 403; direct tests verified the recorded path/query and rejection cases, and authenticated HTTPS smoke succeeded. Keep SmarterASP.NET 1-Click Force HTTPS disabled. Release B is production complete as verified September 19, 2026 (approved workflow #60, deployed merge `8cb55ce`): application-managed HSTS on canonical Production HTTPS responses is `max-age=300`, with no `includeSubDomains` or `preload`. The five-minute policy is intentional for the initial rollout; future increases require separate review. Direct HTTPS header verification and authenticated login, lookup, My Vocabulary access and add-to-vocabulary smoke succeeded. See the [Release B production completion evidence](Updates/PSH-1-https-ssl-production-hardening-implementation-plan.md#20-release-b-production-completion). See the [Release A production completion evidence](Updates/PSH-1-https-ssl-production-hardening-implementation-plan.md#18-release-a-production-completion).
- **CORS**: Only Development enables the configured localhost Angular origins. Production Angular and API use same-origin `/api` and receive no cross-origin grant.
- **Input Validation**: Data annotations for request validation

## Database Status
✅ **Successfully Created and Operational**
- Database: `VocabularyAppDb_Dev` 
- Tables: 8 tables with proper relationships
- Seeded Data: 8 parts of speech records
- Verified in SQL Server Management Studio

## Development Status (implementation, not R7 execution evidence)
- ✅ **Database Layer**: Complete with Entity Framework models and context
- ✅ **Business Logic**: WordService and UserService implemented
- ✅ **API Controllers**: Users, Words and Quiz controllers with the 14 actions above
- ✅ **Authentication**: JWT-based security system
- ✅ **External Integration**: Dictionary API service
- ✅ **Documentation**: Swagger UI with JWT support
- ✅ **Testing**: HTTP test file for endpoint validation

## Verification and future work

Vocabulary/quiz services and the Angular UI are implemented. Broader chat, analytics, sharing and offline work remain outside R7. Historical database/security evidence above is not R7 verification evidence.

`test-api.http` and `VocabularyApp.WebApi/VocabularyApp.WebApi.http` contain all 14 current request examples with local placeholders. They are documentation, not instructions to execute during Phase 7. Phase 8 completed local verification: 454 backend and 193 Angular tests passed; Release solution and Angular production builds passed. TestServer/private SQLite and deterministic provider stubs kept verification isolated. Existing npm audit vulnerabilities remain for separate release/security review; no production access, live provider call or deployment occurred.

## Technology Stack
- **.NET 8.0 (LTS)**: Stable framework for hosting compatibility
- **Entity Framework Core 8.0.10**: Object-relational mapping
- **SQL Server LocalDB**: Development database
- **JWT Bearer Authentication**: Microsoft.AspNetCore.Authentication.JwtBearer
- **Swagger/OpenAPI**: API documentation and testing
- **HttpClient**: External API integration

R7 is implemented and locally execution verified. Source review and any commit/release/deployment decision remain separately authorized; see Phase 8 evidence and limitations above.
