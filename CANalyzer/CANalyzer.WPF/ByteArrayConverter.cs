using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace CANalyzer.WPF.Converters
{
    public class ByteArrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte[] bytes)
            {
                return BitConverter.ToString(bytes).Replace("-", " ");
            }
            return string.Empty;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hexString)
            {
                hexString = hexString.Replace(" ", "").Replace("-", "");
                
                if (hexString.Length % 2 != 0)
                    return Array.Empty<byte>();
                    
                byte[] bytes = new byte[hexString.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = System.Convert.ToByte(hexString.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            return Array.Empty<byte>();
        }
    }
}