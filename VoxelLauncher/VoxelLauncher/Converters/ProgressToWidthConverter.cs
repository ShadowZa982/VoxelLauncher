using Microsoft.UI.Xaml.Data;
using System;

namespace VoxelLauncher.Converters
{
    public class ProgressToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double progress && parameter is double containerWidth)
                return (progress / 100.0) * containerWidth;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}