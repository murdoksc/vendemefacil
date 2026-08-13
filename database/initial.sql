IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Categories_TenantId_Id] UNIQUE ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [OperationMode] int NOT NULL,
        [CurrencyCode] nvarchar(3) NOT NULL,
        [TimeZoneId] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [DisplayName] nvarchar(max) NOT NULL,
        [Email] nvarchar(450) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Role] int NOT NULL,
        [CanViewCosts] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [CategoryId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Products_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Products_Categories_TenantId_CategoryId] FOREIGN KEY ([TenantId], [CategoryId]) REFERENCES [Categories] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [Branches] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Address] nvarchar(max) NULL,
        [IsMain] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Branches_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Branches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [ProductVariants] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Sku] nvarchar(450) NOT NULL,
        [Barcode] nvarchar(450) NULL,
        [Cost] decimal(18,2) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [MinimumStock] decimal(18,3) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ProductVariants] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ProductVariants_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_ProductVariants_Products_TenantId_ProductId] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [Products] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [InventoryBalances] (
        [Id] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [ProductVariantId] uniqueidentifier NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [AverageCost] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_InventoryBalances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryBalances_Branches_TenantId_BranchId] FOREIGN KEY ([TenantId], [BranchId]) REFERENCES [Branches] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryBalances_ProductVariants_TenantId_ProductVariantId] FOREIGN KEY ([TenantId], [ProductVariantId]) REFERENCES [ProductVariants] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE TABLE [InventoryMovements] (
        [Id] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [ProductVariantId] uniqueidentifier NOT NULL,
        [Type] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [Note] nvarchar(max) NULL,
        [PerformedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_InventoryMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryMovements_Branches_TenantId_BranchId] FOREIGN KEY ([TenantId], [BranchId]) REFERENCES [Branches] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryMovements_ProductVariants_TenantId_ProductVariantId] FOREIGN KEY ([TenantId], [ProductVariantId]) REFERENCES [ProductVariants] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_TenantId_Name] ON [Categories] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryBalances_TenantId_BranchId_ProductVariantId] ON [InventoryBalances] ([TenantId], [BranchId], [ProductVariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE INDEX [IX_InventoryBalances_TenantId_ProductVariantId] ON [InventoryBalances] ([TenantId], [ProductVariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE INDEX [IX_InventoryMovements_TenantId_BranchId] ON [InventoryMovements] ([TenantId], [BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE INDEX [IX_InventoryMovements_TenantId_ProductVariantId] ON [InventoryMovements] ([TenantId], [ProductVariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId_CategoryId] ON [Products] ([TenantId], [CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariants_TenantId_Barcode] ON [ProductVariants] ([TenantId], [Barcode]) WHERE [Barcode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE INDEX [IX_ProductVariants_TenantId_ProductId] ON [ProductVariants] ([TenantId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariants_TenantId_Sku] ON [ProductVariants] ([TenantId], [Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Slug] ON [Tenants] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_TenantId_Email] ON [Users] ([TenantId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202833_InitialMultiTenantCatalog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812202833_InitialMultiTenantCatalog', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    ALTER TABLE [Users] ADD CONSTRAINT [AK_Users_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    CREATE INDEX [IX_InventoryMovements_TenantId_PerformedByUserId] ON [InventoryMovements] ([TenantId], [PerformedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    ALTER TABLE [Categories] ADD CONSTRAINT [FK_Categories_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    ALTER TABLE [InventoryMovements] ADD CONSTRAINT [FK_InventoryMovements_Users_TenantId_PerformedByUserId] FOREIGN KEY ([TenantId], [PerformedByUserId]) REFERENCES [Users] ([TenantId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812202925_StrengthenTenantBoundaries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812202925_StrengthenTenantBoundaries', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812211516_AddBusinessBranding'
)
BEGIN
    ALTER TABLE [Tenants] ADD [AccentColor] nvarchar(7) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812211516_AddBusinessBranding'
)
BEGIN
    ALTER TABLE [Tenants] ADD [LogoUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812211516_AddBusinessBranding'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PrimaryColor] nvarchar(7) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812211516_AddBusinessBranding'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812211516_AddBusinessBranding', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE TABLE [CashSessions] (
        [Id] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [OpenedByUserId] uniqueidentifier NOT NULL,
        [OpenedAtUtc] datetimeoffset NOT NULL,
        [OpeningAmount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [ClosedAtUtc] datetimeoffset NULL,
        [CountedAmount] decimal(18,2) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_CashSessions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE TABLE [Sales] (
        [Id] uniqueidentifier NOT NULL,
        [Folio] nvarchar(450) NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [CashSessionId] uniqueidentifier NOT NULL,
        [SoldByUserId] uniqueidentifier NOT NULL,
        [SoldAtUtc] datetimeoffset NOT NULL,
        [Status] int NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Sales] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE TABLE [SaleItems] (
        [Id] uniqueidentifier NOT NULL,
        [SaleId] uniqueidentifier NOT NULL,
        [ProductVariantId] uniqueidentifier NOT NULL,
        [ProductName] nvarchar(max) NOT NULL,
        [VariantName] nvarchar(max) NOT NULL,
        [Sku] nvarchar(max) NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SaleItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SaleItems_Sales_SaleId] FOREIGN KEY ([SaleId]) REFERENCES [Sales] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE TABLE [SalePayments] (
        [Id] uniqueidentifier NOT NULL,
        [SaleId] uniqueidentifier NOT NULL,
        [Method] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [ReceivedAmount] decimal(18,2) NOT NULL,
        [ChangeAmount] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SalePayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SalePayments_Sales_SaleId] FOREIGN KEY ([SaleId]) REFERENCES [Sales] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE INDEX [IX_CashSessions_TenantId_BranchId_Status] ON [CashSessions] ([TenantId], [BranchId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE INDEX [IX_SaleItems_SaleId] ON [SaleItems] ([SaleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE INDEX [IX_SalePayments_SaleId] ON [SalePayments] ([SaleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Sales_TenantId_Folio] ON [Sales] ([TenantId], [Folio]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812212400_AddPointOfSale'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812212400_AddPointOfSale', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    ALTER TABLE [Sales] ADD [CancellationReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    ALTER TABLE [Sales] ADD [CancelledAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    ALTER TABLE [Sales] ADD [CancelledByUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [DifferenceAmount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [ExpectedAmount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812213319_AddCashCloseAndSaleCancellation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812213319_AddCashCloseAndSaleCancellation', N'10.0.0');
END;

COMMIT;
GO

