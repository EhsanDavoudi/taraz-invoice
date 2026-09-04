using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    public class ExcelExportService
    {
        private readonly StorageService _storage;

        public ExcelExportService(StorageService storage)
        {
            _storage = storage;
        }

        public void ExportInvoice(ProformaInvoice invoice, string outputPath, string? customTemplatePath = null)
        {
            var settings = _storage.GetSettings();
            string templateToUse = !string.IsNullOrWhiteSpace(customTemplatePath) && File.Exists(customTemplatePath)
                ? customTemplatePath
                : (!string.IsNullOrWhiteSpace(settings.CustomTemplatePath) && File.Exists(settings.CustomTemplatePath)
                    ? settings.CustomTemplatePath
                    : string.Empty);

            if (!string.IsNullOrEmpty(templateToUse))
            {
                ExportUsingTemplate(invoice, templateToUse, outputPath, settings);
            }
            else
            {
                ExportDefaultFormat(invoice, outputPath, settings);
            }
        }

        /// <summary>
        /// پر کردن قالب سفارشی اکسل کاربر با حفظ ۱۰۰٪ استایل، رنگ‌ها، فونت‌ها و فرمول‌ها
        /// </summary>
        private void ExportUsingTemplate(ProformaInvoice invoice, string templatePath, string outputPath, AppSettings settings)
        {
            using var workbook = new XLWorkbook(templatePath);
            var worksheet = workbook.Worksheets.FirstOrDefault() ?? workbook.Worksheets.Add("پیش‌فاکتور");

            // جایگزینی تگ‌های متنی در کل شیت
            ReplacePlaceholder(worksheet, "{{INVOICE_NO}}", invoice.InvoiceNumber);
            ReplacePlaceholder(worksheet, "{{شماره_فاکتور}}", invoice.InvoiceNumber);
            ReplacePlaceholder(worksheet, "{{DATE}}", invoice.IssueDateShamsi);
            ReplacePlaceholder(worksheet, "{{تاریخ}}", invoice.IssueDateShamsi);
            ReplacePlaceholder(worksheet, "{{VALID_UNTIL}}", invoice.ValidUntilShamsi);
            ReplacePlaceholder(worksheet, "{{اعتبار}}", invoice.ValidUntilShamsi);
            ReplacePlaceholder(worksheet, "{{CUSTOMER_NAME}}", invoice.CompanyName);
            ReplacePlaceholder(worksheet, "{{COMPANY_NAME}}", invoice.CompanyName);
            ReplacePlaceholder(worksheet, "{{نام_مشتری}}", invoice.CompanyName);
            ReplacePlaceholder(worksheet, "{{نام_شرکت}}", invoice.CompanyName);
            ReplacePlaceholder(worksheet, "{{CUSTOMER_PHONE}}", invoice.CompanyPhone);
            ReplacePlaceholder(worksheet, "{{تلفن_مشتری}}", invoice.CompanyPhone);
            ReplacePlaceholder(worksheet, "{{CUSTOMER_ADDRESS}}", invoice.CompanyAddress);
            ReplacePlaceholder(worksheet, "{{نشانی_مشتری}}", invoice.CompanyAddress);
            ReplacePlaceholder(worksheet, "{{CUSTOMER_ECO_CODE}}", invoice.CompanyEconomicCode);
            ReplacePlaceholder(worksheet, "{{کد_اقتصادی_مشتری}}", invoice.CompanyEconomicCode);
            ReplacePlaceholder(worksheet, "{{CUSTOMER_NAT_CODE}}", invoice.CompanyNationalCode);
            ReplacePlaceholder(worksheet, "{{شناسه_ملی_مشتری}}", invoice.CompanyNationalCode);
            ReplacePlaceholder(worksheet, "{{EXPERT_NAME}}", invoice.ExpertName);
            ReplacePlaceholder(worksheet, "{{نام_کارشناس}}", invoice.ExpertName);
            ReplacePlaceholder(worksheet, "{{EXPERT_PHONE}}", invoice.ExpertPhone);
            ReplacePlaceholder(worksheet, "{{تلفن_کارشناس}}", invoice.ExpertPhone);
            ReplacePlaceholder(worksheet, "{{SELLER_NAME}}", settings.SellerName);
            ReplacePlaceholder(worksheet, "{{نام_فروشنده}}", settings.SellerName);
            ReplacePlaceholder(worksheet, "{{SELLER_PHONE}}", settings.SellerPhone);
            ReplacePlaceholder(worksheet, "{{تلفن_فروشنده}}", settings.SellerPhone);
            ReplacePlaceholder(worksheet, "{{SELLER_ADDRESS}}", settings.SellerAddress);
            ReplacePlaceholder(worksheet, "{{نشانی_فروشنده}}", settings.SellerAddress);
            ReplacePlaceholder(worksheet, "{{SELLER_ECO_CODE}}", settings.SellerEconomicCode);
            ReplacePlaceholder(worksheet, "{{کد_اقتصادی_فروشنده}}", settings.SellerEconomicCode);
            ReplacePlaceholder(worksheet, "{{SELLER_NAT_CODE}}", settings.SellerNationalCode);
            ReplacePlaceholder(worksheet, "{{شناسه_ملی_فروشنده}}", settings.SellerNationalCode);
            ReplacePlaceholder(worksheet, "{{SUBTOTAL}}", $"{invoice.TotalRawAmount:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{مبلغ_ناخالص}}", $"{invoice.TotalRawAmount:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{TOTAL_DISCOUNT}}", $"{invoice.TotalDiscount:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{مجموع_تخفیف}}", $"{invoice.TotalDiscount:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{GRAND_TOTAL}}", $"{invoice.GrandTotal:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{مبلغ_کل}}", $"{invoice.GrandTotal:N0} {settings.CurrencyUnit}");
            ReplacePlaceholder(worksheet, "{{GRAND_TOTAL_WORDS}}", JalaliDateHelper.NumberToWords((long)invoice.GrandTotal) + " " + settings.CurrencyUnit);
            ReplacePlaceholder(worksheet, "{{مبلغ_به_حروف}}", JalaliDateHelper.NumberToWords((long)invoice.GrandTotal) + " " + settings.CurrencyUnit);
            ReplacePlaceholder(worksheet, "{{NOTES}}", invoice.Notes);
            ReplacePlaceholder(worksheet, "{{شرایط_پرداخت}}", invoice.Notes);

            // یافتن محل درج اقلام در جدول
            // بررسی می‌کنیم آیا سلولی با مقدار {{ITEMS}} یا {{اقلام}} وجود دارد؟
            int startRow = settings.TemplateItemStartRow;
            int startCol = 1;

            var itemsCell = worksheet.Search("{{ITEMS}}").FirstOrDefault() ?? worksheet.Search("{{اقلام}}").FirstOrDefault();
            if (itemsCell != null)
            {
                startRow = itemsCell.Address.RowNumber;
                startCol = itemsCell.Address.ColumnNumber;
                itemsCell.Value = ""; // پاک کردن متن تگ
            }

            // درج سطرهای اقلام (فقط شرح، تعداد، قیمت نهایی فروش و جمع سطر)
            int currentRow = startRow;
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                var item = invoice.Items[i];
                if (i > 0)
                {
                    worksheet.Row(currentRow).InsertRowsBelow(1);
                    currentRow++;
                }

                worksheet.Cell(currentRow, startCol).Value = item.RowNumber;
                worksheet.Cell(currentRow, startCol + 1).Value = item.ItemName;
                worksheet.Cell(currentRow, startCol + 2).Value = (double)item.Quantity;
                worksheet.Cell(currentRow, startCol + 3).Value = (double)item.FinalUnitPrice;
                worksheet.Cell(currentRow, startCol + 4).Value = (double)item.TotalPrice;

                // فرمت عددی با جداکننده سه رقمی
                worksheet.Cell(currentRow, startCol + 3).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, startCol + 4).Style.NumberFormat.Format = "#,##0";
            }

            workbook.SaveAs(outputPath);
        }

        /// <summary>
        /// خروجی استاندارد و شکیل پیش‌فرض با راست‌چین، استایل اداری و فونت زیبا
        /// </summary>
        private void ExportDefaultFormat(ProformaInvoice invoice, string outputPath, AppSettings settings)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("پیش‌فاکتور");
            ws.RightToLeft = true; // راست‌چین

            // تنظیم فونت کلی
            ws.Style.Font.FontName = "Tahoma";
            ws.Style.Font.FontSize = 10;

            // سربرگ عنوان اصلی
            ws.Range("A1:E1").Merge();
            var titleCell = ws.Cell("A1");
            titleCell.Value = "پـیـش‌فـاکـتـور فـروش (Proforma Invoice)";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 15;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 58, 138); // آبی تیره
            titleCell.Style.Font.FontColor = XLColor.White;
            ws.Row(1).Height = 35;

            // ردیف اطلاعات پیش‌فاکتور (شماره، تاریخ، کارشناس)
            ws.Cell("A3").Value = "شماره پیش‌فاکتور:";
            ws.Cell("A3").Style.Font.Bold = true;
            ws.Cell("B3").Value = invoice.InvoiceNumber;
            ws.Cell("B3").Style.Font.Bold = true;
            ws.Cell("B3").Style.Font.FontColor = XLColor.FromHtml("#1E40AF");

            ws.Cell("D3").Value = "تاریخ صدور:";
            ws.Cell("D3").Style.Font.Bold = true;
            ws.Cell("E3").Value = invoice.IssueDateShamsi;

            ws.Cell("A4").Value = "کارشناس صادرکننده:";
            ws.Cell("A4").Style.Font.Bold = true;
            ws.Cell("B4").Value = invoice.ExpertName + (string.IsNullOrEmpty(invoice.ExpertPhone) ? "" : $" ({invoice.ExpertPhone})");

            ws.Cell("D4").Value = "مهلت اعتبار:";
            ws.Cell("D4").Style.Font.Bold = true;
            ws.Cell("E4").Value = invoice.ValidUntilShamsi;

            // کادر اطلاعات فروشنده
            ws.Range("A6:E6").Merge();
            var sellerHeader = ws.Cell("A6");
            sellerHeader.Value = "مشخصات فروشنده";
            sellerHeader.Style.Font.Bold = true;
            sellerHeader.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);

            ws.Cell("A7").Value = "نام فروشنده:";
            ws.Cell("B7").Value = settings.SellerName;
            ws.Cell("D7").Value = "شناسه ملی / اقتصادی:";
            ws.Cell("E7").Value = $"{settings.SellerNationalCode} / {settings.SellerEconomicCode}";

            ws.Cell("A8").Value = "نشانی و تلفن:";
            ws.Range("B8:E8").Merge();
            ws.Cell("B8").Value = $"{settings.SellerAddress} - تلفن: {settings.SellerPhone}";

            // کادر اطلاعات خریدار
            ws.Range("A10:E10").Merge();
            var buyerHeader = ws.Cell("A10");
            buyerHeader.Value = "مشخصات خریدار";
            buyerHeader.Style.Font.Bold = true;
            buyerHeader.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);

            ws.Cell("A11").Value = "نام شرکت / شخص:";
            ws.Cell("B11").Value = invoice.CompanyName;
            ws.Cell("D11").Value = "کد اقتصادی / ملی:";
            ws.Cell("E11").Value = $"{invoice.CompanyEconomicCode} / {invoice.CompanyNationalCode}";

            ws.Cell("A12").Value = "نشانی و تلفن خریدار:";
            ws.Range("B12:E12").Merge();
            ws.Cell("B12").Value = $"{invoice.CompanyAddress} - تلفن: {invoice.CompanyPhone}";

            // جدول اقلام (فقط ستون‌های مشتری‌پسند: ردیف، شرح کالا، تعداد، قیمت واحد فروش، مبلغ کل)
            int tableHeaderRow = 14;
            ws.Row(tableHeaderRow).Height = 26;

            ws.Cell(tableHeaderRow, 1).Value = "ردیف";
            ws.Cell(tableHeaderRow, 2).Value = "شرح کالا یا خدمات";
            ws.Cell(tableHeaderRow, 3).Value = "تعداد / مقدار";
            ws.Cell(tableHeaderRow, 4).Value = $"قیمت واحد ({settings.CurrencyUnit})";
            ws.Cell(tableHeaderRow, 5).Value = $"مبلغ کل ({settings.CurrencyUnit})";

            var headerRange = ws.Range(tableHeaderRow, 1, tableHeaderRow, 5);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(51, 65, 85); // Slate 700
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            int rowIdx = tableHeaderRow + 1;
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                var item = invoice.Items[i];
                ws.Row(rowIdx).Height = 22;

                ws.Cell(rowIdx, 1).Value = item.RowNumber;
                ws.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 2).Value = item.ItemName;
                ws.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(rowIdx, 3).Value = (double)item.Quantity;
                ws.Cell(rowIdx, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 4).Value = (double)item.FinalUnitPrice;
                ws.Cell(rowIdx, 4).Style.NumberFormat.Format = "#,##0";
                ws.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 5).Value = (double)item.TotalPrice;
                ws.Cell(rowIdx, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // خطوط دور جدول
                var rowRange = ws.Range(rowIdx, 1, rowIdx, 5);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                if (i % 2 == 1)
                {
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromArgb(248, 250, 252);
                }

                rowIdx++;
            }

            // سطر جمع کل
            ws.Row(rowIdx).Height = 26;
            ws.Range(rowIdx, 1, rowIdx, 4).Merge();
            var totalLabel = ws.Cell(rowIdx, 1);
            totalLabel.Value = "جمع کل نهایی قابل پرداخت:";
            totalLabel.Style.Font.Bold = true;
            totalLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            var totalValue = ws.Cell(rowIdx, 5);
            totalValue.Value = (double)invoice.GrandTotal;
            totalValue.Style.Font.Bold = true;
            totalValue.Style.NumberFormat.Format = "#,##0";
            totalValue.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            totalValue.Style.Fill.BackgroundColor = XLColor.FromArgb(254, 240, 138); // Yellow highlight

            var totalRange = ws.Range(rowIdx, 1, rowIdx, 5);
            totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            totalRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            rowIdx += 2;

            // مبلغ به حروف
            ws.Range(rowIdx, 1, rowIdx, 5).Merge();
            ws.Cell(rowIdx, 1).Value = "مبلغ به حروف: " + JalaliDateHelper.NumberToWords((long)invoice.GrandTotal) + " " + settings.CurrencyUnit;
            ws.Cell(rowIdx, 1).Style.Font.Bold = true;
            ws.Cell(rowIdx, 1).Style.Font.Italic = true;

            rowIdx += 2;

            // شرایط پرداخت و توضیحات
            ws.Range(rowIdx, 1, rowIdx, 5).Merge();
            ws.Cell(rowIdx, 1).Value = "شرایط و توضیحات:\n" + invoice.Notes;
            ws.Cell(rowIdx, 1).Style.Alignment.WrapText = true;
            ws.Row(rowIdx).Height = 45;

            rowIdx += 3;

            // مهر و امضا
            ws.Range(rowIdx, 1, rowIdx, 2).Merge();
            ws.Cell(rowIdx, 1).Value = "مهر و امضای خریدار:";
            ws.Cell(rowIdx, 1).Style.Font.Bold = true;

            ws.Range(rowIdx, 4, rowIdx, 5).Merge();
            ws.Cell(rowIdx, 4).Value = "مهر و امضای فروشنده:";
            ws.Cell(rowIdx, 4).Style.Font.Bold = true;

            // تنظیم عرض ستون‌ها
            ws.Column(1).Width = 8;
            ws.Column(2).Width = 38;
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 22;
            ws.Column(5).Width = 24;

            workbook.SaveAs(outputPath);
        }

        public void GenerateSampleTemplateFile(string targetPath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("قالب پیش‌فاکتور");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tahoma";

            // سربرگ قالب
            ws.Range("A1:E1").Merge();
            var title = ws.Cell("A1");
            title.Value = "قالب پیش‌فاکتور اختصاصی - {{SELLER_NAME}}";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Fill.BackgroundColor = XLColor.FromArgb(15, 23, 42);
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 32;

            ws.Cell("A3").Value = "شماره پیش‌فاکتور: {{INVOICE_NO}}";
            ws.Cell("D3").Value = "تاریخ: {{DATE}}";
            ws.Cell("A4").Value = "خریدار: {{CUSTOMER_NAME}}";
            ws.Cell("D4").Value = "مهلت اعتبار: {{VALID_UNTIL}}";
            ws.Cell("A5").Value = "کارشناس فروش: {{EXPERT_NAME}}";
            ws.Cell("D5").Value = "تلفن کارشناس: {{EXPERT_PHONE}}";

            ws.Range("A3:E5").Style.Font.Bold = true;

            // جدول اقلام با تگ {{ITEMS}}
            int headerRow = 7;
            ws.Cell(headerRow, 1).Value = "ردیف";
            ws.Cell(headerRow, 2).Value = "شرح کالا یا خدمات";
            ws.Cell(headerRow, 3).Value = "تعداد";
            ws.Cell(headerRow, 4).Value = "قیمت واحد فروش (تومان)";
            ws.Cell(headerRow, 5).Value = "مبلغ کل (تومان)";

            var hRange = ws.Range(headerRow, 1, headerRow, 5);
            hRange.Style.Font.Bold = true;
            hRange.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 41, 59);
            hRange.Style.Font.FontColor = XLColor.White;
            hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // سطر شروع اقلام
            int itemRow = 8;
            ws.Cell(itemRow, 1).Value = "{{ITEMS}}";
            var itemRange = ws.Range(itemRow, 1, itemRow, 5);
            itemRange.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);
            itemRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            itemRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // جمع کل و فوتر
            ws.Cell(10, 1).Value = "جمع کل پیش‌فاکتور:";
            ws.Cell(10, 1).Style.Font.Bold = true;
            ws.Cell(10, 5).Value = "{{GRAND_TOTAL}}";
            ws.Cell(10, 5).Style.Font.Bold = true;

            ws.Cell(12, 1).Value = "مبلغ به حروف: {{GRAND_TOTAL_WORDS}}";
            ws.Cell(13, 1).Value = "شرایط: {{NOTES}}";

            ws.Column(1).Width = 8;
            ws.Column(2).Width = 35;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 22;
            ws.Column(5).Width = 24;

            wb.SaveAs(targetPath);
        }

        private void ReplacePlaceholder(IXLWorksheet ws, string placeholder, string replacement)
        {
            var cells = ws.Search(placeholder);
            foreach (var cell in cells)
            {
                if (cell.HasFormula) continue;
                string val = cell.GetString();
                cell.Value = val.Replace(placeholder, replacement ?? string.Empty);
            }
        }
    }
}
