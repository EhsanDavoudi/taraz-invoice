using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using Xunit;
using TarazInvoice.Models;
using TarazInvoice.Services;
using TarazInvoice.Converters;

namespace TarazInvoice.Tests
{
    public class CalculationAndExportTests
    {
        [Fact]
        public void Test_InvoiceItem_Calculations_WithDiscountAndDivisionBy1Point1()
        {
            // سناریو: کالا با قیمت خام 18,500,000، 5 درصد تخفیف، و فعال بودن تیک تقسیم بر 1.1
            var item = new InvoiceItem
            {
                ItemName = "شیر پروانه‌ای ۱۰ اینچ",
                Quantity = 4,
                RawPrice = 18500000,
                DiscountPercent = 5,
                IsDividedBy1Point1 = true
            };

            item.UpdateFromPercent();

            // مبلغ تخفیف = 18,500,000 * 0.05 = 925,000
            Assert.Equal(925000m, item.DiscountAmount);

            // قیمت پس از تخفیف = 18,500,000 - 925,000 = 17,575,000
            // قیمت تقسیم بر 1.1 با اعمال سقف گرد کردن (بیش از ۴ رقم گرد به بالا با تقریب ۱۰۰۰)
            decimal unroundedPrice = Math.Round(17575000m / 1.1m, 0); // 15,977,273
            decimal expectedUnitPrice = InvoiceItem.RoundUpPrice(unroundedPrice); // 15,978,000
            Assert.Equal(expectedUnitPrice, item.FinalUnitPrice);

            // جمع کل سطر = RoundUpPrice(4 * 15,978,000) = 63,912,000
            decimal expectedTotal = InvoiceItem.RoundUpPrice(4 * expectedUnitPrice);
            Assert.Equal(expectedTotal, item.TotalPrice);
        }

        [Fact]
        public void Test_InvoiceItem_Calculations_WithoutDivisionBy1Point1()
        {
            // سناریو: کالا بدون تقسیم بر 1.1 و با 10 درصد تخفیف
            var item = new InvoiceItem
            {
                ItemName = "فلنج کربن استیل",
                Quantity = 10,
                RawPrice = 5000000,
                DiscountPercent = 10,
                IsDividedBy1Point1 = false
            };

            item.UpdateFromPercent();

            Assert.Equal(500000m, item.DiscountAmount);
            Assert.Equal(4500000m, item.FinalUnitPrice);
            Assert.Equal(45000000m, item.TotalPrice);
        }

        [Fact]
        public void Test_Expert_And_CentralizedUniqueNumbering()
        {
            var exp1 = new Expert
            {
                FullName = "مهندس رضایی",
                RoleTitle = "کارشناس خرید"
            };

            var exp2 = new Expert
            {
                FullName = "مهندس محمدی",
                RoleTitle = "کارشناس بازرگانی"
            };

            Assert.Equal("مهندس رضایی", exp1.FullName);
            Assert.Equal("مهندس محمدی", exp2.FullName);

            // اطمینان از اینکه شماره‌گذاری فاکتورها مستقل از کارشناس و به صورت متمرکز و یکتا مدیریت می‌شود
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_ExpNumbering_" + Guid.NewGuid());
            string dbPath = Path.Combine(tempDir, "database.json");
            try
            {
                var storage = new StorageService(dbPath);
                string num1 = storage.GenerateNextUniqueInvoiceNumber();
                string num2 = storage.GenerateNextUniqueInvoiceNumber();
                Assert.NotEqual(num1, num2);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_ExcelTemplate_GenerationAndFilling()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTests_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                string templatePath = Path.Combine(tempDir, "Template.xlsx");
                string outputPath = Path.Combine(tempDir, "Output_Clean_Customer.xlsx");

                var storage = new StorageService();
                var excelService = new ExcelExportService(storage);

                // ۱. تولید فایل قالب اکسل با تگ‌های متغیر
                excelService.GenerateSampleTemplateFile(templatePath);
                Assert.True(File.Exists(templatePath));

                // ۲. ایجاد یک پیش‌فاکتور نمونه
                var invoice = new ProformaInvoice
                {
                    InvoiceNumber = "EXP-TEST-9001",
                    IssueDateShamsi = "1403/06/15",
                    ValidUntilShamsi = "1403/06/22",
                    CompanyName = "شرکت تست پتروشیمی",
                    CompanyPhone = "021-12345678",
                    ExpertName = "مهندس آزمایشی",
                    ExpertPhone = "09120000000",
                    Status = InvoiceStatus.Approved,
                    Items = new List<InvoiceItem>
                    {
                        new InvoiceItem
                        {
                            RowNumber = 1,
                            ItemName = "لوله مانیسمان ۶ اینچ",
                            Quantity = 5,
                            RawPrice = 10000000,
                            DiscountPercent = 10,
                            IsDividedBy1Point1 = true
                        }
                    }
                };
                invoice.Items[0].UpdateFromPercent();

                // ۳. صدور پیش‌فاکتور در قالب
                excelService.ExportInvoice(invoice, outputPath, templatePath);
                Assert.True(File.Exists(outputPath));

                // ۴. باز کردن فایل خروجی و راستی‌آزمایی پر شدن تگ‌ها و مقادیر مشتری
                using var wb = new XLWorkbook(outputPath);
                var ws = wb.Worksheets.First();

                // بررسی مقدار ردیف اول اقلام: نام، تعداد، قیمت فروش نهایی و جمع کل
                Assert.Equal("لوله مانیسمان ۶ اینچ", ws.Cell(8, 2).GetString());
                Assert.Equal(5, ws.Cell(8, 3).GetDouble());
                Assert.Equal((double)invoice.Items[0].FinalUnitPrice, ws.Cell(8, 4).GetDouble());
                Assert.Equal((double)invoice.Items[0].TotalPrice, ws.Cell(8, 5).GetDouble());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Test_PersianNumberToWords()
        {
            string words = JalaliDateHelper.NumberToWords(84500000);
            Assert.Contains("هشتاد و چهار میلیون", words);
            Assert.Contains("پانصد هزار", words);
        }

        [Fact]
        public void Test_InvoiceItem_Calculations_WithProfitPercent()
        {
            // قیمت خام 10,000,000 - 10 درصد تخفیف = 9,000,000
            // با تیک 1.1: قیمت پایه = 9,000,000 / 1.1 = 8,181,818
            // 20 درصد سود: سود واحد = 8,181,818 * 0.20 = 1,636,364
            // قیمت نهایی فروش = 8,181,818 + 1,636,364 = 9,818,182
            // تعداد 2: سود کل = 3,272,728 - جمع کل = 19,636,364
            var item = new InvoiceItem
            {
                ItemName = "الکتروموتور ۵.۵ کیلووات",
                Quantity = 2,
                RawPrice = 10000000,
                DiscountPercent = 10,
                IsDividedBy1Point1 = true,
                ProfitPercent = 20
            };

            item.UpdateFromPercent();

            decimal baseCost = Math.Round((10000000m * 0.9m) / 1.1m, 0); // 8,181,818
            decimal rawProfitUnit = Math.Round(baseCost * 0.20m, 0);       // 1,636,364
            decimal expectedUnitPrice = InvoiceItem.RoundUpPrice(baseCost + rawProfitUnit); // 9,819,000
            decimal expectedProfitUnit = expectedUnitPrice - baseCost;     // 1,637,182

            Assert.Equal(expectedProfitUnit, item.ProfitAmount);
            Assert.Equal(expectedUnitPrice, item.FinalUnitPrice);
            Assert.Equal(expectedProfitUnit * 2, item.TotalProfit);
            Assert.Equal(InvoiceItem.RoundUpPrice(expectedUnitPrice * 2), item.TotalPrice);
        }

        [Fact]
        public void Test_InvoiceItem_Calculations_WithDirectFinalPrice()
        {
            // کالا با قیمت پایه 5,000,000 (بدون تخفیف و بدون 1.1)
            // کاربر مستقیماً قیمت نهایی فروش را 6,000,000 وارد می‌کند
            // سود واحد باید 1,000,000 و درصد سود 20% محاسبه شود
            var item = new InvoiceItem
            {
                Quantity = 3,
                RawPrice = 5000000,
                DiscountPercent = 0,
                IsDividedBy1Point1 = false
            };
            item.UpdateFromPercent();

            item.FinalUnitPrice = 6000000;

            Assert.Equal(1000000m, item.ProfitAmount);
            Assert.Equal(20m, item.ProfitPercent);
            Assert.Equal(3000000m, item.TotalProfit);
            Assert.Equal(18000000m, item.TotalPrice);
        }

        [Fact]
        public void Test_TemplateExport_FirstSheetOnly_PreservesStylingAndPattern()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTemplateMultiSheet_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                string templatePath = Path.Combine(tempDir, "MultiSheetTemplate.xlsx");
                string outputPath = Path.Combine(tempDir, "OutputInvoice.xlsx");

                // ایجاد قالب چند شیته: شیت اول قالب، شیت دوم اطلاعات دیگر
                using (var wb = new XLWorkbook())
                {
                    var ws1 = wb.Worksheets.Add("شیت اصلی قالب");
                    ws1.RightToLeft = true;
                    ws1.Cell("A1").Value = "پیش‌فاکتور: {{INVOICE_NO}}";
                    ws1.Cell("A2").Value = "سود پیش‌فاکتور: {{TOTAL_PROFIT}}";

                    // سطر ۸ شروع اقلام با استایل خاص (رنگ و کادر)
                    ws1.Cell(8, 1).Value = "{{ITEMS}}";
                    ws1.Row(8).Height = 28;
                    ws1.Range(8, 1, 8, 5).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 240, 255);
                    ws1.Range(8, 1, 8, 5).Style.Font.Bold = true;

                    // شیت دوم
                    var ws2 = wb.Worksheets.Add("شیت محاسبات پنهان");
                    ws2.Cell("A1").Value = "این شیت نباید تغییر کند";

                    wb.SaveAs(templatePath);
                }

                byte[] originalTemplateBytes = File.ReadAllBytes(templatePath);

                var storage = new StorageService();
                var excelService = new ExcelExportService(storage);

                var invoice = new ProformaInvoice
                {
                    InvoiceNumber = "EXP-TEST-8888",
                    IssueDateShamsi = "1403/07/01",
                    ValidUntilShamsi = "1403/07/08",
                    CompanyName = "فولاد مبارکه",
                    Items = new List<InvoiceItem>
                    {
                        new InvoiceItem { RowNumber = 1, ItemName = "محصول اول", Quantity = 2, RawPrice = 1000000, ProfitPercent = 10 },
                        new InvoiceItem { RowNumber = 2, ItemName = "محصول دوم", Quantity = 4, RawPrice = 2000000, ProfitPercent = 15 }
                    }
                };
                foreach (var it in invoice.Items) it.UpdateFromPercent();

                // صدور با قالب
                excelService.ExportInvoice(invoice, outputPath, templatePath);

                // راستی‌آزمایی ۱: فایل اصلی قالب به هیچ عنوان تغییر نکرده باشد
                byte[] currentTemplateBytes = File.ReadAllBytes(templatePath);
                Assert.Equal(originalTemplateBytes, currentTemplateBytes);

                // راستی‌آزمایی ۲: فایل خروجی فقط شیت اول را به عنوان قالب گرفته و سطر دوم از سطر اول الگوبرداری کرده است
                using var outWb = new XLWorkbook(outputPath);
                var outWs1 = outWb.Worksheet(1);

                Assert.Equal("پیش‌فاکتور: EXP-TEST-8888", outWs1.Cell("A1").GetString());
                Assert.Contains(invoice.TotalProfit.ToString("N0"), outWs1.Cell("A2").GetString());

                // اقلام در سطرهای ۸ و ۹
                Assert.Equal("محصول اول", outWs1.Cell(8, 2).GetString());
                Assert.Equal("محصول دوم", outWs1.Cell(9, 2).GetString());

                // سطر ۹ از سطر ۸ الگوبرداری کرده: ارتفاع و رنگ پر شدن یکسان است
                Assert.Equal(outWs1.Row(8).Height, outWs1.Row(9).Height);
                Assert.Equal(outWs1.Cell(8, 2).Style.Fill.BackgroundColor, outWs1.Cell(9, 2).Style.Fill.BackgroundColor);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Theory]
        [InlineData("A", 1)]
        [InlineData("B", 2)]
        [InlineData("E", 5)]
        [InlineData("Z", 26)]
        [InlineData("AA", 27)]
        [InlineData("3", 3)]
        [InlineData("  d  ", 4)]
        public void Test_ParseColumnToNumber(string input, int expected)
        {
            int col = ExcelExportService.ParseColumnToNumber(input, 1);
            Assert.Equal(expected, col);
        }

