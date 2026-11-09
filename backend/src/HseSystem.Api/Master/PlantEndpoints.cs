using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Data;

namespace HseSystem.Api.Master;

public static class PlantEndpoints
{
    public static RouteGroupBuilder MapPlantEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/master/plants", async (HseDb db, CancellationToken ct) =>
        {
            await using var conn = db.Connection();
            var plants = await conn.QueryAsync<PlantInfo>(new CommandDefinition(
                "SELECT PlantId, Code, Name FROM mst.Plants WHERE IsActive = 1 ORDER BY SortOrder, Code;", cancellationToken: ct));
            return TypedResults.Ok(plants.ToList());
        });
        return api;
    }
}
