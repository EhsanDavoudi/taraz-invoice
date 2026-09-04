using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    public class DatabaseModel
    {
        public List<Company> Companies { get; set; } = new List<Company>();
        public List<Expert> Experts { get; set; } = new List<Expert>();
        public List<ProformaInvoice> Invoices { get; set; } = new List<ProformaInvoice>();
        public AppSettings Settings { get; set; } = new AppSettings();
    }

    public class StorageService
    {
        private static readonly object _lock = new object();
        private readonly string _filePath;
        private DatabaseModel _data;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public StorageService()
        {
            string appDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            _filePath = Path.Combine(appDataDir, "database.json");
            _data = LoadFromFile();
        }

        private DatabaseModel LoadFromFile()
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_filePath);
                        var model = JsonSerializer.Deserialize<DatabaseModel>(json, _jsonOptions);
                        if (model != null)
                        {
                            return model;
                        }
                    }
                    catch
                    {
                        // در صورت بروز خطا در خواندن، یک مدل جدید با داده‌های نمونه تولید می‌شود
                    }
                }

                var seed = CreateSeedData();
                SaveInternal(seed);
                return seed;
            }
        }

        private void SaveInternal(DatabaseModel model)
        {
            string json = JsonSerializer.Serialize(model, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }

        public void Save()
        {
            lock (_lock)
            {
                SaveInternal(_data);
            }
        }

        private DatabaseModel CreateSeedData()
        {
            var seed = new DatabaseModel();

            // شرکت‌های نمونه
            var c1 = new Company
            {
                Name = "شرکت پتروشیمی زاگرس",
                EconomicCode = "411122334455",
                NationalCode = "10102030405",
                RegistrationNo = "12456",
                Phone = "021-88776655",
                Mobile = "09121112233",
                Address = "تهران، شهرک غرب، خیابان مهستان، پلاک ۱۲",
                Description = "مشتری صنایع پتروشیمی و پالایشگاهی"
            };

            var c2 = new Company
            {
                Name = "مجتمع صنعتی فولاد البرز",
                EconomicCode = "411998877665",
                NationalCode = "10103040506",
                RegistrationNo = "78910",
                Phone = "026-34556677",
                Mobile = "09123334455",
                Address = "کرج، شهرک صنعتی بهارستان، فاز ۲",
                Description = "تأمین مقاطع و ورق‌های صنعتی"
            };

            seed.Companies.Add(c1);
            seed.Companies.Add(c2);

            // کارشناسان نمونه با شماره فاکتور مجزا
            var exp1 = new Expert
            {
                FullName = "مهندس محمد رضایی",
                RoleTitle = "مدیر فروش تجهیزات صنعتی",
                PhoneNumber = "09121234567",
                Email = "rezaei@taraz.ir",
                CompanyId = c1.Id,
                CompanyName = c1.Name,
                InvoicePrefix = "EXP-REZ-",
                NextInvoiceNumber = 1002 // ۱۰۱ قبلا صادر شده
            };

            var exp2 = new Expert
            {
                FullName = "مهندس سارا محمدی",
                RoleTitle = "کارشناس بازرگانی داخلی",
                PhoneNumber = "09129876543",
                Email = "mohammadi@taraz.ir",
                CompanyId = c2.Id,
                CompanyName = c2.Name,
                InvoicePrefix = "EXP-SM-",
                NextInvoiceNumber = 2001
            };

            seed.Experts.Add(exp1);
            seed.Experts.Add(exp2);

            // پیش‌فاکتور نمونه اول برای کارشناس رضایی
            var inv1 = new ProformaInvoice
            {
                InvoiceNumber = "EXP-REZ-1001",
                IssueDateShamsi = JalaliDateHelper.GetCurrentShamsiDate(),
                ValidUntilShamsi = JalaliDateHelper.GetShamsiDateAfterDays(7),
                CompanyId = c1.Id,
                CompanyName = c1.Name,
                CompanyPhone = c1.Phone,
                CompanyAddress = c1.Address,
                CompanyEconomicCode = c1.EconomicCode,
                CompanyNationalCode = c1.NationalCode,
                ExpertId = exp1.Id,
                ExpertName = exp1.FullName,
                ExpertPhone = exp1.PhoneNumber,
                Status = InvoiceStatus.Sent,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        RowNumber = 1,
                        ItemName = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰",
                        Quantity = 4,
                        RawPrice = 18500000,
                        DiscountPercent = 5,
                        IsDividedBy1Point1 = true
                    },
                    new InvoiceItem
                    {
                        RowNumber = 2,
                        ItemName = "فلنج کربن استیل گلوجوش ۶ اینچ",
                        Quantity = 12,
                        RawPrice = 4200000,
                        DiscountPercent = 10,
                        IsDividedBy1Point1 = false
                    }
                }
            };

            // محاسبه مجدد اقلام نمونه
            foreach (var item in inv1.Items)
            {
                item.UpdateFromPercent();
            }

            seed.Invoices.Add(inv1);
            return seed;
        }

        // دسترسی به داده‌ها
        public List<Company> GetCompanies() => _data.Companies;
        public List<Expert> GetExperts() => _data.Experts;
        public List<ProformaInvoice> GetInvoices() => _data.Invoices;
        public AppSettings GetSettings() => _data.Settings;

        // عملیات شرکت‌ها
        public void AddOrUpdateCompany(Company company)
        {
            var existing = _data.Companies.FirstOrDefault(c => c.Id == company.Id);
            if (existing != null)
            {
                int index = _data.Companies.IndexOf(existing);
                _data.Companies[index] = company;
            }
            else
            {
                _data.Companies.Add(company);
            }
            Save();
        }

        public void DeleteCompany(Guid companyId)
        {
            _data.Companies.RemoveAll(c => c.Id == companyId);
            Save();
        }

        // عملیات کارشناسان
        public void AddOrUpdateExpert(Expert expert)
        {
            var existing = _data.Experts.FirstOrDefault(e => e.Id == expert.Id);
            if (existing != null)
            {
                int index = _data.Experts.IndexOf(existing);
                _data.Experts[index] = expert;
            }
            else
            {
                _data.Experts.Add(expert);
            }
            Save();
        }

        public void DeleteExpert(Guid expertId)
        {
            _data.Experts.RemoveAll(e => e.Id == expertId);
            Save();
        }

        // عملیات پیش‌فاکتورها
        public void AddOrUpdateInvoice(ProformaInvoice invoice)
        {
            var existing = _data.Invoices.FirstOrDefault(i => i.Id == invoice.Id);
            if (existing != null)
            {
                int index = _data.Invoices.IndexOf(existing);
                _data.Invoices[index] = invoice;
            }
            else
            {
                _data.Invoices.Add(invoice);
            }
            Save();
        }

        public void DeleteInvoice(Guid invoiceId)
        {
            _data.Invoices.RemoveAll(i => i.Id == invoiceId);
            Save();
        }

        // پشتیبان‌گیری
        public void BackupTo(string targetPath)
        {
            lock (_lock)
            {
                string json = JsonSerializer.Serialize(_data, _jsonOptions);
                File.WriteAllText(targetPath, json);
            }
        }

        // بازیابی
        public void RestoreFrom(string sourcePath)
        {
            lock (_lock)
            {
                string json = File.ReadAllText(sourcePath);
                var restored = JsonSerializer.Deserialize<DatabaseModel>(json, _jsonOptions);
                if (restored != null)
                {
                    _data = restored;
                    Save();
                }
            }
        }
    }
}
