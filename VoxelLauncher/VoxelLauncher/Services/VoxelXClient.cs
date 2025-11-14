using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoxelLauncher.Services
{
    public static class VoxelXClient
    {
        private const string VoxelXDirName = "VoxelX";
        private static readonly string[] DefaultFiles = {
                "enabled.json",
                "config.json",
                "sidebar.json"
            };

        public static async Task EnsureVoxelXAsync(string minecraftPath)
        {
            var voxelXDir = Path.Combine(minecraftPath, VoxelXDirName);
            Directory.CreateDirectory(voxelXDir);

            foreach (var file in DefaultFiles)
            {
                var destPath = Path.Combine(voxelXDir, file);
                if (!File.Exists(destPath))
                {
                    var resourceName = $"VoxelLauncher1.Assets.VoxelX.{file}";
                    var stream = typeof(VoxelXClient).Assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var fileStream = File.Create(destPath);
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
            Directory.CreateDirectory(Path.Combine(voxelXDir, "resourcepacks"));
            Directory.CreateDirectory(Path.Combine(voxelXDir, "shaders"));
        }

        public static void AddJvmArgs(ref List<string> jvmArgs, string minecraftPath)
        {
            if (jvmArgs == null) jvmArgs = new List<string>();
            var voxelXDir = Path.Combine(minecraftPath, VoxelXDirName);
            var configPath = Path.Combine(voxelXDir, "config.json");
            if (!File.Exists(configPath)) return;
            var jarPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "VoxelXTweaker.jar");
            jvmArgs.Add($"-javaagent:{jarPath}=--voxelxConfig={configPath}");

            Console.WriteLine($"[VOXELX] Added: -javaagent:{jarPath}=--voxelxConfig={configPath}");
        }

    }
}