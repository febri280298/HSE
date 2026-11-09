/* ============================================================================
   HSE System 003 - temuan Safety & 5R Patrol + master area dan departemen
   Jalankan di database web_hse (pilih web_hse di DBeaver, bukan master),
   sebagai sysadmin / pemilik database, SESUDAH skrip 002.

   HASIL
     mst.Departments        departemen (diisi awal; bisa diubah dari aplikasi)
     mst.Areas              area per plant (diisi admin dari aplikasi)
     patrol.Findings        temuan + perbaikan + hasil verifikasi
     patrol.FindingPhotos   foto temuan dan foto sesudah perbaikan
     patrol.FindingEvents   riwayat: siapa melakukan apa, kapan

   STATUS TEMUAN
     Open        baru dicatat
     OnProgress  PIC sedang mengerjakan  (P di Excel lama)
     Verifikasi  PIC selesai, menunggu pemeriksaan
     NotClose    ditolak pemeriksa, kembali ke PIC  (NC di Excel lama)
     Close       disetujui pemeriksa  (C di Excel lama)
   Overdue tidak disimpan: dihitung dari DueDate dan Status.

   CARA MENJALANKAN (DBeaver): Ctrl+A lalu Ctrl+Enter (satu blok).
   Aman dijalankan ulang: yang sudah ada dilewati, data tidak diubah.
   ============================================================================ */

SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Pilih database web_hse dulu (bukan master). Skrip dihentikan, belum ada yang dibuat.', 1;
IF OBJECT_ID(N'auth.Users', N'U') IS NULL OR OBJECT_ID(N'mst.Plants', N'U') IS NULL
    THROW 50000, 'Jalankan 002_auth_dan_master.sql dulu. Skrip dihentikan, belum ada yang dibuat.', 1;

BEGIN TRANSACTION;

IF SCHEMA_ID(N'patrol') IS NULL EXEC (N'CREATE SCHEMA patrol AUTHORIZATION dbo;');

/* ---------- Master ---------- */
IF OBJECT_ID(N'mst.Departments', N'U') IS NULL
BEGIN
    CREATE TABLE mst.Departments (
        DeptId    int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
        Code      varchar(10)        NOT NULL CONSTRAINT UQ_Departments_Code UNIQUE,
        Name      nvarchar(100)      NOT NULL,
        IsActive  bit                NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1)
    );
    PRINT 'Tabel mst.Departments dibuat.';
END;

IF OBJECT_ID(N'mst.Areas', N'U') IS NULL
BEGIN
    CREATE TABLE mst.Areas (
        AreaId    int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Areas PRIMARY KEY,
        PlantId   int                NOT NULL CONSTRAINT FK_Areas_Plants REFERENCES mst.Plants (PlantId),
        Name      nvarchar(100)      NOT NULL,
        IsActive  bit                NOT NULL CONSTRAINT DF_Areas_IsActive DEFAULT (1),
        CONSTRAINT UQ_Areas_Plant_Name UNIQUE (PlantId, Name)
    );
    PRINT 'Tabel mst.Areas dibuat.';
END;

