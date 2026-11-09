/* ============================================================================
   HSE System 005 - KPI K3 (Summary KPI, Kalender Safety, PPT P2K3L, PICA)
   Jalankan di database web_hse (pilih web_hse di DBeaver, bukan master),
   sebagai sysadmin / pemilik database, SESUDAH skrip 004.

   HASIL
     kpi.PlantDepartments   departemen yang wajib melapor KPI di tiap plant
     kpi.MonthlyInputs      isian bulanan: jumlah accident dan kasus penyakit
     kpi.Documents          file Kalender Safety (PDF) dan PPT P2K3L per bulan
     kpi.Picas              Problem Identification and Corrective Action

   Temuan Penyimpangan 5R & Safety tidak disimpan di sini: dihitung dari
   patrol.Findings, supaya selalu sama dengan laporan safety patrol.

   CARA MENJALANKAN (DBeaver): Ctrl+A lalu Ctrl+Enter (satu blok).
   Aman dijalankan ulang.
   ============================================================================ */

SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Pilih database web_hse dulu (bukan master). Skrip dihentikan, belum ada yang dibuat.', 1;
IF OBJECT_ID(N'patrol.GenbaSchedules', N'U') IS NULL
    THROW 50000, 'Jalankan 004_penugasan_departemen.sql dulu. Skrip dihentikan, belum ada yang dibuat.', 1;

BEGIN TRANSACTION;

IF SCHEMA_ID(N'kpi') IS NULL
    EXEC (N'CREATE SCHEMA kpi AUTHORIZATION dbo;');

IF OBJECT_ID(N'kpi.PlantDepartments', N'U') IS NULL
BEGIN
    CREATE TABLE kpi.PlantDepartments (
        PlantId int NOT NULL CONSTRAINT FK_KpiPlantDepartments_Plants REFERENCES mst.Plants (PlantId),
        DeptId  int NOT NULL CONSTRAINT FK_KpiPlantDepartments_Departments REFERENCES mst.Departments (DeptId),
        CONSTRAINT PK_KpiPlantDepartments PRIMARY KEY (PlantId, DeptId)
    );

    /* Sesuai dashboard spreadsheet: KIMU & TEGAL 4 departemen, BTP 4 departemen, BTI 11 departemen. */
    INSERT INTO kpi.PlantDepartments (PlantId, DeptId)
    SELECT p.PlantId, d.DeptId
    FROM (VALUES
            ('KIMU', 'PRD'), ('KIMU', 'MTC'), ('KIMU', 'QA'), ('KIMU', 'PCD'),
            ('TEGAL', 'PRD'), ('TEGAL', 'MTC'), ('TEGAL', 'QA'), ('TEGAL', 'PCD'),
            ('BTP', 'PRD'), ('BTP', 'PCD'), ('BTP', 'QA'), ('BTP', 'MTC'),
            ('BTI', 'PRD'), ('BTI', 'PUR'), ('BTI', 'PCD'), ('BTI', 'QA'), ('BTI', 'HRGA'), ('BTI', 'MTC'),
            ('BTI', 'ENG'), ('BTI', 'IT'), ('BTI', 'MKT'), ('BTI', 'FAT'), ('BTI', 'RND')
         ) AS v (PlantCode, DeptCode)
    JOIN mst.Plants AS p ON p.Code = v.PlantCode
    JOIN mst.Departments AS d ON d.Code = v.DeptCode;
    PRINT 'Tabel kpi.PlantDepartments dibuat dan diisi.';
END;

IF OBJECT_ID(N'kpi.MonthlyInputs', N'U') IS NULL
BEGIN
    CREATE TABLE kpi.MonthlyInputs (
        PlantId       int           NOT NULL CONSTRAINT FK_KpiInputs_Plants REFERENCES mst.Plants (PlantId),
        DeptId        int           NOT NULL CONSTRAINT FK_KpiInputs_Departments REFERENCES mst.Departments (DeptId),
        PeriodYear    int           NOT NULL,
        PeriodMonth   int           NOT NULL CONSTRAINT CK_KpiInputs_Month CHECK (PeriodMonth BETWEEN 1 AND 12),
        -- NULL = belum diisi; 0 = sudah diisi dan memang nihil.
        AccidentCount int           NULL,
        DiseaseCount  int           NULL,
        DiseaseTypes  nvarchar(200) NULL,
        AccidentNote  nvarchar(200) NULL,
        UpdatedBy     int           NOT NULL CONSTRAINT FK_KpiInputs_Users REFERENCES auth.Users (UserId),
        UpdatedAt     datetime2(0)  NOT NULL CONSTRAINT DF_KpiInputs_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_KpiInputs PRIMARY KEY (PlantId, DeptId, PeriodYear, PeriodMonth)
    );
    PRINT 'Tabel kpi.MonthlyInputs dibuat.';
