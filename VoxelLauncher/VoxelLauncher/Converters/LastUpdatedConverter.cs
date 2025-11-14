using Microsoft.UI.Xaml.Data;
using System;

namespace VoxelLauncher.Converters
{
    public class LastUpdatedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string str && DateTime.TryParse(str, out var dt))
            {
                var diff = DateTime.Now - dt;
                if (diff.TotalMinutes < 1) return "Vừa cập nhật";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} phút trước";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} giờ trước";
                return $"{(int)diff.TotalDays} ngày trước";
            }
            return "Không rõ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}