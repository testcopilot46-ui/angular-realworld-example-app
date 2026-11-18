using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitApi;

public record UserDto(string Username, string Email, string? Bio, string? Image, string Token);

public record ProfileDto(string Username, string? Bio, string? Image, bool Following);

public record ArticleDto(
    string Slug,
    string Title,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    List<string> TagList,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Favorited,
    int FavoritesCount,
    ProfileDto Author);

public record CommentDto(int Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, ProfileDto Author);

public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}
