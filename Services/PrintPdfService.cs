using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    public class PrintPdfService
    {
        private readonly StorageService _storage;

        public PrintPdfService(StorageService storage)
        {
            _storage = storage;
        }

        public FlowDocument CreateInvoiceFlowDocument(ProformaInvoice invoice)
        {
            var settings = _storage.GetSettings();

            var doc = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new FontFamily("Segoe UI, Tahoma, Vazirmatn"),
                FontSize = 11.5,
                PagePadding = new Thickness(40),
                PageWidth = 794,
                PageHeight = 1123,
                ColumnWidth = 714
            };

            // سربرگ
            var headerTable = new Table { Margin = new Thickness(0, 0, 0, 18) };
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(200) });
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(314) });
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(200) });

            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow();

            // لوگو / نام شرکت فروشنده
            var sellerCell = new TableCell(new Paragraph(new Run(settings.SellerName))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 58, 138))
            });

            // عنوان وسط
            var titleCell = new TableCell(new Paragraph(new Run("پـیـش‌فـاکـتـور فـروش"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            });

            // اطلاعات شماره و تاریخ فاکتور
            var infoPara = new Paragraph();
            infoPara.Inlines.Add(new Bold(new Run("شماره: ")));
            infoPara.Inlines.Add(new Run(invoice.InvoiceNumber + "\n"));
            infoPara.Inlines.Add(new Bold(new Run("تاریخ: ")));
            infoPara.Inlines.Add(new Run(invoice.IssueDateShamsi + "\n"));
            infoPara.Inlines.Add(new Bold(new Run("اعتبار: ")));
            infoPara.Inlines.Add(new Run(invoice.ValidUntilShamsi));
            infoPara.TextAlignment = TextAlignment.Left;
            var infoCell = new TableCell(infoPara);

            headerRow.Cells.Add(sellerCell);
            headerRow.Cells.Add(titleCell);
            headerRow.Cells.Add(infoCell);
            headerGroup.Rows.Add(headerRow);
            headerTable.RowGroups.Add(headerGroup);
            doc.Blocks.Add(headerTable);

            // کادر اطلاعات خریدار و فروشنده
            var partiesTable = new Table { Margin = new Thickness(0, 0, 0, 15) };
            partiesTable.Columns.Add(new TableColumn { Width = new GridLength(352) });
            partiesTable.Columns.Add(new TableColumn { Width = new GridLength(352) });

            var partiesGroup = new TableRowGroup();
            var partiesRow = new TableRow();

            // مشخصات فروشنده
            var sellerBox = new TableCell
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6)
            };
            sellerBox.Blocks.Add(new Paragraph(new Bold(new Run("مشخصات فروشنده")))
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 4)
            });
            sellerBox.Blocks.Add(new Paragraph(new Run($"نام: {settings.SellerName}\nکد اقتصادی: {settings.SellerEconomicCode} | شناسه ملی: {settings.SellerNationalCode}\nنشانی: {settings.SellerAddress}\nتلفن: {settings.SellerPhone}\nکارشناس صادرکننده: {invoice.ExpertName} ({invoice.ExpertPhone})"))
            {
                Margin = new Thickness(0)
            });

            // مشخصات خریدار
            var buyerBox = new TableCell
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6)
            };
            buyerBox.Blocks.Add(new Paragraph(new Bold(new Run("مشخصات خریدار")))
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 4)
            });
            buyerBox.Blocks.Add(new Paragraph(new Run($"نام شرکت/مشتری: {invoice.CompanyName}\nکد اقتصادی: {invoice.CompanyEconomicCode} | شناسه ملی: {invoice.CompanyNationalCode}\nنشانی: {invoice.CompanyAddress}\nتلفن: {invoice.CompanyPhone}"))
            {
                Margin = new Thickness(0)
            });

            partiesRow.Cells.Add(sellerBox);
            partiesRow.Cells.Add(buyerBox);
            partiesGroup.Rows.Add(partiesRow);
            partiesTable.RowGroups.Add(partiesGroup);
            doc.Blocks.Add(partiesTable);

            // جدول اقلام فاکتور (فقط ستون‌های تمیز مشتری: ردیف، شرح کالا، تعداد، قیمت واحد فروش، مبلغ کل)
            var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 15) };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(45) });  // ردیف
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(329) }); // شرح
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(70) });  // تعداد
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(135) }); // قیمت واحد
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(135) }); // مبلغ کل

            var tableGroup = new TableRowGroup();

            // سطر هدر جدول
            var thRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)) };
            AddHeaderCell(thRow, "ردیف", TextAlignment.Center);
            AddHeaderCell(thRow, "شرح کالا یا خدمات", TextAlignment.Right);
            AddHeaderCell(thRow, "تعداد", TextAlignment.Center);
            AddHeaderCell(thRow, $"قیمت واحد ({settings.CurrencyUnit})", TextAlignment.Center);
            AddHeaderCell(thRow, $"مبلغ کل ({settings.CurrencyUnit})", TextAlignment.Center);
            tableGroup.Rows.Add(thRow);

            // سطرهای اقلام
            int idx = 0;
            foreach (var item in invoice.Items)
            {
                var tr = new TableRow
                {
                    Background = idx % 2 == 1 ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : Brushes.White
                };

                AddDataCell(tr, item.RowNumber.ToString(), TextAlignment.Center);
                AddDataCell(tr, item.ItemName, TextAlignment.Right);
                AddDataCell(tr, item.Quantity.ToString("G29"), TextAlignment.Center);
                AddDataCell(tr, $"{item.FinalUnitPrice:N0}", TextAlignment.Center);
                AddDataCell(tr, $"{item.TotalPrice:N0}", TextAlignment.Center);

                tableGroup.Rows.Add(tr);
                idx++;
            }

            // سطر جمع نهایی فاکتور
            var totalRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(254, 240, 138)) };
            var totalLabelCell = new TableCell(new Paragraph(new Bold(new Run("جمع کل قابل پرداخت:"))) { TextAlignment = TextAlignment.Left, Margin = new Thickness(6) })
            {
                ColumnSpan = 4,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1)
            };
            var totalValueCell = new TableCell(new Paragraph(new Bold(new Run($"{invoice.GrandTotal:N0} {settings.CurrencyUnit}"))) { TextAlignment = TextAlignment.Center, Margin = new Thickness(6) })
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1)
            };

            totalRow.Cells.Add(totalLabelCell);
            totalRow.Cells.Add(totalValueCell);
            tableGroup.Rows.Add(totalRow);

            itemsTable.RowGroups.Add(tableGroup);
            doc.Blocks.Add(itemsTable);

            // مبلغ به حروف
            var wordsPara = new Paragraph(new Bold(new Run($"مبلغ کل به حروف: {JalaliDateHelper.NumberToWords((long)invoice.GrandTotal)} {settings.CurrencyUnit}")))
            {
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };
            doc.Blocks.Add(wordsPara);

            // شرایط و توضیحات
            var termsSection = new Section();
            termsSection.Blocks.Add(new Paragraph(new Bold(new Run("شرایط و توضیحات پیش‌فاکتور:"))) { Margin = new Thickness(0, 0, 0, 4) });
            termsSection.Blocks.Add(new Paragraph(new Run(invoice.Notes)) { Margin = new Thickness(0, 0, 0, 30) });
            doc.Blocks.Add(termsSection);

            // بخش امضا
            var signTable = new Table();
            signTable.Columns.Add(new TableColumn { Width = new GridLength(357) });
            signTable.Columns.Add(new TableColumn { Width = new GridLength(357) });
            var signGroup = new TableRowGroup();
            var signRow = new TableRow();

            signRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("مهر و امضای خریدار:\n\n\n\n"))) { TextAlignment = TextAlignment.Center }));
            signRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("مهر و امضای فروشنده:\n\n\n\n"))) { TextAlignment = TextAlignment.Center }));

            signGroup.Rows.Add(signRow);
            signTable.RowGroups.Add(signGroup);
            doc.Blocks.Add(signTable);

            return doc;
        }

        private void AddHeaderCell(TableRow row, string text, TextAlignment align)
        {
            var p = new Paragraph(new Bold(new Run(text)))
            {
                Foreground = Brushes.White,
                TextAlignment = align,
                Margin = new Thickness(6, 8, 6, 8)
            };
            var cell = new TableCell(p)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                BorderThickness = new Thickness(1)
            };
            row.Cells.Add(cell);
        }

        private void AddDataCell(TableRow row, string text, TextAlignment align)
        {
            var p = new Paragraph(new Run(text))
            {
                TextAlignment = align,
                Margin = new Thickness(6, 6, 6, 6)
            };
            var cell = new TableCell(p)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1)
            };
            row.Cells.Add(cell);
        }

        public void PrintOrExportToPdf(ProformaInvoice invoice)
        {
            var doc = CreateInvoiceFlowDocument(invoice);
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() == true)
            {
                IDocumentPaginatorSource idpSource = doc;
                printDialog.PrintDocument(idpSource.DocumentPaginator, $"پیش‌فاکتور_{invoice.InvoiceNumber}");
            }
        }
    }
}
