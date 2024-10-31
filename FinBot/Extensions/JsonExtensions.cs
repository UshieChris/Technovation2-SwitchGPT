using System.Text.Json;

namespace FinBot.Extensions;

public static class JsonExtensions
{
#pragma warning disable CS8603 // Possible null reference return.
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T FromJson<T>(this string json)
        => JsonSerializer.Deserialize<T>(json, _jsonOptions);

    public static string ToJson<T>(this T obj) =>
        JsonSerializer.Serialize(obj, _jsonOptions);

    public static T FromJsonAnonymously<T>(this string json, T anonymousTypeObject)
        => JsonSerializer.Deserialize<T>(json, _jsonOptions);

    public static ValueTask<TValue> FromJsonAnonymouslyAsync<TValue>(this Stream stream, TValue anonymousTypeObject, CancellationToken cancellationToken = default)
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
        => JsonSerializer.DeserializeAsync<TValue>(stream, _jsonOptions, cancellationToken);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
}

#pragma warning restore CS8603 // Possible null reference return.
