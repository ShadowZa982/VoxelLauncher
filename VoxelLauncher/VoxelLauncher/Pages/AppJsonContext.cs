// AppJsonContext.cs
using CmlLib.Core.Auth;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoxelLauncher.Pages;

namespace VoxelLauncher
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(List<MinecraftProfile>))]
    [JsonSerializable(typeof(MinecraftProfile))]
    [JsonSerializable(typeof(MSession))]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }
}