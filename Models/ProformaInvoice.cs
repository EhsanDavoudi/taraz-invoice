using System;
using System.Collections.Generic;
using System.Linq;

namespace TarazInvoice.Models
{
    public class ProformaInvoice
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        // شماره اختصاصی پیش‌فاکتور (که از کارشناس مربوطه مشتق شده)
        public string InvoiceNumber { get; set; } = string.Empty;
        
        // تاریخ‌های شمسی
        public string IssueDateShamsi { get; set; } = string.Empty;
        public string ValidUntilShamsi { get; set; } = string.Empty;
        
        // اطلاعات شرکت خریدار
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyPhone { get; set; } = string.Empty;
        public string CompanyEconomicCode { get; set; } = string.Empty;
        public string CompanyNationalCode { get; set; } = string.Empty;
        public string CompanyAddress { get; set; } = string.Empty;

        // اطلاعات کارشناس صادرکننده
        public Guid ExpertId { get; set; }
        public string ExpertName { get; set; } = string.Empty;
        public string ExpertPhone { get; set; } = string.Empty;

        // وضعیت پیش‌فاکتور (۵ وضعیت)
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
        
        // لیست اقلام
        public List<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();

        // خلاصه‌های مالی محاسبه‌شده
        public decimal TotalRawAmount => Items?.Sum(i => i.Quantity * i.RawPrice) ?? 0;
        public decimal TotalDiscount => Items?.Sum(i => i.Quantity * i.DiscountAmount) ?? 0;
        public decimal GrandTotal => Items?.Sum(i => i.TotalPrice) ?? 0;

        // توضیحات و شرایط پرداخت
        public string Notes { get; set; } = "۱. پیش‌فاکتور فوق به مدت ۷ روز کاری دارای اعتبار می‌باشد.\n۲. قیمت‌ها نهایی بوده و نحوه تسویه به صورت توافق طرفین خواهد بود.";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // کپی گرفتن سریع از پیش‌فاکتور
        public ProformaInvoice CloneForNew(string newInvoiceNumber)
        {
            return new ProformaInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = newInvoiceNumber,
                IssueDateShamsi = this.IssueDateShamsi,
                ValidUntilShamsi = this.ValidUntilShamsi,
                CompanyId = this.CompanyId,
                CompanyName = this.CompanyName,
                CompanyPhone = this.CompanyPhone,
                CompanyEconomicCode = this.CompanyEconomicCode,
                CompanyNationalCode = this.CompanyNationalCode,
                CompanyAddress = this.CompanyAddress,
                ExpertId = this.ExpertId,
                ExpertName = this.ExpertName,
                ExpertPhone = this.ExpertPhone,
                Status = InvoiceStatus.Draft,
                Items = this.Items.Select(item => item.Clone()).ToList(),
                Notes = this.Notes,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
    }
}
