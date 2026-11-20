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
        public List<StorageItem> StorageItems { get; set; } = new List<StorageItem>();
        public AppSettings Settings { get; set; } = new AppSettings();
    }

    public class StorageService : IDisposable
    {
        private static readonly object _lock = new object();
        private readonly string _filePath;
        private DatabaseModel _data;
        private DateTime _lastWriteTimeUtc = DateTime.MinValue;
        private long _lastFileSize = 0;
        private readonly FileSystemWatcher? _fileWatcher;
        private bool _isSavingInternal = false;

        public event Action? DataChanged;

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

            // دیده‌بان فایل محلی / شبکه برای اطلاع‌رسانی بلادرنگ تغییرات دیتابیس توسط سایر کاربران
            try
            {
                _fileWatcher = new FileSystemWatcher(appDataDir, "database.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Changed += OnExternalFileChanged;
                _fileWatcher.Created += OnExternalFileChanged;
            }
            catch { }
        }

        private void OnExternalFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_isSavingInternal) return;

            // تاخیر کوتاه برای پایان نوشتن سایر کلاینت‌ها روی شبکه
            System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
            {
                if (CheckAndReloadIfDiskChanged())
                {
                    DataChanged?.Invoke();
                }
            });
        }

        public bool CheckAndReloadIfDiskChanged()
        {
            if (_isSavingInternal) return false;
            lock (_lock)
            {
                if (!File.Exists(_filePath)) return false;
                try
                {
                    var fileInfo = new FileInfo(_filePath);
                    var currentDiskTime = fileInfo.LastWriteTimeUtc;
                    var currentLength = fileInfo.Length;
                    if (currentDiskTime > _lastWriteTimeUtc.AddMilliseconds(20) || currentLength != _lastFileSize)
                    {
                        var reloaded = LoadFromFile();
                        _data = reloaded;
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public void ReloadFromDisk()
        {
            lock (_lock)
            {
                _data = LoadFromFile();
                DataChanged?.Invoke();
            }
        }

        private DatabaseModel LoadFromFile()
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_filePath);
                            _lastFileSize = new FileInfo(_filePath).Length;
                            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using var reader = new StreamReader(fs);
                            string json = reader.ReadToEnd();
                            
                            var model = JsonSerializer.Deserialize<DatabaseModel>(json, _jsonOptions);
                            if (model != null)
                            {
                                model.StorageItems ??= new List<StorageItem>();
                                model.Companies ??= new List<Company>();
                                model.Experts ??= new List<Expert>();
                                model.Invoices ??= new List<ProformaInvoice>();
                                model.Settings ??= new AppSettings();

                                // اطمینان از پیش‌فرض و استفاده قطعی از ریال در کل سامانه
                                model.Settings.CurrencyUnit = "ریال";
                                foreach (var inv in model.Invoices)
                                {
                                    if (inv.CurrencyUnit == "تومان" || string.IsNullOrWhiteSpace(inv.CurrencyUnit))
                                    {
                                        inv.CurrencyUnit = "ریال";
                                    }
                                }

                                // هماهنگ‌سازی حساب‌های بانکی
                                if (model.Settings.BankAccounts == null || model.Settings.BankAccounts.Count == 0)
                                {
                                    model.Settings.BankAccounts = new List<BankAccountInfo>
                                    {
                                        new BankAccountInfo
                                        {
                                            BankName = "بانک ملی ایران",
                                            ShebaNumber = "IR95017000000118033698006",
                                            CardNumber = "۶۰۳۷۹۹۸۰۰۴۱۰۴۱۶",
                                            OwnerName = "شرکت آریان نیرو گستر اترک"
                                        }
                                    };
                                }

                                if (model.StorageItems.Count == 0)
                                {
                                    PopulateDefaultStorageItems(model.StorageItems);
                                }
                                return model;
                            }
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                }

                var seed = CreateSeedData();
                SaveInternal(seed);
                return seed;
            }
        }

        private void SaveInternal(DatabaseModel model)
        {
            _isSavingInternal = true;
            try
            {
                string json = JsonSerializer.Serialize(model, _jsonOptions);
                string tempPath = _filePath + $".{Guid.NewGuid():N}.tmp";

                for (int attempt = 0; attempt < 15; attempt++)
                {
                    try
                    {
                        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        using (var writer = new StreamWriter(fs))
                        {
                            writer.Write(json);
                            writer.Flush();
                        }
                        break;
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(50 + (attempt * 10));
                    }
                }

                if (File.Exists(tempPath))
                {
                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        try
                        {
                            File.Copy(tempPath, _filePath, overwrite: true);
                            try { File.Delete(tempPath); } catch { }
                            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_filePath);
                            _lastFileSize = new FileInfo(_filePath).Length;
                            break;
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(60 + (attempt * 15));
                        }
                    }
                }
            }
            finally
            {
                _isSavingInternal = false;
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public void Dispose()
        {
            try
            {
                _fileWatcher?.Dispose();
            }
            catch { }
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

            // کالاهای نمونه انبار
            PopulateDefaultStorageItems(seed.StorageItems);

            return seed;
        }

        private static void PopulateDefaultStorageItems(List<StorageItem> items)
        {
            if (items == null) return;
            items.Add(new StorageItem
            {
                Code = "PRD-101",
                Name = "کلمپ فلکسیبل جریان و هارمونیک مدل AFLEX 3003 برند PROVA",
                Unit = "عدد",
                DefaultPrice = 1037760000,
                StockQuantity = 15,
                Category = "ابزار دقیق و اندازه‌گیری",
                Description = "کلمپ فلکسیبل جریان و هارمونیک ساخت برند PROVA با دقت بالا"
            });
            items.Add(new StorageItem
            {
                Code = "PRD-102",
                Name = "هیتر هویه دیجیتالی دو کاره گرداک مدل GORDAK 952 با یکسال گارانتی",
                Unit = "عدد",
                DefaultPrice = 298350000,
                StockQuantity = 22,
                Category = "تجهیزات لحیم‌کاری و تعمیرات",
                Description = "هیتر هویه دو کاره گرداک دیجیتال با یکسال گارانتی رسمی"
            });
            items.Add(new StorageItem
            {
                Code = "PRD-103",
                Name = "شیر پروانه‌ای استیل ۱۰ اینچ کلاس ۱۵۰",
                Unit = "عدد",
                DefaultPrice = 18500000,
                StockQuantity = 8,
                Category = "شیرآلات صنعتی",
                Description = "شیر پروانه‌ای استنلس استیل ۳۱۶ با لاینر EPDM"
            });
            items.Add(new StorageItem
            {
                Code = "PRD-104",
                Name = "فلنج کربن استیل گلوجوش ۶ اینچ",
                Unit = "عدد",
                DefaultPrice = 4200000,
                StockQuantity = 35,
                Category = "اتصالات پایپینگ",
                Description = "فلنج گلودار کربن استیل کلاس ۱۵۰ استاندارد ASME"
            });
            items.Add(new StorageItem
            {
                Code = "PRD-105",
                Name = "مولتی‌متر دیجیتال صنعتی Fluke مدل 87V",
                Unit = "دستگاه",
                DefaultPrice = 645000000,
                StockQuantity = 6,
                Category = "ابزار دقیق و اندازه‌گیری",
                Description = "مولتی‌متر صنعتی دقیق Fluke True RMS"
            });
        }

        // دسترسی به داده‌ها با بررسی همگام‌سازی شبکه و فایل
        public List<Company> GetCompanies()
        {
            CheckAndReloadIfDiskChanged();
            return _data.Companies;
        }

        public List<Expert> GetExperts()
        {
            CheckAndReloadIfDiskChanged();
            return _data.Experts;
        }

        public List<ProformaInvoice> GetInvoices()
        {
            CheckAndReloadIfDiskChanged();
            return _data.Invoices;
        }

        public AppSettings GetSettings()
        {
            CheckAndReloadIfDiskChanged();
            return _data.Settings;
        }

        public List<StorageItem> GetStorageItems()
        {
            CheckAndReloadIfDiskChanged();
            return _data.StorageItems;
        }

        // عملیات شرکت‌ها
        public void AddOrUpdateCompany(Company company)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
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
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public void DeleteCompany(Guid companyId)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                _data.Companies.RemoveAll(c => c.Id == companyId);
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        // عملیات کارشناسان
        public void AddOrUpdateExpert(Expert expert)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
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
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public void DeleteExpert(Guid expertId)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                _data.Experts.RemoveAll(e => e.Id == expertId);
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        // عملیات پیش‌فاکتورها
        public void AddOrUpdateInvoice(ProformaInvoice invoice)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
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
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public void DeleteInvoice(Guid invoiceId)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                _data.Invoices.RemoveAll(i => i.Id == invoiceId);
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        // عملیات انبار و کالاها (Storage Items)
        public void AddOrUpdateStorageItem(StorageItem item)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                var existing = _data.StorageItems.FirstOrDefault(p => p.Id == item.Id);
                if (existing != null)
                {
                    int index = _data.StorageItems.IndexOf(existing);
                    item.UpdatedAt = DateTime.Now;
                    _data.StorageItems[index] = item;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(item.Code))
                    {
                        item.Code = $"PRD-{_data.StorageItems.Count + 101}";
                    }
                    item.CreatedAt = DateTime.Now;
                    item.UpdatedAt = DateTime.Now;
                    _data.StorageItems.Add(item);
                }
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public void DeleteStorageItem(Guid itemId)
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                _data.StorageItems.RemoveAll(p => p.Id == itemId);
                SaveInternal(_data);
            }
            DataChanged?.Invoke();
        }

        public StorageItem EnsureStorageItemExists(string name, string unit, decimal defaultPrice, bool updatePriceIfDifferent = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return null!;
            var trimmed = name.Trim();
            var existing = _data.StorageItems.FirstOrDefault(p => 
                string.Equals(p.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                // بروزرسانی قیمت در صورتی که کاربر قیمت را تغییر داده باشد یا قبلاً صفر بوده باشد
                if (updatePriceIfDifferent && defaultPrice > 0 && defaultPrice != existing.DefaultPrice)
                {
                    existing.DefaultPrice = defaultPrice;
                    existing.UpdatedAt = DateTime.Now;
                    Save();
                }
                else if (existing.DefaultPrice <= 0 && defaultPrice > 0)
                {
                    existing.DefaultPrice = defaultPrice;
                    existing.UpdatedAt = DateTime.Now;
                    Save();
                }
                return existing;
            }

            var newItem = new StorageItem
            {
                Id = Guid.NewGuid(),
                Code = $"PRD-{_data.StorageItems.Count + 101}",
                Name = trimmed,
                Unit = string.IsNullOrWhiteSpace(unit) ? "عدد" : unit.Trim(),
                DefaultPrice = defaultPrice,
                StockQuantity = 0,
                Category = "عمومی",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            _data.StorageItems.Add(newItem);
            Save();
            return newItem;
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
