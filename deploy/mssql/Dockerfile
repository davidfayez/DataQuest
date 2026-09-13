# Azure SQL Edge: a lightweight SQL Server engine that runs within Railway's 1 GB
# free-tier limit (full SQL Server 2022 needs 2 GB and crashes here). Same TDS wire
# protocol and T-SQL surface used by the app's EF Core SqlServer provider.
# Runs as root so it can create its /.system directory on Railway's filesystem.
FROM mcr.microsoft.com/azure-sql-edge:latest
USER root
