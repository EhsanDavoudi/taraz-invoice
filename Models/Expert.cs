using System;
using System.Collections.Generic;
using System.Linq;

namespace TarazInvoice.Models
{
    public class Expert
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        // انتساب به یک یا چند شرکت (طرف حساب)
        public Guid? CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;

        // لیست شناسه‌های شرکت‌های طرف قرارداد با این کارشناس
        public List<Guid> AssignedCompanyIds { get; set; } = new List<Guid>();
        public List<string> AssignedCompanyNames { get; set; } = new List<string>();

        public string FullName { get; set; } = string.Empty;
        public string RoleTitle { get; set; } = "کارشناس فروش";
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        
        // شماره‌گذاری اختصاصی پیش‌فاکتور برای هر کارشناس
        public string InvoicePrefix { get; set; } = "EXP-";
        public int NextInvoiceNumber { get; set; } = 1001;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [System.Text.Json.Serialization.JsonIgnore]
        public string AssignedCompaniesDisplay
        {
            get
            {
                if (AssignedCompanyNames != null && AssignedCompanyNames.Count > 0)
                {
                    return string.Join("، ", AssignedCompanyNames);
                }
                if (!string.IsNullOrWhiteSpace(CompanyName))
                {
                    return CompanyName;
                }
                return "همه شرکت‌ها (عمومی)";
            }
        }

        /// <summary>
        /// بررسی اینکه آیا این کارشناس به شرکت داده‌شده متصل است یا کارشناس عمومی است
        /// </summary>
        public bool BelongsToCompany(Guid companyId)
        {
            // اگر کارشناس هیچ شرکت اختصاصی ندارد، عمومی است و به همه شرکت‌ها منتسب می‌شود
            if (AssignedCompanyIds == null || AssignedCompanyIds.Count == 0)
            {
                if (!CompanyId.HasValue || CompanyId.Value == Guid.Empty) return true;
                return CompanyId.Value == companyId;
            }

            return AssignedCompanyIds.Contains(companyId) || (CompanyId.HasValue && CompanyId.Value == companyId);
        }

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
