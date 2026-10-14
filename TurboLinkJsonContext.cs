using System.Collections.Generic;
using System.Text.Json.Serialization;
using Google.Protobuf.Compiler;

namespace protoc_gen_turbolink
{
    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        WriteIndented = true)]
    [JsonSerializable(typeof(CodeGeneratorRequest))]
    [JsonSerializable(typeof(Dictionary<string, GrpcServiceFile>))]
    internal partial class TurboLinkJsonContext : JsonSerializerContext
    {
    }
}
