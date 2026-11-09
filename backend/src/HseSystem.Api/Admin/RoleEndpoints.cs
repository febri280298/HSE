using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HseSystem.Api.Admin;

public static class RoleEndpoints
{
    public static RouteGroupBuilder MapRoleEndpoints(this RouteGroupBuilder api)
    {
        var roles = api.MapGroup("/admin/roles").RequireAuthorization(Permissions.PeranKelola);
        roles.MapGet("/", ListAsync);
        roles.MapGet("/permissions", () => TypedResults.Ok(Permissions.All));
        roles.MapPost("/", CreateAsync);
        roles.MapPut("/{roleId:int}", UpdateAsync);
        roles.MapDelete("/{roleId:int}", DeleteAsync);
        return roles;
    }

    internal static async Task<Ok<IReadOnlyList<RoleResponse>>> ListAsync(RoleStore store, CancellationToken ct) =>
        TypedResults.Ok(await store.ListAsync(ct));

    internal static async Task<Results<Ok<RoleResponse>, ValidationProblem>> CreateAsync(
        RoleRequest request, RoleStore store, CancellationToken ct)
    {
        try
        {
            var (name, description, permissions) = Validate(request);
            var roleId = await store.CreateAsync(name, description, permissions, ct);
            return TypedResults.Ok((await store.ListAsync(ct)).Single(r => r.RoleId == roleId));
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<RoleResponse>, ValidationProblem, NotFound>> UpdateAsync(
        int roleId, RoleRequest request, RoleStore store, UserSessions sessions, CancellationToken ct)
    {
        try
        {
            var (name, description, permissions) = Validate(request);
            if (!await store.UpdateAsync(roleId, name, description, permissions, ct))
            {
                return TypedResults.NotFound();
            }

            // Hak akses peran berubah: berlaku seketika untuk semua pemegang peran ini.
            sessions.InvalidateAll();
            return TypedResults.Ok((await store.ListAsync(ct)).Single(r => r.RoleId == roleId));
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<NoContent, ValidationProblem, NotFound>> DeleteAsync(
        int roleId, RoleStore store, UserSessions sessions, CancellationToken ct)
    {
        try
        {
            if (!await store.DeleteAsync(roleId, ct))
            {
                return TypedResults.NotFound();
            }

            sessions.InvalidateAll();
            return TypedResults.NoContent();
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    private static (string Name, string? Description, IReadOnlyList<string> Permissions) Validate(RoleRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 50)
        {
            throw new ValidationFailure("name", "Nama peran 2-50 karakter.");
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > 200)
        {
            throw new ValidationFailure("description", "Keterangan maksimal 200 karakter.");
        }

        var permissions = (request.Permissions ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (permissions.FirstOrDefault(p => !Permissions.Exists(p)) is { } unknown)
        {
            throw new ValidationFailure("permissions", $"Hak akses tidak dikenal: {unknown}.");
        }

        return (name, description, permissions);
    }
}
