BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812125615_AddToolResources'
)
BEGIN
    CREATE TABLE [ToolResources] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] int NOT NULL,
        [VideoUrl] nvarchar(2000) NULL,
        [ImageStoragePath] nvarchar(400) NULL,
        [ImageContentType] nvarchar(100) NULL,
        [ImageFileName] nvarchar(260) NULL,
        [SortOrder] int NOT NULL,
        [IsPublished] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ToolResources] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812125615_AddToolResources'
)
BEGIN
    CREATE TABLE [ToolResourceTranslations] (
        [Id] uniqueidentifier NOT NULL,
        [ToolResourceId] uniqueidentifier NOT NULL,
        [LanguageCode] nvarchar(10) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ToolResourceTranslations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ToolResourceTranslations_ToolResources_ToolResourceId] FOREIGN KEY ([ToolResourceId]) REFERENCES [ToolResources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812125615_AddToolResources'
)
BEGIN
    CREATE INDEX [IX_ToolResources_IsPublished_SortOrder] ON [ToolResources] ([IsPublished], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812125615_AddToolResources'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ToolResourceTranslations_ToolResourceId_LanguageCode] ON [ToolResourceTranslations] ([ToolResourceId], [LanguageCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812125615_AddToolResources'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812125615_AddToolResources', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812164807_AddSubTransactionTypeCountries'
)
BEGIN
    CREATE TABLE [SubTransactionTypeCountries] (
        [Id] uniqueidentifier NOT NULL,
        [SubTransactionTypeId] uniqueidentifier NOT NULL,
        [CountryId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_SubTransactionTypeCountries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubTransactionTypeCountries_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [Countries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SubTransactionTypeCountries_SubTransactionTypes_SubTransactionTypeId] FOREIGN KEY ([SubTransactionTypeId]) REFERENCES [SubTransactionTypes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812164807_AddSubTransactionTypeCountries'
)
BEGIN
    CREATE INDEX [IX_SubTransactionTypeCountries_CountryId_SubTransactionTypeId] ON [SubTransactionTypeCountries] ([CountryId], [SubTransactionTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812164807_AddSubTransactionTypeCountries'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubTransactionTypeCountries_SubTransactionTypeId_CountryId] ON [SubTransactionTypeCountries] ([SubTransactionTypeId], [CountryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812164807_AddSubTransactionTypeCountries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812164807_AddSubTransactionTypeCountries', N'10.0.0');
END;

COMMIT;
GO

