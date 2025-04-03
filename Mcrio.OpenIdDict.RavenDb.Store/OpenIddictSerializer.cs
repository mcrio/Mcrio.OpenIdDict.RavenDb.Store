using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// Exposes <see cref="JsonTypeInfo"/> properties for all
/// the OpenIddict types suitable for JSON serialization.
/// Original implementation <see href="https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Abstractions/Primitives/OpenIddictSerializer.cs#L30"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonValue))]
[JsonSerializable(typeof(JsonWebKey))]
[JsonSerializable(typeof(JsonWebKeySet))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(OpenIddictMessage), TypeInfoPropertyName = "Message")]
[JsonSerializable(typeof(OpenIddictParameter), TypeInfoPropertyName = "Parameter")]
[JsonSerializable(typeof(OpenIddictRequest), TypeInfoPropertyName = "Request")]
[JsonSerializable(typeof(OpenIddictResponse), TypeInfoPropertyName = "Response")]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
public partial class OpenIddictSerializer : JsonSerializerContext
{
}