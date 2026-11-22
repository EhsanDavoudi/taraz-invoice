using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
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

    public class DbLocationConfig
    {
        public string? CustomDatabaseFolder { get; set; }
    }

    public class StorageService : IDisposable
    {
        private static readonly object _lock = new object();
        private readonly string _localDirectory;
        private readonly string _localFilePath;
        private readonly string _locationConfigPath;

        private string? _primaryDirectory;
        private string _primaryFilePath;

        private DatabaseModel _data = new DatabaseModel();
        private DateTime _lastWriteTimeUtc = DateTime.MinValue;
        private long _lastFileSize = 0;
        private FileSystemWatcher? _fileWatcher;
        private bool _isSavingInternal = false;

        public event Action? DataChanged;
        public event Action<bool, string>? ConnectionStatusChanged;

        public bool IsNetworkConnected { get; private set; } = true;
        public DateTime? LastSyncTime { get; private set; } = null;
        public int PendingLocalChangesCount { get; private set; } = 0;

        public string LocalDatabasePath => _localFilePath;
        public string? PrimaryDatabaseDirectory => _primaryDirectory;
        public string PrimaryDatabasePath => _primaryFilePath;

        public bool IsCustomDirectoryConfigured =>
            !string.IsNullOrWhiteSpace(_primaryDirectory) &&
            !string.Equals(Path.GetFullPath(_primaryDirectory.TrimEnd('\\', '/')),
                           Path.GetFullPath(_localDirectory.TrimEnd('\\', '/')),
                           StringComparison.OrdinalIgnoreCase);

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public StorageService(string? customFilePath = null)
        {
            _localDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(_localDirectory))
            {
                Directory.CreateDirectory(_localDirectory);
            }
            _localFilePath = Path.Combine(_localDirectory, "database.json");
            _locationConfigPath = Path.Combine(_localDirectory, "db_location.json");

            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                _primaryDirectory = Path.GetDirectoryName(customFilePath);
                _primaryFilePath = customFilePath;
            }
            else
            {
                string? savedFolder = LoadLocationConfig();
                if (!string.IsNullOrWhiteSpace(savedFolder))
                {
                    _primaryDirectory = savedFolder;
                    _primaryFilePath = Path.Combine(savedFolder, "database.json");
                }
                else
                {
                    _primaryDirectory = null;
                    _primaryFilePath = _localFilePath;
                }
            }

            InitializeAndSync();
            SetupFileWatcher();
        }

        private string? LoadLocationConfig()
        {
            try
            {
                if (File.Exists(_locationConfigPath))
                {
                    string json = File.ReadAllText(_locationConfigPath);
                    var config = JsonSerializer.Deserialize<DbLocationConfig>(json, _jsonOptions);
                    return config?.CustomDatabaseFolder?.Trim();
                }
            }
            catch { }
            return null;
        }

        private void SaveLocationConfig(string? folder)
        {
            try
            {
                var config = new DbLocationConfig { CustomDatabaseFolder = folder?.Trim() ?? string.Empty };
                string json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(_locationConfigPath, json);
            }
            catch { }
        }

        public static bool IsDirectoryAccessible(string? dirPath, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(dirPath)) return false;
            try
            {
                var task = Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(dirPath))
                        {
                            try { Directory.CreateDirectory(dirPath); } catch { return false; }
                        }
                        string testFile = Path.Combine(dirPath, $".taraz_access_test_{Guid.NewGuid():N}.tmp");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (task.Wait(timeoutMs))
                {
                    return task.Result;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeAndSync()
        {
            lock (_lock)
            {
                DatabaseModel? localModel = null;
                if (File.Exists(_localFilePath))
                {
                    localModel = LoadFromFile(_localFilePath);
                }

                if (IsCustomDirectoryConfigured)
                {
                    bool accessible = IsDirectoryAccessible(_primaryDirectory, 2000);
                    IsNetworkConnected = accessible;

                    if (accessible)
                    {
                        DatabaseModel? primaryModel = null;
                        if (File.Exists(_primaryFilePath))
                        {
                            primaryModel = LoadFromFile(_primaryFilePath);
                        }

                        if (primaryModel != null && localModel != null)
                        {
                            _data = MergeDatabases(localModel, primaryModel);
                            SaveToFileInternal(_localFilePath, _data);
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.Now;
                            PendingLocalChangesCount = 0;
                        }
                        else if (primaryModel != null)
                        {
                            _data = primaryModel;
                            SaveToFileInternal(_localFilePath, _data);
                            LastSyncTime = DateTime.Now;
                            PendingLocalChangesCount = 0;
                        }
                        else if (localModel != null)
                        {
                            _data = localModel;
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.Now;
                            PendingLocalChangesCount = 0;
                        }
                        else
                        {
                            _data = CreateSeedData();
                            SaveToFileInternal(_localFilePath, _data);
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.Now;
                            PendingLocalChangesCount = 0;
                        }

                        UpdateDiskFingerprint(_primaryFilePath);
                        return;
                    }
                    else
                    {
                        // پوشه شبکه یا سفارشی در دسترس نیست؛ استفاده از دیتابیس ثانویه محلی
                        if (localModel != null)
                        {
                            _data = localModel;
                        }
                        else
                        {
                            _data = CreateSeedData();
                            SaveToFileInternal(_localFilePath, _data);
                        }
                        PendingLocalChangesCount = 1;
                        UpdateDiskFingerprint(_localFilePath);
                        return;
                    }
                }

                // حالت عادی (فقط دیتابیس محلی)
                if (localModel != null)
                {
                    _data = localModel;
                }
                else
                {
                    _data = CreateSeedData();
                    SaveToFileInternal(_localFilePath, _data);
                }
                IsNetworkConnected = true;
                PendingLocalChangesCount = 0;
                UpdateDiskFingerprint(_localFilePath);
            }
        }

        private void SetupFileWatcher()
        {
            try
            {
                _fileWatcher?.Dispose();
                _fileWatcher = null;

                string targetPath = (IsCustomDirectoryConfigured && IsNetworkConnected) ? _primaryFilePath : _localFilePath;
                string? watchDir = Path.GetDirectoryName(targetPath);
                string watchFile = Path.GetFileName(targetPath);

                if (!string.IsNullOrEmpty(watchDir) && Directory.Exists(watchDir))
                {
                    _fileWatcher = new FileSystemWatcher(watchDir, watchFile)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };

                    _fileWatcher.Changed += OnExternalFileChanged;
                    _fileWatcher.Created += OnExternalFileChanged;
                }
            }
            catch { }
        }

        private void OnExternalFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_isSavingInternal) return;

            Task.Delay(100).ContinueWith(_ =>
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
                if (IsCustomDirectoryConfigured)
                {
                    bool accessible = IsDirectoryAccessible(_primaryDirectory, 1000);
                    if (!accessible)
                    {
                        if (IsNetworkConnected)
                        {
                            IsNetworkConnected = false;
                            ConnectionStatusChanged?.Invoke(false, "ارتباط با پوشه اصلی/شبکه قطع شده است. تغییرات در دیتابیس ثانویه محلی ذخیره می‌شوند.");
                        }
                        return false;
                    }

                    // در صورت بازگشت ارتباط شبکه پس از قطع بودن: ادغام خودکار
                    if (!IsNetworkConnected)
                    {
                        IsNetworkConnected = true;
                        ConnectionStatusChanged?.Invoke(true, "ارتباط با پوشه شبکه مجدداً برقرار شد. ادغام و همگام‌سازی خودکار انجام گردید.");
                        PerformSyncAndMergeInternal();
                        return true;
                    }

                    // بررسی تغییرات روی دیسک فایل شبکه توسط سایر کلاینت‌ها
                    if (File.Exists(_primaryFilePath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(_primaryFilePath);
                            var currentDiskTime = fileInfo.LastWriteTimeUtc;
                            var currentLength = fileInfo.Length;
                            if (currentDiskTime > _lastWriteTimeUtc.AddMilliseconds(50) || currentLength != _lastFileSize)
                            {
                                var remote = LoadFromFile(_primaryFilePath);
                                if (remote != null)
                                {
                                    _data = MergeDatabases(_data, remote);
                                    SaveToFileInternal(_localFilePath, _data);
                                    UpdateDiskFingerprint(_primaryFilePath);
                                    return true;
                                }
                            }
                        }
                        catch { }
                    }
                    return false;
                }
                else
                {
                    // حالت دیتابیس محلی
                    if (!File.Exists(_localFilePath)) return false;
                    try
                    {
                        var fileInfo = new FileInfo(_localFilePath);
                        var currentDiskTime = fileInfo.LastWriteTimeUtc;
                        var currentLength = fileInfo.Length;
                        if (currentDiskTime > _lastWriteTimeUtc.AddMilliseconds(50) || currentLength != _lastFileSize)
                        {
                            var reloaded = LoadFromFile(_localFilePath);
                            if (reloaded != null)
                            {
                                _data = reloaded;
                                UpdateDiskFingerprint(_localFilePath);
                                return true;
                            }
                        }
                    }
                    catch { }
                    return false;
                }
            }
        }

        public void ReloadFromDisk()
        {
            lock (_lock)
            {
                if (IsCustomDirectoryConfigured && IsDirectoryAccessible(_primaryDirectory, 1500))
                {
                    PerformSyncAndMergeInternal();
                }
                else
                {
                    var reloaded = LoadFromFile(_localFilePath);
                    if (reloaded != null)
                    {
                        _data = reloaded;
                    }
                }
                DataChanged?.Invoke();
            }
        }

        private DatabaseModel? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    string json = reader.ReadToEnd();

                    var model = JsonSerializer.Deserialize<DatabaseModel>(json, _jsonOptions);
                    if (model != null)
                    {
                        NormalizeModel(model);
                        return model;
                    }
                }
                catch
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            return null;
        }

        private static void NormalizeModel(DatabaseModel model)
        {
            model.StorageItems ??= new List<StorageItem>();
            model.Companies ??= new List<Company>();
            model.Experts ??= new List<Expert>();
            model.Invoices ??= new List<ProformaInvoice>();
            model.Settings ??= new AppSettings();

            if (string.IsNullOrWhiteSpace(model.Settings.InvoicePrefix))
            {
                model.Settings.InvoicePrefix = "ANA 405-";
            }
            if (model.Settings.NextInvoiceNumber <= 0)
            {
                model.Settings.NextInvoiceNumber = 1001;
            }

            model.Settings.CurrencyUnit = "ریال";
            foreach (var inv in model.Invoices)
            {
                if (inv.CurrencyUnit == "تومان" || string.IsNullOrWhiteSpace(inv.CurrencyUnit))
                {
                    inv.CurrencyUnit = "ریال";
                }
            }

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
        }

        private void SaveToFileInternal(string filePath, DatabaseModel model)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(model, _jsonOptions);
            string tempPath = filePath + $".{Guid.NewGuid():N}.tmp";

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
                        File.Copy(tempPath, filePath, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                        break;
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(60 + (attempt * 15));
                    }
                }
            }
        }

        private void UpdateDiskFingerprint(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    _lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
                    _lastFileSize = new FileInfo(filePath).Length;
                }
            }
            catch { }
        }

        private void SaveInternal(DatabaseModel model)
        {
            _isSavingInternal = true;
            try
            {
                // ۱. همیشه ابتدا در پایگاه‌داده ثانویه محلی در پوشه برنامه ذخیره شود (تضمین حفظ اطلاعات)
                SaveToFileInternal(_localFilePath, model);

                // ۲. اگر پوشه سفارشی/شبکه تنظیم شده است:
                if (IsCustomDirectoryConfigured)
                {
                    bool accessible = IsDirectoryAccessible(_primaryDirectory, 1500);
                    if (accessible)
                    {
                        if (PendingLocalChangesCount > 0 && File.Exists(_primaryFilePath))
                        {
                            var remote = LoadFromFile(_primaryFilePath);
                            if (remote != null)
                            {
                                model = MergeDatabases(model, remote);
                                SaveToFileInternal(_localFilePath, model);
                            }
                        }

                        SaveToFileInternal(_primaryFilePath, model);
                        IsNetworkConnected = true;
                        LastSyncTime = DateTime.Now;
                        PendingLocalChangesCount = 0;
                        UpdateDiskFingerprint(_primaryFilePath);
                    }
                    else
                    {
                        // عدم اتصال به شبکه؛ داده در دیتابیس ثانویه ذخیره شد
                        IsNetworkConnected = false;
                        PendingLocalChangesCount++;
                        UpdateDiskFingerprint(_localFilePath);
                    }
                }
                else
                {
                    IsNetworkConnected = true;
                    PendingLocalChangesCount = 0;
                    UpdateDiskFingerprint(_localFilePath);
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

        // ==========================================
        // الگوریتم ادغام هوشمند دیتابیس محلی و شبکه
        // ==========================================
        public static DatabaseModel MergeDatabases(DatabaseModel local, DatabaseModel primary)
        {
            var merged = new DatabaseModel();

            // ۱. ادغام پیش‌فاکتورها (Invoices) بر اساس شناسه و زمان بروزرسانی
            var invoiceMap = new Dictionary<Guid, ProformaInvoice>();
            foreach (var inv in primary.Invoices)
            {
                invoiceMap[inv.Id] = inv;
            }

            foreach (var localInv in local.Invoices)
            {
                if (invoiceMap.TryGetValue(localInv.Id, out var existing))
                {
                    if (localInv.UpdatedAt > existing.UpdatedAt)
                    {
                        invoiceMap[localInv.Id] = localInv;
                    }
                }
                else
                {
                    // فاکتور جدید ایجاد شده در حالت آفلاین
                    invoiceMap[localInv.Id] = localInv;
                }
            }

            // تضمین عدم تکرار شماره فاکتورها در ادغام
            var finalInvoices = invoiceMap.Values.OrderBy(i => i.CreatedAt).ToList();
            var usedNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var inv in finalInvoices)
            {
                string normNum = NormalizeDigits(inv.InvoiceNumber);
                if (string.IsNullOrWhiteSpace(normNum) || usedNumbers.Contains(normNum))
                {
                    // در صورت تکرار شماره به علت صدور همزمان آفلاین، شماره جدید یکتا داده می‌شود
                    string pref = !string.IsNullOrWhiteSpace(primary.Settings.InvoicePrefix) ? primary.Settings.InvoicePrefix : "ANA 405-";
                    int nextCandidate = Math.Max(primary.Settings.NextInvoiceNumber, local.Settings.NextInvoiceNumber);
                    while (usedNumbers.Contains(NormalizeDigits($"{pref}{nextCandidate}")))
                    {
                        nextCandidate++;
                    }
                    inv.InvoiceNumber = $"{pref}{nextCandidate}";
                    inv.UpdatedAt = DateTime.Now;
                    usedNumbers.Add(NormalizeDigits(inv.InvoiceNumber));
                }
                else
                {
                    usedNumbers.Add(normNum);
                }
            }
            merged.Invoices = finalInvoices.OrderByDescending(i => i.UpdatedAt).ToList();

            // ۲. ادغام شرکت‌ها (Companies)
            var companyMap = new Dictionary<Guid, Company>();
            foreach (var c in primary.Companies) companyMap[c.Id] = c;
            foreach (var lc in local.Companies)
            {
                if (companyMap.TryGetValue(lc.Id, out var existing))
                {
                    if (lc.UpdatedAt > existing.UpdatedAt) companyMap[lc.Id] = lc;
                }
                else
                {
                    var sameName = companyMap.Values.FirstOrDefault(x => string.Equals(x.Name?.Trim(), lc.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (sameName != null)
                    {
                        if (lc.UpdatedAt > sameName.UpdatedAt) companyMap[sameName.Id] = lc;
                    }
                    else
                    {
                        companyMap[lc.Id] = lc;
                    }
                }
            }
            merged.Companies = companyMap.Values.OrderBy(c => c.Name).ToList();

            // ۳. ادغام کارشناسان (Experts)
            var expertMap = new Dictionary<Guid, Expert>();
            foreach (var exp in primary.Experts) expertMap[exp.Id] = exp;
            foreach (var le in local.Experts)
            {
                if (expertMap.TryGetValue(le.Id, out var existing))
                {
                    if (le.UpdatedAt > existing.UpdatedAt) expertMap[le.Id] = le;
                }
                else
                {
                    var sameName = expertMap.Values.FirstOrDefault(x => string.Equals(x.FullName?.Trim(), le.FullName?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (sameName != null)
                    {
                        if (le.UpdatedAt > sameName.UpdatedAt) expertMap[sameName.Id] = le;
                    }
                    else
                    {
                        expertMap[le.Id] = le;
                    }
                }
            }
            merged.Experts = expertMap.Values.OrderBy(e => e.FullName).ToList();

            // ۴. ادغام کالاهای انبار (StorageItems)
            var storageMap = new Dictionary<Guid, StorageItem>();
            foreach (var itm in primary.StorageItems) storageMap[itm.Id] = itm;
            foreach (var li in local.StorageItems)
            {
                if (storageMap.TryGetValue(li.Id, out var existing))
                {
                    if (li.UpdatedAt > existing.UpdatedAt) storageMap[li.Id] = li;
                }
                else
                {
                    var sameName = storageMap.Values.FirstOrDefault(x => string.Equals(x.Name?.Trim(), li.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (sameName != null)
                    {
                        if (li.UpdatedAt > sameName.UpdatedAt) storageMap[sameName.Id] = li;
                    }
                    else
                    {
                        storageMap[li.Id] = li;
                    }
                }
            }
            merged.StorageItems = storageMap.Values.OrderBy(s => s.Name).ToList();

            // ۵. ادغام تنظیمات (Settings)
            var baseSettings = primary.Settings ?? local.Settings ?? new AppSettings();
            merged.Settings = baseSettings;

            // شماره‌گذاری بعدی پیش‌فاکتور باید بالاترین باشد تا تداخل رخ ندهد
            int maxNextNum = Math.Max(primary.Settings?.NextInvoiceNumber ?? 1001, local.Settings?.NextInvoiceNumber ?? 1001);
            string activePrefix = NormalizeDigits(merged.Settings.InvoicePrefix ?? "ANA 405-");

            foreach (var inv in merged.Invoices)
            {
                string norm = NormalizeDigits(inv.InvoiceNumber);
                if (norm.StartsWith(activePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string numPart = norm.Substring(activePrefix.Length).Trim();
                    if (int.TryParse(numPart, out int parsed) && parsed >= maxNextNum)
                    {
                        maxNextNum = parsed + 1;
                    }
                }
            }
            merged.Settings.NextInvoiceNumber = maxNextNum;

            // ادغام حساب‌های بانکی
            var banks = new List<BankAccountInfo>(primary.Settings?.BankAccounts ?? new List<BankAccountInfo>());
            foreach (var b in local.Settings?.BankAccounts ?? new List<BankAccountInfo>())
            {
                if (!banks.Any(x => (!string.IsNullOrEmpty(x.ShebaNumber) && x.ShebaNumber == b.ShebaNumber) ||
                                    (!string.IsNullOrEmpty(x.CardNumber) && x.CardNumber == b.CardNumber) ||
                                    (!string.IsNullOrEmpty(x.AccountNumber) && x.AccountNumber == b.AccountNumber)))
                {
                    banks.Add(b);
                }
            }
            merged.Settings.BankAccounts = banks;

            return merged;
        }

        private bool PerformSyncAndMergeInternal()
        {
            try
            {
                if (!IsCustomDirectoryConfigured) return false;
                if (!IsDirectoryAccessible(_primaryDirectory, 2000))
                {
                    IsNetworkConnected = false;
                    return false;
                }

                var localModel = LoadFromFile(_localFilePath) ?? _data;
                DatabaseModel? remoteModel = null;
                if (File.Exists(_primaryFilePath))
                {
                    remoteModel = LoadFromFile(_primaryFilePath);
                }

                DatabaseModel merged;
                if (remoteModel != null)
                {
                    merged = MergeDatabases(localModel, remoteModel);
                }
                else
                {
                    merged = localModel;
                }

                _data = merged;
                SaveToFileInternal(_localFilePath, _data);
                SaveToFileInternal(_primaryFilePath, _data);
                IsNetworkConnected = true;
                LastSyncTime = DateTime.Now;
                PendingLocalChangesCount = 0;
                UpdateDiskFingerprint(_primaryFilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // تغییر مسیر پوشه پایگاه داده توسط کاربر (شبکه یا پوشه سفارشی)
        public bool SetCustomDatabaseDirectory(string? newDirectory, out string errorMessage)
        {
            errorMessage = string.Empty;
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(newDirectory))
                {
                    ResetToDefaultDatabaseDirectory();
                    return true;
                }

                string trimmed = newDirectory.Trim();
                if (string.Equals(Path.GetFullPath(trimmed.TrimEnd('\\', '/')),
                                  Path.GetFullPath(_localDirectory.TrimEnd('\\', '/')),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    ResetToDefaultDatabaseDirectory();
                    return true;
                }

                if (!IsDirectoryAccessible(trimmed, 3000))
                {
                    errorMessage = "دسترسی به پوشه انتخاب شده یا مجوز ذخیره‌سازی در آن برقرار نشد. لطفاً درستی مسیر شبکه و دسترسی آن را بررسی فرمایید.";
                    return false;
                }

                _primaryDirectory = trimmed;
                _primaryFilePath = Path.Combine(trimmed, "database.json");
                SaveLocationConfig(trimmed);

                // اگر در مسیر جدید قبلاً فایل پایگاه‌داده وجود داشته باشد، ادغام انجام می‌دهیم
                if (File.Exists(_primaryFilePath))
                {
                    var remote = LoadFromFile(_primaryFilePath);
                    if (remote != null)
                    {
                        _data = MergeDatabases(_data, remote);
                        SaveToFileInternal(_localFilePath, _data);
                        SaveToFileInternal(_primaryFilePath, _data);
                    }
                    else
                    {
                        SaveToFileInternal(_primaryFilePath, _data);
                    }
                }
                else
                {
                    SaveToFileInternal(_primaryFilePath, _data);
                }

                IsNetworkConnected = true;
                LastSyncTime = DateTime.Now;
                PendingLocalChangesCount = 0;
                UpdateDiskFingerprint(_primaryFilePath);
                SetupFileWatcher();
            }

            DataChanged?.Invoke();
            return true;
        }

        public void ResetToDefaultDatabaseDirectory()
        {
            lock (_lock)
            {
                _primaryDirectory = null;
                _primaryFilePath = _localFilePath;
                SaveLocationConfig(null);
                IsNetworkConnected = true;
                PendingLocalChangesCount = 0;
                UpdateDiskFingerprint(_localFilePath);
                SetupFileWatcher();
            }
            DataChanged?.Invoke();
        }

        public bool SyncNow(out string message)
        {
            lock (_lock)
            {
                if (!IsCustomDirectoryConfigured)
                {
                    message = "پایگاه داده در حال حاضر روی پوشه پیش‌فرض برنامه قرار دارد.";
                    return true;
                }

                bool success = PerformSyncAndMergeInternal();
                if (success)
                {
                    message = "همگام‌سازی و ادغام کامل با پوشه شبکه با موفقیت انجام شد.";
                    DataChanged?.Invoke();
                    return true;
                }
                else
                {
                    message = "امکان برقراری ارتباط با پوشه شبکه وجود ندارد. داده‌ها در پایگاه داده ثانویه محلی بدون وقفه نگهداری می‌شوند.";
                    return false;
                }
            }
        }

        // ==========================================
        // دسترسی به داده‌ها
        // ==========================================
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
                company.UpdatedAt = DateTime.Now;
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
                expert.UpdatedAt = DateTime.Now;
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
                invoice.UpdatedAt = DateTime.Now;
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

        // ==========================================
        // مدیریت شماره‌گذاری یکتا و پیشوند پیش‌فاکتور
        // ==========================================
        public static string NormalizeDigits(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Trim()
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        }

        public bool IsInvoiceNumberInUse(string? invoiceNumber, Guid? excludeInvoiceId = null)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber)) return false;
            CheckAndReloadIfDiskChanged();
            string target = NormalizeDigits(invoiceNumber);
            return _data.Invoices.Any(inv =>
                inv.Id != excludeInvoiceId &&
                !string.IsNullOrWhiteSpace(inv.InvoiceNumber) &&
                string.Equals(NormalizeDigits(inv.InvoiceNumber), target, StringComparison.OrdinalIgnoreCase));
        }

        public string PeekNextUniqueInvoiceNumber()
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                return CalculateNextUniqueInvoiceNumber(_data.Settings.InvoicePrefix, _data.Settings.NextInvoiceNumber, advance: false);
            }
        }

        public string GenerateNextUniqueInvoiceNumber()
        {
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                string nextNum = CalculateNextUniqueInvoiceNumber(_data.Settings.InvoicePrefix, _data.Settings.NextInvoiceNumber, advance: true);
                SaveInternal(_data);
                return nextNum;
            }
        }

        private string CalculateNextUniqueInvoiceNumber(string? prefix, int startNumber, bool advance)
        {
            string pref = !string.IsNullOrWhiteSpace(prefix) ? prefix : "ANA 405-";
            int currentNum = startNumber > 0 ? startNumber : 1001;

            var existingNumbers = new HashSet<string>(
                _data.Invoices
                    .Where(i => !string.IsNullOrWhiteSpace(i.InvoiceNumber))
                    .Select(i => NormalizeDigits(i.InvoiceNumber)),
                StringComparer.OrdinalIgnoreCase
            );

            while (existingNumbers.Contains(NormalizeDigits($"{pref}{currentNum}")))
            {
                currentNum++;
            }

            string result = $"{pref}{currentNum}";

            if (advance)
            {
                _data.Settings.NextInvoiceNumber = currentNum + 1;
            }

            return result;
        }

        public void RegisterUsedInvoiceNumber(string? usedNumber)
        {
            if (string.IsNullOrWhiteSpace(usedNumber)) return;
            lock (_lock)
            {
                CheckAndReloadIfDiskChanged();
                string pref = _data.Settings.InvoicePrefix ?? "ANA 405-";
                string normalizedUsed = NormalizeDigits(usedNumber);
                string normalizedPref = NormalizeDigits(pref);

                if (normalizedUsed.StartsWith(normalizedPref, StringComparison.OrdinalIgnoreCase))
                {
                    string numPart = normalizedUsed.Substring(normalizedPref.Length).Trim();
                    if (int.TryParse(numPart, out int parsedNum))
                    {
                        if (parsedNum >= _data.Settings.NextInvoiceNumber)
                        {
                            _data.Settings.NextInvoiceNumber = parsedNum + 1;
                            SaveInternal(_data);
                        }
                    }
                }
            }
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
                    NormalizeModel(restored);
                    _data = restored;
                    Save();
                }
            }
        }

        private DatabaseModel CreateSeedData()
        {
            var seed = new DatabaseModel();

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

            var exp1 = new Expert
            {
                FullName = "مهندس محمد رضایی",
                RoleTitle = "مدیر فروش تجهیزات صنعتی",
                PhoneNumber = "09121234567",
                Email = "rezaei@taraz.ir",
                CompanyId = c1.Id,
                CompanyName = c1.Name,
                InvoicePrefix = "EXP-REZ-",
                NextInvoiceNumber = 1002
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

            foreach (var item in inv1.Items)
            {
                item.UpdateFromPercent();
            }

            seed.Invoices.Add(inv1);
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
    }
}
