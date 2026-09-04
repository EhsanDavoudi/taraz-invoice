using System;

namespace TarazInvoice.Models
{
    public class AppSettings
    {
        // اطلاعات شرکت ما (فروشنده)
        public string SellerName { get; set; } = "شرکت بازرگانی تاراز";
        public string SellerNationalCode { get; set; } = "14008765432";
        public string SellerEconomicCode { get; set; } = "411567891234";
        public string SellerRegistrationNo { get; set; } = "54321";
        public string SellerPhone { get; set; } = "021-88889999";
        public string SellerAddress { get; set; } = "تهران، خیابان ولیعصر، برج تجارت، طبقه ۴";
        public string SellerPostalCode { get; set; } = "1968634567";

        // تنظیمات قالب اکسل
        public string CustomTemplatePath { get; set; } = string.Empty;
        public int TemplateItemStartRow { get; set; } = 11; // سطر شروع اقلام در قالب

        // تنظیمات عمومی
        public string CurrencyUnit { get; set; } = "تومان";
        public string DefaultTerms { get; set; } = "۱. پیش‌فاکتور فوق به مدت ۷ روز کاری دارای اعتبار می‌باشد.\n۲. قیمت‌ها نهایی بوده و نحوه تسویه به صورت توافق طرفین خواهد بود.";
    }
}
