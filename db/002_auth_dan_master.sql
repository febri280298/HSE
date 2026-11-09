/* ============================================================================
   HSE System 002 - login, peran (role), hak akses, dan master plant
   Jalankan di database web_hse (pilih web_hse di DBeaver, bukan master),
   sebagai sysadmin / pemilik database.

   HASIL
     auth.Users            pengguna (password di-hash PBKDF2 oleh aplikasi)
     auth.Roles            peran; dibuat bebas oleh admin dari aplikasi
     auth.RolePermissions  hak akses per peran. Kode hak akses didefinisikan
                           aplikasi (backend Auth/Permissions.cs)
     auth.UserRoles        peran tiap pengguna (boleh lebih dari satu)
     auth.UserPlants       plant tiap pengguna
     mst.Plants            KIMU, TEGAL, BTI (Plant 5), BTP (Plant 1)
     Peran "Administrator" (IsSystem = 1): otomatis punya SEMUA hak akses,
     termasuk hak yang ditambahkan kemudian. Tidak bisa diubah/dihapus.

   Pengguna pertama dibuat APLIKASI dari pengaturan Bootstrap:AdminUsername +
   Bootstrap:AdminPassword (lihat README), bukan dari skrip ini.

   CARA MENJALANKAN (DBeaver): Ctrl+A lalu Ctrl+Enter (satu blok).
   Aman dijalankan ulang: yang sudah ada dilewati, data tidak diubah.
   ============================================================================ */

SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Pilih database web_hse dulu (bukan master). Skrip dihentikan, belum ada yang dibuat.', 1;

BEGIN TRANSACTION;

IF SCHEMA_ID(N'auth') IS NULL EXEC (N'CREATE SCHEMA auth AUTHORIZATION dbo;');
IF SCHEMA_ID(N'mst') IS NULL EXEC (N'CREATE SCHEMA mst AUTHORIZATION dbo;');

/* ---------- Master plant ---------- */
IF OBJECT_ID(N'mst.Plants', N'U') IS NULL
BEGIN
    CREATE TABLE mst.Plants (
        PlantId   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Plants PRIMARY KEY,
        Code      varchar(20)        NOT NULL CONSTRAINT UQ_Plants_Code UNIQUE,
        Name      nvarchar(100)      NOT NULL,
        IsActive  bit                NOT NULL CONSTRAINT DF_Plants_IsActive DEFAULT (1),
        SortOrder int                NOT NULL CONSTRAINT DF_Plants_SortOrder DEFAULT (0)
    );
    PRINT 'Tabel mst.Plants dibuat.';
END;

/* ---------- Pengguna ---------- */
IF OBJECT_ID(N'auth.Users', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Users (
        UserId             int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username           nvarchar(50)       NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
        DisplayName        nvarchar(100)      NOT NULL,
        Email              nvarchar(200)      NULL,
        PasswordHash       nvarchar(400)      NOT NULL,
        IsActive           bit                NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        MustChangePassword bit                NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT (1),
        -- Berubah saat password diganti/di-reset atau akun dinonaktifkan: sesi login lama langsung tidak berlaku.
        SecurityStamp      uniqueidentifier   NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT (NEWID()),
        FailedLoginCount   int                NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0),
        LockoutUntil       datetime2(0)       NULL,
        LastLoginAt        datetime2(0)       NULL,
        CreatedAt          datetime2(0)       NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt          datetime2(0)       NOT NULL CONSTRAINT DF_Users_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Tabel auth.Users dibuat.';
END;

/* ---------- Peran & hak akses ---------- */
IF OBJECT_ID(N'auth.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Roles (
        RoleId      int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
        Name        nvarchar(50)       NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE,
        Description nvarchar(200)      NULL,
        IsSystem    bit                NOT NULL CONSTRAINT DF_Roles_IsSystem DEFAULT (0),
        CreatedAt   datetime2(0)       NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt   datetime2(0)       NOT NULL CONSTRAINT DF_Roles_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Tabel auth.Roles dibuat.';
END;

IF OBJECT_ID(N'auth.RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE auth.RolePermissions (
        RoleId     int         NOT NULL CONSTRAINT FK_RolePermissions_Roles REFERENCES auth.Roles (RoleId) ON DELETE CASCADE,
        Permission varchar(64) NOT NULL,
        CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, Permission)
    );
    PRINT 'Tabel auth.RolePermissions dibuat.';
END;

IF OBJECT_ID(N'auth.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserRoles (
        UserId int NOT NULL CONSTRAINT FK_UserRoles_Users REFERENCES auth.Users (UserId) ON DELETE CASCADE,
        -- Tanpa cascade: peran yang masih dipakai tidak boleh terhapus.
        RoleId int NOT NULL CONSTRAINT FK_UserRoles_Roles REFERENCES auth.Roles (RoleId),
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId)
    );
    CREATE INDEX IX_UserRoles_RoleId ON auth.UserRoles (RoleId);
    PRINT 'Tabel auth.UserRoles dibuat.';
END;

IF OBJECT_ID(N'auth.UserPlants', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserPlants (
        UserId  int NOT NULL CONSTRAINT FK_UserPlants_Users REFERENCES auth.Users (UserId) ON DELETE CASCADE,
        PlantId int NOT NULL CONSTRAINT FK_UserPlants_Plants REFERENCES mst.Plants (PlantId),
        CONSTRAINT PK_UserPlants PRIMARY KEY (UserId, PlantId)
    );
    PRINT 'Tabel auth.UserPlants dibuat.';
END;

/* ---------- Data awal ---------- */
INSERT INTO mst.Plants (Code, Name, SortOrder)
SELECT v.Code, v.Name, v.SortOrder
FROM (VALUES
        ('KIMU',  N'KIMU Plant',    1),
        ('TEGAL', N'TEGAL Plant',   2),
        ('BTI',   N'BTI (Plant 5)', 3),
        ('BTP',   N'BTP (Plant 1)', 4)
     ) AS v (Code, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM mst.Plants AS p WHERE p.Code = v.Code);
PRINT CONCAT('Plant baru ditambahkan: ', @@ROWCOUNT);

IF NOT EXISTS (SELECT 1 FROM auth.Roles WHERE IsSystem = 1)
BEGIN
    INSERT INTO auth.Roles (Name, Description, IsSystem)
    VALUES (N'Administrator', N'Semua hak akses, termasuk hak yang ditambahkan kemudian. Tidak bisa diubah atau dihapus.', 1);
    PRINT 'Peran Administrator dibuat.';
END;

COMMIT TRANSACTION;
PRINT 'Selesai: skema auth + mst siap.';
