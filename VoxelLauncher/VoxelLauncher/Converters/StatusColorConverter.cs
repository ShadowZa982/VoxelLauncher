using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using System;

namespace VoxelLauncher.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int users && users >= 0)
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.LimeGreen);
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.IndianRed);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}