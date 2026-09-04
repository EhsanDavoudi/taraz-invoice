using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using Xunit;
using TarazInvoice.Models;
using TarazInvoice.Services;

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
            // قیمت تقسیم بر 1.1 = 17,575,000 / 1.1 = 15,977,273 (گرد شده به نزدیک‌ترین عدد صحیح)
            decimal expectedUnitPrice = Math.Round(17575000m / 1.1m, 0);
            Assert.Equal(expectedUnitPrice, item.FinalUnitPrice);

            // جمع کل سطر = 4 * 15,977,273
            decimal expectedTotal = Math.Round(4 * expectedUnitPrice, 0);
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
        public void Test_Expert_IndependentSequentialNumbering()
        {
            var exp1 = new Expert
            {
                FullName = "مهندس رضایی",
                InvoicePrefix = "EXP-REZ-",
                NextInvoiceNumber = 1001
            };

            var exp2 = new Expert
            {
                FullName = "مهندس محمدی",
                InvoicePrefix = "EXP-MOH-",
                NextInvoiceNumber = 5001
            };

            Assert.Equal("EXP-REZ-1001", exp1.GetPreviewNextInvoiceNumber());
            Assert.Equal("EXP-MOH-5001", exp2.GetPreviewNextInvoiceNumber());

            // ثبت فاکتور اول برای رضایی
            string num1 = exp1.ConsumeNextInvoiceNumber();
            Assert.Equal("EXP-REZ-1001", num1);
            Assert.Equal("EXP-REZ-1002", exp1.GetPreviewNextInvoiceNumber());

            // ثبت فاکتور دوم برای رضایی
            string num2 = exp1.ConsumeNextInvoiceNumber();
            Assert.Equal("EXP-REZ-1002", num2);
            Assert.Equal("EXP-REZ-1003", exp1.GetPreviewNextInvoiceNumber());

            // کارشناس دوم مستقل است و تغییر نکرده
            Assert.Equal("EXP-MOH-5001", exp2.GetPreviewNextInvoiceNumber());
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
    }
}
