using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    /// <summary>
    /// Vector PDF export (real, selectable, searchable text) using QuestPDF.
    /// NuGet: QuestPDF  (you can remove PdfSharpCore).
    /// All sizes below are in WPF px (96 dpi) and converted to PDF points with Px().
    /// </summary>
    public static class InvoicePdfBuilder
    {
        private const double RowH = 32;          // one text line row = 32px, 2 lines = 64px, ...
        private const float BorderW = 0.5f;      // pt. Adjacent cells overlap, so inner lines look 2x this
        private const string BorderHex = "#64748B";
        private const string HeaderBg = "#ACCCF2";
        private const string SummaryBg = "#ACCCF2";

        private const string Dark = "#0F172A";
        private const string Slate = "#1E293B";

        private static bool _initDone;
        private static string? _fontsDir;
        private static readonly HashSet<string> _registeredFonts = new(StringComparer.OrdinalIgnoreCase);

        private static float Px(double px) => (float)(px * 0.75);

        /// <summary>
        /// "Fonts" folder next to the exe (falls back to %LocalAppData%\TarazInvoice\Fonts
        /// if the exe folder is not writable, e.g. Program Files).
        /// </summary>
        internal static string FontsDir => _fontsDir ??= ResolveFontsDir();

        private static string ResolveFontsDir()
        {
            var roots = new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarazInvoice")
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = Path.Combine(root, "Fonts");
                    Directory.CreateDirectory(dir);
                    var probe = Path.Combine(dir, ".write-test");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    return dir;
                }
                catch { }
            }
            return Path.Combine(AppContext.BaseDirectory, "Fonts");
        }

        private static void Init()
        {
            if (_initDone) return;
            _initDone = true;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // Runs on every save, so a font exported after a settings change is picked up too.
        private static void RegisterFonts()
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(FontsDir))
                {
                    var ext = Path.GetExtension(f);
                    if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!_registeredFonts.Add(f)) continue;

                    try
                    {
                        using var s = File.OpenRead(f);
                        FontManager.RegisterFontFromStream(s);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string[] Fonts(string? setting)
        {
            return (string.IsNullOrWhiteSpace(setting) ? PrintPdfService.DefaultFont : setting)
                .Split(',')
                .Select(x => x.Trim())
                .Select(x => x.Contains('#') ? x.Substring(x.LastIndexOf('#') + 1) : x) // WPF "pack://...#Font" style
                .Where(x => x.Length > 0)
                .ToArray();
        }

        private static byte[]? ReadBytes(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return File.ReadAllBytes(path);
            }
            catch { }
            return null;
        }

        public static void Save(ProformaInvoice invoice, AppSettings settings, string path)
        {
            Init();
            RegisterFonts();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            Document.Create(c => Compose(c, invoice, settings)).GeneratePdf(path);
        }

        // ---------- cell styles ----------

        private static IContainer Box(IContainer c, double minH = RowH, string? bg = null)
        {
            c = c.Border(BorderW).BorderColor(BorderHex);
            if (bg != null) c = c.Background(bg);
            return c.MinHeight(Px(minH)).PaddingHorizontal(Px(4)).AlignMiddle();
        }

        private static IContainer TermBox(IContainer c, bool highlighted)
        {
            c = c.Border(BorderW).BorderColor(BorderHex);
            if (highlighted) c = c.Background("#FEF08A");
            return c.PaddingVertical(Px(4)).PaddingHorizontal(Px(6));
        }

        // ---------- document ----------

        private static void Compose(IDocumentContainer container, ProformaInvoice invoice, AppSettings s)
        {
            var fonts = Fonts(s.TableFontFamily);
            var companyFonts = Fonts(s.CompanyNameFontFamily);
            double fs = s.TableFontSize > 0 ? s.TableFontSize : 11;
            double companyFs = s.CompanyNameFontSize > 0 ? s.CompanyNameFontSize : 18;
            float body = Px(fs);   // the ONE body font size (pt), applied explicitly to every text below
            byte[]? logo = ReadBytes(s.CompanyLogoPath);
            byte[]? stamp = ReadBytes(s.CompanyStampPath);
            string cur = "ریال";

            string issueDt = string.IsNullOrWhiteSpace(invoice.IssueDateShamsi) ? JalaliDateHelper.GetCurrentShamsiDate() : invoice.IssueDateShamsi;
            var metas = new[]
            {
                ("تاریخ :", issueDt),
                ("شماره :", invoice.InvoiceNumber ?? ""),
                ("پیوست :", string.IsNullOrWhiteSpace(invoice.Attachment) ? "ندارد" : invoice.Attachment),
                ("صفحه :", string.IsNullOrWhiteSpace(invoice.PageNumberText) ? "یک" : invoice.PageNumberText)
            };

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(Px(30));
                page.MarginVertical(Px(24));
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(fonts).FontSize(body).FontColor(Dark));

                page.Content().Column(col =>
                {
                    // ===== 1. header: company (right) | title (center) | info (far left) =====
                    col.Item().PaddingBottom(Px(12)).Row(row =>
                    {
                        row.ConstantItem(Px(270)).Row(r =>
                        {
                            if (logo != null)
                                r.ConstantItem(Px(52)).Height(Px(52)).Image(logo).FitArea();

                            r.RelativeItem().PaddingHorizontal(logo != null ? Px(10) : 0).AlignMiddle().Text(t =>
                            {
                                t.AlignStart();
                                t.Span(s.SellerName ?? "").FontFamily(companyFonts).FontSize(Px(companyFs)).Bold().FontColor(Slate);
                            });
                        });

                        row.ConstantItem(Px(194)).PaddingTop(Px(10)).AlignCenter()
                            .Text("پیش فاکتور").FontSize(Px(26)).Bold().FontColor(Dark);

                        row.RelativeItem();

                        // AutoItem = exactly as wide as its content, so it sits flush against the left edge
                        row.AutoItem().Column(ic =>
                        {
                            foreach (var (label, val) in metas)
                            {
                                ic.Item().PaddingVertical(Px(2)).Row(r =>
                                {
                                    r.ConstantItem(Px(50)).Text(label).Bold().FontSize(body);
                                    r.AutoItem().Text(val).FontSize(body);
                                });
                            }
                        });
                    });

                    // ===== 2. customer + intro =====
                    col.Item().PaddingTop(Px(4)).PaddingBottom(Px(8)).Column(ic =>
                    {
                        string customerLine = PrintPdfService.FormatCustomerDisplayName(invoice.CustomerLabelPrefix, invoice.CompanyName, invoice.CompanyPhone);
                        ic.Item().PaddingBottom(Px(3)).Text(customerLine).Bold().FontSize(body);

                        ic.Item().PaddingBottom(Px(3)).Text(t =>
                        {
                            t.Span("موضوع: ").Bold().FontSize(body);
                            t.Span(invoice.Subject ?? "").FontSize(body);
                        });

                        ic.Item().Text(PrintPdfService.ResolveGreeting(invoice, s)).FontSize(body).FontColor(Slate);
                    });

                    // ===== 3. items table =====
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(Px(45));   // row
                            c.RelativeColumn();         // item name (rest of the width)
                            c.ConstantColumn(Px(50));   // qty
                            c.ConstantColumn(Px(45));   // unit
                            c.ConstantColumn(Px(130));  // unit price
                            c.ConstantColumn(Px(130));  // total
                        });

                        table.Header(h =>
                        {
                            foreach (var title in new[] { "ردیف", "شرح کالا", "مقدار", "واحد", $"قیمت واحد ({cur})", $"جمع کل ({cur})" })
                                Box(h.Cell(), RowH, HeaderBg).AlignCenter().Text(title).Bold().FontSize(body);
                        });

                        int idx = 1;
                        foreach (var item in invoice.Items)
                        {
                            string name = (item.ItemName ?? "").Trim();

                            // item-name column ~334px minus cell padding/border
                            int lines = PrintPdfService.CountLines(name, s.TableFontFamily ?? PrintPdfService.DefaultFont, fs, 320);
                            double h = RowH * lines; // 32 for one line, 64 for two, 96 for three...

                            Box(table.Cell(), h).AlignCenter().Text(idx.ToString()).FontSize(body);

                            Box(table.Cell(), h).Text(t =>
                            {
                                t.AlignStart(); // RTL start = right
                                t.Span(name).FontSize(body);
                            });

                            Box(table.Cell(), h).AlignCenter().Text(item.Quantity.ToString("G29")).FontSize(body);
                            Box(table.Cell(), h).AlignCenter().Text(string.IsNullOrWhiteSpace(item.Unit) ? "عدد" : item.Unit).FontSize(body);
                            Box(table.Cell(), h).AlignCenter().Text($"{item.FinalUnitPrice:N0}").FontSize(body);
                            Box(table.Cell(), h).AlignCenter().Text($"{item.TotalPrice:N0}").FontSize(body);
                            idx++;
                        }
                    });

                    // ===== 4. totals: full width, same font size =====
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(Px(130));
                        });

                        void SumRow(string label, decimal value)
                        {
                            Box(table.Cell(), RowH, SummaryBg).PaddingHorizontal(Px(4)).Text(label).Bold().FontSize(body);
                            Box(table.Cell(), RowH).AlignCenter().Text($"{value:N0}").Bold().FontSize(body);
                        }

                        SumRow($"جمع کل ( {cur} )", invoice.GrandTotal);
                        SumRow($"ارزش افزوده ( {cur} )", invoice.VatAmount);
                        SumRow($"جمع کل با ارزش افزوده ({cur})", invoice.GrandTotalWithVat);
                    });

                    // ===== 5. terms =====
                    col.Item().PaddingTop(Px(14)).Column(tc =>
                    {
                        // "توضیحات :" tab (no bottom border: the terms table below draws the full top line)
                        tc.Item().Row(r =>
                        {
                            r.ConstantItem(Px(160))
                                .BorderTop(BorderW).BorderLeft(BorderW).BorderRight(BorderW).BorderColor(BorderHex)
                                .Background(HeaderBg)
                                .PaddingVertical(Px(4)).AlignCenter()
                                .Text("توضیحات :").Bold().FontSize(body);
                            r.RelativeItem();
                        });

                        tc.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(Px(24));
                                c.RelativeColumn();
                            });

                            foreach (var (text, hl) in PrintPdfService.BuildTerms(invoice, s))
                            {
                                TermBox(table.Cell(), hl).AlignCenter().Text("*").Bold().FontSize(body);

                                TermBox(table.Cell(), hl).Text(t =>
                                {
                                    t.AlignStart();
                                    var span = t.Span(text).FontSize(body);
                                    if (hl) span.Bold().FontColor("#713F12");
                                });
                            }
                        });
                    });

                    // ===== 6. stamp / signature =====
                    col.Item().PaddingTop(Px(14)).Row(row =>
                    {
                        row.ConstantItem(Px(384)).Column(sc =>
                        {
                            if (stamp != null)
                            {
                                sc.Item().AlignCenter().Width(Px(140)).Height(Px(75)).Image(stamp).FitArea();
                            }
                            else
                            {
                                sc.Item().AlignCenter().Width(Px(135)).Height(Px(65))
                                    .Border(Px(1.5)).BorderColor("#2563EB").Background("#EFF4FE")
                                    .Padding(Px(4)).Column(bc =>
                                    {
                                        bc.Item().Text(t =>
                                        {
                                            t.AlignEnd();
                                            t.Span($"شماره ثبت: {s.SellerRegistrationNo}").FontSize(Px(8)).FontColor("#2563EB");
                                        });
                                        bc.Item().ExtendVertical().AlignMiddle().AlignCenter()
                                            .Text(s.SellerName ?? "").Bold().FontSize(Px(9.5)).FontColor("#2563EB");
                                    });
                            }

                            sc.Item().PaddingTop(Px(2)).AlignCenter()
                                .Text(PrintPdfService.ResolveSignatory(invoice, s)).Bold().FontSize(body).FontColor(Dark);
                        });

                        // expert: visually at the left
                        row.RelativeItem().PaddingTop(Px(20)).Text(t =>
                        {
                            t.AlignEnd();
                            t.Span($"{PrintPdfService.ResolveExpertRole(invoice, s)} {invoice.ExpertName}").FontSize(body).FontColor("#334155");
                        });
                    });

                    // ===== 7. cut line + address =====
                    col.Item().PaddingTop(Px(14)).Row(r =>
                    {
                        for (int i = 0; i < 104; i++)
                        {
                            r.ConstantItem(Px(4)).LineHorizontal(0.75f).LineColor(Color.FromHex(BorderHex));
                            r.ConstantItem(Px(3));
                        }
                    });

                    col.Item().PaddingTop(Px(6)).AlignCenter()
                        .Text($"آدرس دفتر: {s.SellerAddress}    تلفن: {s.SellerPhone}").FontSize(body).FontColor(Slate);
                });
            });
        }
    }
}