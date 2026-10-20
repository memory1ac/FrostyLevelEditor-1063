using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LevelEditorPlugin.Converters;

public class LogicEntityIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string iconString = (string)value;
        return (new ImageSourceConverter().ConvertFromString($"pack://application:,,,/LevelEditorPlugin;component/Images/{iconString}.png")) as ImageSource;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
