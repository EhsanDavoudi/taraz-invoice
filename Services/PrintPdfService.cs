using System;
using System.Collections.Generic;
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

        public PrintPdfService(StorageService storage)
        {
            _storage = storage;
        }

        public FlowDocument CreateInvoiceFlowDocument(ProformaInvoice invoice)
        {
            var settings = _storage.GetSettings();

            var mainFontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.TableFontFamily) ? "Vazirmatn, Tahoma, Segoe UI" : settings.TableFontFamily);
            var companyFontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.CompanyNameFontFamily) ? "Vazirmatn, Tahoma, Segoe UI" : settings.CompanyNameFontFamily);
            double companyFontSize = settings.CompanyNameFontSize > 0 ? settings.CompanyNameFontSize : 18;
            double tableFontSize = settings.TableFontSize > 0 ? settings.TableFontSize : 11;

            var doc = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = mainFontFamily,
                FontSize = tableFontSize,
                PagePadding = new Thickness(30, 24, 30, 24),
                PageWidth = 794,
                PageHeight = 1123,
                ColumnWidth = 734
            };

            var borderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            var lightBorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            var headerBgBrush = new SolidColorBrush(Color.FromRgb(220, 230, 242));
            var summaryBgBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));

            // ==================== ۱. سربرگ اصلی (مطابق نمونه تصویر) ====================
            var headerTable = new Table { Margin = new Thickness(0, 0, 0, 12), CellSpacing = 0 };
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(320) }); // نام شرکت و لوگو (راست - پهنای کافی برای جلوگیری از بریده شدن نام)
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(214) }); // عنوان پیش‌فاکتور (وسط)
            headerTable.Columns.Add(new TableColumn { Width = new GridLength(200) }); // تاریخ و شماره (چپ)

            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow();

            // الف) بخش راست: لوگو و نام شرکت با قلم سفارشی
            var companyCell = new TableCell { Padding = new Thickness(4) };
            var companyStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // لوگوی کاربر اگر تنظیم شده باشد یا لوگوی پیش‌فرض
            string logoPath = settings.CompanyLogoPath;
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    var img = new Image
                    {
                        Source = bmp,
                        Width = 52,
                        Height = 52,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    companyStack.Children.Add(img);
                }
                catch { }
            }

            var companyNameBlock = new TextBlock
            {
                Text = settings.SellerName,
                FontFamily = companyFontFamily,
                FontSize = companyFontSize > 16 ? 16 : companyFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            companyStack.Children.Add(companyNameBlock);
            companyCell.Blocks.Add(new BlockUIContainer(companyStack));

            // ب) بخش وسط: عنوان درشت «پیش فاکتور»
            var titleCell = new TableCell(new Paragraph(new Run("پیش فاکتور"))
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 12, 0, 0)
            });

            // ج) بخش چپ: کادر تاریخ، شماره، پیوست و صفحه (مشابه نمونه اکسل کاربر)
            var infoBox = new TableCell
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2)
            };
            var infoTable = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(65) });
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(125) });
            var infoGroup = new TableRowGroup();

            void AddMetaRow(string label, string val, bool isLast = false)
            {
                var r = new TableRow();
                var lblCell = new TableCell(new Paragraph(new Bold(new Run(label))) { Margin = new Thickness(4, 2, 4, 2), FontSize = 10, TextAlignment = TextAlignment.Right })
                {
                    BorderBrush = lightBorderBrush,
                    BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1)
                };
                var valCell = new TableCell(new Paragraph(new Run(val)) { Margin = new Thickness(4, 2, 4, 2), FontSize = 10, TextAlignment = TextAlignment.Center })
                {
                    BorderBrush = lightBorderBrush,
                    BorderThickness = new Thickness(1, 0, 0, isLast ? 0 : 1)
                };
                r.Cells.Add(lblCell);
                r.Cells.Add(valCell);
                infoGroup.Rows.Add(r);
            }

            string issueDt = string.IsNullOrWhiteSpace(invoice.IssueDateShamsi) ? JalaliDateHelper.GetCurrentShamsiDate() : invoice.IssueDateShamsi;
            AddMetaRow("تاریخ :", issueDt);
            AddMetaRow("شماره :", invoice.InvoiceNumber ?? "");
            AddMetaRow("پیوست :", string.IsNullOrWhiteSpace(invoice.Attachment) ? "ندارد" : invoice.Attachment);
            AddMetaRow("صفحه :", string.IsNullOrWhiteSpace(invoice.PageNumberText) ? "یک" : invoice.PageNumberText, isLast: true);

            infoTable.RowGroups.Add(infoGroup);
            infoBox.Blocks.Add(infoTable);

            headerRow.Cells.Add(companyCell);
            headerRow.Cells.Add(titleCell);
            headerRow.Cells.Add(infoBox);
            headerGroup.Rows.Add(headerRow);
            headerTable.RowGroups.Add(headerGroup);
            doc.Blocks.Add(headerTable);

            // ==================== ۲. مشخصات مشتری و متن مقدمه ====================
            var introSection = new Section { Margin = new Thickness(0, 4, 0, 8) };

            string custPrefix = !string.IsNullOrWhiteSpace(invoice.CustomerLabelPrefix) ? invoice.CustomerLabelPrefix : (!string.IsNullOrWhiteSpace(settings.CustomerLabelPrefix) ? settings.CustomerLabelPrefix : "نام مشتری شرکت محترم ");
            string custName = string.IsNullOrWhiteSpace(invoice.CompanyName) ? "مشتری محترم" : invoice.CompanyName;

            string formattedPrefix = custPrefix.TrimEnd() + " ";
            var customerPara = new Paragraph { Margin = new Thickness(0, 0, 0, 3), FontSize = 11.5 };
            customerPara.Inlines.Add(new Bold(new Run(formattedPrefix)));
            customerPara.Inlines.Add(new Run(custName.TrimStart()));
            if (!string.IsNullOrWhiteSpace(invoice.CompanyPhone))
            {
                customerPara.Inlines.Add(new Run($"  (تلفن: {invoice.CompanyPhone})"));
            }
            introSection.Blocks.Add(customerPara);

            var subjectPara = new Paragraph { Margin = new Thickness(0, 0, 0, 3), FontSize = 11 };
            subjectPara.Inlines.Add(new Bold(new Run("موضوع: ")));
            if (!string.IsNullOrWhiteSpace(invoice.Subject))
            {
                subjectPara.Inlines.Add(new Run(invoice.Subject));
            }
            introSection.Blocks.Add(subjectPara);

            string greeting = !string.IsNullOrWhiteSpace(invoice.GreetingText) ? invoice.GreetingText : (!string.IsNullOrWhiteSpace(settings.GreetingText) ? settings.GreetingText : "احتراما\" قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:");
            var greetingsPara = new Paragraph(new Run(greeting))
            {
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            introSection.Blocks.Add(greetingsPara);
            doc.Blocks.Add(introSection);

            string currUnit = !string.IsNullOrWhiteSpace(invoice.CurrencyUnit) ? invoice.CurrencyUnit : (!string.IsNullOrWhiteSpace(settings.CurrencyUnit) ? settings.CurrencyUnit : "ریال");

            // ==================== ۳. جدول اقلام کالا با شکست سطر هوشمند (Wrap) ====================
            // ستون‌ها: ردیف | شرح کالا (عرض بزرگ با Wrap) | مقدار | واحد | قیمت واحد | جمع کل
            var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(35) });  // ردیف
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(334) }); // شرح کالا (قابلیت شکست ۲ یا ۳ خطی)
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(50) });  // مقدار
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(55) });  // واحد
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(130) }); // قیمت واحد
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(130) }); // جمع کل

            var tableGroup = new TableRowGroup();

            // هدر جدول (مطابق نمونه رنگ آبی کم‌رنگ / سورمه‌ای)
            var thRow = new TableRow { Background = headerBgBrush };
            AddInvoiceTableCell(thRow, "ردیف", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            AddInvoiceTableCell(thRow, "شرح کالا", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            AddInvoiceTableCell(thRow, "مقدار", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            AddInvoiceTableCell(thRow, "واحد", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            AddInvoiceTableCell(thRow, $"قیمت واحد ({currUnit})", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            AddInvoiceTableCell(thRow, $"جمع کل ({currUnit})", TextAlignment.Center, isHeader: true, borderBrush: borderBrush);
            tableGroup.Rows.Add(thRow);

            // سطرهای اقلام - شرح کالا در صورت طولانی بودن به ۲ یا ۳ خط می‌شکند
            int rowIdx = 1;
            foreach (var item in invoice.Items)
            {
                var tr = new TableRow();

                // ردیف
                AddInvoiceTableCell(tr, rowIdx.ToString(), TextAlignment.Center, isHeader: false, borderBrush: borderBrush);

                // شرح کالا با شکست خطوط و جهت اکیداً راست‌چین برای حفظ ترتیب صحیح اعداد و خط‌تیره‌های فارسی
                string safeItemName = "\u200F" + item.ItemName + "\u200F";
                var namePara = new Paragraph(new Run(safeItemName))
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(6, 6, 6, 6),
                    FontSize = tableFontSize,
                    FontFamily = mainFontFamily,
                    LineHeight = 17
                };
                var nameCell = new TableCell(namePara)
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1)
                };
                tr.Cells.Add(nameCell);

                // مقدار
                AddInvoiceTableCell(tr, item.Quantity.ToString("G29"), TextAlignment.Center, isHeader: false, borderBrush: borderBrush);

                // واحد
                string unitStr = string.IsNullOrWhiteSpace(item.Unit) ? "عدد" : item.Unit;
                AddInvoiceTableCell(tr, unitStr, TextAlignment.Center, isHeader: false, borderBrush: borderBrush);

                // قیمت واحد
                AddInvoiceTableCell(tr, $"{item.FinalUnitPrice:N0}", TextAlignment.Center, isHeader: false, borderBrush: borderBrush);

                // جمع کل
                AddInvoiceTableCell(tr, $"{item.TotalPrice:N0}", TextAlignment.Center, isHeader: false, borderBrush: borderBrush);

                tableGroup.Rows.Add(tr);
                rowIdx++;
            }

            itemsTable.RowGroups.Add(tableGroup);
            doc.Blocks.Add(itemsTable);

            // ==================== ۴. جدول جمع‌های مالی و ارزش افزوده ====================
            // ۳ سطر در سمت چپ: جمع کل، ارزش افزوده، جمع کل با ارزش افزوده
            var summaryTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(474) }); // فضای خالی / مبلغ به حروف
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(130) }); // برچسب جمع
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(130) }); // مقدار جمع

            var summaryGroup = new TableRowGroup();

            // سطر ۱: جمع کل
            var sumRow1 = new TableRow();
            var wordsCell = new TableCell(new Paragraph(new Bold(new Run($"مبلغ کل به حروف: {JalaliDateHelper.NumberToWords((long)invoice.GrandTotalWithVat)} {currUnit}")))
            {
                FontSize = 10.5,
                Margin = new Thickness(4)
            })
            {
                RowSpan = 3
            };
            sumRow1.Cells.Add(wordsCell);
            AddInvoiceTableCell(sumRow1, $"جمع کل ( {currUnit} )", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, bgBrush: summaryBgBrush, isBold: true);
            AddInvoiceTableCell(sumRow1, $"{invoice.GrandTotal:N0}", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, isBold: true);
            summaryGroup.Rows.Add(sumRow1);

            // سطر ۲: ارزش افزوده
            var sumRow2 = new TableRow();
            Brush vatBg = invoice.HighlightVat ? new SolidColorBrush(Color.FromRgb(254, 240, 138)) : summaryBgBrush;
            AddInvoiceTableCell(sumRow2, $"ارزش افزوده ( {currUnit} )", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, bgBrush: vatBg, isBold: true);
            AddInvoiceTableCell(sumRow2, $"{invoice.VatAmount:N0}", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, bgBrush: vatBg, isBold: true);
            summaryGroup.Rows.Add(sumRow2);

            // سطر ۳: جمع کل با ارزش افزوده
            var sumRow3 = new TableRow { Background = new SolidColorBrush(Color.FromRgb(254, 240, 138)) };
            AddInvoiceTableCell(sumRow3, $"جمع کل با ارزش افزوده ({currUnit})", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, isBold: true);
            AddInvoiceTableCell(sumRow3, $"{invoice.GrandTotalWithVat:N0}", TextAlignment.Center, isHeader: false, borderBrush: borderBrush, isBold: true);
            summaryGroup.Rows.Add(sumRow3);

            summaryTable.RowGroups.Add(summaryGroup);
            doc.Blocks.Add(summaryTable);

            // ==================== ۵. کادر توضیحات و شرایط پرداخت (مطابق تصویر کاربر) ====================
            var termsBorder = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };
            termsBorder.Columns.Add(new TableColumn { Width = new GridLength(24) });  // علامت لوزی ◆
            termsBorder.Columns.Add(new TableColumn { Width = new GridLength(710) }); // متن شرط

            var termsGroup = new TableRowGroup();

            // هدر توضیحات
            var termsHeaderRow = new TableRow { Background = headerBgBrush };
            var termsHeaderCell = new TableCell(new Paragraph(new Bold(new Run("توضیحات :"))) { Margin = new Thickness(6, 4, 6, 4) })
            {
                ColumnSpan = 2,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            };
            termsHeaderRow.Cells.Add(termsHeaderCell);
            termsGroup.Rows.Add(termsHeaderRow);

            // ردیف‌های اطلاعات بانکی (پشتیبانی از چند حساب، شماره شبا و شماره کارت)
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
                        AddTermBulletRow(termsGroup, info, borderBrush);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(settings.BankAccountNumber) || !string.IsNullOrWhiteSpace(settings.BankCardNumber))
            {
                string bankInfo = $"شماره حساب {settings.BankAccountNumber} شماره کارت {settings.BankCardNumber}";
                AddTermBulletRow(termsGroup, bankInfo, borderBrush);
            }

            // ردیف ۲: اعتبار فاکتور (با هایلایت زرد پیش‌فرض مطابق تصویر)
            string validInfo = !string.IsNullOrWhiteSpace(invoice.ValidityText) ? invoice.ValidityText : (!string.IsNullOrWhiteSpace(settings.DefaultValidityText) ? settings.DefaultValidityText : "اعتبار این پیش فاکتورتا پایان روز می باشد.");
            AddTermBulletRow(termsGroup, validInfo, borderBrush, isHighlighted: invoice.HighlightValidity);

            // ردیف ۳: نوع تسویه
            string paymentTerms = !string.IsNullOrWhiteSpace(invoice.PaymentTerms) ? invoice.PaymentTerms : settings.PaymentTerms;
            if (!string.IsNullOrWhiteSpace(paymentTerms))
            {
                AddTermBulletRow(termsGroup, $"نوع تسویه : {paymentTerms}", borderBrush, isHighlighted: invoice.HighlightPaymentTerms);
            }

            // ردیف ۴: توضیحات سفارشی فاکتور
            string notesText = !string.IsNullOrWhiteSpace(invoice.Notes) ? invoice.Notes : settings.DefaultTerms;
            if (!string.IsNullOrWhiteSpace(notesText))
            {
                AddTermBulletRow(termsGroup, $"توضیحات : {notesText}", borderBrush, isHighlighted: invoice.HighlightNotes);
            }

            // ردیف ۵: زمان تحویل
            string deliveryDays = !string.IsNullOrWhiteSpace(invoice.DeliveryDays) ? invoice.DeliveryDays : settings.DeliveryDays;
            if (!string.IsNullOrWhiteSpace(deliveryDays))
            {
                AddTermBulletRow(termsGroup, $"زمان تحویل : {deliveryDays}", borderBrush, isHighlighted: invoice.HighlightDeliveryDays);
            }

            // ردیف ۶: محل تحویل کالا
            string deliveryLocation = !string.IsNullOrWhiteSpace(invoice.DeliveryLocation) ? invoice.DeliveryLocation : settings.DeliveryLocation;
            if (!string.IsNullOrWhiteSpace(deliveryLocation))
            {
                AddTermBulletRow(termsGroup, $"محل تحویل کالا:{deliveryLocation}", borderBrush, isHighlighted: invoice.HighlightDeliveryLocation);
            }

            termsBorder.RowGroups.Add(termsGroup);
            doc.Blocks.Add(termsBorder);

            // ==================== ۶. امضا و مهر (مطابق دقیق تصویر) ====================
            var signTable = new Table { Margin = new Thickness(0, 0, 0, 10) };
            signTable.Columns.Add(new TableColumn { Width = new GridLength(384) }); // مهر و امضا و مسئول فروش (سمت راست/وسط)
            signTable.Columns.Add(new TableColumn { Width = new GridLength(350) }); // کارشناس (سمت چپ)

            var signGroup = new TableRowGroup();
            var signRow = new TableRow();

            // کارشناسی خرید / فروش (سمت چپ پایین فاکتور)
            var expertSignCell = new TableCell
            {
                Padding = new Thickness(8, 20, 8, 8)
            };
            var expertPara = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Left };
            string expertRole = !string.IsNullOrWhiteSpace(invoice.ExpertRoleLabel) ? invoice.ExpertRoleLabel : (!string.IsNullOrWhiteSpace(settings.ExpertRoleLabel) ? settings.ExpertRoleLabel : "کارشناسی خرید:");
            expertPara.Inlines.Add(new Run($"{expertRole} {invoice.ExpertName}") { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)) });
            expertSignCell.Blocks.Add(expertPara);

            // مهر و امضای شرکت فروشنده و مسئول پیش‌فاکتور (سمت راست/وسط)
            var sellerSignCell = new TableCell
            {
                Padding = new Thickness(8),
                TextAlignment = TextAlignment.Center
            };
            var signStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            // اگر کاربر تصویر مهر اختصاصی را مشخص کرده باشد
            if (!string.IsNullOrWhiteSpace(settings.CompanyStampPath) && File.Exists(settings.CompanyStampPath))
            {
                try
                {
                    var stampBmp = new BitmapImage();
                    stampBmp.BeginInit();
                    stampBmp.UriSource = new Uri(settings.CompanyStampPath, UriKind.Absolute);
                    stampBmp.CacheOption = BitmapCacheOption.OnLoad;
                    stampBmp.EndInit();

                    var stampImg = new Image
                    {
                        Source = stampBmp,
                        MaxHeight = 75,
                        MaxWidth = 140,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    signStack.Children.Add(stampImg);
                }
                catch { }
            }
            else
            {
                // مهر رسمی گرافیکی شرکت آریان نیرو گستر اترک
                var stampBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
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
                    BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 1, 3, 1),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                regBorder.Child = new TextBlock
                {
                    Text = $"شماره ثبت\n{settings.SellerRegistrationNo}",
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    TextAlignment = TextAlignment.Center
                };
                stampGrid.Children.Add(regBorder);

                var stampName = new TextBlock
                {
                    Text = settings.SellerName,
                    FontWeight = FontWeights.Bold,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetRow(stampName, 1);
                stampGrid.Children.Add(stampName);

                stampBorder.Child = stampGrid;
                signStack.Children.Add(stampBorder);
            }

            // نام امضاکننده پیش‌فاکتور (محمد احمدی پور هدش)
            string signatoryName = !string.IsNullOrWhiteSpace(invoice.SignatoryName) ? invoice.SignatoryName : (!string.IsNullOrWhiteSpace(settings.SignatoryName) ? settings.SignatoryName : "محمد احمدی پور هدش");
            var sellerNameSign = new TextBlock
            {
                Text = signatoryName,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            signStack.Children.Add(sellerNameSign);
            sellerSignCell.Blocks.Add(new BlockUIContainer(signStack));

            // در حالت راست‌چین FlowDocument، ستون اول در سمت راست و ستون دوم در سمت چپ قرار می‌گیرد
            signRow.Cells.Add(sellerSignCell); // سمت راست/وسط: مهر و مسئول امضا
            signRow.Cells.Add(expertSignCell); // سمت چپ: کارشناسی خرید/فروش
            signGroup.Rows.Add(signRow);
            signTable.RowGroups.Add(signGroup);
            doc.Blocks.Add(signTable);

            // ==================== ۷. خط‌چین برش و نشانی و تلفن دفتر در انتهای برگه ====================
            var footerSeparator = new Table { Margin = new Thickness(0, 4, 0, 6), CellSpacing = 0 };
            footerSeparator.Columns.Add(new TableColumn { Width = new GridLength(734) });
            var sepGroup = new TableRowGroup();
            var sepRow = new TableRow();
            sepRow.Cells.Add(new TableCell(new Paragraph(new Run("- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -"))
            {
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0)
            })
            {
                Padding = new Thickness(0)
            });
            sepGroup.Rows.Add(sepRow);
            footerSeparator.RowGroups.Add(sepGroup);
            doc.Blocks.Add(footerSeparator);

            string addressLine = $"آدرس دفتر: {settings.SellerAddress}    تلفن: {settings.SellerPhone}";
            var footerPara = new Paragraph(new Run(addressLine))
            {
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            doc.Blocks.Add(footerPara);

            return doc;
        }

        private static void AddTermBulletRow(TableRowGroup group, string text, Brush borderBrush, bool isHighlighted = false)
        {
            var tr = new TableRow();
            Brush? bg = isHighlighted ? new SolidColorBrush(Color.FromRgb(254, 240, 138)) : null; // Yellow highlighter (#FEF08A)

            var bulletCell = new TableCell(new Paragraph(new Run("◆")) { TextAlignment = TextAlignment.Center, Margin = new Thickness(2), FontSize = 8, FontWeight = FontWeights.Bold })
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Background = bg
            };

            var p = new Paragraph(isHighlighted ? new Bold(new Run(text)) : new Run(text))
            {
                Margin = new Thickness(6, 3, 6, 3),
                FontSize = 10.5,
                Foreground = isHighlighted ? new SolidColorBrush(Color.FromRgb(113, 63, 18)) : new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };

            var textCell = new TableCell(p)
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = bg
            };

            tr.Cells.Add(bulletCell);
            tr.Cells.Add(textCell);
            group.Rows.Add(tr);
        }

        private static void AddInvoiceTableCell(TableRow row, string text, TextAlignment align, bool isHeader, Brush borderBrush, Brush? bgBrush = null, bool isBold = false)
        {
            var p = new Paragraph(isHeader || isBold ? new Bold(new Run(text)) : new Run(text))
            {
                TextAlignment = align,
                Margin = new Thickness(4, isHeader ? 6 : 4, 4, isHeader ? 6 : 4),
                FontSize = isHeader ? 11 : 10.5
            };
            var cell = new TableCell(p)
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            };
            if (bgBrush != null)
            {
                cell.Background = bgBrush;
            }
            row.Cells.Add(cell);
        }

        public FlowDocument CreateReportFlowDocument(List<ProformaInvoice> invoices, string reportTitle, string filterSummary)
        {
            var settings = _storage.GetSettings();
            var doc = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.TableFontFamily) ? "Vazirmatn, Tahoma, Segoe UI" : settings.TableFontFamily),
                FontSize = 11,
                PagePadding = new Thickness(35),
                PageWidth = 794,
                PageHeight = 1123,
                ColumnWidth = 724
            };

            var borderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));

            // سربرگ گزارش
            var headerPara = new Paragraph();
            headerPara.Inlines.Add(new Bold(new Run(reportTitle + "\n")) { FontSize = 16 });
            headerPara.Inlines.Add(new Run($"{settings.SellerName} | تاریخ گزارش: {JalaliDateHelper.GetCurrentShamsiDate()}\n") { FontSize = 10.5, Foreground = Brushes.Gray });
            headerPara.Inlines.Add(new Run($"فیلتر اعمال شده: {filterSummary}") { FontSize = 10, Foreground = Brushes.DarkSlateGray });
            headerPara.TextAlignment = TextAlignment.Center;
            doc.Blocks.Add(headerPara);

            // خلاصه‌های کلیدی
            decimal totalAmount = invoices.Sum(i => i.GrandTotal);
            decimal totalProfit = invoices.Sum(i => i.TotalProfit);

            var summaryPara = new Paragraph(new Bold(new Run(
                $"تعداد فاکتورها: {invoices.Count} | مجموع مبلغ: {totalAmount:N0} {settings.CurrencyUnit} | مجموع سود: {totalProfit:N0} {settings.CurrencyUnit}")))
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(8),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8, 0, 14)
            };
            doc.Blocks.Add(summaryPara);

            // جدول اقلام گزارش
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 16) };
            table.Columns.Add(new TableColumn { Width = new GridLength(35) });  // ردیف
            table.Columns.Add(new TableColumn { Width = new GridLength(110) }); // شماره
            table.Columns.Add(new TableColumn { Width = new GridLength(85) });  // تاریخ
            table.Columns.Add(new TableColumn { Width = new GridLength(164) }); // مشتری
            table.Columns.Add(new TableColumn { Width = new GridLength(110) }); // کارشناس
            table.Columns.Add(new TableColumn { Width = new GridLength(110) }); // جمع کل
            table.Columns.Add(new TableColumn { Width = new GridLength(110) }); // وضعیت

            var group = new TableRowGroup();
            var th = new TableRow { Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)) };
            AddReportCell(th, "ردیف", TextAlignment.Center, true, borderBrush);
            AddReportCell(th, "شماره فاکتور", TextAlignment.Center, true, borderBrush);
            AddReportCell(th, "تاریخ", TextAlignment.Center, true, borderBrush);
            AddReportCell(th, "نام مشتری", TextAlignment.Right, true, borderBrush);
            AddReportCell(th, "کارشناس", TextAlignment.Center, true, borderBrush);
            AddReportCell(th, "مبلغ کل", TextAlignment.Center, true, borderBrush);
            AddReportCell(th, "وضعیت", TextAlignment.Center, true, borderBrush);
            group.Rows.Add(th);

            int idx = 1;
            foreach (var inv in invoices)
            {
                var tr = new TableRow { Background = idx % 2 == 1 ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : Brushes.White };
                AddReportCell(tr, idx.ToString(), TextAlignment.Center, false, borderBrush);
                AddReportCell(tr, inv.InvoiceNumber, TextAlignment.Center, false, borderBrush);
                AddReportCell(tr, inv.IssueDateShamsi, TextAlignment.Center, false, borderBrush);
                AddReportCell(tr, inv.CompanyName, TextAlignment.Right, false, borderBrush);
                AddReportCell(tr, inv.ExpertName, TextAlignment.Center, false, borderBrush);
                AddReportCell(tr, $"{inv.GrandTotal:N0}", TextAlignment.Center, false, borderBrush);
                AddReportCell(tr, inv.Status.ToPersianString(), TextAlignment.Center, false, borderBrush);
                group.Rows.Add(tr);
                idx++;
            }

            table.RowGroups.Add(group);
            doc.Blocks.Add(table);

            return doc;
        }

        private static void AddReportCell(TableRow row, string text, TextAlignment align, bool isHeader, Brush borderBrush)
        {
            var p = new Paragraph(isHeader ? new Bold(new Run(text)) : new Run(text))
            {
                Foreground = isHeader ? Brushes.White : Brushes.Black,
                TextAlignment = align,
                Margin = new Thickness(4, 5, 4, 5),
                FontSize = 10
            };
            var cell = new TableCell(p)
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            };
            row.Cells.Add(cell);
        }

        public static string GetSafeInvoiceFileName(ProformaInvoice invoice, string extension = "pdf")
        {
            string num = !string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoice.InvoiceNumber.Trim() : "فاکتور";
            string comp = !string.IsNullOrWhiteSpace(invoice.CompanyName) ? invoice.CompanyName.Trim() : "مشتری";
            string exp = !string.IsNullOrWhiteSpace(invoice.ExpertName) ? invoice.ExpertName.Trim() : "کارشناس";
            string raw = $"{num}_{comp}_{exp}";
            string safe = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrWhiteSpace(safe)) safe = $"پیش‌فاکتور_{invoice.InvoiceNumber}";
            return string.IsNullOrWhiteSpace(extension) ? safe : $"{safe}.{extension.TrimStart('.')}";
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

        public bool SaveInvoiceAsPdf(ProformaInvoice invoice)
        {
            var doc = CreateInvoiceFlowDocument(invoice);
            var printDialog = new PrintDialog();
            string docTitle = GetSafeInvoiceFileName(invoice, "");

            try
            {
                var localPrintServer = new System.Printing.LocalPrintServer();
                var pdfQueue = localPrintServer.GetPrintQueues()
                    .FirstOrDefault(q => q.Name.Contains("PDF", StringComparison.OrdinalIgnoreCase));
                if (pdfQueue != null)
                {
                    printDialog.PrintQueue = pdfQueue;
                    IDocumentPaginatorSource idpSource = doc;
                    printDialog.PrintDocument(idpSource.DocumentPaginator, docTitle);
                    return true;
                }
            }
            catch { }

            // اگر چاپگر PDF پیش‌فرض سیستم پیدا نشد، پنجره استاندارد انتخاب چاپگر باز می‌شود
            if (printDialog.ShowDialog() == true)
            {
                IDocumentPaginatorSource idpSource = doc;
                printDialog.PrintDocument(idpSource.DocumentPaginator, docTitle);
                return true;
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
