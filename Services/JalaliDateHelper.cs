using System;
using System.Globalization;
using System.Text;

namespace TarazInvoice.Services
{
    public static class JalaliDateHelper
    {
        private static readonly PersianCalendar _pc = new PersianCalendar();

        public static string GetCurrentShamsiDate()
        {
            DateTime now = DateTime.Now;
            return $"{_pc.GetYear(now):0000}/{_pc.GetMonth(now):00}/{_pc.GetDayOfMonth(now):00}";
        }

        public static string GetShamsiDateAfterDays(int days)
        {
            DateTime future = DateTime.Now.AddDays(days);
            return $"{_pc.GetYear(future):0000}/{_pc.GetMonth(future):00}/{_pc.GetDayOfMonth(future):00}";
        }

        public static string ToPersianDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace('0', '۰')
                .Replace('1', '۱')
                .Replace('2', '۲')
                .Replace('3', '۳')
                .Replace('4', '۴')
                .Replace('5', '۵')
                .Replace('6', '۶')
                .Replace('7', '۷')
                .Replace('8', '۸')
                .Replace('9', '۹');
        }

        public static string FormatMoney(decimal amount, string unit = "تومان")
        {
            return $"{amount:N0} {unit}";
        }

        // تبدیل عدد به حروف فارسی جهت درج در پیش‌فاکتور
        private static readonly string[] Yekan = { "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه" };
        private static readonly string[] Dahha = { "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };
        private static readonly string[] DahYek = { "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };
        private static readonly string[] Sadha = { "", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد" };
        private static readonly string[] Tabaghat = { "", "هزار", "میلیون", "میلیارد", "تریلیون" };

        public static string NumberToWords(long number)
        {
            if (number == 0) return "صفر";
            if (number < 0) return "منفی " + NumberToWords(Math.Abs(number));

            var parts = new System.Collections.Generic.List<string>();
            int group = 0;

            while (number > 0)
            {
                int rem = (int)(number % 1000);
                if (rem != 0)
                {
                    string groupText = ThreeDigitsToWord(rem);
                    if (!string.IsNullOrEmpty(Tabaghat[group]))
                        groupText += " " + Tabaghat[group];
                    parts.Insert(0, groupText);
                }
                number /= 1000;
                group++;
            }

            return string.Join(" و ", parts);
        }

        private static string ThreeDigitsToWord(int num)
        {
            int s = num / 100;
            int d = (num % 100) / 10;
            int y = num % 10;

            var parts = new System.Collections.Generic.List<string>();
            if (s > 0) parts.Add(Sadha[s]);

            if (d == 1)
            {
                parts.Add(DahYek[y]);
            }
            else
            {
                if (d > 1) parts.Add(Dahha[d]);
                if (y > 0) parts.Add(Yekan[y]);
            }

            return string.Join(" و ", parts);
        }
    }
}
