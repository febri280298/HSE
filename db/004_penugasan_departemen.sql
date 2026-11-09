/* ============================================================================
   HSE System 004 - temuan ditugaskan ke departemen oleh tim HSE + jadwal genba
   Jalankan di database web_hse (pilih web_hse di DBeaver, bukan master),
   sebagai sysadmin / pemilik database, SESUDAH skrip 003.

   PERUBAHAN ALUR
     Patroller mencatat temuan (status Open, departemen dan due date masih kosong)
     Tim HSE menentukan departemen + PIC + due date (status Ditugaskan)
     User departemen itu mengerjakan perbaikan, lalu diperiksa seperti sebelumnya

   HASIL
     auth.Users.DeptId          departemen tiap pengguna
     patrol.Findings            DeptId, PicUserId, dan DueDate boleh kosong selama
                                Open, status baru Ditugaskan
     patrol.GenbaSchedules      jadwal genba: satu tanggal per plant per bulan

   Temuan Open yang sudah punya departemen menjadi Ditugaskan.

   CARA MENJALANKAN (DBeaver): Ctrl+A lalu Ctrl+Enter (satu blok).
   Aman dijalankan ulang.
   ============================================================================ */

SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Pilih database web_hse dulu (bukan master). Skrip dihentikan, belum ada yang dibuat.', 1;
IF OBJECT_ID(N'patrol.Findings', N'U') IS NULL
    THROW 50000, 'Jalankan 003_temuan_patrol.sql dulu. Skrip dihentikan, belum ada yang dibuat.', 1;

BEGIN TRANSACTION;

/* ---------- Departemen pengguna ---------- */
IF COL_LENGTH(N'auth.Users', N'DeptId') IS NULL
BEGIN
    EXEC (N'ALTER TABLE auth.Users ADD DeptId int NULL CONSTRAINT FK_Users_Departments REFERENCES mst.Departments (DeptId);');
    PRINT 'Kolom auth.Users.DeptId dibuat.';
END;

/* ---------- Status baru ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Findings_Status' AND definition LIKE N'%Ditugaskan%')
BEGIN
    ALTER TABLE patrol.Findings DROP CONSTRAINT CK_Findings_Status;
    ALTER TABLE patrol.Findings ADD CONSTRAINT CK_Findings_Status
        CHECK (Status IN ('Open', 'Ditugaskan', 'OnProgress', 'Verifikasi', 'NotClose', 'Close'));
    PRINT 'Status Ditugaskan ditambahkan.';
END;

/* ---------- Due date boleh kosong selama Open ---------- */
IF COLUMNPROPERTY(OBJECT_ID(N'patrol.Findings'), N'DueDate', 'AllowsNull') = 0
BEGIN
    -- Kolom yang dipakai indeks tidak bisa diubah; indeksnya dibuat ulang.
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Findings_Period' AND object_id = OBJECT_ID(N'patrol.Findings'))
        DROP INDEX IX_Findings_Period ON patrol.Findings;
    ALTER TABLE patrol.Findings ALTER COLUMN DueDate date NULL;
    CREATE INDEX IX_Findings_Period ON patrol.Findings (PeriodYear, PeriodMonth, PlantId) INCLUDE (Status, Type, DueDate);
    PRINT 'DueDate boleh kosong.';
END;

/* ---------- Departemen temuan boleh kosong selama Open ---------- */
IF COLUMNPROPERTY(OBJECT_ID(N'patrol.Findings'), N'DeptId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE patrol.Findings DROP CONSTRAINT FK_Findings_Departments;
    ALTER TABLE patrol.Findings ALTER COLUMN DeptId int NULL;
    ALTER TABLE patrol.Findings ADD CONSTRAINT FK_Findings_Departments FOREIGN KEY (DeptId) REFERENCES mst.Departments (DeptId);
    PRINT 'DeptId temuan boleh kosong.';
END;

/* ---------- PIC ditunjuk tim HSE, jadi boleh kosong selama Open ----------
   Database yang sempat memakai versi lama skrip ini sudah kehilangan kolomnya: ditambahkan kembali. */
IF COL_LENGTH(N'patrol.Findings', N'PicUserId') IS NULL
BEGIN
    EXEC (N'ALTER TABLE patrol.Findings ADD PicUserId int NULL CONSTRAINT FK_Findings_Pic REFERENCES auth.Users (UserId);');
    EXEC (N'CREATE INDEX IX_Findings_Pic ON patrol.Findings (PicUserId, Status);');
    PRINT 'Kolom PicUserId ditambahkan.';
END
ELSE IF COLUMNPROPERTY(OBJECT_ID(N'patrol.Findings'), N'PicUserId', 'AllowsNull') = 0
BEGIN
    DROP INDEX IX_Findings_Pic ON patrol.Findings;
    ALTER TABLE patrol.Findings DROP CONSTRAINT FK_Findings_Pic;
    ALTER TABLE patrol.Findings ALTER COLUMN PicUserId int NULL;
    ALTER TABLE patrol.Findings ADD CONSTRAINT FK_Findings_Pic FOREIGN KEY (PicUserId) REFERENCES auth.Users (UserId);
    CREATE INDEX IX_Findings_Pic ON patrol.Findings (PicUserId, Status);
    PRINT 'PicUserId boleh kosong.';
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Findings_Dept' AND object_id = OBJECT_ID(N'patrol.Findings'))
    CREATE INDEX IX_Findings_Dept ON patrol.Findings (DeptId, Status);

/* ---------- Temuan lama yang sudah punya departemen ---------- */
UPDATE patrol.Findings SET Status = 'Ditugaskan' WHERE Status = 'Open' AND DeptId IS NOT NULL;
PRINT CONCAT('Temuan Open yang sudah punya departemen, menjadi Ditugaskan: ', @@ROWCOUNT);

/* ---------- Jadwal genba: sebulan sekali per plant ---------- */
IF OBJECT_ID(N'patrol.GenbaSchedules', N'U') IS NULL
BEGIN
    CREATE TABLE patrol.GenbaSchedules (
        ScheduleId  int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_GenbaSchedules PRIMARY KEY,
        PlantId     int          NOT NULL CONSTRAINT FK_GenbaSchedules_Plants REFERENCES mst.Plants (PlantId),
        PeriodYear  int          NOT NULL,
        PeriodMonth int          NOT NULL,
        PlannedDate date         NOT NULL,
        UpdatedBy   int          NOT NULL CONSTRAINT FK_GenbaSchedules_Users REFERENCES auth.Users (UserId),
        UpdatedAt   datetime2(0) NOT NULL CONSTRAINT DF_GenbaSchedules_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_GenbaSchedules_Period UNIQUE (PlantId, PeriodYear, PeriodMonth)
    );
    PRINT 'Tabel patrol.GenbaSchedules dibuat.';
END;

COMMIT TRANSACTION;
PRINT 'Selesai: penugasan departemen dan jadwal genba siap.';
