using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Data;

/// <summary>Pembuat koneksi ke database web_hse (login hse_app).</summary>
public sealed class HseDb(string connectionString)
{
    public SqlConnection Connection() => new(connectionString);

    /// <summary>Pelanggaran UNIQUE / PRIMARY KEY.</summary>
    public static bool IsUniqueViolation(SqlException ex) => ex.Number is 2627 or 2601;
}
