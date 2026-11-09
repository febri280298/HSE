using System.Security.Claims;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HseSystem.Api.Kpi;

public static class KpiEndpoints
{
    public static RouteGroupBuilder MapKpiEndpoints(this RouteGroupBuilder api)
    {
        var kpi = api.MapGroup("/kpi").RequireAuthorization(Permissions.KpiAkses);
        kpi.MapGet("/options", OptionsAsync);
        kpi.MapGet("/monitoring", MonitoringAsync);
        kpi.MapGet("/summary", SummaryAsync);
        kpi.MapPut("/input", SaveInputAsync);

        kpi.MapGet("/dokumen", DocumentsAsync);
        kpi.MapGet("/dokumen/{documentId:int}/unduh", DownloadAsync);

        // Token antiforgery tidak dipakai: cookie login SameSite=Strict tidak ikut terkirim dari situs lain.
        kpi.MapPost("/dokumen", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentRequestLimit));

        kpi.MapGet("/pica", ListPicasAsync);
        kpi.MapPost("/pica", CreatePicaAsync);
        kpi.MapPut("/pica/{picaId:int}", UpdatePicaAsync);
        kpi.MapDelete("/pica/{picaId:int}", DeletePicaAsync);

        kpi.MapGet("/peserta", ParticipantsAsync).RequireAuthorization(Permissions.MasterKelola);
        kpi.MapPut("/peserta", SaveParticipantsAsync).RequireAuthorization(Permissions.MasterKelola);
        return kpi;
    }

    /// <summary>Batas body upload: sedikit di atas batas file, supaya file kebesaran dijawab dengan pesan yang jelas.</summary>
    private const long DocumentRequestLimit = (50 * 1024 * 1024) + (1024 * 1024);

    internal static async Task<Ok<KpiOptions>> OptionsAsync(ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct) =>
        TypedResults.Ok(await store.OptionsAsync(await ScopeAsync(user, sessions, ct), ct));

    internal static async Task<Ok<KpiMonitoringResponse>> MonitoringAsync(
        int? year, int? month, ClaimsPrincipal user, UserSessions sessions, KpiStore store, TimeProvider time, CancellationToken ct) =>
        TypedResults.Ok(await store.MonitoringAsync(await ScopeAsync(user, sessions, ct), year ?? time.GetLocalNow().Year, month, ct));

    internal static async Task<Results<Ok<KpiSummaryResponse>, NotFound>> SummaryAsync(
        int year, int plantId, int? deptId, int? reviewMonth, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct)
    {
        var summary = await store.SummaryAsync(await ScopeAsync(user, sessions, ct), year, plantId, deptId, reviewMonth, ct);
        return summary is null ? TypedResults.NotFound() : TypedResults.Ok(summary);
    }

    internal static async Task<Results<NoContent, ValidationProblem>> SaveInputAsync(
        KpiInputRequest request, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct)
    {
        try
        {
            await store.SaveInputAsync(await ScopeAsync(user, sessions, ct), request, ct);
            return TypedResults.NoContent();
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<KpiDocumentsResponse>, NotFound, ValidationProblem>> DocumentsAsync(
        string? kind, int year, int plantId, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct)
    {
        if (!KpiDocumentKind.IsValid(kind))
        {
            return Problems.Invalid("kind", "Jenis dokumen tidak dikenal.");
        }

        var result = await store.DocumentsAsync(await ScopeAsync(user, sessions, ct), kind!, year, plantId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    internal static async Task<Results<NoContent, ValidationProblem>> UploadAsync(
        IFormFile? file, [FromForm] string? kind, [FromForm] int plantId, [FromForm] int deptId, [FromForm] int year, [FromForm] int month,
        ClaimsPrincipal user, UserSessions sessions, KpiStore store, DocumentStorage storage, CancellationToken ct)
    {
        if (file is null)
        {
            return Problems.Invalid("file", "Pilih file terlebih dahulu.");
        }

        StoredDocument? stored = null;
        try
        {
            var scope = await ScopeAsync(user, sessions, ct);
            await store.EnsureUploadAllowedAsync(scope, kind, plantId, deptId, year, month, ct);
            stored = await storage.SaveAsync(file, kind!, year, ct);
            var previous = await store.SaveDocumentAsync(scope, kind!, plantId, deptId, year, month, stored, ct);
            if (previous is not null)
            {
                storage.Delete(previous);
            }

            return TypedResults.NoContent();
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
        catch
        {
            // Jangan tinggalkan file yatim kalau pencatatan ke database gagal.
            if (stored is not null)
            {
                storage.Delete(stored.Path);
            }

            throw;
        }
    }

    internal static async Task<Results<PhysicalFileHttpResult, NotFound>> DownloadAsync(
        int documentId, ClaimsPrincipal user, UserSessions sessions, KpiStore store, DocumentStorage storage, CancellationToken ct)
    {
        var row = await store.DownloadAsync(await ScopeAsync(user, sessions, ct), documentId, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var path = storage.FullPath(row.StoredPath);
        if (!File.Exists(path))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(
            path, row.ContentType, KpiDocumentKind.DownloadName(row.Kind, row.Month, row.DeptName, row.PlantName, Path.GetExtension(row.StoredPath)));
    }

    internal static async Task<Ok<IReadOnlyList<KpiPicaItem>>> ListPicasAsync(
        int year, int? plantId, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct) =>
        TypedResults.Ok(await store.ListPicasAsync(await ScopeAsync(user, sessions, ct), year, plantId, ct));

    internal static async Task<Results<Ok<PicaCreated>, ValidationProblem>> CreatePicaAsync(
        KpiPicaRequest request, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(new PicaCreated(await store.CreatePicaAsync(await ScopeAsync(user, sessions, ct), request, ct)));
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<NoContent, NotFound, ValidationProblem>> UpdatePicaAsync(
        int picaId, KpiPicaRequest request, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct)
    {
        try
        {
            return await store.UpdatePicaAsync(await ScopeAsync(user, sessions, ct), picaId, request, ct)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<NoContent, NotFound>> DeletePicaAsync(
        int picaId, ClaimsPrincipal user, UserSessions sessions, KpiStore store, CancellationToken ct) =>
        await store.DeletePicaAsync(await ScopeAsync(user, sessions, ct), picaId, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    internal static async Task<Ok<IReadOnlyList<int>>> ParticipantsAsync(int plantId, KpiStore store, CancellationToken ct) =>
        TypedResults.Ok(await store.ParticipantsAsync(plantId, ct));

    internal static async Task<Results<NoContent, ValidationProblem>> SaveParticipantsAsync(KpiParticipantsRequest request, KpiStore store, CancellationToken ct)
    {
        try
        {
            await store.SaveParticipantsAsync(request, ct);
            return TypedResults.NoContent();
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    private static async Task<KpiScope> ScopeAsync(ClaimsPrincipal user, UserSessions sessions, CancellationToken ct)
    {
        var session = await sessions.GetAsync(user.GetUserId()!.Value, ct);
        return KpiScope.From(session!);
    }
}

public sealed record PicaCreated(int PicaId);
