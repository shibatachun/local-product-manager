# Deprecated SQLite support

The application runtime is PostgreSQL-only. Files in this directory are retained solely as
historical migration/reference material and are not compiled into or invoked by the API.

`DatabaseSchemaService.cs` is the former SQLite startup upgrader. The separate
`LocalProductManager.DatabaseMigration` utility remains a one-way archival data migration tool;
it is not referenced by the API or AppHost runtime.
