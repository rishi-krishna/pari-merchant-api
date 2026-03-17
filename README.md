# PaRiMerchant API

ASP.NET Core modular monolith backend for the PaRiMerchant Angular frontend.

## Projects

- `src/PaRiMerchant.Api`: HTTP API, controllers, auth, middleware, Swagger
- `src/PaRiMerchant.Application`: use cases, DTOs, service contracts
- `src/PaRiMerchant.Domain`: entities, enums, business rules
- `src/PaRiMerchant.Infrastructure`: EF Core, MySQL, security, storage, seeding
- `tests/PaRiMerchant.Tests`: unit test scaffolding

## Requirements

- .NET 8 SDK
- MySQL 8+

## Next steps

1. Install the .NET 8 SDK.
2. Restore and build:
   - `dotnet restore src/PaRiMerchant.Api/PaRiMerchant.Api.csproj`
   - `dotnet build src/PaRiMerchant.Api/PaRiMerchant.Api.csproj`
3. Update `appsettings.Development.json` secrets and MySQL connection string.
4. Add an EF migration and database update:
   - `dotnet ef migrations add InitialCreate --project src/PaRiMerchant.Infrastructure --startup-project src/PaRiMerchant.Api`
   - `dotnet ef database update --project src/PaRiMerchant.Infrastructure --startup-project src/PaRiMerchant.Api`

## Dev seed defaults

- Mobile: `9000000000`
- Password: `Demo@1234`
- MPIN: `654321`

These are development-only seed values and must be changed outside development.
No contacts, beneficiaries, or KYC bank details are pre-seeded.
