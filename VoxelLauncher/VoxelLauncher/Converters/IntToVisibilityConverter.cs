using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace VoxelLauncher.Converters
{
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool visible = value is int i && i > 0;
            if (parameter?.ToString() == "inverse") visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
