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
        
        // نام / عنوان پیش‌فاکتور
        public string Title { get; set; } = string.Empty;
        
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
        public decimal TotalProfit => Items?.Sum(i => i.TotalProfit) ?? 0;
        public decimal GrandTotal => InvoiceItem.RoundUpPrice(Items?.Sum(i => i.TotalPrice) ?? 0);
        public decimal ProfitMarginPercent => (GrandTotal > 0 && TotalProfit > 0) ? Math.Round((TotalProfit / GrandTotal) * 100m, 1) : 0;

        // ارزش افزوده و جمع نهایی
        public decimal VatPercent { get; set; } = 10m;
        public decimal GrandTotalWithVat => (VatPercent > 0)
            ? InvoiceItem.RoundUpPrice(GrandTotal + Math.Round(GrandTotal * (VatPercent / 100m), 0))
            : GrandTotal;
        public decimal VatAmount => (VatPercent > 0) ? (GrandTotalWithVat - GrandTotal) : 0;

        // سربرگ فاکتور
        public string Subject { get; set; } = string.Empty;
        public string Attachment { get; set; } = "ندارد";
        public string PageNumberText { get; set; } = "یک";
        public string CustomerLabelPrefix { get; set; } = "نام مشتری شرکت محترم ";
        public string GreetingText { get; set; } = string.Empty;
        public string CurrencyUnit { get; set; } = "ریال";

        // توضیحات و شرایط پرداخت اختصاصی هر پیش‌فاکتور (مطابق نمونه مصوب)
        public string ValidityText { get; set; } = "اعتبار این پیش فاکتورتا پایان روز می باشد.";
        public string PaymentTerms { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string DeliveryDays { get; set; } = string.Empty;
        public string DeliveryLocation { get; set; } = "مشهد";

        // مسئول امضا و کارشناس
        public string SignatoryName { get; set; } = "محمد احمدی پور هدش";
        public string ExpertRoleLabel { get; set; } = string.Empty;

        // حساب‌های بانکی اختصاصی این پیش‌فاکتور (در صورت عدم تعیین، از تنظیمات سامانه خوانده می‌شود)
        public List<BankAccountInfo> BankAccounts { get; set; } = new List<BankAccountInfo>();

        // قابلیت هایلایت (برجسته‌سازی زرد) بخش‌های مهم پیش‌فاکتور (پیش‌فرض خاموش)
        public bool HighlightValidity { get; set; } = false;
        public bool HighlightPaymentTerms { get; set; } = false;
        public bool HighlightNotes { get; set; } = false;
        public bool HighlightDeliveryDays { get; set; } = false;
        public bool HighlightDeliveryLocation { get; set; } = false;
        public bool HighlightVat { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // کپی گرفتن سریع از پیش‌فاکتور
        public ProformaInvoice CloneForNew(string newInvoiceNumber)
        {
            return new ProformaInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = newInvoiceNumber,
                Title = this.Title,
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
                Subject = this.Subject,
                Attachment = this.Attachment,
                PageNumberText = this.PageNumberText,
                CustomerLabelPrefix = this.CustomerLabelPrefix,
                GreetingText = this.GreetingText,
                CurrencyUnit = this.CurrencyUnit,
                VatPercent = this.VatPercent,
                ValidityText = this.ValidityText,
                PaymentTerms = this.PaymentTerms,
                Notes = this.Notes,
                DeliveryDays = this.DeliveryDays,
                DeliveryLocation = this.DeliveryLocation,
                SignatoryName = this.SignatoryName,
                ExpertRoleLabel = this.ExpertRoleLabel,
                BankAccounts = this.BankAccounts?.Select(b => new BankAccountInfo { BankName = b.BankName, AccountNumber = b.AccountNumber, ShebaNumber = b.ShebaNumber, CardNumber = b.CardNumber, OwnerName = b.OwnerName }).ToList() ?? new List<BankAccountInfo>(),
                HighlightValidity = this.HighlightValidity,
                HighlightPaymentTerms = this.HighlightPaymentTerms,
                HighlightNotes = this.HighlightNotes,
                HighlightDeliveryDays = this.HighlightDeliveryDays,
                HighlightDeliveryLocation = this.HighlightDeliveryLocation,
                HighlightVat = this.HighlightVat,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
    }
}
