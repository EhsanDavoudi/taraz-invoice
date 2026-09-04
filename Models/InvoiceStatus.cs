using System;

namespace TarazInvoice.Models
{
    public enum InvoiceStatus
    {
        Draft = 0,       // پیش‌نویس
        Sent = 1,        // ارسال‌شده
        Pending = 2,     // در انتظار تأیید
        Approved = 3,    // تأیید شده
        Canceled = 4     // باطل / لغو شده
    }

    public static class InvoiceStatusExtensions
    {
        public static string ToPersianString(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "پیش‌نویس",
                InvoiceStatus.Sent => "ارسال‌شده",
                InvoiceStatus.Pending => "در انتظار تأیید",
                InvoiceStatus.Approved => "تأیید شده",
                InvoiceStatus.Canceled => "لغو / باطل شده",
                _ => "نامشخص"
            };
        }

        public static string GetColorHex(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "#64748B",     // Slate
                InvoiceStatus.Sent => "#2563EB",      // Blue
                InvoiceStatus.Pending => "#D97706",   // Amber
                InvoiceStatus.Approved => "#059669",  // Emerald
                InvoiceStatus.Canceled => "#DC2626",  // Red
                _ => "#6B7280"
            };
        }

        public static string GetBgColorHex(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "#F1F5F9",
                InvoiceStatus.Sent => "#EFF6FF",
                InvoiceStatus.Pending => "#FFFBEB",
                InvoiceStatus.Approved => "#ECFDF5",
                InvoiceStatus.Canceled => "#FEF2F2",
                _ => "#F3F4F6"
            };
        }
    }
}
