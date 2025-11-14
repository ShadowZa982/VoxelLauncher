using System;
using Microsoft.UI.Xaml.Data;

namespace VoxelLauncher.Converters
{
    public class DateTimeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, string l)
            => value is DateTime dt ? $"{dt:dd/MM HH:mm}" : "";
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }
}
