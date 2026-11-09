using Microsoft.Extensions.Caching.Memory;

namespace HseSystem.Api.Auth;

/// <summary>
/// Keadaan pengguna (aktif, peran, hak akses, plant) dibaca dari database setiap request, di-cache 30 detik.
/// Perubahan oleh admin memanggil <see cref="Invalidate"/> / <see cref="InvalidateAll"/>, sehingga berlaku seketika
/// tanpa pengguna harus login ulang.
/// </summary>
public sealed class UserSessions(AuthStore store, IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private long _generation;

    public async Task<UserSession?> GetAsync(int userId, CancellationToken ct)
    {
        var key = Key(userId);
        if (cache.TryGetValue(key, out UserSession? session))
        {
            return session;
        }

        session = await store.GetSessionAsync(userId, ct);
        cache.Set(key, session, Lifetime);
        return session;
    }

    /// <summary>Data satu pengguna berubah (diubah admin, ganti password, login).</summary>
    public void Invalidate(int userId) => cache.Remove(Key(userId));

    /// <summary>Hak akses peran berubah: bisa mengenai banyak pengguna sekaligus.</summary>
    public void InvalidateAll() => Interlocked.Increment(ref _generation);

    private CacheKey Key(int userId) => new(Interlocked.Read(ref _generation), userId);

    private readonly record struct CacheKey(long Generation, int UserId);
}
