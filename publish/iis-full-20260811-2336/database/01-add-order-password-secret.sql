BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260808152157_AddOrderPasswordSecret'
)
BEGIN
    ALTER TABLE [Orders] ADD [PasswordSecret] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260808152157_AddOrderPasswordSecret'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260808152157_AddOrderPasswordSecret', N'10.0.0');
END;

COMMIT;
GO

