using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    public class PrintPdfService
    {
        private readonly StorageService _storage;

        // ---- Layout constants (tune here) ----
        internal const string DefaultFont = "Vazirmatn, Tahoma, Segoe UI";
        private const double RowH = 32;            // total row height for ONE line (borders included)
        private const double CellBorder = 1;       // px, each side
        private const double ContentW = 734;       // 794 - 2*30 page padding
        private const double HeaderSideW = 290;    // company block width (right)
        private const double HeaderTitleW = 194;   // title block width (center of page)

        // In a RightToLeft FlowDocument, TextAlignment.Right aligns to the right side (start of Persian reading)
        private const TextAlignment RtlStart = TextAlignment.Right;
        private const TextAlignment RtlEnd = TextAlignment.Left;

        public PrintPdfService(StorageService storage)
        {
            _storage = storage;
        }

        public static string FormatPersianRtlText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Select(l => l.Trim());
            return string.Join("\n", lines);
        }

        public static string FormatCustomerDisplayName(string? prefix, string? companyName, string? phone = null)
        {
            string p = prefix?.Trim() ?? "";
            string name = companyName?.Trim() ?? "مشتری محترم";

            if (string.IsNullOrWhiteSpace(p))
            {
                p = "نام مشتری: شرکت محترم";
            }

            if (p.StartsWith("نام مشتری") && !p.StartsWith("نام مشتری:"))
            {
                p = "نام مشتری: " + p.Substring("نام مشتری".Length).TrimStart();
            }

            if ((p.EndsWith("شرکت محترم") || p.EndsWith("شرکت")) && name.StartsWith("شرکت "))
            {
                name = name.Substring(5).TrimStart();
            }

            string full = $"{p.TrimEnd()} {name.TrimStart()}".Trim();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                full += $" (تلفن: {phone.Trim()})";
            }
            return full;
        }

        // ====================================================================
        //  Shared data helpers (used by both the FlowDocument and the PDF builder)
        // ====================================================================

        internal static string ResolveGreeting(ProformaInvoice invoice, AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(invoice.GreetingText)) return invoice.GreetingText;
            if (!string.IsNullOrWhiteSpace(settings.GreetingText)) return settings.GreetingText;
            return "احتراماً، قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام می‌گردد:";
        }

        internal static string ResolveSignatory(ProformaInvoice invoice, AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(invoice.SignatoryName)) return invoice.SignatoryName;
            if (!string.IsNullOrWhiteSpace(settings.SignatoryName)) return settings.SignatoryName;
            return "محمد احمدی پور هدش";
        }

        internal static string ResolveExpertRole(ProformaInvoice invoice, AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(invoice.ExpertRoleLabel)) return invoice.ExpertRoleLabel;
            if (!string.IsNullOrWhiteSpace(settings.ExpertRoleLabel)) return settings.ExpertRoleLabel;
            return "کارشناسی خرید:";
        }

        internal static List<(string Text, bool Highlight)> BuildTerms(ProformaInvoice invoice, AppSettings settings)
        {
            var list = new List<(string, bool)>();

            var bankAccounts = (invoice.BankAccounts != null && invoice.BankAccounts.Count > 0)
                ? invoice.BankAccounts
                : settings.BankAccounts;

            if (bankAccounts != null && bankAccounts.Count > 0)
            {
                foreach (var acc in bankAccounts)
                {
                    string info = acc.ToFormattedTerm();
                    if (!string.IsNullOrWhiteSpace(info)) list.Add((info, false));
                }
            }
            else if (!string.IsNullOrWhiteSpace(settings.BankAccountNumber) || !string.IsNullOrWhiteSpace(settings.BankCardNumber))
            {
                list.Add(($"شماره حساب {settings.BankAccountNumber} شماره کارت {settings.BankCardNumber}", false));
            }

            string validInfo = !string.IsNullOrWhiteSpace(invoice.ValidityText) ? invoice.ValidityText
                : (!string.IsNullOrWhiteSpace(settings.DefaultValidityText) ? settings.DefaultValidityText : "اعتبار این پیش فاکتور تا پایان روز کاری می‌باشد.");
            list.Add((validInfo, invoice.HighlightValidity));

            string deliveryLocation = !string.IsNullOrWhiteSpace(invoice.DeliveryLocation) ? invoice.DeliveryLocation : settings.DeliveryLocation;
            if (!string.IsNullOrWhiteSpace(deliveryLocation))
                list.Add(($"محل تحویل کالا: {deliveryLocation}", invoice.HighlightDeliveryLocation));

            string paymentTerms = !string.IsNullOrWhiteSpace(invoice.PaymentTerms) ? invoice.PaymentTerms : settings.PaymentTerms;
            if (!string.IsNullOrWhiteSpace(paymentTerms))
                list.Add(($"نوع تسویه : {paymentTerms}", invoice.HighlightPaymentTerms));

            string deliveryDays = !string.IsNullOrWhiteSpace(invoice.DeliveryDays) ? invoice.DeliveryDays : settings.DeliveryDays;
            if (!string.IsNullOrWhiteSpace(deliveryDays))
                list.Add(($"زمان تحویل : {deliveryDays}", invoice.HighlightDeliveryDays));

            string notesText = !string.IsNullOrWhiteSpace(invoice.Notes) ? invoice.Notes : settings.DefaultTerms;
            if (!string.IsNullOrWhiteSpace(notesText))
                list.Add(($"توضیحات : {notesText}", invoice.HighlightNotes));

            return list;
        }

        // ====================================================================
        //  FlowDocument helpers
        // ====================================================================

        private static void AddMultilineText(InlineCollection inlines, string? text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) inlines.Add(new LineBreak());
                inlines.Add(new Run(lines[i]));
            }
        }

        private static Paragraph MakePara(string? text, FontFamily font, double size, TextAlignment align,
            Thickness? margin = null, bool bold = false, Brush? fg = null, double lineHeight = 0)
        {
            var p = new Paragraph
            {
                FontFamily = font,
                FontSize = size,
                TextAlignment = align,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = margin ?? new Thickness(0)
            };
            if (bold) p.FontWeight = FontWeights.Bold;
            if (fg != null) p.Foreground = fg;
            if (lineHeight > 0)
            {
                p.LineHeight = lineHeight;
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            if (text != null) AddMultilineText(p.Inlines, text);
            return p;
        }

        private static TableCell MakeCell(Paragraph content, Brush? borderBrush, Brush? bg = null)
        {
            var cell = new TableCell(content)
            {
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = content.TextAlignment,
                BorderBrush = borderBrush,
                BorderThickness = borderBrush == null ? new Thickness(0) : new Thickness(CellBorder)
            };
            if (bg != null) cell.Background = bg;
            return cell;
        }

        /// <summary>One-line bordered cell. Line height = RowH minus the cell borders, so the row is exactly RowH px.</summary>
        private static TableCell LineCell(string text, FontFamily font, double size, TextAlignment align,
            Brush borderBrush, bool bold = false, Brush? bg = null, double hMargin = 4, double? lineH = null)
        {
            var p = MakePara(text, font, size, align, new Thickness(hMargin, 0, hMargin, 0), bold, null, lineH ?? (RowH - 2 * CellBorder));
            return MakeCell(p, borderBrush, bg);
        }

        private static FormattedText MakeFormatted(string text, FontFamily font, double size, bool bold)
        {
            var tf = new Typeface(font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            return new FormattedText(text, CultureInfo.GetCultureInfo("fa-IR"), FlowDirection.RightToLeft,
                tf, size, Brushes.Black, 1.0);
        }

        private static double MeasureWidth(string text, FontFamily font, double size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            try { return MakeFormatted(text, font, size, bold).WidthIncludingTrailingWhitespace; }
            catch { return text.Length * size * 0.6; }
        }

        /// <summary>How many lines the text needs in a column of the given width (real measurement).</summary>
        internal static int CountLines(string text, string fontFamilyName, double fontSize, double maxWidth)
        {
            return CountLines(text, new FontFamily(string.IsNullOrWhiteSpace(fontFamilyName) ? DefaultFont : fontFamilyName), fontSize, maxWidth);
        }

        private static int CountLines(string text, FontFamily font, double fontSize, double maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text)) return 1;
            try
            {
                var ft = MakeFormatted(text.Replace("\r\n", "\n").Replace('\r', '\n'), font, fontSize, false);
                ft.MaxTextWidth = maxWidth;
                ft.LineHeight = RowH;
                return Math.Max(1, (int)Math.Round(ft.Height / RowH));
            }
            catch
            {
                return Math.Max(text.Split('\n').Length, (int)Math.Ceiling(text.Length / 38.0));
            }
        }

        private static BitmapImage? LoadImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ====================================================================
        //  Invoice FlowDocument (preview + printing). The PDF uses InvoicePdfBuilder.
        // ====================================================================

        public FlowDocument CreateInvoiceFlowDocument(ProformaInvoice invoice, AppSettings? overrideSettings = null)
        {
            var settings = overrideSettings ?? _storage.GetSettings();

            var font = new FontFamily(string.IsNullOrWhiteSpace(settings.TableFontFamily) ? DefaultFont : settings.TableFontFamily);
            var companyFont = new FontFamily(string.IsNullOrWhiteSpace(settings.CompanyNameFontFamily) ? DefaultFont : settings.CompanyNameFontFamily);
            double companyFontSize = settings.CompanyNameFontSize > 0 ? settings.CompanyNameFontSize : 18;
            double fs = settings.TableFontSize > 0 ? settings.TableFontSize : 11; // ONE body font size for everything

            var doc = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = font,
                FontSize = fs,
                PagePadding = new Thickness(30, 24, 30, 24),
                PageWidth = 794,
                PageHeight = 1123,
                ColumnWidth = ContentW
            };

            var borderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            var headerBgBrush = new SolidColorBrush(Color.FromRgb(220, 230, 242));
            var summaryBgBrush = Brushes.White;
            var darkText = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var slateText = new SolidColorBrush(Color.FromRgb(30, 41, 59));

            // ==================== 1. Header ====================
            string issueDt = string.IsNullOrWhiteSpace(invoice.IssueDateShamsi) ? JalaliDateHelper.GetCurrentShamsiDate() : invoice.IssueDateShamsi;
            var metas = new[]
            {
                ("تاریخ :", issueDt),
                ("شماره :", invoice.InvoiceNumber ?? ""),
                ("پیوست :", string.IsNullOrWhiteSpace(invoice.Attachment) ? "ندارد" : invoice.Attachment),
                ("صفحه :", string.IsNullOrWhiteSpace(invoice.PageNumberText) ? "یک" : invoice.PageNumberText)
            };

            // Info block is exactly as wide as its content, so it sits flush against the left edge
            double labelW = Math.Ceiling(metas.Max(m => MeasureWidth(m.Item1, font, fs, true))) + 8;
            double valueW = Math.Ceiling(metas.Max(m => MeasureWidth(m.Item2, font, fs, false))) + 6;
            valueW = Math.Min(Math.Max(valueW, 40), 200);
            double infoW = labelW + valueW;
            double spacerW = Math.Max(1, ContentW - HeaderSideW - HeaderTitleW - infoW);

            var headerTable = new Table { Margin = new Thickness(0, 0, 0, 12), CellSpacing = 0, FontFamily = font };
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(HeaderSideW) });
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(HeaderTitleW) });
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(spacerW) });
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(infoW) });

            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow();

            var companyCell = new TableCell { Padding = new Thickness(4) };
            var companyStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var logo = LoadImage(settings.CompanyLogoPath);
            if (logo != null)
            {
                companyStack.Children.Add(new Image
                {
                    Source = logo,
                    Width = settings.LogoWidth > 0 ? settings.LogoWidth : 68,
                    Height = settings.LogoHeight > 0 ? settings.LogoHeight : 68,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 10, 0)
                });
            }

            companyStack.Children.Add(new TextBlock
            {
                Text = settings.SellerName,
                FontFamily = companyFont,
                FontSize = companyFontSize,
                FontWeight = FontWeights.Normal,
                Foreground = slateText,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = HeaderSideW - 8 - (logo != null ? 78 : 0)
            });
            companyCell.Blocks.Add(new BlockUIContainer(companyStack));

            var titleCell = new TableCell(MakePara("پیش فاکتور", font, 32, TextAlignment.Center,
                new Thickness(0, 8, 0, 0), bold: true, fg: darkText));

            var spacerCell = new TableCell(MakePara("", font, 1, TextAlignment.Center));

            var infoCell = new TableCell { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
            var infoTable = new Table { CellSpacing = 0, Margin = new Thickness(0), FontFamily = font };
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(labelW) });
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(valueW) });
            var infoGroup = new TableRowGroup();
            foreach (var (label, val) in metas)
            {
                var r = new TableRow();
                r.Cells.Add(new TableCell(MakePara(label, font, fs, RtlStart, new Thickness(0, 2, 0, 2), bold: true)) { BorderThickness = new Thickness(0) });
                r.Cells.Add(new TableCell(MakePara(val, font, fs, RtlStart, new Thickness(0, 2, 0, 2))) { BorderThickness = new Thickness(0) });
                infoGroup.Rows.Add(r);
            }
            infoTable.RowGroups.Add(infoGroup);
            infoCell.Blocks.Add(infoTable);

            headerRow.Cells.Add(companyCell);
            headerRow.Cells.Add(titleCell);
            headerRow.Cells.Add(spacerCell);
            headerRow.Cells.Add(infoCell);
            headerGroup.Rows.Add(headerRow);
            headerTable.RowGroups.Add(headerGroup);
            doc.Blocks.Add(headerTable);

            // ==================== 2. Customer + intro ====================
            var introSection = new Section { Margin = new Thickness(0, 4, 0, 8), FontFamily = font };

            string customerLine = FormatCustomerDisplayName(invoice.CustomerLabelPrefix, invoice.CompanyName, invoice.CompanyPhone);
            introSection.Blocks.Add(MakePara(customerLine, font, fs, RtlStart, new Thickness(0, 0, 0, 3), bold: true));

            var subjectPara = MakePara(null, font, fs, RtlStart, new Thickness(0, 0, 0, 3));
            subjectPara.Inlines.Add(new Bold(new Run("موضوع: ")));
            if (!string.IsNullOrWhiteSpace(invoice.Subject))
            {
                subjectPara.Inlines.Add(new Run(invoice.Subject));
            }
            introSection.Blocks.Add(subjectPara);

            introSection.Blocks.Add(MakePara(ResolveGreeting(invoice, settings), font, fs, RtlStart, new Thickness(0, 0, 0, 0), fg: slateText));
            doc.Blocks.Add(introSection);

            string currUnit = "ریال";

            // ==================== 3. Items table ====================
            const double nameColW = 334;
            var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0), FlowDirection = FlowDirection.RightToLeft, FontFamily = font };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(35) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(nameColW) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(50) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(55) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(130) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(130) });

            var tableGroup = new TableRowGroup();

            double headerFs = fs > 9 ? fs - 1.5 : (fs * 0.88);
            const double HeaderRowH = 24;
            double headerInner = HeaderRowH - 2 * CellBorder;
            var thRow = new TableRow { Background = headerBgBrush };
            thRow.Cells.Add(LineCell("ردیف", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            thRow.Cells.Add(LineCell("شرح کالا", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            thRow.Cells.Add(LineCell("مقدار", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            thRow.Cells.Add(LineCell("واحد", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            thRow.Cells.Add(LineCell($"قیمت واحد ({currUnit})", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            thRow.Cells.Add(LineCell($"جمع کل ({currUnit})", font, headerFs, TextAlignment.Center, borderBrush, bold: true, lineH: headerInner));
            tableGroup.Rows.Add(thRow);

            int rowIdx = 1;
            foreach (var item in invoice.Items)
            {
                var tr = new TableRow();
                string itemName = (item.ItemName ?? "").Trim();

                // available width = column - paragraph margins (8+8) - borders - safety
                int lines = CountLines(itemName, font, fs, nameColW - 16 - 2 * CellBorder - 4);

                // Row pitch is EXACTLY RowH * lines (32, 64, 96 ...) borders included:
                double inner = RowH * lines - 2 * CellBorder;      // content height inside the borders
                double oneLine = RowH - 2 * CellBorder;            // line height of single-line cells
                double vPad = (inner - oneLine) / 2.0;             // vertically centers single-line cells

                TableCell NumCell(string text)
                {
                    var p = MakePara(text, font, fs, TextAlignment.Center,
                        new Thickness(4, vPad, 4, vPad), false, null, oneLine);
                    return MakeCell(p, borderBrush);
                }

                tr.Cells.Add(NumCell(rowIdx.ToString()));

                var namePara = MakePara(itemName, font, fs, RtlStart,
                    new Thickness(8, 0, 8, 0), false, null, inner / lines);
                tr.Cells.Add(MakeCell(namePara, borderBrush));

                tr.Cells.Add(NumCell(item.Quantity.ToString("G29")));
                tr.Cells.Add(NumCell(string.IsNullOrWhiteSpace(item.Unit) ? "عدد" : item.Unit));
                tr.Cells.Add(NumCell($"{item.FinalUnitPrice:N0}"));
                tr.Cells.Add(NumCell($"{item.TotalPrice:N0}"));

                tableGroup.Rows.Add(tr);
                rowIdx++;
            }

            itemsTable.RowGroups.Add(tableGroup);
            doc.Blocks.Add(itemsTable);

            // نوار خاکستری باریک بالای جمع‌های مالی
            var grayBarTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 0) };
            grayBarTable.Columns.Add(new TableColumn { Width = new GridLength(ContentW) });
            var grayGroup = new TableRowGroup();
            var grayRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(89, 89, 89)) };
            var grayCell = new TableCell(new Paragraph(new Run("")) { FontSize = 4, LineHeight = 10, Margin = new Thickness(0) }) { BorderThickness = new Thickness(1), BorderBrush = borderBrush };
            grayRow.Cells.Add(grayCell);
            grayGroup.Rows.Add(grayRow);
            grayBarTable.RowGroups.Add(grayGroup);
            doc.Blocks.Add(grayBarTable);

            // ==================== 4. Totals: full width, same font size ====================
            var summaryTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14), FontFamily = font };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(ContentW - 130) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(130) });

            var summaryGroup = new TableRowGroup();
            void AddSumRow(string label, decimal value)
            {
                var r = new TableRow();
                r.Cells.Add(LineCell(label, font, fs, RtlStart, borderBrush, bold: true, bg: summaryBgBrush, hMargin: 8));
                r.Cells.Add(LineCell($"{value:N0}", font, fs, TextAlignment.Center, borderBrush, bold: true));
                summaryGroup.Rows.Add(r);
            }
            AddSumRow($"جمع کل ( {currUnit} )", invoice.GrandTotal);
            AddSumRow($"ارزش افزوده ( {currUnit} )", invoice.VatAmount);
            AddSumRow($"جمع کل با ارزش افزوده ({currUnit})", invoice.GrandTotalWithVat);

            summaryTable.RowGroups.Add(summaryGroup);
            doc.Blocks.Add(summaryTable);

            // ==================== 5. Terms: ONE table (no gap / white line under "توضیحات") ====================
            var termsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14), FontFamily = font };
            termsTable.Columns.Add(new TableColumn { Width = new GridLength(24) });
            termsTable.Columns.Add(new TableColumn { Width = new GridLength(136) });
            termsTable.Columns.Add(new TableColumn { Width = new GridLength(ContentW - 160) });

            var termsGroup = new TableRowGroup();

            var thr = new TableRow();
            thr.Cells.Add(new TableCell(MakePara("توضیحات :", font, fs, RtlStart, new Thickness(8, 4, 8, 4), bold: true))
            {
                ColumnSpan = 2,
                Background = headerBgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            });
            // closes the top line of the terms box over the empty part next to the "توضیحات" tab
            thr.Cells.Add(new TableCell(MakePara("", font, 1, TextAlignment.Center))
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            });
            termsGroup.Rows.Add(thr);

            foreach (var (text, hl) in BuildTerms(invoice, settings))
            {
                AddTermBulletRow(termsGroup, text, borderBrush, font, fs, hl);
            }

            termsTable.RowGroups.Add(termsGroup);
            doc.Blocks.Add(termsTable);

            // ==================== 6. Signature / stamp ====================
            var signTable = new Table { Margin = new Thickness(0, 0, 0, 10), FontFamily = font };
            signTable.Columns.Add(new TableColumn { Width = new GridLength(384) });
            signTable.Columns.Add(new TableColumn { Width = new GridLength(350) });

            var signGroup = new TableRowGroup();
            var signRow = new TableRow();

            var expertSignCell = new TableCell { Padding = new Thickness(8, 20, 8, 8) };
            expertSignCell.Blocks.Add(MakePara($"{ResolveExpertRole(invoice, settings)} {invoice.ExpertName}", font, fs, RtlEnd,
                null, false, new SolidColorBrush(Color.FromRgb(51, 65, 85))));

            var sellerSignCell = new TableCell { Padding = new Thickness(8), TextAlignment = TextAlignment.Center };
            var signStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var stampBmp = LoadImage(settings.CompanyStampPath);
            if (stampBmp != null)
            {
                signStack.Children.Add(new Image
                {
                    Source = stampBmp,
                    Width = settings.StampWidth > 0 ? settings.StampWidth : 140,
                    Height = settings.StampHeight > 0 ? settings.StampHeight : 75,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }
            else
            {
                var blue = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                var stampBorder = new Border
                {
                    BorderBrush = blue,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Width = 135,
                    Height = 65,
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = new SolidColorBrush(Color.FromArgb(10, 37, 99, 235))
                };
                var stampGrid = new Grid();
                stampGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                stampGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var regBorder = new Border
                {
                    BorderBrush = blue,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 1, 3, 1),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                regBorder.Child = new TextBlock
                {
                    Text = $"شماره ثبت\n{settings.SellerRegistrationNo}",
                    FontFamily = font,
                    FontSize = 8,
                    Foreground = blue,
                    TextAlignment = TextAlignment.Center
                };
                stampGrid.Children.Add(regBorder);

                var stampName = new TextBlock
                {
                    Text = settings.SellerName,
                    FontFamily = font,
                    FontWeight = FontWeights.Normal,
                    FontSize = 9.5,
                    Foreground = blue,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetRow(stampName, 1);
                stampGrid.Children.Add(stampName);

                stampBorder.Child = stampGrid;
                signStack.Children.Add(stampBorder);
            }

            signStack.Children.Add(new TextBlock
            {
                Text = ResolveSignatory(invoice, settings),
                FontFamily = font,
                FontWeight = FontWeights.Bold,
                FontSize = fs,
                Foreground = darkText,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            sellerSignCell.Blocks.Add(new BlockUIContainer(signStack));

            signRow.Cells.Add(sellerSignCell);
            signRow.Cells.Add(expertSignCell);
            signGroup.Rows.Add(signRow);
            signTable.RowGroups.Add(signGroup);
            doc.Blocks.Add(signTable);

            // ==================== 7. Cut line + address ====================
            var cutLine = new System.Windows.Shapes.Line
            {
                X1 = 0,
                X2 = ContentW,
                Y1 = 0,
                Y2 = 0,
                Width = ContentW,
                Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Margin = new Thickness(0, 6, 0, 6)
            };
            doc.Blocks.Add(new BlockUIContainer(cutLine));

            string addressLine = $"آدرس دفتر: {settings.SellerAddress}    تلفن: {settings.SellerPhone}";
            doc.Blocks.Add(MakePara(addressLine, font, fs, TextAlignment.Center, new Thickness(0, 2, 0, 0), fg: slateText));

            return doc;
        }

        private static void AddTermBulletRow(TableRowGroup group, string text, Brush borderBrush, FontFamily font, double fs, bool isHighlighted)
        {
            var tr = new TableRow();
            Brush? bg = isHighlighted ? new SolidColorBrush(Color.FromRgb(254, 240, 138)) : null; // #FEF08A

            var bulletCell = new TableCell(MakePara("*", font, fs, TextAlignment.Center, new Thickness(2), bold: true))
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Background = bg
            };

            var fg = isHighlighted ? new SolidColorBrush(Color.FromRgb(113, 63, 18)) : new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var textCell = new TableCell(MakePara(text, font, fs, RtlStart, new Thickness(6, 4, 6, 4), isHighlighted, fg))
            {
                ColumnSpan = 2,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = bg
            };

            tr.Cells.Add(bulletCell);
            tr.Cells.Add(textCell);
            group.Rows.Add(tr);
        }

        // ====================================================================
        //  Report document
        // ====================================================================

        public FlowDocument CreateReportFlowDocument(List<ProformaInvoice> invoices, string reportTitle, string filterSummary)
        {
            var settings = _storage.GetSettings();
            var font = new FontFamily(string.IsNullOrWhiteSpace(settings.TableFontFamily) ? DefaultFont : settings.TableFontFamily);

            var doc = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = font,
                FontSize = 11,
                PagePadding = new Thickness(35),
                PageWidth = 794,
                PageHeight = 1123,
                ColumnWidth = 724
            };

            var borderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));

            var headerPara = new Paragraph { FontFamily = font, TextAlignment = TextAlignment.Center };
            headerPara.Inlines.Add(new Bold(new Run(reportTitle)) { FontSize = 16 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run($"{settings.SellerName} | تاریخ گزارش: {JalaliDateHelper.GetCurrentShamsiDate()}") { FontSize = 10.5, Foreground = Brushes.Gray });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run($"فیلتر اعمال شده: {filterSummary}") { FontSize = 10, Foreground = Brushes.DarkSlateGray });
            doc.Blocks.Add(headerPara);

            decimal totalAmount = invoices.Sum(i => i.GrandTotal);
            decimal totalProfit = invoices.Sum(i => i.TotalProfit);

            var summaryPara = new Paragraph(new Bold(new Run(
                $"تعداد فاکتورها: {invoices.Count} | مجموع مبلغ: {totalAmount:N0} {settings.CurrencyUnit} | مجموع سود: {totalProfit:N0} {settings.CurrencyUnit}")))
            {
                FontFamily = font,
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(8),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8, 0, 14)
            };
            doc.Blocks.Add(summaryPara);

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 16), FlowDirection = FlowDirection.RightToLeft, FontFamily = font };
            table.Columns.Add(new TableColumn { Width = new GridLength(35) });
            table.Columns.Add(new TableColumn { Width = new GridLength(110) });
            table.Columns.Add(new TableColumn { Width = new GridLength(85) });
            table.Columns.Add(new TableColumn { Width = new GridLength(164) });
            table.Columns.Add(new TableColumn { Width = new GridLength(110) });
            table.Columns.Add(new TableColumn { Width = new GridLength(110) });
            table.Columns.Add(new TableColumn { Width = new GridLength(110) });

            var group = new TableRowGroup();
            var th = new TableRow { Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)) };
            AddReportCell(th, "ردیف", TextAlignment.Center, true, borderBrush, font);
            AddReportCell(th, "شماره فاکتور", TextAlignment.Center, true, borderBrush, font);
            AddReportCell(th, "تاریخ", TextAlignment.Center, true, borderBrush, font);
            AddReportCell(th, "نام مشتری", RtlStart, true, borderBrush, font);
            AddReportCell(th, "کارشناس", TextAlignment.Center, true, borderBrush, font);
            AddReportCell(th, "مبلغ کل", TextAlignment.Center, true, borderBrush, font);
            AddReportCell(th, "وضعیت", TextAlignment.Center, true, borderBrush, font);
            group.Rows.Add(th);

            int idx = 1;
            foreach (var inv in invoices)
            {
                var tr = new TableRow { Background = idx % 2 == 1 ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : Brushes.White };
                AddReportCell(tr, idx.ToString(), TextAlignment.Center, false, borderBrush, font);
                AddReportCell(tr, inv.InvoiceNumber, TextAlignment.Center, false, borderBrush, font);
                AddReportCell(tr, inv.IssueDateShamsi, TextAlignment.Center, false, borderBrush, font);
                AddReportCell(tr, inv.CompanyName, RtlStart, false, borderBrush, font);
                AddReportCell(tr, inv.ExpertName, TextAlignment.Center, false, borderBrush, font);
                AddReportCell(tr, $"{inv.GrandTotal:N0}", TextAlignment.Center, false, borderBrush, font);
                AddReportCell(tr, inv.Status.ToPersianString(), TextAlignment.Center, false, borderBrush, font);
                group.Rows.Add(tr);
                idx++;
            }

            table.RowGroups.Add(group);
            doc.Blocks.Add(table);

            return doc;
        }

        private static void AddReportCell(TableRow row, string? text, TextAlignment align, bool isHeader, Brush borderBrush, FontFamily font)
        {
            var p = MakePara(text ?? "", font, 10, align, new Thickness(4, 5, 4, 5), isHeader,
                isHeader ? Brushes.White : Brushes.Black);
            row.Cells.Add(MakeCell(p, borderBrush));
        }

        // ====================================================================
        //  File name / PDF / print
        // ====================================================================

        public static string GetSafeInvoiceFileName(ProformaInvoice invoice, string extension = "pdf")
        {
            string num = !string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoice.InvoiceNumber.Trim() : "پیش‌فاکتور";
            string comp = !string.IsNullOrWhiteSpace(invoice.CompanyName) ? invoice.CompanyName.Trim() : "";
            string exp = !string.IsNullOrWhiteSpace(invoice.ExpertName) ? invoice.ExpertName.Trim() : "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(num)) parts.Add(num);
            if (!string.IsNullOrWhiteSpace(comp)) parts.Add(comp);
            if (!string.IsNullOrWhiteSpace(exp)) parts.Add(exp);

            string raw = parts.Count > 0 ? string.Join(" - ", parts) : $"پیش‌فاکتور_{DateTime.Now:yyyyMMdd}";
            var invalidChars = Path.GetInvalidFileNameChars();
            string safe = string.Join(" ", raw.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();

            char[] extraInvalid = new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' };
            safe = string.Join(" ", safe.Split(extraInvalid, StringSplitOptions.RemoveEmptyEntries)).Trim();

            while (safe.Contains("  ")) safe = safe.Replace("  ", " ");
            if (string.IsNullOrWhiteSpace(safe)) safe = $"پیش‌فاکتور_{invoice.InvoiceNumber}";
            return string.IsNullOrWhiteSpace(extension) ? safe : $"{safe}.{extension.TrimStart('.')}";
        }

        /// <summary>Vector PDF with real text (QuestPDF). No more page screenshots.</summary>
        public void SaveInvoiceAsPdfFile(ProformaInvoice invoice, string targetPdfPath, AppSettings? overrideSettings = null)
        {
            var settings = overrideSettings ?? _storage.GetSettings();
            ExportFontsForPdf(settings);
            InvoicePdfBuilder.Save(invoice, settings, targetPdfPath);
        }

        /// <summary>
        /// Finds the font files behind the fonts chosen in settings (table font + company font, regular and bold)
        /// and copies them into the Fonts folder, where the PDF builder picks them up.
        /// Already-copied files are skipped, so this is cheap to call on every export.
        /// </summary>
        internal static void ExportFontsForPdf(AppSettings settings)
        {
            var names = new[] { settings.TableFontFamily, settings.CompanyNameFontFamily, DefaultFont }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .SelectMany(s => s!.Split(','))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            string dir = InvoicePdfBuilder.FontsDir;

            foreach (var name in names)
            {
                foreach (var weight in new[] { FontWeights.Normal, FontWeights.Bold })
                {
                    try
                    {
                        var tf = new Typeface(new FontFamily(name), FontStyles.Normal, weight, FontStretches.Normal);
                        if (!tf.TryGetGlyphTypeface(out var gt)) continue;

                        // WPF silently falls back to a default font when the family doesn't exist; don't copy that.
                        bool isRequestedFamily =
                            gt.FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) ||
                            gt.Win32FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
                        if (!isRequestedFamily) continue;

                        var uri = gt.FontUri;
                        string fileName = Path.GetFileName(uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
                        if (string.IsNullOrWhiteSpace(fileName)) continue;

                        string target = Path.Combine(dir, fileName);
                        if (File.Exists(target)) continue;

                        if (uri.IsFile)
                        {
                            File.Copy(uri.LocalPath, target);
                        }
                        else
                        {
                            // font embedded as an app resource (pack:// URI)
                            var res = Application.GetResourceStream(uri);
                            if (res == null) continue;
                            using var src = res.Stream;
                            using var dst = File.Create(target);
                            src.CopyTo(dst);
                        }
                    }
                    catch { }
                }
            }
        }

        public void PrintOrExportToPdf(ProformaInvoice invoice)
        {
            var doc = CreateInvoiceFlowDocument(invoice);
            var printDialog = new PrintDialog();
            string docTitle = GetSafeInvoiceFileName(invoice, "");

            if (printDialog.ShowDialog() == true)
            {
                IDocumentPaginatorSource idpSource = doc;
                printDialog.PrintDocument(idpSource.DocumentPaginator, docTitle);
            }
        }

        public bool SaveInvoiceAsPdf(ProformaInvoice invoice, AppSettings? overrideSettings = null)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "ذخیره پیش‌فاکتور به صورت فایل PDF",
                    Filter = "فایل‌های PDF (*.pdf)|*.pdf",
                    FileName = GetSafeInvoiceFileName(invoice, "pdf"),
                    DefaultExt = ".pdf",
                    AddExtension = true
                };

                if (sfd.ShowDialog() == true)
                {
                    SaveInvoiceAsPdfFile(invoice, sfd.FileName, overrideSettings);
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(sfd.FileName)
                        {
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch { }
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در ذخیره PDF:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        public void PrintReport(List<ProformaInvoice> invoices, string reportTitle, string filterSummary)
        {
            var doc = CreateReportFlowDocument(invoices, reportTitle, filterSummary);
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() == true)
            {
                IDocumentPaginatorSource idpSource = doc;
                printDialog.PrintDocument(idpSource.DocumentPaginator, reportTitle);
            }
        }
    }
}