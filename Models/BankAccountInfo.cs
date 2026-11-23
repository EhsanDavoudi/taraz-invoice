using System;
using System.Collections.Generic;

namespace TarazInvoice.Models
{
    public class BankAccountInfo
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string BankName { get; set; } = "";       // نام بانک
        public string AccountNumber { get; set; } = "";               // شماره حساب
        public string ShebaNumber { get; set; } = "";                 // شماره شبا (با IR یا بدون IR)
        public string CardNumber { get; set; } = "";                  // شماره کارت
        public string OwnerName { get; set; } = "";                   // به نام

        public string ToFormattedTerm()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(AccountNumber))
                parts.Add($"شماره حساب {AccountNumber.Trim()}");
            
            if (!string.IsNullOrWhiteSpace(ShebaNumber))
            {
                string sh = ShebaNumber.Trim();
                parts.Add(sh.StartsWith("IR", StringComparison.OrdinalIgnoreCase) ? $"شماره شبا {sh}" : $"شماره شبا IR{sh}");
            }

            if (!string.IsNullOrWhiteSpace(BankName))
                parts.Add(BankName.Trim());

            if (!string.IsNullOrWhiteSpace(CardNumber))
                parts.Add($"شماره کارت {CardNumber.Trim()}");

            if (!string.IsNullOrWhiteSpace(OwnerName))
                parts.Add($"به نام {OwnerName.Trim()}");

            return string.Join("  ", parts);
        }

        public override string ToString() => ToFormattedTerm();
    }
}
