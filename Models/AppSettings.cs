using System;
using System.Collections.Generic;

namespace TarazInvoice.Models
{
    public class AppSettings
    {
        // اطلاعات شرکت ما (فروشنده)
        public string SellerName { get; set; } = "شرکت آریان نیرو گستر اترک";
        public string SellerNationalCode { get; set; } = "14008765432";
        public string SellerEconomicCode { get; set; } = "411567891234";
        public string SellerRegistrationNo { get; set; } = "۸۸۶۵۴";
        public string SellerPhone { get; set; } = "۵- ۰۵۱۳۷۱۳۲۰۸۴";
        public string SellerAddress { get; set; } = "مشهد- خیابان سنایی- بین سنایی ۱۲ و ۱۴- روبروی سنایی ۱۵- پلاک ۱۱۵";
        public string SellerPostalCode { get; set; } = "1968634567";

        // مسئول امضا، سربرگ و عبارات استاندارد پیش‌فاکتور
        public string SignatoryName { get; set; } = "محمد احمدی پور هدش";
        public string ExpertRoleLabel { get; set; } = "کارشناسی خرید:";
        public string DefaultValidityText { get; set; } = "اعتبار این پیش فاکتورتا پایان روز می باشد.";
        public string CustomerLabelPrefix { get; set; } = "نام مشتری شرکت محترم ";
        public string GreetingText { get; set; } = "احتراما\" قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:";

        // تصویر لوگو و مهر و امضا
        public string CompanyLogoPath { get; set; } = string.Empty;
        public string CompanyStampPath { get; set; } = string.Empty;
        public double LogoWidth { get; set; } = 68;
        public double LogoHeight { get; set; } = 68;
        public double StampWidth { get; set; } = 140;
        public double StampHeight { get; set; } = 75;

        // تنظیمات قلم و فونت پیش‌فاکتور (جهت عنوان شرکت و سایر سطرها)
        public string CompanyNameFontFamily { get; set; } = "Vazirmatn";
        public double CompanyNameFontSize { get; set; } = 18;
        public string TableFontFamily { get; set; } = "Vazirmatn";
        public double TableFontSize { get; set; } = 11;

        // اطلاعات مالی و بانکی پیش‌فاکتور (پشتیبانی از چندین حساب بانکی، شماره شبا و شماره کارت)
        public List<BankAccountInfo> BankAccounts { get; set; } = new List<BankAccountInfo>
        {
            new BankAccountInfo
            {
                BankName = "بانک ملی ایران",
                AccountNumber = "",
                ShebaNumber = "IR95017000000118033698006",
                CardNumber = "۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶",
                OwnerName = "شرکت آریان نیرو گستر اترک"
            }
        };

        public string BankAccountNumber { get; set; } = "IR95017000000118033698006 بانک ملی ایران";
        public string BankCardNumber { get; set; } = "۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶ شرکت آریان نیرو گستر اترک";
        public string PaymentTerms { get; set; } = "نقد";
        public string DeliveryDays { get; set; } = "موجود";
        public string DeliveryLocation { get; set; } = "مشهد";
        public decimal VatPercent { get; set; } = 10m; // ارزش افزوده (۱۰٪)

        // تنظیمات عمومی (همیشه بر اساس ریال)
        public string CurrencyUnit { get; set; } = "ریال";
        public string DefaultTerms { get; set; } = "فرایند تحویل بار پس از وصول پیش پرداخت در صورت عدم تغییر قیمت انجام خواهد شد.در صورت تغییر قیمت پیش فاکتور اصلاحی تقدیم میگردد.";

        // تنظیمات پیشوند و شماره‌گذاری یکتای پیش‌فاکتور
        public string InvoicePrefix { get; set; } = "ANA 405-";
        public int NextInvoiceNumber { get; set; } = 1001;

        // مسیر پوشه اختصاصی / شبکه پایگاه‌داده
        public string CustomDatabaseFolder { get; set; } = string.Empty;
    }
}
