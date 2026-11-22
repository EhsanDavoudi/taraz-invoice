using System;

namespace TarazInvoice.Models
{
    public enum CompanyType
    {
        Legal = 0, // حقوقی (شرکت، سازمان، کارخانه)
        Real = 1   // حقیقی (شخص، فروشگاه، مهندس آزاد)
    }

    public class Company
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        
        // نوع طرف حساب (حقوقی یا حقیقی)
        public CompanyType CompanyType { get; set; } = CompanyType.Legal;
        public string CompanyTypeTitle => CompanyType == CompanyType.Real ? "حقیقی" : "حقوقی";

        // کد اقتصادی و شماره/شناسه ملی تجمیع‌شده
        public string NationalCode { get; set; } = string.Empty;     // شناسه ملی / کد ملی
        public string EconomicCode { get; set; } = string.Empty;     // کد اقتصادی

        public string NationalOrEconomicCode
        {
            get => !string.IsNullOrWhiteSpace(EconomicCode) ? EconomicCode : NationalCode;
            set
            {
                EconomicCode = value ?? string.Empty;
                NationalCode = value ?? string.Empty;
            }
        }

        public string RegistrationNo { get; set; } = string.Empty;   // شماره ثبت
        public string Phone { get; set; } = string.Empty;            // تلفن ثابت
        public string Mobile { get; set; } = string.Empty;           // تلفن همراه
        public string Address { get; set; } = string.Empty;          // نشانی
        public string PostalCode { get; set; } = string.Empty;       // کد پستی
        public string Email { get; set; } = string.Empty;            // ایمیل
        public string Description { get; set; } = string.Empty;      // توضیحات
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public override string ToString() => Name;
    }
}