END;

IF OBJECT_ID(N'kpi.Documents', N'U') IS NULL
BEGIN
    CREATE TABLE kpi.Documents (
        DocumentId  int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_KpiDocuments PRIMARY KEY,
        Kind        varchar(10)    NOT NULL CONSTRAINT CK_KpiDocuments_Kind CHECK (Kind IN ('Kalender', 'PPT')),
        PlantId     int            NOT NULL CONSTRAINT FK_KpiDocuments_Plants REFERENCES mst.Plants (PlantId),
        DeptId      int            NOT NULL CONSTRAINT FK_KpiDocuments_Departments REFERENCES mst.Departments (DeptId),
        PeriodYear  int            NOT NULL,
        PeriodMonth int            NOT NULL CONSTRAINT CK_KpiDocuments_Month CHECK (PeriodMonth BETWEEN 1 AND 12),
        FileName    nvarchar(200)  NOT NULL,
        StoredPath  nvarchar(260)  NOT NULL,
        ContentType varchar(120)   NOT NULL,
        SizeBytes   bigint         NOT NULL,
        UploadedBy  int            NOT NULL CONSTRAINT FK_KpiDocuments_Users REFERENCES auth.Users (UserId),
        UploadedAt  datetime2(0)   NOT NULL CONSTRAINT DF_KpiDocuments_UploadedAt DEFAULT (SYSUTCDATETIME()),
        -- Satu file per departemen per bulan: upload ulang menggantikan yang lama.
        CONSTRAINT UQ_KpiDocuments_Period UNIQUE (Kind, PlantId, DeptId, PeriodYear, PeriodMonth)
    );
    PRINT 'Tabel kpi.Documents dibuat.';
END;

IF OBJECT_ID(N'kpi.Picas', N'U') IS NULL
BEGIN
    CREATE TABLE kpi.Picas (
        PicaId           int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_KpiPicas PRIMARY KEY,
        PlantId          int            NOT NULL CONSTRAINT FK_KpiPicas_Plants REFERENCES mst.Plants (PlantId),
        DeptId           int            NOT NULL CONSTRAINT FK_KpiPicas_Departments REFERENCES mst.Departments (DeptId),
        PeriodYear       int            NOT NULL,
        PeriodMonth      int            NOT NULL CONSTRAINT CK_KpiPicas_Month CHECK (PeriodMonth BETWEEN 1 AND 12),
        KpiType          varchar(10)    NOT NULL CONSTRAINT CK_KpiPicas_Type CHECK (KpiType IN ('Accident', 'Penyakit')),
        ProblemIssue     nvarchar(1000) NOT NULL,
        CorrectiveAction nvarchar(1000) NOT NULL,
        DueDate          date           NOT NULL,
        PicName          nvarchar(100)  NOT NULL,
        Status           varchar(10)    NOT NULL CONSTRAINT CK_KpiPicas_Status CHECK (Status IN ('Open', 'Closed')),
        CreatedBy        int            NOT NULL CONSTRAINT FK_KpiPicas_CreatedBy REFERENCES auth.Users (UserId),
        UpdatedBy        int            NOT NULL CONSTRAINT FK_KpiPicas_UpdatedBy REFERENCES auth.Users (UserId),
        CreatedAt        datetime2(0)   NOT NULL CONSTRAINT DF_KpiPicas_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        datetime2(0)   NOT NULL CONSTRAINT DF_KpiPicas_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_KpiPicas_Period ON kpi.Picas (PlantId, PeriodYear, PeriodMonth);
    PRINT 'Tabel kpi.Picas dibuat.';
END;

COMMIT TRANSACTION;
PRINT 'Selesai: KPI K3 siap.';
