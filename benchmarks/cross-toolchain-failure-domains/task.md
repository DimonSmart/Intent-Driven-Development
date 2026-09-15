# Greenfield TaskBoard application

Create a greenfield TaskBoard application with all of the following capabilities and supporting project infrastructure.

- Build a .NET 10 ASP.NET Core API with a stable REST contract for listing, creating, updating, and completing tasks. Include automated backend tests for the API behavior.
- Persist task data in SQL Server using EF Core migrations. Provide local SQL Server provisioning suitable for developer machines, including initialization/migration support.
- Build an Angular frontend that consumes the REST API and supports viewing, creating, editing, and completing tasks. Include the frontend build/test setup needed to verify it independently.
- Add .NET Aspire orchestration so the already-created API, frontend, and SQL Server can be started together for local development with one documented developer command.
- Add production publishing configuration for the API and frontend. Keep it provider-neutral: prepare deterministic build/publish artifacts and configuration without requiring access to a real cloud account.
- Add concise developer documentation describing local startup and production publishing commands.

All of these requirements are known now. Do not omit a requirement merely because another part will be implemented first. The resulting repository should remain in useful, coherent states as the work progresses.
