#nullable disable
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TarazInvoice.Models;

namespace TarazInvoice.Converters
{
    public class StatusToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                return status.ToPersianString();
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                string hex = status.GetColorHex();
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToBgColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                string hex = status.GetBgColorHex();
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            }
            return (SolidColorBrush)new BrushConverter().ConvertFrom("#F3F4F6")!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CurrencyFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return $"{d:N0} ریال";
            }
            if (value is double db)
            {
                return $"{db:N0} ریال";
            }
            return "0 ریال";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DecimalNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return d == 0 ? "0" : d.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (value is double db)
            {
                return db == 0 ? "0" : db.ToString("N0", CultureInfo.InvariantCulture);
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return 0m;
            string s = value.ToString() ?? "";
            s = s.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                 .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
                 .Replace(",", "").Replace("،", "").Replace(" ", "").Trim();

            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return 0m;
        }
    }

    public class NullablePercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            string s = value.ToString() ?? "";
            s = s.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                 .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
                 .Replace("%", "").Replace("٪", "").Replace(",", "").Replace("،", "").Replace(" ", "").Trim();

            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            s = s.Replace('/', '.').Replace('٫', '.');

            // اگر کاربر در حال تایپ ممیز در انتهای عدد است (مثلاً "6.")، مانع پاک شدن ممیز توسط بایندینگ شویم
            if (s.EndsWith(".") || s == "-")
            {
                return Binding.DoNothing;
            }

            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return (decimal?)result;
            }
            return null;
        }
    }

    public class NullablePercentDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return $"{d:G29} %";
            }
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NotApprovedToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status && status == InvoiceStatus.Approved)
            {
                return System.Windows.Visibility.Collapsed;
            }
            return System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