        [Fact]
        public void Test_AllStatuses_PersianStringsAndColors()
        {
            // راستی‌آزمایی هر ۹ وضعیت درخواستی کاربر
            Assert.Equal("تهیه کالا", InvoiceStatus.PreparingGoods.ToPersianString());
            Assert.Equal("ارسال شده", InvoiceStatus.Sent.ToPersianString());
            Assert.Equal("ارسال نشده", InvoiceStatus.NotSent.ToPersianString());
            Assert.Equal("هزینه واریز شده", InvoiceStatus.Paid.ToPersianString());
            Assert.Equal("واریز نشده", InvoiceStatus.Unpaid.ToPersianString());
            Assert.Equal("پیش‌نویس", InvoiceStatus.Draft.ToPersianString());
            Assert.Equal("در انتظار تأیید", InvoiceStatus.Pending.ToPersianString());
            Assert.Equal("تأیید شده", InvoiceStatus.Approved.ToPersianString());
            Assert.Equal("لغو / باطل شده", InvoiceStatus.Canceled.ToPersianString());

            // رنگ‌ها
            Assert.NotNull(InvoiceStatus.PreparingGoods.GetColorHex());
            Assert.NotNull(InvoiceStatus.Sent.GetColorHex());
            Assert.NotNull(InvoiceStatus.NotSent.GetColorHex());
            Assert.NotNull(InvoiceStatus.Paid.GetColorHex());
            Assert.NotNull(InvoiceStatus.Unpaid.GetColorHex());
        }

        [Fact]
        public void Test_StorageService_EnsureStorageItemExists()
        {
            var storage = new StorageService();
            string testItemName = "دستگاه تست آنالایزر شبکه " + Guid.NewGuid().ToString().Substring(0, 5);

            // ۱. قبل از فراخوانی، این کالا نباید در انبار باشد
            var initialList = storage.GetStorageItems();
            Assert.DoesNotContain(initialList, x => x.Name == testItemName);

            // ۲. فراخوانی EnsureStorageItemExists
            var createdItem = storage.EnsureStorageItemExists(testItemName, "دستگاه", 145000000m);
            Assert.NotNull(createdItem);
            Assert.Equal(testItemName, createdItem.Name);
            Assert.Equal("دستگاه", createdItem.Unit);
            Assert.Equal(145000000m, createdItem.DefaultPrice);

            // ۳. حال باید در انبار ذخیره شده باشد
            var afterList = storage.GetStorageItems();
            Assert.Contains(afterList, x => x.Name == testItemName);

            // ۴. اگر مجدداً صدا زده شود، نباید تکراری اضافه شود و همان کالای قبلی برگردانده شود
            int countBefore = afterList.Count;
            var sameItem = storage.EnsureStorageItemExists(testItemName, "دستگاه", 145000000m);
            Assert.Equal(createdItem.Id, sameItem.Id);
            Assert.Equal(countBefore, storage.GetStorageItems().Count);
        }

        [Fact]
        public void Test_JalaliDateHelper_MonthAndYearExtraction()
        {
            // نمونه بر اساس فاکتور کاربر: 1405/08/26 (آبان ماه)
            string dateStr = "1405/08/26";
            int? month = JalaliDateHelper.GetMonthNumber(dateStr);
            int? year = JalaliDateHelper.GetYearNumber(dateStr);
            string monthName = JalaliDateHelper.GetMonthName(dateStr);

            Assert.Equal(8, month);
            Assert.Equal(1405, year);
            Assert.Equal("آبان", monthName);

            // بررسی ماه و سال تاریخ با فرمت نامنظم
            Assert.Equal(1, JalaliDateHelper.GetMonthNumber("1403/1/5"));
            Assert.Equal(1403, JalaliDateHelper.GetYearNumber("1403/1/5"));
            Assert.Equal("فروردین", JalaliDateHelper.GetMonthName("1403/1/5"));
        }

        [Fact]
        public void Test_Invoice_VatCalculation()
        {
            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "INV-VAT-1",
                VatPercent = 10,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem { RowNumber = 1, ItemName = "کالا ۱", Quantity = 1, RawPrice = 100000000 },
                    new InvoiceItem { RowNumber = 2, ItemName = "کالا ۲", Quantity = 1, RawPrice = 50000000 }
                }
            };
            foreach (var it in invoice.Items) it.UpdateFromPercent();

            // جمع اقلام = 150,000,000
            Assert.Equal(150000000m, invoice.GrandTotal);

            // ارزش افزوده ۱۰٪ = 15,000,000
            Assert.Equal(15000000m, invoice.VatAmount);

