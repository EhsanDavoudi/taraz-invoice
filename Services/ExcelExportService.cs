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
            if (!string.IsNullOrWhiteSpace(customTemplatePath) && File.Exists(customTemplatePath))
            {
                ExportUsingTemplate(invoice, customTemplatePath, outputPath, settings);
            }
            else
            {
                ExportDefaultFormat(invoice, outputPath, settings);
            }
        }

        private void ExportUsingTemplate(ProformaInvoice invoice, string templatePath, string outputPath, AppSettings settings)
        {
            using var fs = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(fs);
            var worksheet = workbook.Worksheets.FirstOrDefault() ?? workbook.Worksheet(1);
            worksheet.RightToLeft = true;

            // جایگزینی هوشمند تگ‌های متنی در کل شیت اول
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
            ReplacePlaceholder(worksheet, "{{SUBTOTAL}}", $"{invoice.TotalRawAmount:N0} ریال");
            ReplacePlaceholder(worksheet, "{{TOTAL_DISCOUNT}}", $"{invoice.TotalDiscount:N0} ریال");
            ReplacePlaceholder(worksheet, "{{TOTAL_PROFIT}}", $"{invoice.TotalProfit:N0} ریال");
            ReplacePlaceholder(worksheet, "{{GRAND_TOTAL}}", $"{invoice.GrandTotal:N0} ریال");
            ReplacePlaceholder(worksheet, "{{مبلغ_کل}}", $"{invoice.GrandTotal:N0} ریال");
            ReplacePlaceholder(worksheet, "{{GRAND_TOTAL_WORDS}}", JalaliDateHelper.NumberToWords((long)invoice.GrandTotal) + " ریال");
            ReplacePlaceholder(worksheet, "{{NOTES}}", invoice.Notes);

            int startRow = 8;
            int colRow = 1;
            int colName = 2;
            int colQty = 3;
            int colPrice = 4;
            int colTotal = 5;

            var itemsCell = worksheet.Search("{{ITEMS}}").FirstOrDefault() ?? worksheet.Search("{{اقلام}}").FirstOrDefault();
            if (itemsCell != null)
            {
                startRow = itemsCell.Address.RowNumber;
                int tagCol = itemsCell.Address.ColumnNumber;
                itemsCell.Value = "";
                colRow = tagCol;
                colName = tagCol + 1;
                colQty = tagCol + 2;
                colPrice = tagCol + 3;
                colTotal = tagCol + 4;
            }

            int currentRow = startRow;
            var patternRow = worksheet.Row(startRow);
            double patternHeight = patternRow.Height > 0 ? patternRow.Height : 22;

            for (int i = 0; i < invoice.Items.Count; i++)
            {
                var item = invoice.Items[i];
                if (i > 0)
                {
                    worksheet.Row(currentRow).InsertRowsBelow(1);
                    currentRow++;
                    var newRow = worksheet.Row(currentRow);
                    newRow.Height = patternHeight;

                    for (int c = 1; c <= 10; c++)
                    {
                        newRow.Cell(c).Style = patternRow.Cell(c).Style;
                    }
                }

                worksheet.Cell(currentRow, colRow).Value = item.RowNumber;
                var templateCellName = worksheet.Cell(currentRow, colName);
                templateCellName.Value = item.ItemName;
                templateCellName.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                templateCellName.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;
                templateCellName.Style.Alignment.WrapText = true;
                worksheet.Cell(currentRow, colQty).Value = (double)item.Quantity;
                worksheet.Cell(currentRow, colPrice).Value = (double)item.FinalUnitPrice;
                worksheet.Cell(currentRow, colTotal).Value = (double)item.TotalPrice;

                worksheet.Cell(currentRow, colPrice).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, colTotal).Style.NumberFormat.Format = "#,##0";
            }

            workbook.SaveAs(outputPath);
        }

        public static int ParseColumnToNumber(string? colStr, int defaultCol = 1)
        {
            if (string.IsNullOrWhiteSpace(colStr)) return defaultCol;
            colStr = colStr.Trim().ToUpperInvariant();
            if (int.TryParse(colStr, out int num) && num > 0) return num;

            int result = 0;
            foreach (char c in colStr)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    result = result * 26 + (c - 'A' + 1);
                }
            }
            return result > 0 ? result : defaultCol;
        }

        /// <summary>
        /// خروجی استاندارد و رسمی مصوب سیستم، دقیقاً مطابق نمونه سربرگ و پیش‌فاکتور آریان نیرو گستر اترک
        /// </summary>
        private void ExportDefaultFormat(ProformaInvoice invoice, string outputPath, AppSettings settings)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("پیش‌فاکتور");
            ws.RightToLeft = true; // راست‌چین

            // تنظیم جهت و تراز پیش‌فرض ستون شرح کالا به صورت راست‌چین
            ws.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Column(2).Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;

            // تنظیم فونت و گزینه‌های صفحه
            string fontName = !string.IsNullOrWhiteSpace(settings.TableFontFamily) ? settings.TableFontFamily : "Vazirmatn";
            ws.Style.Font.FontName = fontName;
            ws.Style.Font.FontSize = 10;
            ws.ShowGridLines = true;

            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.PageSetup.Margins.Top = 0.35;
            ws.PageSetup.Margins.Bottom = 0.35;
            ws.PageSetup.Margins.Left = 0.35;
            ws.PageSetup.Margins.Right = 0.35;

            string currUnit = "ریال";

            // ==================== ۱. سربرگ پیش‌فاکتور (مطابق دقیق تصویر) ====================
            // الف) بخش راست: لوگو در ستون ۱ (ردیف) و نام شرکت در ستون ۲ (شرح کالا) با چیدمان وسط‌چین
            string logoPath = !string.IsNullOrWhiteSpace(settings.CompanyLogoPath) && File.Exists(settings.CompanyLogoPath) 
                ? settings.CompanyLogoPath 
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png");

            if (File.Exists(logoPath))
            {
                try
                {
                    var pic = ws.AddPicture(logoPath);
                    pic.MoveTo(ws.Cell(2, 1), 6, 2);
                    pic.WithSize(46, 46);
                }
                catch { }
            }

            ws.Range("B2:B3").Merge();
            var sellerCell = ws.Cell("B2");
            sellerCell.Value = settings.SellerName;
            sellerCell.Style.Font.Bold = true;
            sellerCell.Style.Font.FontSize = (settings.CompanyNameFontSize > 0) ? (int)Math.Min(settings.CompanyNameFontSize, 15) : 15;
            sellerCell.Style.Font.FontName = !string.IsNullOrWhiteSpace(settings.CompanyNameFontFamily) ? settings.CompanyNameFontFamily : fontName;
            sellerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sellerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // ب) بخش وسط: عنوان پیش فاکتور
            ws.Range("C2:D3").Merge();
            var titleCell = ws.Cell("C2");
            titleCell.Value = "پیش فاکتور";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 22;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // ج) بخش چپ: کادر ۴ سطری تاریخ، شماره، پیوست و صفحه (کادر دار)
            string issueDt = string.IsNullOrWhiteSpace(invoice.IssueDateShamsi) ? JalaliDateHelper.GetCurrentShamsiDate() : invoice.IssueDateShamsi;
            string invNum = invoice.InvoiceNumber ?? "";
            string attach = string.IsNullOrWhiteSpace(invoice.Attachment) ? "ندارد" : invoice.Attachment;
            string pageTxt = string.IsNullOrWhiteSpace(invoice.PageNumberText) ? "یک" : invoice.PageNumberText;

            ws.Cell("E1").Value = "تاریخ :";
            ws.Cell("F1").Value = issueDt;
            ws.Cell("E2").Value = "شماره :";
            ws.Cell("F2").Value = invNum;
            ws.Cell("E3").Value = "پیوست :";
            ws.Cell("F3").Value = attach;
            ws.Cell("E4").Value = "صفحه :";
            ws.Cell("F4").Value = pageTxt;

            var metaRange = ws.Range("E1:F4");
            metaRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            metaRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range("E1:E4").Style.Font.Bold = true;
            ws.Range("E1:E4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range("F1:F4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ==================== ۲. مشخصات مشتری و متن مقدمه ====================
            ws.Range("A6:F6").Merge();
            ws.Cell("A6").Value = PrintPdfService.FormatCustomerDisplayName(invoice.CustomerLabelPrefix, invoice.CompanyName, invoice.CompanyPhone);
            ws.Cell("A6").Style.Font.Bold = true;
            ws.Cell("A6").Style.Font.FontSize = 11;
            ws.Cell("A6").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Range("A7:F7").Merge();
            ws.Cell("A7").Value = $"موضوع : {invoice.Subject}";
            ws.Cell("A7").Style.Font.Bold = true;
            ws.Cell("A7").Style.Font.FontSize = 10.5;
            ws.Cell("A7").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            string greeting = !string.IsNullOrWhiteSpace(invoice.GreetingText) ? invoice.GreetingText : (!string.IsNullOrWhiteSpace(settings.GreetingText) ? settings.GreetingText : "احتراما\" قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:");
            ws.Range("A8:F8").Merge();
            ws.Cell("A8").Value = greeting;
            ws.Cell("A8").Style.Font.FontSize = 10.5;
            ws.Cell("A8").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            // ==================== ۳. جدول اقلام کالا با شکست ۲ یا ۳ خطی شرح ====================
            int tableHeaderRow = 9;
            ws.Row(tableHeaderRow).Height = 32;

            ws.Cell(tableHeaderRow, 1).Value = "ردیف";
            ws.Cell(tableHeaderRow, 2).Value = "شرح کالا";
            ws.Cell(tableHeaderRow, 3).Value = "مقدار";
            ws.Cell(tableHeaderRow, 4).Value = "واحد";
            ws.Cell(tableHeaderRow, 5).Value = $"قیمت واحد ({currUnit})";
            ws.Cell(tableHeaderRow, 6).Value = $"جمع کل ({currUnit})";

            var headerRange = ws.Range(tableHeaderRow, 1, tableHeaderRow, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(217, 226, 243); // آبی کم‌رنگ #D9E2F3
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            int rowIdx = tableHeaderRow + 1;
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                var item = invoice.Items[i];
                var row = ws.Row(rowIdx);

                // محاسبه ارتفاع سطر: ۳۲ پیکسل برای یک خط، دو برابر (۶۴) برای دو خط و متناسب برای خطوط بیشتر
                string itemNameStr = item.ItemName ?? "";
                int charCount = itemNameStr.Length;
                int explicitLines = itemNameStr.Split('\n').Length;
                int wrapLines = (int)Math.Ceiling(Math.Max(1, charCount) / 48.0);
                int estimatedLines = Math.Max(explicitLines, wrapLines);

                if (estimatedLines <= 1)
                {
                    row.Height = 32;
                }
                else if (estimatedLines == 2)
                {
                    row.Height = 64;
                }
                else
                {
                    row.Height = estimatedLines * 32;
                }

                // ۱. ابتدا خطوط کادر دور سطر و چینش عمودی ردیف اعمال می‌شود
                var rowRange = ws.Range(rowIdx, 1, rowIdx, 6);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // ۲. ستون ردیف: وسط‌چین
                ws.Cell(rowIdx, 1).Value = item.RowNumber;
                ws.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIdx, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // ۳. ستون شرح کالا: اکیداً راست‌چین (RTL) و تراز به راست با پشتیبانی از شکست خطوط چندخطی
                var cellName = ws.Cell(rowIdx, 2);
                cellName.Value = itemNameStr;
                cellName.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                cellName.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;
                cellName.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cellName.Style.Alignment.WrapText = true;

                // ۴. سایر ستون‌های عددی و واحد: وسط‌چین
                ws.Cell(rowIdx, 3).Value = (double)item.Quantity;
                ws.Cell(rowIdx, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIdx, 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                ws.Cell(rowIdx, 4).Value = string.IsNullOrWhiteSpace(item.Unit) ? "عدد" : item.Unit;
                ws.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIdx, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                ws.Cell(rowIdx, 5).Value = (double)item.FinalUnitPrice;
                ws.Cell(rowIdx, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIdx, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                ws.Cell(rowIdx, 6).Value = (double)item.TotalPrice;
                ws.Cell(rowIdx, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIdx, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                rowIdx++;
            }

            // نوار خاکستری باریک بالای جمع‌های مالی
            ws.Row(rowIdx).Height = 8;
            var grayBar = ws.Range(rowIdx, 1, rowIdx, 6);
            grayBar.Style.Fill.BackgroundColor = XLColor.FromArgb(89, 89, 89);
            rowIdx++;

            // ==================== ۴. سطرهای جمع مالی (مطابق دقیق تصویر) ====================
            // سطر ۱: جمع کل
            ws.Row(rowIdx).Height = 24;
            ws.Range(rowIdx, 1, rowIdx, 4).Merge();
            ws.Cell(rowIdx, 5).Value = $"جمع کل ( {currUnit} )";
            ws.Cell(rowIdx, 5).Style.Font.Bold = true;
            ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(rowIdx, 6).Value = (double)invoice.GrandTotal;
            ws.Cell(rowIdx, 6).Style.Font.Bold = true;
            ws.Cell(rowIdx, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var sumRange1 = ws.Range(rowIdx, 5, rowIdx, 6);
            sumRange1.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sumRange1.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rowIdx++;

            // سطر ۲: ارزش افزوده
            ws.Row(rowIdx).Height = 24;
            ws.Range(rowIdx, 1, rowIdx, 4).Merge();
            ws.Cell(rowIdx, 5).Value = $"ارزش افزوده ( {currUnit} )";
            ws.Cell(rowIdx, 5).Style.Font.Bold = true;
            ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(rowIdx, 6).Value = (double)invoice.VatAmount;
            ws.Cell(rowIdx, 6).Style.Font.Bold = true;
            ws.Cell(rowIdx, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var sumRange2 = ws.Range(rowIdx, 5, rowIdx, 6);
            sumRange2.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sumRange2.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            // هایلایت زرد از سطرهای مالی حذف شد تا مطابق نمونه سند رسمی و درخواست کاربر کاملاً شفاف و تمیز باشد
            rowIdx++;

            // سطر ۳: جمع کل با ارزش افزوده
            ws.Row(rowIdx).Height = 24;
            ws.Range(rowIdx, 1, rowIdx, 4).Merge();
            ws.Cell(rowIdx, 5).Value = $"جمع کل با ارزش افزوده ({currUnit})";
            ws.Cell(rowIdx, 5).Style.Font.Bold = true;
            ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(rowIdx, 6).Value = (double)invoice.GrandTotalWithVat;
            ws.Cell(rowIdx, 6).Style.Font.Bold = true;
            ws.Cell(rowIdx, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var sumRange3 = ws.Range(rowIdx, 5, rowIdx, 6);
            sumRange3.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sumRange3.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rowIdx++;

            rowIdx++; // فاصله خالی

            // ==================== ۵. جدول توضیحات و شرایط فروش (مطابق دقیق تصویر) ====================
            // هدر توضیحات در سمت راست جدول (بالای ستون لوزی و توضیحات)
            ws.Row(rowIdx).Height = 22;
            ws.Range(rowIdx, 1, rowIdx, 2).Merge();
            var termsHeader = ws.Cell(rowIdx, 1);
            termsHeader.Value = "توضیحات :";
            termsHeader.Style.Font.Bold = true;
            termsHeader.Style.Fill.BackgroundColor = XLColor.FromArgb(217, 226, 243);
            var termsHeaderRange = ws.Range(rowIdx, 1, rowIdx, 2);
            termsHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            termsHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            termsHeader.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rowIdx++;

            void AddExcelTermRow(string text, bool isHighlighted)
            {
                ws.Row(rowIdx).Height = 22;
                var bulletCell = ws.Cell(rowIdx, 1);
                bulletCell.Value = "◆"; // علامت لوزی دقیق
                bulletCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                bulletCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                bulletCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                var textRange = ws.Range(rowIdx, 2, rowIdx, 6);
                textRange.Merge();
                var cell = ws.Cell(rowIdx, 2);
                cell.Value = text;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                textRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                var fullRow = ws.Range(rowIdx, 1, rowIdx, 6);
                fullRow.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                fullRow.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                if (isHighlighted)
                {
                    fullRow.Style.Fill.BackgroundColor = XLColor.FromArgb(254, 240, 138); // هایلایت زرد استاندارد
                    fullRow.Style.Font.Bold = true;
                }
                rowIdx++;
            }

            // ۱. اطلاعات حساب بانکی (پشتیبانی از چندین حساب، شماره شبا و شماره کارت)
            var bankAccounts = (invoice.BankAccounts != null && invoice.BankAccounts.Count > 0)
                ? invoice.BankAccounts
                : settings.BankAccounts;

            if (bankAccounts != null && bankAccounts.Count > 0)
            {
                foreach (var acc in bankAccounts)
                {
                    string info = acc.ToFormattedTerm();
                    if (!string.IsNullOrWhiteSpace(info))
                    {
                        AddExcelTermRow(info, false);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(settings.BankAccountNumber) || !string.IsNullOrWhiteSpace(settings.BankCardNumber))
            {
                string bankInfo = $"شماره حساب {settings.BankAccountNumber} شماره کارت {settings.BankCardNumber}";
                AddExcelTermRow(bankInfo, false);
            }

            // ۲. اعتبار پیش‌فاکتور (با هایلایت زرد)
            string validInfo = !string.IsNullOrWhiteSpace(invoice.ValidityText) ? invoice.ValidityText : (!string.IsNullOrWhiteSpace(settings.DefaultValidityText) ? settings.DefaultValidityText : "اعتبار این پیش فاکتورتا پایان روز می باشد.");
            AddExcelTermRow(validInfo, invoice.HighlightValidity);

            // ۳. نوع تسویه
            string pTerms = !string.IsNullOrWhiteSpace(invoice.PaymentTerms) ? invoice.PaymentTerms : settings.PaymentTerms;
            if (!string.IsNullOrWhiteSpace(pTerms))
            {
                AddExcelTermRow($"نوع تسویه: {pTerms}", invoice.HighlightPaymentTerms);
            }

            // ۴. توضیحات سفارشی فاکتور (با هایلایت زرد)
            string notes = !string.IsNullOrWhiteSpace(invoice.Notes) ? invoice.Notes : settings.DefaultTerms;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                AddExcelTermRow($"توضیحات: {notes}", invoice.HighlightNotes);
            }

            // ۵. زمان تحویل (با هایلایت زرد)
            string dDays = !string.IsNullOrWhiteSpace(invoice.DeliveryDays) ? invoice.DeliveryDays : settings.DeliveryDays;
            if (!string.IsNullOrWhiteSpace(dDays))
            {
                AddExcelTermRow($"زمان تحویل: {dDays}", invoice.HighlightDeliveryDays);
            }

            // ۶. محل تحویل کالا
            string dLoc = !string.IsNullOrWhiteSpace(invoice.DeliveryLocation) ? invoice.DeliveryLocation : settings.DeliveryLocation;
            if (!string.IsNullOrWhiteSpace(dLoc))
            {
                AddExcelTermRow($"محل تحویل کالا: {dLoc}", invoice.HighlightDeliveryLocation);
            }

            rowIdx += 2;

            // ==================== ۶. امضا، مهر و مسئول (مطابق تصویر) ====================
            // سمت چپ (ستون‌های ۵ و ۶ در راست‌چین): کارشناسی خرید / فروش
            string expRole = !string.IsNullOrWhiteSpace(invoice.ExpertRoleLabel) ? invoice.ExpertRoleLabel : (!string.IsNullOrWhiteSpace(settings.ExpertRoleLabel) ? settings.ExpertRoleLabel : "کارشناسی خرید:");
            if (!string.IsNullOrWhiteSpace(invoice.ExpertName) && invoice.ExpertName != "(بدون کارشناس)")
            {
                ws.Range(rowIdx, 5, rowIdx, 6).Merge();
                ws.Cell(rowIdx, 5).Value = $"{expRole} {invoice.ExpertName}";
                ws.Cell(rowIdx, 5).Style.Font.FontSize = 10;
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(rowIdx, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // سمت راست/وسط (ستون‌های ۲ و ۳): نام مسئول امضا و مهر
            string signatory = !string.IsNullOrWhiteSpace(invoice.SignatoryName) ? invoice.SignatoryName : (!string.IsNullOrWhiteSpace(settings.SignatoryName) ? settings.SignatoryName : "محمد احمدی پور هدش");
            ws.Range(rowIdx, 2, rowIdx, 3).Merge();
            ws.Cell(rowIdx, 2).Value = signatory;
            ws.Cell(rowIdx, 2).Style.Font.Bold = true;
            ws.Cell(rowIdx, 2).Style.Font.FontSize = 11;
            ws.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(rowIdx, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // تلاش برای درج تصویر مهر در صورت وجود
            if (!string.IsNullOrWhiteSpace(settings.CompanyStampPath) && File.Exists(settings.CompanyStampPath))
            {
                try
                {
                    var stampPic = ws.AddPicture(settings.CompanyStampPath);
                    stampPic.MoveTo(ws.Cell(rowIdx - 2, 3), 5, 5);
                    stampPic.WithSize(90, 65);
                }
                catch { }
            }

            rowIdx += 2;

            // ==================== ۷. خط‌چین برش و آدرس دفتر در انتهای برگه ====================
            var dashedRow = ws.Range(rowIdx, 1, rowIdx, 6);
            dashedRow.Style.Border.BottomBorder = XLBorderStyleValues.Dashed;
            dashedRow.Style.Border.BottomBorderColor = XLColor.FromArgb(100, 116, 139);
            rowIdx++;

            ws.Row(rowIdx).Height = 22;
            ws.Range(rowIdx, 1, rowIdx, 6).Merge();
            var footerCell = ws.Cell(rowIdx, 1);
            footerCell.Value = $"آدرس دفتر: {settings.SellerAddress}    تلفن: {settings.SellerPhone}";
            footerCell.Style.Font.FontSize = 10;
            footerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            footerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // تنظیم عرض ستون‌ها متناسب با نمونه
            ws.Column(1).Width = 7;
            ws.Column(2).Width = 52;
            ws.Column(3).Width = 9;
            ws.Column(4).Width = 9;
            ws.Column(5).Width = 18;
            ws.Column(6).Width = 20;

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
            ws.Cell("A5").Value = "کارشناس خرید: {{EXPERT_NAME}}";
            ws.Cell("D5").Value = "تلفن کارشناس: {{EXPERT_PHONE}}";

            ws.Range("A3:E5").Style.Font.Bold = true;

            // جدول اقلام با تگ {{ITEMS}}
            int headerRow = 7;
            ws.Cell(headerRow, 1).Value = "ردیف";
            ws.Cell(headerRow, 2).Value = "شرح کالا یا خدمات";
            ws.Cell(headerRow, 3).Value = "تعداد";
            ws.Cell(headerRow, 4).Value = "قیمت واحد فروش (ریال)";
            ws.Cell(headerRow, 5).Value = "مبلغ کل (ریال)";

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

        public void ExportReport(List<ProformaInvoice> invoices, string outputPath, string filterSummary)
        {
            var settings = _storage.GetSettings();
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("گزارش پیش‌فاکتورها");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tahoma";
            ws.Style.Font.FontSize = 10;

            // سربرگ گزارش
            ws.Range("A1:G1").Merge();
            var title = ws.Cell("A1");
            title.Value = $"گزارش تحلیلی پیش‌فاکتورهای فروش - {settings.SellerName}";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 41, 59);
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 32;

            ws.Range("A2:G2").Merge();
            var sub = ws.Cell("A2");
            sub.Value = $"تاریخ گزارش: {JalaliDateHelper.GetCurrentShamsiDate()} | فیلتر اعمال‌شده: {filterSummary}";
            sub.Style.Font.Italic = true;
            sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sub.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);

            // جدول
            int headerRow = 4;
            ws.Row(headerRow).Height = 24;
            ws.Cell(headerRow, 1).Value = "ردیف";
            ws.Cell(headerRow, 2).Value = "شماره پیش‌فاکتور";
            ws.Cell(headerRow, 3).Value = "تاریخ صدور";
            ws.Cell(headerRow, 4).Value = "نام شرکت خریدار";
            ws.Cell(headerRow, 5).Value = "کارشناس صادرکننده";
            ws.Cell(headerRow, 6).Value = "مبلغ کل (ریال)";
            ws.Cell(headerRow, 7).Value = "وضعیت";

            var hRange = ws.Range(headerRow, 1, headerRow, 7);
            hRange.Style.Font.Bold = true;
            hRange.Style.Fill.BackgroundColor = XLColor.FromArgb(51, 65, 85);
            hRange.Style.Font.FontColor = XLColor.White;
            hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int r = headerRow + 1;
            int idx = 1;
            foreach (var inv in invoices)
            {
                ws.Cell(r, 1).Value = idx;
                ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 2).Value = inv.InvoiceNumber;
                ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 3).Value = inv.IssueDateShamsi;
                ws.Cell(r, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 4).Value = inv.CompanyName;
                ws.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(r, 4).Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;

                ws.Cell(r, 5).Value = inv.ExpertName;
                ws.Cell(r, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 6).Value = (double)inv.GrandTotal;
                ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 7).Value = inv.Status.ToPersianString();
                ws.Cell(r, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var rowRange = ws.Range(r, 1, r, 7);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                if (idx % 2 == 0)
                {
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromArgb(248, 250, 252);
                }

                r++;
                idx++;
            }

            // سطر مجموع
            ws.Row(r).Height = 24;
            ws.Range(r, 1, r, 5).Merge();
            var sumLabel = ws.Cell(r, 1);
            sumLabel.Value = $"مجموع کل ({invoices.Count} پیش‌فاکتور):";
            sumLabel.Style.Font.Bold = true;
            sumLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            var sumVal = ws.Cell(r, 6);
            sumVal.Value = (double)invoices.Sum(i => i.GrandTotal);
            sumVal.Style.Font.Bold = true;
            sumVal.Style.NumberFormat.Format = "#,##0";
            sumVal.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sumVal.Style.Fill.BackgroundColor = XLColor.FromArgb(254, 240, 138);

            var sumRange = ws.Range(r, 1, r, 7);
            sumRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            sumRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            ws.Column(1).Width = 8;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 32;
            ws.Column(5).Width = 22;
            ws.Column(6).Width = 22;
            ws.Column(7).Width = 18;

            wb.SaveAs(outputPath);
        }

        public void ExportStorage(List<StorageItem> items, string outputPath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("اقلام انبار");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tahoma";
            ws.Style.Font.FontSize = 10;

            ws.Range("A1:F1").Merge();
            var title = ws.Cell("A1");
            title.Value = "فهرست اقلام و موجودی انبار کالاها";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 41, 59);
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 32;

            int headerRow = 3;
            ws.Cell(headerRow, 1).Value = "ردیف";
            ws.Cell(headerRow, 2).Value = "کد کالا";
            ws.Cell(headerRow, 3).Value = "نام کالا / شرح";
            ws.Cell(headerRow, 4).Value = "واحد";
            ws.Cell(headerRow, 5).Value = "قیمت خام واحد";
            ws.Cell(headerRow, 6).Value = "دسته‌بندی";

            var hRange = ws.Range(headerRow, 1, headerRow, 6);
            hRange.Style.Font.Bold = true;
            hRange.Style.Fill.BackgroundColor = XLColor.FromArgb(51, 65, 85);
            hRange.Style.Font.FontColor = XLColor.White;
            hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int r = headerRow + 1;
            int idx = 1;
            foreach (var item in items)
            {
                ws.Cell(r, 1).Value = idx;
                ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 2).Value = item.Code;
                ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 3).Value = item.Name;
                ws.Cell(r, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(r, 3).Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;

                ws.Cell(r, 4).Value = item.Unit;
                ws.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 5).Value = (double)item.DefaultPrice;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(r, 6).Value = item.Category;
                ws.Cell(r, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var rowRange = ws.Range(r, 1, r, 6);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                r++;
                idx++;
            }

            ws.Column(1).Width = 8;
            ws.Column(2).Width = 14;
            ws.Column(3).Width = 40;
            ws.Column(4).Width = 10;
            ws.Column(5).Width = 18;
            ws.Column(6).Width = 14;

            wb.SaveAs(outputPath);
        }
    }
}