/* ---------- Temuan ---------- */
IF OBJECT_ID(N'patrol.Findings', N'U') IS NULL
BEGIN
    CREATE TABLE patrol.Findings (
        FindingId      int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Findings PRIMARY KEY,
        PlantId        int            NOT NULL CONSTRAINT FK_Findings_Plants REFERENCES mst.Plants (PlantId),
        PeriodYear     int            NOT NULL,
        PeriodMonth    int            NOT NULL,
        -- Nomor urut per plant per bulan, sama seperti kolom No di Excel lama.
        SeqNo          int            NOT NULL,
        PatrolDate     date           NOT NULL,
        Type           varchar(10)    NOT NULL CONSTRAINT CK_Findings_Type CHECK (Type IN ('Safety', '5R')),
        Detail         nvarchar(500)  NOT NULL,
        AreaId         int            NOT NULL CONSTRAINT FK_Findings_Areas REFERENCES mst.Areas (AreaId),
        DeptId         int            NOT NULL CONSTRAINT FK_Findings_Departments REFERENCES mst.Departments (DeptId),
        PicUserId      int            NOT NULL CONSTRAINT FK_Findings_Pic REFERENCES auth.Users (UserId),
        DueDate        date           NOT NULL,
        Status         varchar(12)    NOT NULL CONSTRAINT DF_Findings_Status DEFAULT ('Open')
                                      CONSTRAINT CK_Findings_Status CHECK (Status IN ('Open', 'OnProgress', 'Verifikasi', 'NotClose', 'Close')),
        Countermeasure nvarchar(1000) NULL,
        RepairedBy     int            NULL CONSTRAINT FK_Findings_RepairedBy REFERENCES auth.Users (UserId),
        RepairedAt     datetime2(0)   NULL,
        VerifiedBy     int            NULL CONSTRAINT FK_Findings_VerifiedBy REFERENCES auth.Users (UserId),
        VerifiedAt     datetime2(0)   NULL,
        VerifyNote     nvarchar(500)  NULL,
        CreatedBy      int            NOT NULL CONSTRAINT FK_Findings_CreatedBy REFERENCES auth.Users (UserId),
        CreatedAt      datetime2(0)   NOT NULL CONSTRAINT DF_Findings_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      datetime2(0)   NOT NULL CONSTRAINT DF_Findings_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_Findings_Number UNIQUE (PlantId, PeriodYear, PeriodMonth, SeqNo)
    );
    CREATE INDEX IX_Findings_Period ON patrol.Findings (PeriodYear, PeriodMonth, PlantId) INCLUDE (Status, Type, DueDate);
    CREATE INDEX IX_Findings_Pic ON patrol.Findings (PicUserId, Status);
    PRINT 'Tabel patrol.Findings dibuat.';
END;

IF OBJECT_ID(N'patrol.FindingPhotos', N'U') IS NULL
BEGIN
    CREATE TABLE patrol.FindingPhotos (
        PhotoId   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_FindingPhotos PRIMARY KEY,
        FindingId int            NOT NULL CONSTRAINT FK_FindingPhotos_Findings REFERENCES patrol.Findings (FindingId) ON DELETE CASCADE,
        Kind      varchar(10)    NOT NULL CONSTRAINT CK_FindingPhotos_Kind CHECK (Kind IN ('Temuan', 'Perbaikan')),
        -- Path relatif di penyimpanan foto; satu file hanya boleh dipakai satu temuan.
        Path      nvarchar(260)  NOT NULL CONSTRAINT UQ_FindingPhotos_Path UNIQUE,
        FileName  nvarchar(120)  NOT NULL
    );
    CREATE INDEX IX_FindingPhotos_Finding ON patrol.FindingPhotos (FindingId, Kind);
    PRINT 'Tabel patrol.FindingPhotos dibuat.';
END;

IF OBJECT_ID(N'patrol.FindingEvents', N'U') IS NULL
BEGIN
    CREATE TABLE patrol.FindingEvents (
        EventId   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_FindingEvents PRIMARY KEY,
        FindingId int            NOT NULL CONSTRAINT FK_FindingEvents_Findings REFERENCES patrol.Findings (FindingId) ON DELETE CASCADE,
        Action    varchar(12)    NOT NULL,
        Note      nvarchar(1000) NULL,
        UserId    int            NOT NULL CONSTRAINT FK_FindingEvents_Users REFERENCES auth.Users (UserId),
        CreatedAt datetime2(0)   NOT NULL CONSTRAINT DF_FindingEvents_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_FindingEvents_Finding ON patrol.FindingEvents (FindingId);
    PRINT 'Tabel patrol.FindingEvents dibuat.';
END;

/* ---------- Data awal: departemen dari jadwal presentasi KPI dan laporan patrol ---------- */
INSERT INTO mst.Departments (Code, Name)
SELECT v.Code, v.Name
FROM (VALUES
        ('PRD',  N'Produksi'),
        ('MTC',  N'Maintenance'),
        ('QA',   N'Quality'),
        ('PCD',  N'PCD'),
        ('ENG',  N'Engineering'),
        ('PUR',  N'Purchasing'),
        ('HRGA', N'HR-GA'),
        ('IT',   N'IT'),
        ('MKT',  N'Marketing'),
        ('FAT',  N'FAT'),
        ('RND',  N'RND'),
        ('HSE',  N'HSE')
     ) AS v (Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM mst.Departments AS d WHERE d.Code = v.Code);
PRINT CONCAT('Departemen baru ditambahkan: ', @@ROWCOUNT);

COMMIT TRANSACTION;
PRINT 'Selesai: skema patrol siap.';