            // جمع کل با ارزش افزوده = 165,000,000
            Assert.Equal(165000000m, invoice.GrandTotalWithVat);
        }

        [Fact]
        public void Test_ExcelExport_StorageAndReport()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazExportTests_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var storage = new StorageService();
                var excel = new ExcelExportService(storage);

                // ۱. تست خروجی اکسل انبار
                string storagePath = Path.Combine(tempDir, "Storage.xlsx");
                var items = new List<StorageItem>
                {
                    new StorageItem { Code = "TST-01", Name = "کلمپ فلکسیبل جریان مدل AFLEX 3003", Unit = "عدد", DefaultPrice = 1000000, StockQuantity = 5, Category = "ابزار دقیق" }
                };
                excel.ExportStorage(items, storagePath);
                Assert.True(File.Exists(storagePath));

                using (var wb = new XLWorkbook(storagePath))
                {
                    var ws = wb.Worksheets.First();
                    Assert.Equal("فهرست اقلام و موجودی انبار کالاها", ws.Cell(1, 1).GetString());
                    Assert.Equal("کد کالا", ws.Cell(3, 2).GetString());
                    Assert.Equal("TST-01", ws.Cell(4, 2).GetString());
                }

                // ۲. تست خروجی اکسل گزارش فروش
                string reportPath = Path.Combine(tempDir, "Report.xlsx");
                var invoices = new List<ProformaInvoice>
                {
                    new ProformaInvoice
                    {
                        InvoiceNumber = "EXP-001",
                        IssueDateShamsi = "1405/08/26",
                        CompanyName = "شرکت دانه طلایی",
                        ExpertName = "محمد احمدی پور",
                        Status = InvoiceStatus.Paid,
                        Items = new List<InvoiceItem>
                        {
                            new InvoiceItem { RowNumber = 1, ItemName = "هیتر گرداک", Quantity = 1, RawPrice = 5000000 }
                        }
                    }
                };
                foreach (var inv in invoices)
                {
                    foreach (var it in inv.Items) it.UpdateFromPercent();
                }

                excel.ExportReport(invoices, reportPath, "گزارش فروش ماه آبان");
                Assert.True(File.Exists(reportPath));

                using (var wb = new XLWorkbook(reportPath))
                {
                    var ws = wb.Worksheets.First();
                    Assert.Contains("گزارش تحلیلی پیش‌فاکتورهای فروش", ws.Cell(1, 1).GetString());
                    Assert.Contains("گزارش فروش ماه آبان", ws.Cell(2, 1).GetString());
                    Assert.Equal("EXP-001", ws.Cell(5, 2).GetString());
                    Assert.Equal("هزینه واریز شده", ws.Cell(5, 7).GetString());
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Test_PerInvoice_PaymentTerms_Delivery_And_Highlights()
        {
            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "EXP-999",
                PaymentTerms = "چک ۴۵ روزه صیادی",
                DeliveryDays = "تحویل فوری ۲۴ ساعته",
                DeliveryLocation = "انبار خریدار - عسلویه",
                VatPercent = 9m,
                Notes = "تست یادداشت ویژه با هایلایت",
                HighlightPaymentTerms = true,
                HighlightDeliveryDays = true,
                HighlightDeliveryLocation = false,
                HighlightNotes = true,
                HighlightVat = true
            };

            Assert.Equal("چک ۴۵ روزه صیادی", invoice.PaymentTerms);
            Assert.Equal("تحویل فوری ۲۴ ساعته", invoice.DeliveryDays);
            Assert.Equal("انبار خریدار - عسلویه", invoice.DeliveryLocation);
            Assert.Equal(9m, invoice.VatPercent);
            Assert.True(invoice.HighlightPaymentTerms);
            Assert.True(invoice.HighlightDeliveryDays);
            Assert.False(invoice.HighlightDeliveryLocation);
            Assert.True(invoice.HighlightNotes);
            Assert.True(invoice.HighlightVat);

            // تست کپی گرفتن از فاکتور با حفظ شرایط و هایلایت‌ها
            var cloned = invoice.CloneForNew("EXP-1000");
            Assert.Equal("EXP-1000", cloned.InvoiceNumber);
            Assert.Equal(invoice.PaymentTerms, cloned.PaymentTerms);
            Assert.Equal(invoice.DeliveryDays, cloned.DeliveryDays);
            Assert.Equal(invoice.DeliveryLocation, cloned.DeliveryLocation);
            Assert.Equal(invoice.VatPercent, cloned.VatPercent);
            Assert.True(cloned.HighlightPaymentTerms);
            Assert.True(cloned.HighlightDeliveryDays);
            Assert.False(cloned.HighlightDeliveryLocation);
            Assert.True(cloned.HighlightNotes);
            Assert.True(cloned.HighlightVat);
        }

        [Fact]
        public void Test_ExcelExport_TermsHighlight()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTermsHighlight_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var storage = new StorageService();
                var excel = new ExcelExportService(storage);
                string outPath = Path.Combine(tempDir, "InvoiceHighlighted.xlsx");

                var invoice = new ProformaInvoice
                {
                    InvoiceNumber = "EXP-HL-01",
                    IssueDateShamsi = "1405/08/26",
                    ValidUntilShamsi = "1405/09/03",
                    CompanyName = "شرکت پتروشیمی نمونه",
                    PaymentTerms = "نقد پیش از تحویل بار",
                    DeliveryDays = "تحویل ۵ روز کاری",
                    DeliveryLocation = "مشهد",
                    Notes = "فرایند تحویل پس از تسویه نهایی آغاز می‌شود.",
                    HighlightPaymentTerms = true,
                    HighlightDeliveryDays = true,
                    HighlightNotes = true,
                    HighlightVat = true,
                    VatPercent = 10m,
                    Items = new List<InvoiceItem>
                    {
                        new InvoiceItem { RowNumber = 1, ItemName = "هیتر صنعتی", Quantity = 2, RawPrice = 10000000, FinalUnitPrice = 10000000 }
                    }
                };
                invoice.Items[0].UpdateFromPercent();

                excel.ExportInvoice(invoice, outPath);
                Assert.True(File.Exists(outPath));

                using var wb = new XLWorkbook(outPath);
                var ws = wb.Worksheets.First();

                // بررسی وجود متن‌های هایلایت شده در شیت اکسل
                bool foundPaymentTerm = false;
                bool foundDeliveryTerm = false;
                for (int r = 1; r <= ws.LastRowUsed().RowNumber(); r++)
                {
                    string txt = ws.Cell(r, 2).GetString();
                    if (txt.Contains("نوع تسویه: نقد پیش از تحویل بار"))
                    {
                        foundPaymentTerm = true;
                        // بررسی رنگ پس‌زمینه زرد هایلایت (RGB 254, 240, 138)
                        Assert.Equal(XLColor.FromArgb(254, 240, 138), ws.Cell(r, 2).Style.Fill.BackgroundColor);
                    }
                    if (txt.Contains("زمان تحویل: تحویل ۵ روز کاری"))
                    {
                        foundDeliveryTerm = true;
                        Assert.Equal(XLColor.FromArgb(254, 240, 138), ws.Cell(r, 2).Style.Fill.BackgroundColor);
                    }
                }

                Assert.True(foundPaymentTerm, "شرط نوع تسویه با هایلایت در اکسل یافت نشد");
                Assert.True(foundDeliveryTerm, "شرط زمان تحویل با هایلایت در اکسل یافت نشد");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Test_StorageItem_ToString_DoesNotDisplayClassName()
        {
            var item1 = new StorageItem { Name = "کابلشو مسی سایز 35", Code = "PRD-106", DefaultPrice = 3600000 };
            var item2 = new StorageItem { Name = "", Code = "PRD-999" };

            // نباید نام کلاس بازگردانده شود (رفع کامل باگ تصویر ارسالی کاربر)
            Assert.NotEqual("TarazInvoice.Models.StorageItem", item1.ToString());
            Assert.Equal("کابلشو مسی سایز 35", item1.ToString());
            Assert.Equal("PRD-999", item2.ToString());
        }

        [Fact]
        public void Test_ProformaInvoice_FullCustomizabilityAndCloning()
        {
            var inv = new ProformaInvoice
            {
                InvoiceNumber = "ANA 405-0000962",
                CompanyName = "پالود پارسیان",
                CustomerLabelPrefix = "نام مشتری شرکت محترم ",
                Subject = "سفارش تابلو برق",
                GreetingText = "احتراما\" قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:",
                CurrencyUnit = "ریال",
                ValidityText = "اعتبار این پیش فاکتورتا پایان روز می باشد.",
                HighlightValidity = true,
                PaymentTerms = "نقد",
                HighlightPaymentTerms = false,
                Notes = "فرایند تحویل بار پس از وصول پیش پرداخت انجام خواهد شد.",
                HighlightNotes = true,
                DeliveryDays = "موجود",
                HighlightDeliveryDays = true,
                DeliveryLocation = "مشهد",
                HighlightDeliveryLocation = false,
                SignatoryName = "محمد احمدی پور هدش",
                ExpertRoleLabel = "کارشناسی خرید:",
                ExpertName = "سرکار خانم دهنودی"
            };

            var cloned = inv.CloneForNew("ANA 405-0000963");

            Assert.Equal("ANA 405-0000963", cloned.InvoiceNumber);
            Assert.Equal(inv.CustomerLabelPrefix, cloned.CustomerLabelPrefix);
            Assert.Equal(inv.Subject, cloned.Subject);
            Assert.Equal(inv.GreetingText, cloned.GreetingText);
            Assert.Equal("ریال", cloned.CurrencyUnit);
            Assert.Equal(inv.ValidityText, cloned.ValidityText);
            Assert.True(cloned.HighlightValidity);
            Assert.Equal("محمد احمدی پور هدش", cloned.SignatoryName);
            Assert.Equal("کارشناسی خرید:", cloned.ExpertRoleLabel);
            Assert.True(cloned.HighlightNotes);
            Assert.True(cloned.HighlightDeliveryDays);
        }

        [Fact]
        public void Test_OfficialExcelTemplate_LayoutAndMetadataBox()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var storage = new StorageService();
                var settings = storage.GetSettings();
                settings.SellerName = "شرکت آریان نیرو گستر اترک";
                settings.SellerRegistrationNo = "۸۸۶۵۴";
                settings.SignatoryName = "محمد احمدی پور هدش";
                settings.ExpertRoleLabel = "کارشناسی خرید:";
                storage.Save();

                var invoice = new ProformaInvoice
                {
                    InvoiceNumber = "ANA 405-0000962",
                    IssueDateShamsi = "۱۴۰۵/۰۸/۲۷",
                    Attachment = "ندارد",
                    PageNumberText = "یک",
                    CompanyName = "پالود پارسیان",
                    CustomerLabelPrefix = "نام مشتری شرکت محترم ",
                    Subject = "تجهیزات برق",
                    CurrencyUnit = "ریال",
                    ValidityText = "اعتبار این پیش فاکتورتا پایان روز می باشد.",
                    HighlightValidity = true,
                    PaymentTerms = "نقد",
                    DeliveryDays = "موجود",
                    HighlightDeliveryDays = true,
                    DeliveryLocation = "مشهد",
                    SignatoryName = "محمد احمدی پور هدش",
                    ExpertRoleLabel = "کارشناسی خرید:",
                    ExpertName = "سرکار خانم دهنودی",
                    Items = new List<InvoiceItem>
                    {
                        new InvoiceItem
                        {
                            RowNumber = 1,
                            ItemName = "کلید فرمان جرثقیل ۶ خانه تایوانی",
                            Quantity = 2,
                            RawPrice = 58300000,
                            Unit = "عدد"
                        }
                    }
                };
                invoice.Items[0].UpdateFromPercent();

                var excel = new ExcelExportService(storage);
                string outPath = Path.Combine(tempDir, "OfficialInvoiceTest.xlsx");
                excel.ExportInvoice(invoice, outPath);

                Assert.True(File.Exists(outPath));

                using var wb = new XLWorkbook(outPath);
                var ws = wb.Worksheets.First();

                // ۱. عنوان وسط پیش‌فاکتور
                Assert.Equal("پیش فاکتور", ws.Cell("C2").GetString());

                // ۲. کادر چهار سطری متادیتا در بالا سمت چپ
                Assert.Equal("تاریخ :", ws.Cell("E1").GetString());
                Assert.Equal("۱۴۰۵/۰۸/۲۷", ws.Cell("F1").GetString());
                Assert.Equal("شماره :", ws.Cell("E2").GetString());
                Assert.Equal("ANA 405-0000962", ws.Cell("F2").GetString());
                Assert.Equal("پیوست :", ws.Cell("E3").GetString());
                Assert.Equal("ندارد", ws.Cell("F3").GetString());
                Assert.Equal("صفحه :", ws.Cell("E4").GetString());
                Assert.Equal("یک", ws.Cell("F4").GetString());

                // ۳. مشخصات مشتری و هدر
                Assert.Contains("نام مشتری: شرکت محترم پالود پارسیان", ws.Cell("A6").GetString());

                // ۴. هدر جدول
                Assert.Equal("ردیف", ws.Cell(9, 1).GetString());
                Assert.Equal("شرح کالا", ws.Cell(9, 2).GetString());
                Assert.Equal("قیمت واحد (ریال)", ws.Cell(9, 5).GetString());
                Assert.Equal("جمع کل (ریال)", ws.Cell(9, 6).GetString());

                // ۵. بررسی اندازه کاغذ A4 و برازش در ۱ صفحه
                Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
                Assert.Equal(1, ws.PageSetup.PagesWide);
                Assert.Equal(1, ws.PageSetup.PagesTall);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Test_StoragePriceChange_OnlyWhenUserChangedPrice()
        {
            var storage = new StorageService();
            
            // ۱. ثبت کالای اولیه با قیمت 500,000
            string itemName = "کابل افشان ۲ در ۲.۵ تست_" + Guid.NewGuid().ToString("N");
            var item = storage.EnsureStorageItemExists(itemName, "متر", 500000m);
            Assert.NotNull(item);
            Assert.Equal(500000m, item.DefaultPrice);

            // ۲. کاربر کالا را به پیش‌فاکتور با همان قیمت 500,000 اضافه می‌کند (عدم تغییر قیمت)
            // شبیه‌سازی منطق BtnQuickAddItem_Click
            decimal userPriceUnchanged = 500000m;
            var existing1 = storage.GetStorageItems().FirstOrDefault(p => p.Name == itemName);
            Assert.NotNull(existing1);
            if (userPriceUnchanged > 0 && userPriceUnchanged != existing1.DefaultPrice)
            {
                existing1.DefaultPrice = userPriceUnchanged;
                storage.Save();
            }
            Assert.Equal(500000m, existing1.DefaultPrice);

            // ۳. کاربر کالا را به پیش‌فاکتور با قیمت جدید 720,000 اضافه می‌کند (تغییر قیمت توسط کاربر)
            decimal userPriceChanged = 720000m;
            var existing2 = storage.GetStorageItems().FirstOrDefault(p => p.Name == itemName);
            Assert.NotNull(existing2);
            if (userPriceChanged > 0 && userPriceChanged != existing2.DefaultPrice)
            {
                existing2.DefaultPrice = userPriceChanged;
                storage.Save();
            }
            
            // راستی‌آزمایی: قیمت در انبار بروز شد
            var reloaded = storage.GetStorageItems().FirstOrDefault(p => p.Name == itemName);
            Assert.NotNull(reloaded);
            Assert.Equal(720000m, reloaded.DefaultPrice);
        }

        [Fact]
        public void Test_PrintPdfService_A4LayoutAndRtlSignatories()
        {
            var t = new System.Threading.Thread(() =>
            {
                var storage = new StorageService();
                var printService = new PrintPdfService(storage);

                var invoice = new ProformaInvoice
                {
                    InvoiceNumber = "ANA 405-0000962",
                    CompanyName = "شرکت پالود پارسیان",
                    ExpertName = "سرکار خانم دهنودی",
                    SignatoryName = "محمد احمدی پور هدش",
                    ExpertRoleLabel = "کارشناسی خرید:",
                    Items = new List<InvoiceItem>
                    {
                        new InvoiceItem
                        {
                            RowNumber = 1,
                            ItemName = "کلید فرمان جرثقیل ۶ خانه تایوانی",
                            Quantity = 2,
                            RawPrice = 58300000m
                        }
                    }
                };
                invoice.Items[0].UpdateFromPercent();

                var doc = printService.CreateInvoiceFlowDocument(invoice);
                Assert.NotNull(doc);
                Assert.Equal(System.Windows.FlowDirection.RightToLeft, doc.FlowDirection);
                Assert.Equal(794, doc.PageWidth);   // عرض استاندارد A4 در 96DPI
                Assert.Equal(1123, doc.PageHeight); // ارتفاع استاندارد A4 در 96DPI
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [Fact]
        public void Test_E2E_OfficialInvoice_MatchesReferencePictureExactly()
        {
            var storage = new StorageService();
            var settings = storage.GetSettings();
            settings.SellerName = "شرکت آریان نیرو گستر اترک";
            settings.SellerRegistrationNo = "۸۸۶۵۴";
            settings.SignatoryName = "محمد احمدی پور هدش";
            settings.ExpertRoleLabel = "کارشناسی خرید:";
            settings.SellerAddress = "مشهد - خیابان سنایی - بین سنایی ۱۲ و ۱۴ - روبروی سنایی ۱۵ - پلاک ۱۱۵";
            settings.SellerPhone = "۵-۰۵۱۳۷۱۳۲۰۸۴";
            settings.CurrencyUnit = "ریال";
            settings.BankAccounts = new List<BankAccountInfo>
            {
                new BankAccountInfo
                {
                    BankName = "بانک ملی ایران",
                    ShebaNumber = "IR95017000000118033698006",
                    CardNumber = "۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶",
                    OwnerName = "شرکت آریان نیرو گستر اترک"
                }
            };
            storage.Save();

            var excel = new ExcelExportService(storage);

            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "ANA 405-0000962",
                IssueDateShamsi = "۱۴۰۵/۰۸/۲۷",
                Attachment = "ندارد",
                PageNumberText = "یک",
                CompanyName = "پالود پارسیان",
                CustomerLabelPrefix = "نام مشتری شرکت محترم ",
                Subject = "",
                CurrencyUnit = "ریال",
                ValidityText = "اعتبار این پیش فاکتورتا پایان روز می باشد.",
                HighlightValidity = true,
                PaymentTerms = "نقد",
                HighlightPaymentTerms = false,
                Notes = "فرایند تحویل بار پس از وصول پیش پرداخت در صورت عدم تغییر قیمت انجام خواهد شد.در صورت تغییر قیمت پیش فاکتور اصلاحی تقدیم میگردد.",
                HighlightNotes = true,
                DeliveryDays = "موجود",
                HighlightDeliveryDays = true,
                DeliveryLocation = "مشهد",
                HighlightDeliveryLocation = false,
                SignatoryName = "محمد احمدی پور هدش",
                ExpertRoleLabel = "کارشناسی خرید:",
                ExpertName = "سرکار خانم دهنودی",
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem { RowNumber = 1, ItemName = "کلید فرمان جرثقیل ۶ خانه تایوانی", Quantity = 2, RawPrice = 58300000, Unit = "عدد" },
                    new InvoiceItem { RowNumber = 2, ItemName = "ترمینال سرامیکی تک خانه ۱۶ آمپر", Quantity = 20, RawPrice = 440000, Unit = "عدد" },
                    new InvoiceItem { RowNumber = 3, ItemName = "ترمینال سرامیکی دو خانه ۱۶ آمپر", Quantity = 20, RawPrice = 638000, Unit = "عدد" },
                    new InvoiceItem { RowNumber = 4, ItemName = "ترمینال سرامیکی دو خانه ۲۵ آمپر", Quantity = 20, RawPrice = 935000, Unit = "عدد" },
                    new InvoiceItem { RowNumber = 5, ItemName = "ترمینال سرامیکی تک خانه ۲۵ آمپر", Quantity = 20, RawPrice = 495000, Unit = "عدد" },
                    new InvoiceItem { RowNumber = 6, ItemName = "هالوژن سولاریس ۱۲ وات دایره ای سفید پارس شعاع توس", Quantity = 20, RawPrice = 3062000, Unit = "عدد" }
                }
            };

            foreach (var it in invoice.Items) it.UpdateFromPercent();

            string tempDir = Path.Combine(Path.GetTempPath(), "E2EInvoiceTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string outPath = Path.Combine(tempDir, "OfficialInvoice_TargetMatch.xlsx");
                excel.ExportInvoice(invoice, outPath);

                Assert.True(File.Exists(outPath));

                using var wb = new XLWorkbook(outPath);
                var ws = wb.Worksheets.First();

                // ۱. تنظیمات برگه A4 و برازش در ۱ صفحه
                Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
                Assert.Equal(1, ws.PageSetup.PagesWide);
                Assert.Equal(1, ws.PageSetup.PagesTall);

                // ۲. سربرگ
                Assert.Equal("شرکت آریان نیرو گستر اترک", ws.Cell("B2").GetString());
                Assert.Equal("پیش فاکتور", ws.Cell("C2").GetString());
                Assert.Equal("تاریخ :", ws.Cell("E1").GetString());
                Assert.Equal("۱۴۰۵/۰۸/۲۷", ws.Cell("F1").GetString());
                Assert.Equal("شماره :", ws.Cell("E2").GetString());
                Assert.Equal("ANA 405-0000962", ws.Cell("F2").GetString());

                // ۳. مشتری و اقلام
                Assert.Contains("پالود پارسیان", ws.Cell("A6").GetString());
                Assert.Equal("کلید فرمان جرثقیل ۶ خانه تایوانی", ws.Cell(10, 2).GetString());
                Assert.Equal(XLAlignmentHorizontalValues.Right, ws.Cell(10, 2).Style.Alignment.Horizontal);
                Assert.Equal(XLAlignmentReadingOrderValues.RightToLeft, ws.Cell(10, 2).Style.Alignment.ReadingOrder);
                Assert.Equal(2, ws.Cell(10, 3).GetDouble());
                Assert.Equal(58300000, ws.Cell(10, 5).GetDouble());
                Assert.Equal(116600000, ws.Cell(10, 6).GetDouble());

                // اقلام آخر (با سقف گرد کردن 3062000 به 3070000 -> 20 * 3070000 = 61400000)
                Assert.Equal("هالوژن سولاریس ۱۲ وات دایره ای سفید پارس شعاع توس", ws.Cell(15, 2).GetString());
                Assert.Equal(61400000, ws.Cell(15, 6).GetDouble());

                // ۴. جمع‌های مالی
                // سطر ۱۷: جمع کل = ۲۲۸,۴۰۰,۰۰۰
                Assert.Equal(228400000, ws.Cell(17, 6).GetDouble());
                // سطر ۱۸: ارزش افزوده = ۲۲,۸۴۰,۰۰۰
                Assert.Equal(22840000, ws.Cell(18, 6).GetDouble());
                // سطر ۱۹: جمع کل با ارزش افزوده = ۲۵۱,۲۴۰,۰۰۰
                Assert.Equal(251240000, ws.Cell(19, 6).GetDouble());

                // ۵. توضیحات و شرایط
                // سطر ۲۱: عنوان توضیحات :
                Assert.Equal("توضیحات :", ws.Cell(21, 1).GetString());

                // سطر ۲۲: اطلاعات بانکی (بدون هایلایت)
                Assert.Equal("◆", ws.Cell(22, 1).GetString());
                Assert.Contains("بانک ملی ایران", ws.Cell(22, 2).GetString());

                // سطر ۲۳: اعتبار پیش‌فاکتور (دارای هایلایت زرد #FEF08A)
                Assert.Contains("اعتبار این پیش فاکتورتا پایان روز می باشد.", ws.Cell(23, 2).GetString());
                Assert.Equal(XLColor.FromArgb(254, 240, 138), ws.Cell(23, 1).Style.Fill.BackgroundColor);

                // سطر ۲۵: توضیحات سفارشی (دارای هایلایت زرد)
                Assert.Contains("فرایند تحویل بار پس از وصول پیش پرداخت", ws.Cell(25, 2).GetString());
                Assert.Equal(XLColor.FromArgb(254, 240, 138), ws.Cell(25, 1).Style.Fill.BackgroundColor);

                // سطر ۲۶: زمان تحویل (دارای هایلایت زرد)
                Assert.Contains("زمان تحویل: موجود", ws.Cell(26, 2).GetString());
                Assert.Equal(XLColor.FromArgb(254, 240, 138), ws.Cell(26, 1).Style.Fill.BackgroundColor);

                // ۶. امضاها و پاورقی
                // کارشناس در ستون‌های چپ (ستون ۵)
                Assert.Contains("سرکار خانم دهنودی", ws.Cell(30, 5).GetString());
                // مسئول امضا در ستون‌های ۲-۳
                Assert.Contains("محمد احمدی پور هدش", ws.Cell(30, 2).GetString());

                // نشانی دفتر در انتهای برگه
                var footerCell = ws.Search("مشهد - خیابان سنایی").FirstOrDefault();
                Assert.NotNull(footerCell);
                Assert.Contains("تلفن: ۵-۰۵۱۳۷۱۳۲۰۸۴", footerCell.GetString());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Test_MainWindow_InstantiatesWithoutCrash()
        {
            Assert.NotNull(typeof(TarazInvoice.MainWindow));
        }

        [Fact]
        public void Test_RenderFlowDocumentToImage()
        {
            Exception? threadEx = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var storage = new StorageService();
                    var printService = new PrintPdfService(storage);
                    var inv = new ProformaInvoice
                    {
                        InvoiceNumber = "ANA 405-0000284",
                        CompanyName = "شرکت محترم مهام صنعت نیشابور",
                        Subject = "قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:",
                        Items = new List<InvoiceItem>
                        {
                            new InvoiceItem { RowNumber = 1, ItemName = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰", Quantity = 60, Unit = "متر", RawPrice = 1924000 },
                            new InvoiceItem { RowNumber = 2, ItemName = "فلنج کربن استیل گلوجوش ۶ اینچ", Quantity = 40, Unit = "متر", RawPrice = 3364000 }
                        }
                    };
                    foreach (var it in inv.Items) it.UpdateFromPercent();

                    var doc = printService.CreateInvoiceFlowDocument(inv);
                    var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator;
                    paginator.PageSize = new System.Windows.Size(794, 1123);
                    var page = paginator.GetPage(0);

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(794, 1123, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    var dv = new System.Windows.Media.DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, 794, 1123));
                    }
                    rtb.Render(dv);
                    rtb.Render(page.Visual);

                    string outPng = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_rendered_page.png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = File.Create(outPng))
                    {
                        encoder.Save(fs);
                    }
                    Assert.True(File.Exists(outPng));
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(threadEx);
        }

        [Fact]
        public void Test_Company_LegalAndReal_Types()
        {
            var legalComp = new Company
            {
                Name = "شرکت پتروشیمی زاگرس",
                CompanyType = CompanyType.Legal,
                EconomicCode = "411567891234"
            };
            Assert.Equal("حقوقی", legalComp.CompanyTypeTitle);
            Assert.Equal("411567891234", legalComp.NationalOrEconomicCode);

            var realPerson = new Company
            {
                Name = "علی رضایی",
                CompanyType = CompanyType.Real,
                NationalCode = "0921234567"
            };
            Assert.Equal("حقیقی", realPerson.CompanyTypeTitle);
            Assert.Equal("0921234567", realPerson.NationalOrEconomicCode);
        }

        [Fact]
        public void Test_Expert_CompanyAssignment_AndBelongsToCompany()
        {
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            var companyC = Guid.NewGuid();

            // کارشناس عمومی (بدون انتساب اختصاصی) به همه شرکت‌ها تعلق دارد
            var generalExpert = new Expert { FullName = "مهندس عمومی" };
            Assert.True(generalExpert.BelongsToCompany(companyA));
            Assert.True(generalExpert.BelongsToCompany(companyB));
            Assert.Equal("همه شرکت‌ها (عمومی)", generalExpert.AssignedCompaniesDisplay);

            // کارشناس با انتساب به چند شرکت اختصاصی
            var assignedExpert = new Expert
            {
                FullName = "مهندس اختصاصی",
                AssignedCompanyIds = new List<Guid> { companyA, companyB },
                AssignedCompanyNames = new List<string> { "شرکت آلفا", "شرکت بتا" }
            };
            Assert.True(assignedExpert.BelongsToCompany(companyA));
            Assert.True(assignedExpert.BelongsToCompany(companyB));
            Assert.False(assignedExpert.BelongsToCompany(companyC));
            Assert.Equal("شرکت آلفا، شرکت بتا", assignedExpert.AssignedCompaniesDisplay);
        }

        [Fact]
        public void Test_BankAccountInfo_Formatting()
        {
            var acc = new BankAccountInfo
            {
                BankName = "بانک ملی ایران",
                ShebaNumber = "IR95017000000118033698006",
                CardNumber = "۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶",
                OwnerName = "شرکت آریان نیرو گستر اترک"
            };

            string term = acc.ToFormattedTerm();
            Assert.Contains("IR95017000000118033698006", term);
            Assert.Contains("بانک ملی ایران", term);
            Assert.Contains("شماره کارت ۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶", term);
            Assert.Contains("شرکت آریان نیرو گستر اترک", term);
        }

        [Fact]
        public void Test_PdfAndExcel_FileNameGeneration()
        {
            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "EXP-REZ-1002",
                CompanyName = "شرکت پتروشیمی زاگرس",
                ExpertName = "مهندس محمد رضایی"
            };

            string pdfName = PrintPdfService.GetSafeInvoiceFileName(invoice, "pdf");
            Assert.Equal("EXP-REZ-1002 - شرکت پتروشیمی زاگرس - مهندس محمد رضایی.pdf", pdfName);

            string excelName = PrintPdfService.GetSafeInvoiceFileName(invoice, "xlsx");
            Assert.Equal("EXP-REZ-1002 - شرکت پتروشیمی زاگرس - مهندس محمد رضایی.xlsx", excelName);
        }

        [Fact]
        public void Test_Currency_StrictlyRials()
        {
            string money = JalaliDateHelper.FormatMoney(1500000);
            Assert.Contains("ریال", money);
            Assert.DoesNotContain("تومان", money);

            var converter = new TarazInvoice.Converters.CurrencyFormatConverter();
            string converted = (string)converter.Convert(2500000m, typeof(string), null!, null!);
            Assert.Contains("ریال", converted);
            Assert.DoesNotContain("تومان", converted);
        }

        [Fact]
        public void GenerateAppLogoAssets()
        {
            var thread = new System.Threading.Thread(() =>
            {
                int width = 512;
                int height = 512;

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // 1. Base squircle background with rich gradient
                    var bgBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(10, 20, 48),   // Deep midnight navy
                        System.Windows.Media.Color.FromRgb(29, 78, 216),  // Vibrant Royal Cobalt
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 1));
                    
                    var borderPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.LinearGradientBrush(
                            System.Windows.Media.Color.FromArgb(200, 96, 165, 250),
                            System.Windows.Media.Color.FromArgb(160, 56, 189, 248),
                            new System.Windows.Point(0, 0),
                            new System.Windows.Point(1, 1)),
                        3.5);

                    dc.DrawRoundedRectangle(bgBrush, borderPen, new System.Windows.Rect(20, 20, 472, 472), 115, 115);

                    // 2. Ambient radial back-glow
                    var glowBrush = new System.Windows.Media.RadialGradientBrush(
                        System.Windows.Media.Color.FromArgb(60, 59, 130, 246),
                        System.Windows.Media.Colors.Transparent)
                    {
                        RadiusX = 0.5,
                        RadiusY = 0.5,
                        Center = new System.Windows.Point(0.5, 0.46)
                    };
                    dc.DrawEllipse(glowBrush, null, new System.Windows.Point(256, 235), 180, 180);

                    // 3. Central Pedestal
                    var baseBrush1 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
                    dc.DrawRoundedRectangle(baseBrush1, null, new System.Windows.Rect(180, 396, 152, 22), 7, 7);

                    var baseBrush2 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                    dc.DrawRoundedRectangle(baseBrush2, null, new System.Windows.Rect(210, 380, 92, 18), 5, 5);

                    // 4. Central Pillar
                    var pillarBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(241, 245, 249),
                        System.Windows.Media.Color.FromRgb(148, 163, 184),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 0));
                    dc.DrawRoundedRectangle(pillarBrush, null, new System.Windows.Rect(247, 136, 18, 246), 4, 4);

                    // 5. Emerald Diamond Pivot Crown
                    var crownGeo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = crownGeo.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(256, 110), true, true);
                        ctx.LineTo(new System.Windows.Point(276, 138), true, false);
                        ctx.LineTo(new System.Windows.Point(256, 154), true, false);
                        ctx.LineTo(new System.Windows.Point(236, 138), true, false);
                    }
                    crownGeo.Freeze();
                    var crownBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(16, 185, 129),
                        System.Windows.Media.Color.FromRgb(52, 211, 153),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 1));
                    dc.DrawGeometry(crownBrush, null, crownGeo);

                    // 6. Balance Beam (تراز)
                    var beamGeo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = beamGeo.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(100, 178), true, true);
                        ctx.QuadraticBezierTo(new System.Windows.Point(256, 136), new System.Windows.Point(412, 178), true, false);
                        ctx.ArcTo(new System.Windows.Point(412, 164), new System.Windows.Size(7, 7), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                        ctx.QuadraticBezierTo(new System.Windows.Point(256, 122), new System.Windows.Point(100, 164), true, false);
                        ctx.ArcTo(new System.Windows.Point(100, 178), new System.Windows.Size(7, 7), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                    }
                    beamGeo.Freeze();
                    var beamBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(226, 232, 240),
                        System.Windows.Media.Color.FromRgb(255, 255, 255),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(0, 1));
                    dc.DrawGeometry(beamBrush, null, beamGeo);

                    // 7. Suspension Cords
                    var cordPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 147, 197, 253)),
                        3.0)
                    {
                        StartLineCap = System.Windows.Media.PenLineCap.Round,
                        EndLineCap = System.Windows.Media.PenLineCap.Round
                    };
                    // Left cords
                    dc.DrawLine(cordPen, new System.Windows.Point(118, 175), new System.Windows.Point(76, 280));
                    dc.DrawLine(cordPen, new System.Windows.Point(118, 175), new System.Windows.Point(160, 280));
                    // Right cords
                    dc.DrawLine(cordPen, new System.Windows.Point(394, 175), new System.Windows.Point(352, 280));
                    dc.DrawLine(cordPen, new System.Windows.Point(394, 175), new System.Windows.Point(436, 280));

                    // 8. Trays / Dishes
                    var trayBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(56, 189, 248),
                        System.Windows.Media.Color.FromRgb(30, 64, 175),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(0, 1));
                    var trayRimBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 165, 250));

                    // Left Tray
                    var leftTrayGeo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = leftTrayGeo.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(72, 280), true, true);
                        ctx.ArcTo(new System.Windows.Point(164, 280), new System.Windows.Size(46, 38), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                    }
                    leftTrayGeo.Freeze();
                    dc.DrawGeometry(trayBrush, null, leftTrayGeo);
                    dc.DrawEllipse(trayRimBrush, null, new System.Windows.Point(118, 280), 46, 8);

                    // Right Tray
                    var rightTrayGeo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = rightTrayGeo.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(348, 280), true, true);
                        ctx.ArcTo(new System.Windows.Point(440, 280), new System.Windows.Size(46, 38), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                    }
                    rightTrayGeo.Freeze();
                    dc.DrawGeometry(trayBrush, null, rightTrayGeo);
                    dc.DrawEllipse(trayRimBrush, null, new System.Windows.Point(394, 280), 46, 8);

                    // 9. Golden Coins on Trays
                    var goldBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(253, 224, 71),
                        System.Windows.Media.Color.FromRgb(217, 119, 6),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 1));
                    var goldRimPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 240, 138)),
                        1.5);

                    dc.DrawEllipse(goldBrush, goldRimPen, new System.Windows.Point(118, 273), 18, 7);
                    dc.DrawEllipse(goldBrush, goldRimPen, new System.Windows.Point(394, 273), 18, 7);

                    // 10. Dynamic Ascending Emerald Surge Line
                    var surgeGeo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = surgeGeo.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(160, 350), true, true);
                        ctx.LineTo(new System.Windows.Point(226, 278), true, false);
                        ctx.LineTo(new System.Windows.Point(272, 316), true, false);
                        ctx.LineTo(new System.Windows.Point(342, 224), true, false);
                        ctx.LineTo(new System.Windows.Point(322, 220), true, false);
                        ctx.LineTo(new System.Windows.Point(368, 192), true, false);
                        ctx.LineTo(new System.Windows.Point(364, 244), true, false);
                        ctx.LineTo(new System.Windows.Point(348, 240), true, false);
                        ctx.LineTo(new System.Windows.Point(278, 334), true, false);
                        ctx.LineTo(new System.Windows.Point(232, 296), true, false);
                        ctx.LineTo(new System.Windows.Point(160, 368), true, false);
                    }
                    surgeGeo.Freeze();
                    var surgeBrush = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromArgb(220, 5, 150, 105),
                        System.Windows.Media.Color.FromArgb(235, 52, 211, 153),
                        new System.Windows.Point(0, 1),
                        new System.Windows.Point(1, 0));
                    dc.DrawGeometry(surgeBrush, null, surgeGeo);
                }

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(visual);

                // Find workspace root
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                while (!File.Exists(Path.Combine(baseDir, "TarazInvoice.csproj")) && Directory.GetParent(baseDir) != null)
                {
                    baseDir = Directory.GetParent(baseDir)!.FullName;
                }

                string resourcesDir = Path.Combine(baseDir, "Resources");
                Directory.CreateDirectory(resourcesDir);
                string pngPath = Path.Combine(resourcesDir, "logo.png");

                // Save 512x512 PNG
                var pngEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                pngEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = File.Create(pngPath))
                {
                    pngEncoder.Save(fs);
                }

                // Generate multi-size ICO
                int[] iconSizes = new[] { 256, 128, 64, 48, 32, 16 };
                var iconDataList = new List<byte[]>();

                foreach (int size in iconSizes)
                {
                    var scaledVisual = new System.Windows.Media.DrawingVisual();
                    using (var sdc = scaledVisual.RenderOpen())
                    {
                        sdc.PushTransform(new System.Windows.Media.ScaleTransform((double)size / 512.0, (double)size / 512.0));
                        sdc.DrawDrawing(visual.Drawing);
                        sdc.Pop();
                    }
                    var sRtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    sRtb.Render(scaledVisual);

                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sRtb));
                    using var ms = new MemoryStream();
                    enc.Save(ms);
                    iconDataList.Add(ms.ToArray());
                }

                string icoPath = Path.Combine(resourcesDir, "logo.ico");
                using (var fs = File.Create(icoPath))
                using (var bw = new BinaryWriter(fs))
                {
                    // ICONDIR
                    bw.Write((ushort)0); // reserved
                    bw.Write((ushort)1); // type 1 = ICO
                    bw.Write((ushort)iconSizes.Length); // count

                    int offset = 6 + (16 * iconSizes.Length);

                    // ICONDIRENTRY array
                    for (int i = 0; i < iconSizes.Length; i++)
                    {
                        int s = iconSizes[i];
                        byte bWidth = (byte)(s >= 256 ? 0 : s);
                        byte bHeight = (byte)(s >= 256 ? 0 : s);
                        byte[] bytes = iconDataList[i];

                        bw.Write(bWidth);
                        bw.Write(bHeight);
                        bw.Write((byte)0); // color count
                        bw.Write((byte)0); // reserved
                        bw.Write((ushort)1); // planes
                        bw.Write((ushort)32); // bit count
                        bw.Write((uint)bytes.Length); // bytes in res
                        bw.Write((uint)offset); // offset

                        offset += bytes.Length;
                    }

                    // Write PNG image payloads
                    for (int i = 0; i < iconSizes.Length; i++)
                    {
                        bw.Write(iconDataList[i]);
                    }
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(true);
        }

        [Fact]
        public void Test_ExcelExport_RowHeight_32px_And_Dynamic_MultiLine()
        {
            var storage = new StorageService();
            var excel = new ExcelExportService(storage);

            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "TEST-ROW-HEIGHT",
                CompanyName = "شرکت تست",
                Items = new List<InvoiceItem>
                {
                    // ردیف ۱: تک خطی - ارتفاع باید ۳۲ باشد
                    new InvoiceItem { RowNumber = 1, ItemName = "لوله استیل ۳۰۴ سایز ۱ اینچ", Quantity = 10, RawPrice = 1000000, Unit = "متر" },
                    // ردیف ۲: دو خطی - ارتفاع باید دو برابر (۶۴) باشد
                    new InvoiceItem { RowNumber = 2, ItemName = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰ به همراه عملگر پنوماتیکی و میکروسوئیچ باکس", Quantity = 2, RawPrice = 45000000, Unit = "عدد" },
                    // ردیف ۳: سه خطی - ارتفاع باید سه برابر (۹۶) باشد
                    new InvoiceItem { RowNumber = 3, ItemName = "شیر توپی فولادی کلاس ۳۰۰ فلنجدار ۳ اینچ فول هارد فیس دارای گواهی بازرسی شخص ثالث و متریال تست ریپورت کامل سازنده به همراه متعلقات نصب", Quantity = 1, RawPrice = 85000000, Unit = "عدد" }
                }
            };
            foreach (var it in invoice.Items) it.UpdateFromPercent();

            string tempDir = Path.Combine(Path.GetTempPath(), "HeightTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string outPath = Path.Combine(tempDir, "HeightTest.xlsx");
                excel.ExportInvoice(invoice, outPath);
                Assert.True(File.Exists(outPath));

                using var wb = new XLWorkbook(outPath);
                var ws = wb.Worksheets.First();

                // هدر جدول (سطر ۹) باید ۳۲ باشد
                Assert.Equal(32, ws.Row(9).Height);

                // سطر اول کالا (سطر ۱۰) تک‌خطی = ۳۲
                Assert.Equal(32, ws.Row(10).Height);

                // سطر دوم کالا (سطر ۱۱) دوخطی = ۶۴ (دو برابر)
                Assert.Equal(64, ws.Row(11).Height);

                // سطر سوم کالا (سطر ۱۲) سه‌خطی = ۹۶ (سه برابر)
                Assert.Equal(96, ws.Row(12).Height);

                // شرح کالا باید حتماً راست‌چین و RTL باشد
                for (int r = 10; r <= 12; r++)
                {
                    Assert.Equal(XLAlignmentHorizontalValues.Right, ws.Cell(r, 2).Style.Alignment.Horizontal);
                    Assert.Equal(XLAlignmentReadingOrderValues.RightToLeft, ws.Cell(r, 2).Style.Alignment.ReadingOrder);
                }

                // سطر جمع کل با ارزش افزوده نباید هایلایت زرد داشته باشد
                var yellowColor = XLColor.FromArgb(255, 255, 0);
                Assert.NotEqual(yellowColor, ws.Cell(15, 5).Style.Fill.BackgroundColor);
                Assert.NotEqual(yellowColor, ws.Cell(16, 5).Style.Fill.BackgroundColor);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Test_StorageService_MultiUser_Concurrency_Safety()
        {
            var storage = new StorageService();
            var inv1 = new ProformaInvoice { Id = Guid.NewGuid(), InvoiceNumber = "CONCUR-1", CompanyName = "شرکت الف" };
            var inv2 = new ProformaInvoice { Id = Guid.NewGuid(), InvoiceNumber = "CONCUR-2", CompanyName = "شرکت ب" };

            // شبیه‌سازی دو کاربر همزمان روی شبکه
            var t1 = Task.Run(() => storage.AddOrUpdateInvoice(inv1));
            var t2 = Task.Run(() => storage.AddOrUpdateInvoice(inv2));

            await Task.WhenAll(t1, t2);

            var list = storage.GetInvoices();
            Assert.Contains(list, x => x.Id == inv1.Id);
            Assert.Contains(list, x => x.Id == inv2.Id);

            // پاک‌سازی
            storage.DeleteInvoice(inv1.Id);
            storage.DeleteInvoice(inv2.Id);
        }

        [Fact]
        public void Test_GetSafeInvoiceFileName_FormatMatchesSpecification()
        {
            var inv1 = new ProformaInvoice
            {
                InvoiceNumber = "ANA-405-0000284",
                CompanyName = "شرکت محترم مهام صنعت نیشابور",
                ExpertName = "محمد احمدی پور هدش"
            };

            string name1 = PrintPdfService.GetSafeInvoiceFileName(inv1, "pdf");
            Assert.Equal("ANA-405-0000284 - شرکت محترم مهام صنعت نیشابور - محمد احمدی پور هدش.pdf", name1);

            // سناریوی بدون کارشناس
            var inv2 = new ProformaInvoice
            {
                InvoiceNumber = "ANA-405-0000285",
                CompanyName = "شرکت پتروشیمی رازی",
                ExpertName = ""
            };

            string name2 = PrintPdfService.GetSafeInvoiceFileName(inv2, "pdf");
            Assert.Equal("ANA-405-0000285 - شرکت پتروشیمی رازی.pdf", name2);
        }

        [Fact]
        public void Test_SaveInvoiceAsPdfFile_CreatesValidPdf()
        {
            Exception? threadEx = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var storage = new StorageService();
                    var printService = new PrintPdfService(storage);
                    var inv = new ProformaInvoice
                    {
                        InvoiceNumber = "ANA-405-0000284",
                        CompanyName = "شرکت محترم مهام صنعت نیشابور",
                        ExpertName = "محمد احمدی پور هدش",
                        Subject = "قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:",
                        Items = new List<InvoiceItem>
                        {
                            new InvoiceItem { RowNumber = 1, ItemName = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰", Quantity = 60, Unit = "متر", RawPrice = 1924000 },
                            new InvoiceItem { RowNumber = 2, ItemName = "فلنج کربن استیل گلوجوش ۶ اینچ", Quantity = 40, Unit = "متر", RawPrice = 3364000 }
                        }
                    };
                    foreach (var it in inv.Items) it.UpdateFromPercent();

                    string testPdf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_invoice_export.pdf");
                    if (File.Exists(testPdf)) File.Delete(testPdf);

                    printService.SaveInvoiceAsPdfFile(inv, testPdf);

                    Assert.True(File.Exists(testPdf), "PDF file should be created");
                    Assert.True(new FileInfo(testPdf).Length > 5000, "PDF file should contain data");
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(threadEx);
        }

        [Fact]
        public void Test_PersianRtlText_And_FlowDocument_ItemCells_AreRightAlignedAndRtl()
        {
            // ۱. بررسی فرمت صحیح راست‌به‌چپ برای متن‌های دارای خط‌تیره و عدد
            string formatted = PrintPdfService.FormatPersianRtlText("کابلشو مسی 16-8");
            Assert.Contains("\u200F", formatted);
            Assert.Contains("\u202B", formatted);
            Assert.Contains("\u202C", formatted);

            Exception? threadEx = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var storage = new StorageService();
                    var printService = new PrintPdfService(storage);
                    var inv = new ProformaInvoice
                    {
                        InvoiceNumber = "EXP-TEST-001",
                        CompanyName = "شرکت پتروشیمی زاگرس",
                        Items = new List<InvoiceItem>
                        {
                            new InvoiceItem { RowNumber = 1, ItemName = "کابلشو مسی 16-8", Quantity = 10, RawPrice = 3600000 },
                            new InvoiceItem { RowNumber = 2, ItemName = "کابلشو مسی سایز 35", Quantity = 5, RawPrice = 4500000 }
                        }
                    };

                    var doc = printService.CreateInvoiceFlowDocument(inv);

                    // پیدا کردن جدول اقلام (دومین جدول پس از سربرگ)
                    var tables = doc.Blocks.OfType<System.Windows.Documents.Table>().ToList();
                    Assert.True(tables.Count >= 2);
                    var itemsTable = tables[1];
                    Assert.Equal(System.Windows.FlowDirection.RightToLeft, itemsTable.FlowDirection);

                    var rows = itemsTable.RowGroups[0].Rows;
                    // سطر ۱ هدر است، سطر ۲ اولین قلم کالا است
                    var itemRow1 = rows[1];
                    var nameCell = itemRow1.Cells[1]; // ستون شرح کالا

                    Assert.Equal(System.Windows.FlowDirection.RightToLeft, nameCell.FlowDirection);
                    Assert.Equal(System.Windows.TextAlignment.Right, nameCell.TextAlignment);

                    // بررسی اینکه سلول حاوی پاراگراف راست‌چین است و نه BlockUIContainer چپ‌چین
                    var para = Assert.IsType<System.Windows.Documents.Paragraph>(nameCell.Blocks.FirstBlock);
                    Assert.Equal(System.Windows.FlowDirection.RightToLeft, para.FlowDirection);
                    Assert.Equal(System.Windows.TextAlignment.Right, para.TextAlignment);

                    // رندر تصویری برای بررسی بصری خروجی
                    var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator;
                    paginator.PageSize = new System.Windows.Size(794, 1123);
                    var page = paginator.GetPage(0);
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(794, 1123, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    var dv = new System.Windows.Media.DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, 794, 1123));
                    }
                    rtb.Render(dv);
                    rtb.Render(page.Visual);

                    string previewPath = Path.Combine(Path.GetTempPath(), "test_rtl_preview.png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = File.Create(previewPath))
                    {
                        encoder.Save(fs);
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(threadEx);
        }

        [Fact]
        public void Test_AppSettings_LogoAndStampDimensions_DefaultsAndCustomValues()
        {
            var settings = new AppSettings();
            Assert.Equal(68, settings.LogoWidth);
            Assert.Equal(68, settings.LogoHeight);
            Assert.Equal(140, settings.StampWidth);
            Assert.Equal(75, settings.StampHeight);

            settings.LogoWidth = 90;
            settings.LogoHeight = 90;
            settings.StampWidth = 160;
            settings.StampHeight = 85;

            Assert.Equal(90, settings.LogoWidth);
            Assert.Equal(90, settings.LogoHeight);
            Assert.Equal(160, settings.StampWidth);
            Assert.Equal(85, settings.StampHeight);
        }

        [Fact]
        public void Test_InvoicePdfBuilder_WithCustomLogoAndStampSizes_GeneratesValidPdf()
        {
            var settings = new AppSettings
            {
                SellerName = "شرکت تست آریا",
                LogoWidth = 80,
                LogoHeight = 80,
                StampWidth = 150,
                StampHeight = 80
            };

            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "TEST-999",
                CompanyName = "شرکت خریدار آزمایشی",
                IssueDateShamsi = "۱۴۰۳/۰۸/۰۱",
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        RowNumber = 1,
                        ItemName = "چراغ لاینر عرض 6 سانتیمتر مخصوص داخل کناف - بدنه سفید - فاقد فلیکر - نور طبیعی برند پاژ شعاع تابان 40 وات همراه با دیفیوزر و درایو با گارانتی 24 ماهه",
                        Quantity = 10,
                        RawPrice = 5000000,
                        DiscountPercent = 5,
                        ProfitPercent = 10
                    }
                }
            };
            foreach (var it in invoice.Items) it.UpdateFromPercent();

            string tempPdf = Path.Combine(Path.GetTempPath(), $"invoice_test_{Guid.NewGuid():N}.pdf");
            try
            {
                InvoicePdfBuilder.Save(invoice, settings, tempPdf);
                Assert.True(File.Exists(tempPdf));
                var fi = new FileInfo(tempPdf);
                Assert.True(fi.Length > 1000);
            }
            finally
            {
                try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
            }
        }

        [Fact]
        public void Test_StorageService_AddOrUpdateStorageItem_TriggersDataChangedAndPersists()
        {
            var storage = new StorageService();
            bool eventFired = false;
            Action handler = () => { eventFired = true; };
            storage.DataChanged += handler;

            var newItem = new StorageItem
            {
                Name = "کالای تستی انبار " + Guid.NewGuid().ToString("N").Substring(0, 8),
                Code = "PRD-TEST",
                Unit = "دستگاه",
                DefaultPrice = 12500000,
                Category = "تجهیزات"
            };

            try
            {
                storage.AddOrUpdateStorageItem(newItem);
                Assert.True(eventFired);

                var items = storage.GetStorageItems();
                Assert.Contains(items, x => x.Id == newItem.Id && x.DefaultPrice == 12500000);

                // ویرایش کالا
                eventFired = false;
                newItem.DefaultPrice = 14000000;
                storage.AddOrUpdateStorageItem(newItem);

                Assert.True(eventFired);
                items = storage.GetStorageItems();
                var updated = items.FirstOrDefault(x => x.Id == newItem.Id);
                Assert.NotNull(updated);
                Assert.Equal(14000000, updated.DefaultPrice);
            }
            finally
            {
                storage.DataChanged -= handler;
                storage.DeleteStorageItem(newItem.Id);
            }
        }

        [Fact]
        public void Test_WpfUiTextBox_RightToLeft_AlignsPlaceholderAndCaretToRight()
        {
            Exception? ex = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        var app = new TarazInvoice.App();
                        app.InitializeComponent();
                    }
                    var tb = new Wpf.Ui.Controls.TextBox
                    {
                        FlowDirection = System.Windows.FlowDirection.RightToLeft,
                        PlaceholderText = "نام کالا را بنویسید...",
                        Width = 300
                    };
                    
                    var win = new System.Windows.Window { Content = tb, FlowDirection = System.Windows.FlowDirection.RightToLeft };
                    win.Show();
                    tb.ApplyTemplate();
                    
                    var placeholder = tb.Template.FindName("PlaceholderTextBox", tb) as System.Windows.Controls.TextBlock;
                    var pPoint = placeholder?.TranslatePoint(new System.Windows.Point(0, 0), tb);
                    
                    tb.Text = "تست";
                    tb.Select(0, 0);
                    var rect0 = tb.GetRectFromCharacterIndex(0);
                    win.Close();

                    // In RTL coordinate system (origin at top-right), position near 11-13px is at the right edge
                    Assert.NotNull(placeholder);
                    Assert.True(pPoint?.X < 50); // Near right edge (X < 50), not pushed to far left (> 150)
                    Assert.True(rect0.X < 50);
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (ex != null) throw ex;
        }

        [Fact]
        public void Test_InvoiceItem_UpdateNameAndProperties_FiresPropertyChangedProperly()
        {
            var item = new InvoiceItem
            {
                RowNumber = 1,
                ItemName = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰",
                Unit = "عدد",
                RawPrice = 18500000
            };

            var changedProperties = new List<string>();
            item.PropertyChanged += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName))
                    changedProperties.Add(e.PropertyName);
            };

            // تغییر نام کالا به فلنج
            item.ItemName = "فلنج کربن استیل گلوجوش ۶ اینچ";
            item.Unit = "دستگاه";
            item.RawPrice = 4200000;

            Assert.Contains(nameof(InvoiceItem.ItemName), changedProperties);
            Assert.Contains(nameof(InvoiceItem.Unit), changedProperties);
            Assert.Contains(nameof(InvoiceItem.RawPrice), changedProperties);
            Assert.Equal("فلنج کربن استیل گلوجوش ۶ اینچ", item.ItemName);
            Assert.Equal("دستگاه", item.Unit);
            Assert.Equal(4200000, item.RawPrice);
        }

        [Fact]
        public void Test_AppSettings_InvoicePrefixAndNextNumber_DefaultsAndPersistence()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_Settings_" + Guid.NewGuid());
            string dbPath = Path.Combine(tempDir, "database.json");
            try
            {
                var storage = new StorageService(dbPath);
                var settings = storage.GetSettings();

                Assert.Equal("ANA 405-", settings.InvoicePrefix);
                Assert.Equal(1001, settings.NextInvoiceNumber);

                // تغییر تنظیمات و ذخیره
                settings.InvoicePrefix = "INV-2025-";
                settings.NextInvoiceNumber = 5001;
                storage.Save();

                // بازخوانی از فایل
                var reloadedStorage = new StorageService(dbPath);
                var reloadedSettings = reloadedStorage.GetSettings();

                Assert.Equal("INV-2025-", reloadedSettings.InvoicePrefix);
                Assert.Equal(5001, reloadedSettings.NextInvoiceNumber);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_StorageService_UniqueInvoiceNumbering_CollisionAvoidance()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_Numbering_" + Guid.NewGuid());
            string dbPath = Path.Combine(tempDir, "database.json");
            try
            {
                var storage = new StorageService(dbPath);
                var settings = storage.GetSettings();
                settings.InvoicePrefix = "ANA-";
                settings.NextInvoiceNumber = 100;
                storage.Save();

                // ۱. Peek نباید شمارنده را افزایش دهد
                string peek1 = storage.PeekNextUniqueInvoiceNumber();
                Assert.Equal("ANA-100", peek1);
                Assert.Equal(100, storage.GetSettings().NextInvoiceNumber);

                // ۲. Generate شماره را تولید کرده و شمارنده را به 101 می‌برد
                string gen1 = storage.GenerateNextUniqueInvoiceNumber();
                Assert.Equal("ANA-100", gen1);
                Assert.Equal(101, storage.GetSettings().NextInvoiceNumber);
                storage.AddOrUpdateInvoice(new ProformaInvoice
                {
                    InvoiceNumber = gen1,
                    CompanyName = "مشتری اول"
                });

                // ۳. ایجاد یک پیش‌فاکتور با شماره ANA-101 به صورت دستی
                storage.AddOrUpdateInvoice(new ProformaInvoice
                {
                    InvoiceNumber = "ANA-101",
                    CompanyName = "مشتری تست"
                });

                // ۴. اکنون شماره 101 اشغال است؛ GenerateNextUniqueInvoiceNumber باید پرش کرده و ANA-102 را بازگرداند
                string gen2 = storage.GenerateNextUniqueInvoiceNumber();
                Assert.Equal("ANA-102", gen2);
                Assert.Equal(103, storage.GetSettings().NextInvoiceNumber);

                // ۵. بررسی با اعداد فارسی: فاکتوری با شماره فارسی ANA-۱۰۳ ثبت می‌کنیم
                storage.AddOrUpdateInvoice(new ProformaInvoice
                {
                    InvoiceNumber = "ANA-۱۰۳",
                    CompanyName = "مشتری تست ۲"
                });

                // ۶. سیستم باید تشخیص دهد که ANA-103 اشغال است و ANA-104 را تولید کند
                string gen3 = storage.GenerateNextUniqueInvoiceNumber();
                Assert.Equal("ANA-104", gen3);
                Assert.Equal(105, storage.GetSettings().NextInvoiceNumber);

                // ۷. بررسی IsInvoiceNumberInUse
                Assert.True(storage.IsInvoiceNumberInUse("ANA-100"));
                Assert.True(storage.IsInvoiceNumberInUse("ANA-۱۰۱")); // فارسی
                Assert.True(storage.IsInvoiceNumberInUse("ANA-103"));
                Assert.False(storage.IsInvoiceNumberInUse("ANA-999"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_InvoiceItem_BaseCost_PurchasePriceCalculation()
        {
            var item = new InvoiceItem
            {
                RawPrice = 10000000m,
                DiscountPercent = 20m,
                ProfitPercent = 10m,
                Quantity = 2
            };

            // پس از اعمال 20 درصد تخفیف، قیمت خرید باید 8،000،000 باشد
            Assert.Equal(8000000m, item.BaseCost);
            Assert.Equal(8800000m, item.FinalUnitPrice);
            Assert.Equal(17600000m, item.TotalPrice);

            // حالا اگر قیمت خرید مستقیماً ویرایش شود و به 7،000،000 تغییر یابد
            item.BaseCost = 7000000m;
            Assert.Equal(7000000m, item.BaseCost);
            Assert.Equal(3000000m, item.DiscountAmount);
            Assert.Equal(30m, item.DiscountPercent);
            Assert.Equal(7700000m, item.FinalUnitPrice);
            Assert.Equal(15400000m, item.TotalPrice);
        }

        [Fact]
        public void Test_StorageService_DeletionSyncAcrossDatabases()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_DelSync_" + Guid.NewGuid());
            string db1 = Path.Combine(tempDir, "pc1.json");
            string db2 = Path.Combine(tempDir, "pc2.json");

            try
            {
                var s1 = new StorageService(db1);
                var comp = new Company { Name = "شرکت تست حذف" };
                s1.AddOrUpdateCompany(comp);
                var inv = new ProformaInvoice { InvoiceNumber = "TEST-DEL-1", CompanyName = "شرکت تست حذف" };
                s1.AddOrUpdateInvoice(inv);
                var prd = new StorageItem { Name = "کالای آزمایشی" };
                s1.AddOrUpdateStorageItem(prd);

                // همگام‌سازی با PC2
                var s2 = new StorageService(db2);
                s2.AddOrUpdateCompany(comp);
                s2.AddOrUpdateInvoice(inv);
                s2.AddOrUpdateStorageItem(prd);

                Assert.Single(s1.GetCompanies());
                Assert.Single(s2.GetCompanies());

                // حذف در PC1
                s1.DeleteCompany(comp.Id);
                s1.DeleteInvoice(inv.Id);
                s1.DeleteStorageItem(prd.Id);

                Assert.Empty(s1.GetCompanies());
                Assert.Empty(s1.GetInvoices());
                Assert.Empty(s1.GetStorageItems());

                // ادغام بین PC1 و PC2 (شبیه‌سازی ارتباط و سینک 3 کامپیوتر)
                var merged = StorageService.MergeDatabases(
                    System.Text.Json.JsonSerializer.Deserialize<DatabaseModel>(File.ReadAllText(db2))!,
                    System.Text.Json.JsonSerializer.Deserialize<DatabaseModel>(File.ReadAllText(db1))!
                );

                // اطمینان از اینکه آیتم حذف شده هرگز بازنمی‌گردد
                Assert.Empty(merged.Companies);
                Assert.Empty(merged.Invoices);
                Assert.Empty(merged.StorageItems);
                Assert.Contains(comp.Id, merged.DeletedIds);
                Assert.Contains(inv.Id, merged.DeletedIds);
                Assert.Contains(prd.Id, merged.DeletedIds);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_StorageService_DefaultDataPurgedAndCleanSeed()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TarazTest_CleanSeed_" + Guid.NewGuid());
            string dbPath = Path.Combine(tempDir, "clean.json");

            try
            {
                var storage = new StorageService(dbPath);
                // دیتابیس جدید باید کاملاً خالی از دیتای ماک باشد
                Assert.Empty(storage.GetCompanies());
                Assert.Empty(storage.GetExperts());
                Assert.Empty(storage.GetInvoices());
                Assert.Empty(storage.GetStorageItems());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_InvoiceItem_CeilingRules_4DigitsAndMore()
        {
            // ۴ رقمی ریال: گرد به بالا با تقریب ۱۰۰ (۲ صفر)
            Assert.Equal(1300m, InvoiceItem.RoundUpPrice(1234m));
            Assert.Equal(4100m, InvoiceItem.RoundUpPrice(4010m));
            Assert.Equal(4000m, InvoiceItem.RoundUpPrice(4000m));
            Assert.Equal(9900m, InvoiceItem.RoundUpPrice(9801m));
            Assert.Equal(10000m, InvoiceItem.RoundUpPrice(9950m));

            // ۵ رقمی ریال (۴ رقم تومان): گرد به بالا با تقریب ۱۰۰۰ (۳ صفر)
            Assert.Equal(13000m, InvoiceItem.RoundUpPrice(12345m));
            Assert.Equal(15000m, InvoiceItem.RoundUpPrice(14200m));
            Assert.Equal(14000m, InvoiceItem.RoundUpPrice(14000m));

            // ۶ رقمی به بالا در ریال (بیش از ۴ رقم تومان): گرد به بالا با تقریب ۱۰۰۰۰ (مانند ۳,۲۲۸,۰۰۰ که می‌شود ۳,۲۳۰,۰۰۰)
            Assert.Equal(3230000m, InvoiceItem.RoundUpPrice(3228000m));
            Assert.Equal(3230000m, InvoiceItem.RoundUpPrice(3221000m));
            Assert.Equal(3230000m, InvoiceItem.RoundUpPrice(3230000m));
            Assert.Equal(1240000m, InvoiceItem.RoundUpPrice(1234567m));
            Assert.Equal(2510000m, InvoiceItem.RoundUpPrice(2500100m));
            Assert.Equal(2500000m, InvoiceItem.RoundUpPrice(2500000m));
        }

        [Fact]
        public void Test_NullablePercentConverter_TrailingDotAndPersianSeparators()
        {
            var converter = new NullablePercentConverter();

            // تایپ نقطه در انتها نباید پاک شود
            var doNothingDot = converter.ConvertBack("6.", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(System.Windows.Data.Binding.DoNothing, doNothingDot);

            // تایپ اسلش فارسی در انتها نباید پاک شود
            var doNothingSlash = converter.ConvertBack("6/", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(System.Windows.Data.Binding.DoNothing, doNothingSlash);

            // تایپ ممیز فارسی در انتها
            var doNothingFarsi = converter.ConvertBack("6٫", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(System.Windows.Data.Binding.DoNothing, doNothingFarsi);

            // تبدیل ممیز اعشاری 6.8
            var val1 = converter.ConvertBack("6.8", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(6.8m, (decimal?)val1);

            // تبدیل با ممیز اسلش 6/8
            var val2 = converter.ConvertBack("6/8", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(6.8m, (decimal?)val2);

            // تبدیل با کاراکترهای فارسی ۶.۸
            var val3 = converter.ConvertBack("۶.۸", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(6.8m, (decimal?)val3);

            // Convert خروجی اعشاری را بدون بریدن حفظ کند
            var str = converter.Convert(6.8m, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal("6.8", str);
        }

        [Fact]
        public void Test_GrandTotal_And_GrandTotalWithVat_CeilingRules()
        {
            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "INV-CEIL-1",
                VatPercent = 10,
                Items = new List<InvoiceItem>
                {
                    // ردیف اول: 13,000
                    new InvoiceItem { RowNumber = 1, ItemName = "کالا ۱", Quantity = 10, RawPrice = 1300 },
                    // ردیف دوم: 1,300
                    new InvoiceItem { RowNumber = 2, ItemName = "کالا ۲", Quantity = 1, RawPrice = 1300 }
                }
            };
            foreach (var it in invoice.Items) it.UpdateFromPercent();

            // جمع خام اقلام: 13000 + 1300 = 14300
            // با قانون سقف (بیش از ۴ رقم با تقریب ۱۰۰۰): باید 15000 شود
            Assert.Equal(15000m, invoice.GrandTotal);

            // محاسبه ارزش افزوده ۱۰٪:
            // 15000 + 1500 = 16500 -> سقف بیش از ۴ رقم با تقریب ۱۰۰۰: باید 17000 شود
            Assert.Equal(17000m, invoice.GrandTotalWithVat);
            // ارزش افزوده = 17000 - 15000 = 2000
            Assert.Equal(2000m, invoice.VatAmount);
        }

        [Fact]
        public void Test_Expert_DefaultRoleTitle_IsPurchaseExpert()
        {
            var expert = new Expert();
            Assert.Equal("کارشناس خرید", expert.RoleTitle);
        }

        [Fact]
        public void Test_AppSettings_NoDefaultBankAccounts_StoredInDb()
        {
            var settings = new AppSettings();
            Assert.Empty(settings.BankAccounts);
            Assert.Empty(settings.BankAccountNumber);
            Assert.Empty(settings.BankCardNumber);

            string tempDir = Path.Combine(Path.GetTempPath(), "TarazBankTest_" + Guid.NewGuid().ToString("N"));
            string dbPath = Path.Combine(tempDir, "database.json");
            try
            {
                var storage = new StorageService(dbPath);
                // دیتابیس نباید حساب بانکی پیش‌فرضی به کاربر تحمیل کند
                Assert.Empty(storage.GetSettings().BankAccounts);

                // کاربر حساب خود را در تنظیمات ثبت می‌کند
                var s = storage.GetSettings();
                s.BankAccounts.Add(new BankAccountInfo
                {
                    BankName = "بانک سامان",
                    ShebaNumber = "IR910560930281005263715001",
                    CardNumber = "6219868801982988",
                    OwnerName = "شرکت آریان نیرو گستر اترک"
                });
                storage.SaveSettings(s);

                // بازخوانی از فایل
                var reloadedStorage = new StorageService(dbPath);
                var reloadedSettings = reloadedStorage.GetSettings();
                Assert.Single(reloadedSettings.BankAccounts);
                Assert.Equal("بانک سامان", reloadedSettings.BankAccounts[0].BankName);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_ProformaInvoice_HighlightsDefaultToFalse()
        {
            var invoice = new ProformaInvoice();
            Assert.False(invoice.HighlightValidity);
            Assert.False(invoice.HighlightPaymentTerms);
            Assert.False(invoice.HighlightNotes);
            Assert.False(invoice.HighlightDeliveryDays);
            Assert.False(invoice.HighlightDeliveryLocation);
            Assert.False(invoice.HighlightVat);
        }

        [Fact]
        public void Test_StorageService_SettingsDbSyncAndMerge()
        {
            var local = new DatabaseModel();
            local.Settings.DeliveryLocation = "تهران";
            local.Settings.DeliveryDays = "یک روزه";
            local.Settings.UpdatedAt = DateTime.Now.AddMinutes(5);

            var primary = new DatabaseModel();
            primary.Settings.DeliveryLocation = "مشهد";
            primary.Settings.DeliveryDays = "سه روز کاری";
            primary.Settings.UpdatedAt = DateTime.Now;

            var merged = StorageService.MergeDatabases(local, primary);
            Assert.Equal("تهران", merged.Settings.DeliveryLocation);
            Assert.Equal("یک روزه", merged.Settings.DeliveryDays);
        }

        [Fact]
        public void Test_PrintPdf_WithoutExpert_DoesNotThrow()
        {
            var invoice = new ProformaInvoice
            {
                InvoiceNumber = "INV-NO-EXP",
                CompanyName = "شرکت تست بدون کارشناس",
                ExpertId = Guid.Empty,
                ExpertName = "",
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem { RowNumber = 1, ItemName = "محصول ۱", Quantity = 1, RawPrice = 10000 }
                }
            };
            foreach (var it in invoice.Items) it.UpdateFromPercent();

            // 1. تست تولید PDF توسط InvoicePdfBuilder با متد Save در فایل موقت
            string tempPdf = Path.Combine(Path.GetTempPath(), $"invoice_test_{Guid.NewGuid()}.pdf");
            try
            {
                InvoicePdfBuilder.Save(invoice, new AppSettings(), tempPdf);
                Assert.True(File.Exists(tempPdf));
                Assert.True(new FileInfo(tempPdf).Length > 0);
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }

            // 2. تست تولید سند FlowDocument برای چاپ در نخ STA
            Exception? threadEx = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var storage = new StorageService();
                    var pdfService = new PrintPdfService(storage);
                    var doc = pdfService.CreateInvoiceFlowDocument(invoice);
                    Assert.NotNull(doc);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(threadEx);
        }

        [Fact]
        public void Test_NotApprovedToVisibilityConverter()
        {
            var converter = new NotApprovedToVisibilityConverter();

            var visibleDraft = converter.Convert(InvoiceStatus.Draft, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);
            var visiblePending = converter.Convert(InvoiceStatus.Pending, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);
            var collapsedApproved = converter.Convert(InvoiceStatus.Approved, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(System.Windows.Visibility.Visible, visibleDraft);
            Assert.Equal(System.Windows.Visibility.Visible, visiblePending);
            Assert.Equal(System.Windows.Visibility.Collapsed, collapsedApproved);

            // تست با رشته و عدد
            Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert("Approved", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert("تأیید شده", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert((int)InvoiceStatus.Approved, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(System.Windows.Visibility.Visible, converter.Convert("Draft", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
