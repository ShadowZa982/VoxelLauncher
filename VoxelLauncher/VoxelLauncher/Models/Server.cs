using System;
using System.Collections.Generic;

namespace VoxelLauncher.Models
{
    public class Server
    {
        public int Id { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string IpPC { get; set; } = string.Empty;
        public string IpPE { get; set; } = string.Empty;
        public int MaxSlots { get; set; }
        public int CurrentUsers { get; set; }
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                _imageUrl = value ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"[ImageUrl] Set: '{value}'");
            }
        }
        private string _imageUrl = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Facebook { get; set; } = string.Empty;
        public string Discord { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;

        public string LastUpdated { get; set; } = string.Empty;

        public int UserId { get; set; }

        public string OnlineStatus => CurrentUsers >= 0 ? "Online" : "Offline";
        public string PlayerText => $"{CurrentUsers}/{MaxSlots}";
        public double OnlinePercentage => MaxSlots > 0 ? (double)CurrentUsers / MaxSlots * 100 : 0;
        public string LastUpdatedDisplay
        {
            get
            {
                if (DateTime.TryParse(LastUpdated, out var dt))
                {
                    var diff = DateTime.Now - dt;
                    if (diff.TotalMinutes < 1) return "Vừa cập nhật";
                    if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} phút trước";
                    if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} giờ trước";
                    return $"{(int)diff.TotalDays} ngày trước";
                }
                return "Không rõ";
            }
        }
    }

    public class ServerListResponse
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public List<Server> Data { get; set; } = new();
    }

    public class ServerDetailResponse
    {
        public bool Success { get; set; }
        public Server Data { get; set; } = new();
        public string? Message { get; set; }
    }
}