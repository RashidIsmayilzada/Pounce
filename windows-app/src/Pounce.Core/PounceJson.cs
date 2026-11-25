using System.Text.Json.Serialization;

namespace Pounce.Core;

/// <summary>The bootstrap secret, sent to <c>POST /auth/token</c>.</summary>
internal sealed record TokenRequest(string Secret);

/// <summary>The Engine's answer to <c>POST /auth/token</c> (its <c>expiresInSeconds</c> is not needed: a refused token is fetched again).</summary>
internal sealed record TokenResponse(string Token);

/// <summary>Compile-time JSON metadata for the Engine's API: no reflection on the search path.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(List<ResultItem>))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class PounceJson : JsonSerializerContext;
