using System;

namespace TarazInvoice.Models
{
    public class Expert
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string RoleTitle { get; set; } = "کارشناس فروش";
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        
        // شماره‌گذاری اختصاصی پیش‌فاکتور برای هر کارشناس
        public string InvoicePrefix { get; set; } = "EXP-";
        public int NextInvoiceNumber { get; set; } = 1001;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string GetPreviewNextInvoiceNumber()
        {
            return $"{InvoicePrefix}{NextInvoiceNumber}";
        }

        public string ConsumeNextInvoiceNumber()
        {
            string number = $"{InvoicePrefix}{NextInvoiceNumber}";
            NextInvoiceNumber++;
            return number;
        }

        public override string ToString() => FullName;
    }
}
