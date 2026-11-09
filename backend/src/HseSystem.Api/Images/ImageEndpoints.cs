using HseSystem.Api.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HseSystem.Api.Images;

public static class ImageEndpoints
{
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder api)
    {
        var images = api.MapGroup("/images");

        // Token antiforgery tidak dipakai: cookie login SameSite=Strict tidak ikut terkirim dari situs lain,
        // jadi form dari situs luar tidak bisa mengupload atas nama pengguna.
        images.MapPost("/", UploadAsync)
            .RequireAuthorization(Permissions.FotoUpload)
            .DisableAntiforgery();

        return images;
    }

    /// <summary>POST /api/images (multipart): field <c>file</c> = foto, <c>category</c> = folder (temuan, perbaikan, ...).</summary>
    internal static async Task<Results<Ok<ImageResponse>, ValidationProblem>> UploadAsync(
        IFormFile? file,
        [FromForm] string? category,
        ImageStorage storage,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Invalid("file", "Pilih atau ambil foto terlebih dahulu.");
        }

        if (file.Length > storage.Options.MaxBytes)
        {
            return Invalid("file", storage.TooLargeMessage);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            return TypedResults.Ok(await storage.SaveAsync(stream, file.FileName, category, cancellationToken));
        }
        catch (ImageUploadException ex)
        {
            return Invalid(ex.Field, ex.Message);
        }
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
