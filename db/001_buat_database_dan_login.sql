/* ============================================================================
   HSE System 001 - database web_hse + login hse_app
   Server : server SQL perusahaan (mis. 192.168.3.250)
   Jalankan sebagai: sysadmin

   HASIL
     - database web_hse : semua data HSE System.
     - login hse_app    : dipakai aplikasi. Hanya BACA + TULIS data
                          (db_datareader + db_datawriter); tidak bisa mengubah
                          struktur tabel. Struktur hanya diubah lewat skrip db/.

   CARA MENJALANKAN (DBeaver)
     1. Hanya kalau login hse_app BELUM ada: ganti <ISI_PASSWORD_HSE_APP>
        di bawah dengan password kuat. Kalau login sudah ada, biarkan saja.
     2. Ctrl+A lalu Ctrl+Enter (seluruh skrip dijalankan sebagai SATU blok).
     3. Kalau password sempat diisi: tutup file TANPA menyimpan.
   Aman dijalankan ulang.
   ============================================================================ */

USE master;

DECLARE @password nvarchar(128) = N'<ISI_PASSWORD_HSE_APP>';
DECLARE @sql nvarchar(max);

-- Password hanya dibutuhkan untuk membuat login baru.
IF SUSER_ID(N'hse_app') IS NULL AND @password LIKE N'<%'
    THROW 50000, 'Login hse_app belum ada dan password belum diisi. Skrip dihentikan, belum ada yang dibuat.', 1;

/* 1. Database web_hse. */
IF DB_ID(N'web_hse') IS NULL
BEGIN
    CREATE DATABASE web_hse COLLATE SQL_Latin1_General_CP1_CI_AS;
    -- SIMPLE selama pengembangan; ubah ke FULL sebelum dipakai operasional.
    ALTER DATABASE web_hse SET RECOVERY SIMPLE;
    PRINT 'Database web_hse dibuat.';
END
ELSE
    PRINT 'Database web_hse sudah ada, dilewati.';

/* 2. Login hse_app. */
IF SUSER_ID(N'hse_app') IS NULL
BEGIN
    SET @sql = N'CREATE LOGIN hse_app WITH PASSWORD = ' + QUOTENAME(@password, N'''')
             + N', DEFAULT_DATABASE = web_hse, CHECK_POLICY = ON;';
    EXEC sys.sp_executesql @sql;
    PRINT 'Login hse_app dibuat.';
END
ELSE
    PRINT 'Login hse_app sudah ada, password tidak diubah.';

/* 3. User hse_app di web_hse: baca + tulis data saja. */
SET @sql = N'USE web_hse;
IF USER_ID(N''hse_app'') IS NULL CREATE USER hse_app FOR LOGIN hse_app;
ALTER ROLE db_datareader ADD MEMBER hse_app;
ALTER ROLE db_datawriter ADD MEMBER hse_app;
GRANT EXECUTE TO hse_app;';
EXEC sys.sp_executesql @sql;
PRINT 'User hse_app di web_hse siap (baca + tulis data).';
PRINT 'Lanjutkan dengan 002_auth_dan_master.sql di database web_hse.';
