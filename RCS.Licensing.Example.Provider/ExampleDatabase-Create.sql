--DROP TABLE [RealmCustomer];
--DROP TABLE [RealmUser];
--DROP TABLE [Realm];
--DROP TABLE [UserJob];
--DROP TABLE [UserCustomer];
--DROP TABLE [Job];
--DROP TABLE [Customer];
--DROP TABLE [User];
------------------------------------------------------------------------------------------------
CREATE TABLE [Customer]
(
	[Id] INT NOT NULL,
	[Name] NVARCHAR(32) NOT NULL,
	[DisplayName] NVARCHAR(128) NULL,
	[Psw] NVARCHAR(32) NULL,
	[StorageKey] NVARCHAR(1024) NOT NULL,
	[CloudCustomerNames] NVARCHAR(256) NULL,
	[DataLocation] INT NULL,
	[Sequence] INT NULL,
	[Corporation] NVARCHAR(64) NULL,
	[Comment] NVARCHAR(2000) NULL,
	[Info] NVARCHAR(1024) NULL,
	[Logo] NVARCHAR(256) NULL,
	[SignInLogo] NVARCHAR(256) NULL,
	[SignInNote] NVARCHAR(1024) NULL,
	[Credits] INT NULL,
	[Spent] INT NULL,
	[Sunset] DATETIME NULL,
	[MaxJobs] INT NULL,
	[Inactive] BIT NOT NULL CONSTRAINT DF_Customer_Inactive DEFAULT 0,
	[Created] DATETIME NOT NULL CONSTRAINT DF_Customer_Created DEFAULT (GETUTCDATE()),
	CONSTRAINT [PK_Customer_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO
CREATE UNIQUE INDEX [IX_Customer_Name] ON [Customer] ([Name]);
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [Job]
(
	[Id] INT NOT NULL,
	[Name] NVARCHAR(32) NOT NULL,
	[DisplayName] NVARCHAR(128) NULL,
	[CustomerId] INT NULL,
	[VartreeNames] NVARCHAR(128) NULL,
	[DataLocation] INT NULL,
	[Sequence] INT NULL,
	[Cases] INT NULL,
	[LastUpdate] DATETIME NULL,
	[Description] NVARCHAR(2000) NULL,
	[Info] NVARCHAR(1024) NULL,
	[Logo] NVARCHAR(256) NULL,
	[Url] NVARCHAR(256) NULL,
	[IsMobile] BIT NOT NULL CONSTRAINT DF_Job_IsMobile DEFAULT 0,
	[DashboardsFirst] BIT NOT NULL CONSTRAINT DF_Job_DashboardsFirst DEFAULT 0,
	[Inactive] BIT NOT NULL CONSTRAINT DF_Job_Inactive DEFAULT 0,
	[Created] DATETIME NOT NULL CONSTRAINT DF_Job_Created DEFAULT (GETUTCDATE()),
	CONSTRAINT [PK_Job_Id] PRIMARY KEY CLUSTERED ([Id] ASC),
	CONSTRAINT [FK_Job_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customer] ([Id])
);
GO
CREATE INDEX [IX_Job_Name] ON [Job] ([Name]);
GO
--EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Comma joined list of plain variable tree blob names' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'Job', @level2type=N'COLUMN',@level2name=N'VartreeNames';
--GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[User]
(
	[Id] INT NOT NULL,
	[Name] NVARCHAR(128) NOT NULL,
	[ProviderId] NVARCHAR(128) NULL,
	[Psw] NVARCHAR(64) NULL,
	[PassHash] VARBINARY(512) NULL,
	[Email] NVARCHAR(128) NULL,
	[EntityId] NVARCHAR(16) NULL,
	[CloudCustomerNames] NVARCHAR(256) NULL,
	[JobNames] NVARCHAR(256) NULL,
	[VartreeNames] NVARCHAR(256) NULL,
	[DashboardNames] NVARCHAR(256) NULL,
	[DataLocation] INT NULL,
	[Sequence] INT NULL,
	[Uid] UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_User_Uid DEFAULT NEWID(),
	[Comment] NVARCHAR(2000) NULL,
	[Roles] NVARCHAR(128) NULL,
	[Filter] NVARCHAR(128) NULL,
	[LoginMacs] NVARCHAR(256) NULL,
	[LoginCount] INT NULL,
	[LoginMax] INT NULL,
	[LastLogin] DATETIME NULL,
	[Sunset] DATETIME NULL,
	[Version] NVARCHAR(32) NULL,
	[MinVersion] NVARCHAR(32) NULL,
	[IsDisabled] BIT NOT NULL CONSTRAINT DF_User_IsDisabled DEFAULT 0,
	[Created] DATETIME NOT NULL CONSTRAINT DF_User_Created DEFAULT (GETUTCDATE()),
	CONSTRAINT [PK_User_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO
CREATE UNIQUE INDEX [IX_User_Name] ON [User] ([Name]);
GO
------------------------------------------------------------------------------------------------
-- The following column was appended to support the DNA project with global RCS licensing
-- control over how many jobs a DNA login user can create.
ALTER TABLE [User] ADD [MaxJobs] INT NULL;
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[UserCustomer]
(
	[UserId] INT NOT NULL,
	[CustomerId] INT NOT NULL,
	CONSTRAINT [PK_UserCustomer] PRIMARY KEY CLUSTERED ([UserId] ASC, [CustomerId] ASC),
	CONSTRAINT [FK_UserCustomer_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]),
	CONSTRAINT [FK_UserCustomer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customer] ([Id])
)
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[UserJob]
(
	[UserId] INT NOT NULL,
	[JobId] INT NOT NULL,
	CONSTRAINT [PK_UserJob] PRIMARY KEY CLUSTERED ([UserId] ASC, [JobId] ASC),
	CONSTRAINT [FK_UserJob_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]),
	CONSTRAINT [FK_UserJob_JobId] FOREIGN KEY ([JobId]) REFERENCES [Job] ([Id])
)
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[Realm]
(
	[Id] INT NOT NULL,
	[Name] NVARCHAR(16) NOT NULL,
	[Inactive] BIT NOT NULL CONSTRAINT DF_Realm_Inactive DEFAULT 0,
	[Created] DATETIME NOT NULL CONSTRAINT DF_Realm_Created DEFAULT (GETUTCDATE()),
	[Policy] XML NULL,
	CONSTRAINT [PK_Realm_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO
CREATE UNIQUE INDEX [IX_Realm_Name] ON [Realm] ([Name]);
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[RealmUser]
(
	[RealmId] INT NOT NULL,
	[UserId] INT NOT NULL,
	CONSTRAINT [PK_RealmUser] PRIMARY KEY CLUSTERED ([RealmId] ASC, [UserId] ASC),
	CONSTRAINT [FK_RealmUser_RealmId] FOREIGN KEY ([RealmId]) REFERENCES [Realm] ([Id]),
	CONSTRAINT [FK_RealmUser_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id])
)
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[RealmCustomer]
(
	[RealmId] INT NOT NULL,
	[CustomerId] INT NOT NULL,
	CONSTRAINT [PK_RealmCustomer] PRIMARY KEY CLUSTERED ([RealmId] ASC, [CustomerId] ASC),
	CONSTRAINT [FK_RealmCustomer_RealmId] FOREIGN KEY ([RealmId]) REFERENCES [Realm] ([Id]),
	CONSTRAINT [FK_RealmCustomer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customer] ([Id])
)
GO
------------------------------------------------------------------------------------------------
-- You will need to update the customers will valid Azure storage
-- connection strings for your testing and runtime environments.
INSERT INTO [Customer] (Id,[Name],DisplayName,StorageKey) VALUES(30000008,'client1rcs','Client-1 RCS', NULL);
INSERT INTO [Customer] (Id,[Name],DisplayName,StorageKey) VALUES(30000011,'rcspublic','RCS Public', NULL);
INSERT INTO [Customer] (Id,[Name],DisplayName,StorageKey) VALUES(30000022,'rcsruby','Ruby Samples', NULL);
GO
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000001, 30000008, '29997-google-test-carbon-project', 'Metrics Testing', 'VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000002, 30000008, 'demo', 'Demo Testing', 'RubyLib,Test,TsapiTree,VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000003, 30000011, 'aemo', 'Energy', 'VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000004, 30000011, 'cdc-covid', 'CDC Covid', 'VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000005, 30000011, 'firstfleet', 'First Fleet', 'VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000006, 30000011, 'romeo-juliet', 'Romeo and Juliet', 'vartee');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000007, 30000022, 'demo', 'Demo Ruby', 'TsapiTree,VarTree');
INSERT INTO [Job] (Id,CustomerId,[Name],Displayname,VartreeNames) VALUES(20000008, NULL, 'orphan', 'Orphan Job', NULL);
GO
INSERT INTO [User] (Id,[Name],[Email],Psw,Comment) VALUES(10000013, 'john', 'john@mail.com', 'J0hn123', 'John is a normal user.');
INSERT INTO [User] (Id,[Name],[Email],Psw,Comment) VALUES(10000022, 'max', 'max@powerhouse.com', 'Max1mum', 'Max can do anything.');
INSERT INTO [User] (Id,[Name],[Email],Psw,Comment) VALUES(10000335, 'guest', NULL, 'guest', 'Guest user for evaluation and demos.');
GO
INSERT INTO [UserCustomer] VALUES(10000013,30000011);
INSERT INTO [UserCustomer] VALUES(10000013,30000022);
INSERT INTO [UserCustomer] VALUES(10000022,30000008);
INSERT INTO [UserCustomer] VALUES(10000022,30000011);
INSERT INTO [UserCustomer] VALUES(10000022,30000022);
INSERT INTO [UserCustomer] VALUES(10000335,30000011);
INSERT INTO [UserCustomer] VALUES(10000335,30000022);
GO
INSERT INTO [UserJob] VALUES(10000335,20000002);
INSERT INTO [UserJob] VALUES(10000335,20000005);	-- This is a duplicate
GO
------------------------------------------------------------------------------------------------
--SELECT * FROM [Realm];
--SELECT * FROM [RealmUser];
--SELECT * FROM [RealmCustomer];
--SELECT * FROM [Customer];
--SELECT * FROM [Job];
--SELECT * FROM [User];
--SELECT * FROM [UserCustomer];
--SELECT * FROM [UserJob];
------------------------------------------------------------------------------------------------
