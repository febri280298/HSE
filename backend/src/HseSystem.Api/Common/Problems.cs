using Microsoft.AspNetCore.Http.HttpResults;

namespace HseSystem.Api.Common;

/// <summary>Kesalahan isian yang pesannya aman ditampilkan ke pengguna (dijawab 400 oleh endpoint).</summary>
public sealed class ValidationFailure(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public static class Problems
{
    /// <summary>400 dengan format <c>{ errors: { field: [pesan] } }</c> yang dibaca frontend.</summary>
    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
