using CmlLib.Core;
using CmlLib.Core.Version;
using CmlLib.Core.VersionMetadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoxelLauncher.Services
{
    public class MinecraftVersionInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime? ReleaseTime { get; set; }
    }

    public class MinecraftVersionManager
    {
        private readonly MinecraftLauncher _launcher;
        private readonly string _cacheFile;
        private List<MinecraftVersionInfo> _cached = new();
        private DateTime _lastUpdated = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(2);

        public MinecraftVersionManager(MinecraftLauncher launcher, string cacheFile)
        {
            _launcher = launcher;
            _cacheFile = cacheFile;
        }

        public async Task<List<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken ct = default)
        {
            if (_cached.Any() && DateTime.Now - _lastUpdated < _cacheDuration)
                return _cached;

            try
            {
                if (File.Exists(_cacheFile))
                {
                    var json = await File.ReadAllTextAsync(_cacheFile, ct);
                    var cached = JsonSerializer.Deserialize<List<MinecraftVersionInfo>>(json);
                    if (cached != null && cached.Any())
                    {
                        _cached = cached;
                        _lastUpdated = File.GetLastWriteTime(_cacheFile);
                        return _cached;
                    }
                }

                ct.ThrowIfCancellationRequested();
                var versions = await _launcher.GetAllVersionsAsync();
                _cached = versions
                    .Where(v => v.GetVersionType() == MVersionType.Release)
                    .Select(v => new MinecraftVersionInfo
                    {
                        Name = v.Name,
                        Type = "release",
                        ReleaseTime = v.ReleaseTime.UtcDateTime
                    })
                    .OrderByDescending(v => v.ReleaseTime)
                    .ToList();

                _lastUpdated = DateTime.Now;
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                await File.WriteAllTextAsync(_cacheFile, JsonSerializer.Serialize(_cached));
            }
            catch
            {
                if (!_cached.Any() && File.Exists(_cacheFile))
                {
                    var json = await File.ReadAllTextAsync(_cacheFile);
                    _cached = JsonSerializer.Deserialize<List<MinecraftVersionInfo>>(json) ?? new();
                }
            }

            return _cached;
        }
    }
}