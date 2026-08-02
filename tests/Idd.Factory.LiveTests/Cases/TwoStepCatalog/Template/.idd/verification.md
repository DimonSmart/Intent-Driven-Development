# Verification

## product-code-contract

dotnet test tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj --no-restore --filter FullyQualifiedName~ProductCodeTests

## catalog-integration

dotnet test tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj --no-restore --filter FullyQualifiedName~CatalogIntegrationTests

## final

dotnet build MiniCatalog.sln --no-restore
dotnet test tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj --no-restore
