using System;

namespace TarazInvoice.Models
{
    public enum InvoiceStatus
    {
        Draft = 0,          // پیش‌نویس
        PreparingGoods = 1, // تهیه کالا
        Sent = 2,           // ارسال شده
        NotSent = 3,        // ارسال نشده
        Paid = 4,           // هزینه واریز شده
        Unpaid = 5,         // واریز نشده
        Approved = 6,       // تأیید شده
        Pending = 7,        // در انتظار تأیید
        Canceled = 8        // لغو / باطل شده
    }

    public static class InvoiceStatusExtensions
    {
        public static string ToPersianString(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "پیش‌نویس",
                InvoiceStatus.PreparingGoods => "تهیه کالا",
                InvoiceStatus.Sent => "ارسال شده",
                InvoiceStatus.NotSent => "ارسال نشده",
                InvoiceStatus.Paid => "هزینه واریز شده",
                InvoiceStatus.Unpaid => "واریز نشده",
                InvoiceStatus.Approved => "تأیید شده",
                InvoiceStatus.Pending => "در انتظار تأیید",
                InvoiceStatus.Canceled => "لغو / باطل شده",
                _ => "نامشخص"
            };
        }

        public static string GetColorHex(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "#64748B",          // Slate
                InvoiceStatus.PreparingGoods => "#7C3AED", // Purple
                InvoiceStatus.Sent => "#2563EB",           // Blue
                InvoiceStatus.NotSent => "#EA580C",        // Orange
                InvoiceStatus.Paid => "#059669",           // Emerald Green
                InvoiceStatus.Unpaid => "#E11D48",         // Rose Red
                InvoiceStatus.Approved => "#0284C7",       // Sky Blue / Approved
                InvoiceStatus.Pending => "#D97706",        // Amber
                InvoiceStatus.Canceled => "#DC2626",       // Crimson Red
                _ => "#6B7280"
            };
        }

        public static string GetBgColorHex(this InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "#F1F5F9",
                InvoiceStatus.PreparingGoods => "#F5F3FF",
                InvoiceStatus.Sent => "#EFF6FF",
                InvoiceStatus.NotSent => "#FFF7ED",
                InvoiceStatus.Paid => "#ECFDF5",
                InvoiceStatus.Unpaid => "#FFF1F2",
                InvoiceStatus.Approved => "#F0F9FF",
                InvoiceStatus.Pending => "#FFFBEB",
                InvoiceStatus.Canceled => "#FEF2F2",
                _ => "#F3F4F6"
            };
        }
    }
}
