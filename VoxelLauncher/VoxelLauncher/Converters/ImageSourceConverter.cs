using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace VoxelLauncher.Converters
{
    public class ImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string url = value as string;

            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri validUri))
            {
                if (validUri.Scheme == "http" || validUri.Scheme == "https")
                {
                    var bitmap = new BitmapImage();
                    bitmap.UriSource = validUri; 
                    return bitmap;
                }
            }

            try
            {
                var defaultBitmap = new BitmapImage();
                defaultBitmap.UriSource = new Uri("ms-appx:///Assets/Icons/vanilla.png");
                return defaultBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback image failed: {ex.Message}");
                return new BitmapImage();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}