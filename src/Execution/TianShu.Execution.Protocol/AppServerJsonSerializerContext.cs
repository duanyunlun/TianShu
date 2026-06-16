using System.Text.Json.Serialization;

namespace TianShu.Execution.Protocol;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppServerThreadResponseDto))]
[JsonSerializable(typeof(AppServerThreadSummaryDto))]
[JsonSerializable(typeof(AppServerThreadGitInfoDto))]
[JsonSerializable(typeof(AppServerThreadSessionProjectionDto))]
[JsonSerializable(typeof(AppServerThreadSessionConfigurationDto))]
internal sealed partial class AppServerJsonSerializerContext : JsonSerializerContext;
