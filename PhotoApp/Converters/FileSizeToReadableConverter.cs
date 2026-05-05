using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhotoApp.Converters;

public sealed class FileSizeToReadableConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long fileSizeBytes)
        {
            return string.Empty;
        }

        const double bytesInKb = 1024.0;
        const double bytesInMb = 1024.0 * 1024.0;

        if (fileSizeBytes >= bytesInMb)
        {
            return $"{fileSizeBytes / bytesInMb:0.##} Mo";
        }

        return $"{fileSizeBytes / bytesInKb:0.##} Ko";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
