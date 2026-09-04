using System;

namespace TarazInvoice.Models
{
    public class Company
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;     // شناسه ملی
        public string EconomicCode { get; set; } = string.Empty;     // کد اقتصادی
        public string RegistrationNo { get; set; } = string.Empty;   // شماره ثبت
        public string Phone { get; set; } = string.Empty;            // تلفن ثابت
        public string Mobile { get; set; } = string.Empty;           // تلفن همراه
        public string Address { get; set; } = string.Empty;          // نشانی
        public string PostalCode { get; set; } = string.Empty;       // کد پستی
        public string Email { get; set; } = string.Empty;            // ایمیل
        public string Description { get; set; } = string.Empty;      // توضیحات
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString() => Name;
    }
}
