using System;
using System.Collections.Generic;

namespace TarazInvoice.Models
{
    public class AppSettings
    {
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // اطلاعات شرکت ما (فروشنده)
        public string SellerName { get; set; } = string.Empty;
        public string SellerNationalCode { get; set; } = string.Empty;
        public string SellerEconomicCode { get; set; } = string.Empty;
        public string SellerRegistrationNo { get; set; } = string.Empty;
        public string SellerPhone { get; set; } = string.Empty;
        public string SellerAddress { get; set; } = string.Empty;
        public string SellerPostalCode { get; set; } = string.Empty;

        // مسئول امضا، سربرگ و عبارات استاندارد پیش‌فاکتور
        public string SignatoryName { get; set; } = "محمد احمدی پور هدش";
        public string ExpertRoleLabel { get; set; } = string.Empty;
        public string DefaultValidityText { get; set; } = "اعتبار این پیش فاکتورتا پایان روز می باشد.";
        public string CustomerLabelPrefix { get; set; } = "نام مشتری شرکت محترم ";
        public string GreetingText { get; set; } = string.Empty;

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

        // اطلاعات مالی و بانکی پیش‌فاکتور (پشتیبانی از چندین حساب بانکی، شماره شبا و شماره کارت ذخیره‌شده در دیتابیس توسط کاربر)
        public List<BankAccountInfo> BankAccounts { get; set; } = new List<BankAccountInfo>();

        public string BankAccountNumber { get; set; } = string.Empty;
        public string BankCardNumber { get; set; } = string.Empty;
        public string PaymentTerms { get; set; } = string.Empty;
        public string DeliveryDays { get; set; } = string.Empty;
        public string DeliveryLocation { get; set; } = "مشهد";
        public decimal VatPercent { get; set; } = 10m;

        // تنظیمات عمومی (همیشه بر اساس ریال)
        public string CurrencyUnit { get; set; } = "ریال";
        public string DefaultTerms { get; set; } = string.Empty;

        // تنظیمات پیشوند و شماره‌گذاری یکتای پیش‌فاکتور
        public string InvoicePrefix { get; set; } = string.Empty;
        public int NextInvoiceNumber { get; set; } = 1;

        // مسیر پوشه اختصاصی / شبکه پایگاه‌داده
        public string CustomDatabaseFolder { get; set; } = string.Empty;
    }
}
