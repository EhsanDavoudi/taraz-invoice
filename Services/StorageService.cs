using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using TarazInvoice.Models;

namespace TarazInvoice.Services
{
    public class DatabaseModel
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Company> Companies { get; set; } = new List<Company>();
        public List<Expert> Experts { get; set; } = new List<Expert>();
        public List<ProformaInvoice> Invoices { get; set; } = new List<ProformaInvoice>();
        public List<StorageItem> StorageItems { get; set; } = new List<StorageItem>();
        public AppSettings Settings { get; set; } = new AppSettings();
        public List<Guid> DeletedIds { get; set; } = new List<Guid>();
        public Dictionary<Guid, DateTime> DeletedTimestamps { get; set; } = new Dictionary<Guid, DateTime>();
    }

    public class DbLocationConfig
    {
        public string? CustomDatabaseFolder { get; set; }
    }

    public enum LoadStatus
    {
        Ok,
        NotFound,
        Failed
    }

    public class LoadResult
    {
        public LoadStatus Status { get; }
        public DatabaseModel? Model { get; }

        public LoadResult(LoadStatus status, DatabaseModel? model)
        {
            Status = status;
            Model = model;
        }

        public static LoadResult Ok(DatabaseModel model) => new LoadResult(LoadStatus.Ok, model);
        public static LoadResult NotFound() => new LoadResult(LoadStatus.NotFound, null);
        public static LoadResult Failed() => new LoadResult(LoadStatus.Failed, null);
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
        private volatile bool _isSavingInternal = false;

        private readonly Timer? _debounceTimer;
        private readonly Timer? _backgroundPollTimer;
        private int _isPolling = 0;

        public event Action? DataChanged;
        public event Action<bool, string>? ConnectionStatusChanged;

        public bool IsNetworkConnected { get; private set; } = true;
        public DateTime? LastSyncTime { get; private set; } = null;
        public int PendingLocalChangesCount { get; private set; } = 0;

        public string LocalDatabasePath => _localFilePath;
        public string? PrimaryDatabaseDirectory => _primaryDirectory;
        public string PrimaryDatabasePath => _primaryFilePath;

        public bool IsCustomDirectoryConfigured
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_primaryDirectory)) return false;
                try
                {
                    string fullPrimary = Path.GetFullPath(_primaryDirectory.TrimEnd('\\', '/'));
                    string fullLocal = Path.GetFullPath(_localDirectory.TrimEnd('\\', '/'));
                    return !string.Equals(fullPrimary, fullLocal, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public StorageService(string? customFilePath = null)
        {
            _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                string dir = Path.GetDirectoryName(customFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                _localDirectory = dir;
                _localFilePath = customFilePath;
                _locationConfigPath = Path.Combine(dir, "db_location.json");
                _primaryDirectory = dir;
                _primaryFilePath = customFilePath;
            }
            else
            {
                _localDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                if (!Directory.Exists(_localDirectory))
                {
                    try { Directory.CreateDirectory(_localDirectory); } catch { }
                }
                _localFilePath = Path.Combine(_localDirectory, "database.json");
                _locationConfigPath = Path.Combine(_localDirectory, "db_location.json");

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

            // Background polling timer running every 2.5 seconds to detect external disk changes
            _backgroundPollTimer = new Timer(OnBackgroundPoll, null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));
        }

        private void NotifyDataChanged()
        {
            try
            {
                DataChanged?.Invoke();
            }
            catch { }
        }

        private void NotifyConnectionStatusChanged(bool connected, string message)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(connected, message);
            }
            catch { }
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
                var localRes = LoadFromFile(_localFilePath);

                if (IsCustomDirectoryConfigured)
                {
                    bool accessible = false;
                    try
                    {
                        accessible = Directory.Exists(_primaryDirectory) &&
                                     (File.Exists(_primaryFilePath) || IsDirectoryAccessible(_primaryDirectory, 1500));
                    }
                    catch
                    {
                        accessible = false;
                    }

                    IsNetworkConnected = accessible;

                    if (accessible)
                    {
                        var primaryRes = LoadFromFile(_primaryFilePath);

                        if (primaryRes.Status == LoadStatus.Failed)
                        {
                            // Network file exists but failed to read (locked / half-written).
                            // Do NOT overwrite server with localModel! Fall back to local and mark pending changes.
                            if (localRes.Status == LoadStatus.Ok && localRes.Model != null)
                            {
                                _data = localRes.Model;
                            }
                            else
                            {
                                _data = CreateSeedData();
                                SaveToFileInternal(_localFilePath, _data);
                            }
                            IsNetworkConnected = false;
                            PendingLocalChangesCount = 1;
                            UpdateDiskFingerprint(_localFilePath);
                            return;
                        }

                        if (primaryRes.Status == LoadStatus.Ok && primaryRes.Model != null &&
                            localRes.Status == LoadStatus.Ok && localRes.Model != null)
                        {
                            _data = MergeDatabases(localRes.Model, primaryRes.Model);
                            SaveToFileInternal(_localFilePath, _data);
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.UtcNow;
                            PendingLocalChangesCount = 0;
                        }
                        else if (primaryRes.Status == LoadStatus.Ok && primaryRes.Model != null)
                        {
                            _data = primaryRes.Model;
                            SaveToFileInternal(_localFilePath, _data);
                            LastSyncTime = DateTime.UtcNow;
                            PendingLocalChangesCount = 0;
                        }
                        else if (localRes.Status == LoadStatus.Ok && localRes.Model != null)
                        {
                            _data = localRes.Model;
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.UtcNow;
                            PendingLocalChangesCount = 0;
                        }
                        else
                        {
                            _data = CreateSeedData();
                            SaveToFileInternal(_localFilePath, _data);
                            SaveToFileInternal(_primaryFilePath, _data);
                            LastSyncTime = DateTime.UtcNow;
                            PendingLocalChangesCount = 0;
                        }

                        UpdateDiskFingerprint(_primaryFilePath);
                        return;
                    }
                    else
                    {
                        // Network directory is not accessible; use local database
                        if (localRes.Status == LoadStatus.Ok && localRes.Model != null)
                        {
                            _data = localRes.Model;
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

                // Normal mode (local database only)
                if (localRes.Status == LoadStatus.Ok && localRes.Model != null)
                {
                    _data = localRes.Model;
                    SaveToFileInternal(_localFilePath, _data);
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
                    _fileWatcher.Renamed += OnExternalFileRenamed;
                    _fileWatcher.Error += OnFileWatcherError;
                }
            }
            catch { }
        }

        private void OnExternalFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_isSavingInternal) return;
            try
            {
                _debounceTimer?.Change(300, Timeout.Infinite);
            }
            catch { }
        }

        private void OnExternalFileRenamed(object sender, RenamedEventArgs e)
        {
            if (_isSavingInternal) return;
            try
            {
                _debounceTimer?.Change(300, Timeout.Infinite);
            }
            catch { }
        }

        private void OnFileWatcherError(object sender, ErrorEventArgs e)
        {
            Task.Delay(2000).ContinueWith(_ =>
            {
                lock (_lock)
                {
                    SetupFileWatcher();
                }
            });
        }

        private void OnDebounceTimerElapsed(object? state)
        {
            if (_isSavingInternal) return;
            bool changed = false;
            try
            {
                changed = CheckAndReloadIfDiskChanged();
            }
            catch { }

            if (changed)
            {
                NotifyDataChanged();
            }
        }

        private void OnBackgroundPoll(object? state)
        {
            if (_isSavingInternal) return;
            if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0) return;

            try
            {
                bool changed = CheckAndReloadIfDiskChanged();
                if (changed)
                {
                    NotifyDataChanged();
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        public bool CheckAndReloadIfDiskChanged()
        {
            if (_isSavingInternal) return false;

            bool shouldNotifyConn = false;
            bool newConnState = false;
            string connMsg = string.Empty;
            bool dataChanged = false;

            lock (_lock)
            {
                if (IsCustomDirectoryConfigured)
                {
                    FileInfo? fileInfo = null;
                    try
                    {
                        if (File.Exists(_primaryFilePath))
                        {
                            fileInfo = new FileInfo(_primaryFilePath);
                        }
                    }
                    catch
                    {
                        fileInfo = null;
                    }

                    if (fileInfo == null)
                    {
                        if (IsNetworkConnected)
                        {
                            IsNetworkConnected = false;
                            shouldNotifyConn = true;
                            newConnState = false;
                            connMsg = "ارتباط با پوشه اصلی/شبکه قطع شده است. تغییرات در دیتابیس ثانویه محلی ذخیره می‌شوند.";
                        }
                        return false;
                    }

                    if (!IsNetworkConnected)
                    {
                        IsNetworkConnected = true;
                        shouldNotifyConn = true;
                        newConnState = true;
                        connMsg = "ارتباط با پوشه شبکه مجدداً برقرار شد. ادغام و همگام‌سازی خودکار انجام گردید.";
                        SetupFileWatcher();
                        dataChanged = PerformSyncAndMergeInternal();
                    }
                    else
                    {
                        DateTime currentDiskTime = fileInfo.LastWriteTimeUtc;
                        long currentLength = fileInfo.Length;

                        if (currentDiskTime != _lastWriteTimeUtc || currentLength != _lastFileSize)
                        {
                            var remoteRes = LoadFromFile(_primaryFilePath);
                            if (remoteRes.Status == LoadStatus.Ok && remoteRes.Model != null)
                            {
                                _data = MergeDatabases(_data, remoteRes.Model);

                                _isSavingInternal = true;
                                try
                                {
                                    SaveToFileInternal(_localFilePath, _data);
                                    SaveToFileInternal(_primaryFilePath, _data);
                                    UpdateDiskFingerprint(_primaryFilePath);
                                }
                                finally
                                {
                                    _isSavingInternal = false;
                                }

                                dataChanged = true;
                            }
                        }
                    }
                }
                else
                {
                    // Local database mode
                    try
                    {
                        if (File.Exists(_localFilePath))
                        {
                            var fileInfo = new FileInfo(_localFilePath);
                            DateTime currentDiskTime = fileInfo.LastWriteTimeUtc;
                            long currentLength = fileInfo.Length;

                            if (currentDiskTime != _lastWriteTimeUtc || currentLength != _lastFileSize)
                            {
                                var reloaded = LoadFromFile(_localFilePath);
                                if (reloaded.Status == LoadStatus.Ok && reloaded.Model != null)
                                {
                                    _data = reloaded.Model;
                                    _lastWriteTimeUtc = currentDiskTime;
                                    _lastFileSize = currentLength;
                                    dataChanged = true;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (shouldNotifyConn)
            {
                NotifyConnectionStatusChanged(newConnState, connMsg);
            }

            return dataChanged;
        }

        public void ReloadFromDisk()
        {
            bool success = false;
            lock (_lock)
            {
                if (IsCustomDirectoryConfigured && File.Exists(_primaryFilePath))
                {
                    success = PerformSyncAndMergeInternal();
                }
                else
                {
                    var reloaded = LoadFromFile(_localFilePath);
                    if (reloaded.Status == LoadStatus.Ok && reloaded.Model != null)
                    {
                        _data = reloaded.Model;
                        UpdateDiskFingerprint(_localFilePath);
                        success = true;
                    }
                }
            }

            if (success)
            {
                NotifyDataChanged();
            }
        }

        private LoadResult LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return LoadResult.NotFound();

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    string json = reader.ReadToEnd();

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Thread.Sleep(50 + (attempt * 10));
                        continue;
                    }

                    var model = JsonSerializer.Deserialize<DatabaseModel>(json, _jsonOptions);
                    if (model != null)
                    {
                        NormalizeModel(model);
                        return LoadResult.Ok(model);
                    }
                }
                catch
                {
                    Thread.Sleep(50 + (attempt * 10));
                }
            }
            return LoadResult.Failed();
        }
        private static void NormalizeModel(DatabaseModel model)
        {
            model.StorageItems ??= new List<StorageItem>();
            model.Companies ??= new List<Company>();
            model.Experts ??= new List<Expert>();
            model.Invoices ??= new List<ProformaInvoice>();
            model.Settings ??= new AppSettings();
            model.DeletedIds ??= new List<Guid>();
            model.DeletedTimestamps ??= new Dictionary<Guid, DateTime>();

            if (model.SchemaVersion < 1)
            {
                model.SchemaVersion = 1;
            }

            // Remove tombstoned records from memory
            if (model.DeletedIds.Count > 0)
            {
                var delSet = new HashSet<Guid>(model.DeletedIds);
                model.Invoices.RemoveAll(i => delSet.Contains(i.Id));
                model.Companies.RemoveAll(c => delSet.Contains(c.Id));
                model.Experts.RemoveAll(e => delSet.Contains(e.Id));
                model.StorageItems.RemoveAll(s => delSet.Contains(s.Id));
            }

            if (model.Settings.NextInvoiceNumber <= 0)
            {
                model.Settings.NextInvoiceNumber = 1;
            }

            if (string.IsNullOrWhiteSpace(model.Settings.CurrencyUnit))
            {
                model.Settings.CurrencyUnit = "ریال";
            }

            foreach (var inv in model.Invoices)
            {
                if (string.IsNullOrWhiteSpace(inv.CurrencyUnit))
                {
                    inv.CurrencyUnit = model.Settings.CurrencyUnit;
                }
            }

            if (model.Settings.BankAccounts == null)
            {
                model.Settings.BankAccounts = new List<BankAccountInfo>();
            }
        }

        private bool SaveToFileInternal(string filePath, DatabaseModel model)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { return false; }
            }

            string json = JsonSerializer.Serialize(model, _jsonOptions);
            string tempPath = filePath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(json);
                    writer.Flush();
                    fs.Flush(true);
                }

                for (int attempt = 0; attempt < 15; attempt++)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Replace(tempPath, filePath, null);
                            }
                            catch (PlatformNotSupportedException)
                            {
                                File.Copy(tempPath, filePath, overwrite: true);
                                try { File.Delete(tempPath); } catch { }
                            }
                        }
                        else
                        {
                            File.Move(tempPath, filePath);
                        }
                        return true;
                    }
                    catch
                    {
                        Thread.Sleep(60 + (attempt * 15));
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch { }
            }
        }

        private void UpdateDiskFingerprint(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var fi = new FileInfo(filePath);
                    _lastWriteTimeUtc = fi.LastWriteTimeUtc;
                    _lastFileSize = fi.Length;
                }
            }
            catch { }
        }

        private void SaveInternal(DatabaseModel model)
        {
            _isSavingInternal = true;
            try
            {
                // 1. Always save in local secondary database first
                SaveToFileInternal(_localFilePath, model);

                // 2. Custom/network folder handling
                if (IsCustomDirectoryConfigured)
                {
                    if (PendingLocalChangesCount > 0 && File.Exists(_primaryFilePath))
                    {
                        var remoteRes = LoadFromFile(_primaryFilePath);
                        if (remoteRes.Status == LoadStatus.Ok && remoteRes.Model != null)
                        {
                            model = MergeDatabases(model, remoteRes.Model);
                            _data = model; // Ensure in-memory _data receives merged result!
                            SaveToFileInternal(_localFilePath, model);
                        }
                        else if (remoteRes.Status == LoadStatus.Failed)
                        {
                            // Network file is locked or half-written. Do NOT overwrite!
                            IsNetworkConnected = false;
                            PendingLocalChangesCount++;
                            UpdateDiskFingerprint(_localFilePath);
                            return;
                        }
                    }

                    bool primarySaved = SaveToFileInternal(_primaryFilePath, model);
                    if (primarySaved)
                    {
                        _data = model;
                        IsNetworkConnected = true;
                        LastSyncTime = DateTime.UtcNow;
                        PendingLocalChangesCount = 0;
                        UpdateDiskFingerprint(_primaryFilePath);
                    }
                    else
                    {
                        IsNetworkConnected = false;
                        PendingLocalChangesCount++;
                        UpdateDiskFingerprint(_localFilePath);
                    }
                }
                else
                {
                    _data = model;
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
            NotifyDataChanged();
        }

        public void Dispose()
        {
            try { _backgroundPollTimer?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            try { _fileWatcher?.Dispose(); } catch { }
        }

        private static DateTime EnsureUtc(DateTime dt)
        {
            if (dt == DateTime.MinValue) return DateTime.MinValue;
            if (dt == DateTime.MaxValue) return DateTime.MaxValue;
            if (dt.Kind == DateTimeKind.Utc) return dt;
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        // ==========================================
        // الگوریتم ادغام هوشمند دیتابیس محلی و شبکه
        // ==========================================
        public static DatabaseModel MergeDatabases(DatabaseModel local, DatabaseModel primary)
        {
            var merged = new DatabaseModel();

            // ۰. ادغام شناسه‌های حذف‌شده (Tombstones) و مهرهای زمانی حذف
            var mergedDeletedIds = new HashSet<Guid>(local.DeletedIds ?? Enumerable.Empty<Guid>());
            var mergedTimestamps = new Dictionary<Guid, DateTime>();

            if (local.DeletedTimestamps != null)
            {
                foreach (var kvp in local.DeletedTimestamps)
                {
                    mergedTimestamps[kvp.Key] = EnsureUtc(kvp.Value);
                }
            }

            if (primary.DeletedIds != null)
            {
                foreach (var id in primary.DeletedIds)
                {
                    mergedDeletedIds.Add(id);
                }
            }

            if (primary.DeletedTimestamps != null)
            {
                foreach (var kvp in primary.DeletedTimestamps)
                {
                    var utcTime = EnsureUtc(kvp.Value);
                    if (!mergedTimestamps.TryGetValue(kvp.Key, out var existingTime) || utcTime > existingTime)
                    {
                        mergedTimestamps[kvp.Key] = utcTime;
                    }
                }
            }

            // ادغام تنظیمات (Settings) - ابتدا انجام می‌شود تا پیشوند فعال مشخص گردد
            AppSettings mergedSettings;
            if (primary.Settings != null && local.Settings != null)
            {
                mergedSettings = (EnsureUtc(local.Settings.UpdatedAt) >= EnsureUtc(primary.Settings.UpdatedAt))
                    ? local.Settings
                    : primary.Settings;
            }
            else
            {
                mergedSettings = primary.Settings ?? local.Settings ?? new AppSettings();
            }
            merged.Settings = mergedSettings;

            // ۱. ادغام پیش‌فاکتورها (Invoices) بر اساس شناسه و زمان بروزرسانی
            var invoiceMap = new Dictionary<Guid, ProformaInvoice>();
            foreach (var inv in primary.Invoices)
            {
                if (!mergedDeletedIds.Contains(inv.Id))
                {
                    invoiceMap[inv.Id] = inv;
                }
            }

            foreach (var localInv in local.Invoices)
            {
                if (mergedDeletedIds.Contains(localInv.Id))
                {
                    // اگر ویرایش محلی پس از زمان حذف انجام شده باشد، فاکتور بازیابی می‌شود
                    if (mergedTimestamps.TryGetValue(localInv.Id, out var delTime) && EnsureUtc(localInv.UpdatedAt) > delTime)
                    {
                        mergedDeletedIds.Remove(localInv.Id);
                        mergedTimestamps.Remove(localInv.Id);
                        invoiceMap[localInv.Id] = localInv;
                    }
                    continue;
                }

                if (invoiceMap.TryGetValue(localInv.Id, out var existing))
                {
                    if (EnsureUtc(localInv.UpdatedAt) > EnsureUtc(existing.UpdatedAt))
                    {
                        invoiceMap[localInv.Id] = localInv;
                    }
                }
                else
                {
                    invoiceMap[localInv.Id] = localInv;
                }
            }

            // تضمین عدم تکرار شماره فاکتورها در ادغام بدون ایجاد اثر دومینو
            var finalInvoices = invoiceMap.Values.OrderBy(i => EnsureUtc(i.CreatedAt)).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dupes = new List<ProformaInvoice>();

            foreach (var inv in finalInvoices)
            {
                string norm = NormalizeDigits(inv.InvoiceNumber);
                if (string.IsNullOrWhiteSpace(norm) || !seen.Add(norm))
                {
                    dupes.Add(inv);
                }
            }

            string pref = merged.Settings?.InvoicePrefix ?? "";
            int nextCandidate = Math.Max(primary.Settings?.NextInvoiceNumber ?? 1, local.Settings?.NextInvoiceNumber ?? 1);

            foreach (var inv in dupes)
            {
                while (seen.Contains(NormalizeDigits($"{pref}{nextCandidate}")))
                {
                    nextCandidate++;
                }
                inv.InvoiceNumber = $"{pref}{nextCandidate}";
                inv.UpdatedAt = DateTime.UtcNow;
                seen.Add(NormalizeDigits(inv.InvoiceNumber));
                nextCandidate++;
            }

            merged.Invoices = finalInvoices.OrderByDescending(i => EnsureUtc(i.UpdatedAt)).ToList();

            // ۲. ادغام شرکت‌ها (Companies) با نرمال‌سازی فارسی و حذف طرف بازنده جهت جلوگیری از نوسان
            var companyMap = new Dictionary<Guid, Company>();
            foreach (var c in primary.Companies)
            {
                if (!mergedDeletedIds.Contains(c.Id))
                {
                    companyMap[c.Id] = c;
                }
            }
            foreach (var lc in local.Companies)
            {
                if (mergedDeletedIds.Contains(lc.Id))
                {
                    if (mergedTimestamps.TryGetValue(lc.Id, out var delTime) && EnsureUtc(lc.UpdatedAt) > delTime)
                    {
                        mergedDeletedIds.Remove(lc.Id);
                        mergedTimestamps.Remove(lc.Id);
                        companyMap[lc.Id] = lc;
                    }
                    continue;
                }

                if (companyMap.TryGetValue(lc.Id, out var existing))
                {
                    if (EnsureUtc(lc.UpdatedAt) > EnsureUtc(existing.UpdatedAt))
                    {
                        companyMap[lc.Id] = lc;
                    }
                }
                else
                {
                    string normLcName = NormalizePersianText(lc.Name);
                    var sameName = companyMap.Values.FirstOrDefault(x =>
                        string.Equals(NormalizePersianText(x.Name), normLcName, StringComparison.OrdinalIgnoreCase));

                    if (sameName != null)
                    {
                        if (EnsureUtc(lc.UpdatedAt) > EnsureUtc(sameName.UpdatedAt))
                        {
                            companyMap.Remove(sameName.Id);
                            mergedDeletedIds.Add(sameName.Id);
                            mergedTimestamps[sameName.Id] = DateTime.UtcNow;
                            companyMap[lc.Id] = lc;
                        }
                        else
                        {
                            mergedDeletedIds.Add(lc.Id);
                            mergedTimestamps[lc.Id] = DateTime.UtcNow;
                        }
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
            foreach (var exp in primary.Experts)
            {
                if (!mergedDeletedIds.Contains(exp.Id))
                {
                    expertMap[exp.Id] = exp;
                }
            }
            foreach (var le in local.Experts)
            {
                if (mergedDeletedIds.Contains(le.Id))
                {
                    if (mergedTimestamps.TryGetValue(le.Id, out var delTime) && EnsureUtc(le.UpdatedAt) > delTime)
                    {
                        mergedDeletedIds.Remove(le.Id);
                        mergedTimestamps.Remove(le.Id);
                        expertMap[le.Id] = le;
                    }
                    continue;
                }

                if (expertMap.TryGetValue(le.Id, out var existing))
                {
                    if (EnsureUtc(le.UpdatedAt) > EnsureUtc(existing.UpdatedAt))
                    {
                        expertMap[le.Id] = le;
                    }
                }
                else
                {
                    string normLeName = NormalizePersianText(le.FullName);
                    var sameName = expertMap.Values.FirstOrDefault(x =>
                        string.Equals(NormalizePersianText(x.FullName), normLeName, StringComparison.OrdinalIgnoreCase) &&
                        x.CompanyId == le.CompanyId);

                    if (sameName != null)
                    {
                        if (EnsureUtc(le.UpdatedAt) > EnsureUtc(sameName.UpdatedAt))
                        {
                            expertMap.Remove(sameName.Id);
                            mergedDeletedIds.Add(sameName.Id);
                            mergedTimestamps[sameName.Id] = DateTime.UtcNow;
                            expertMap[le.Id] = le;
                        }
                        else
                        {
                            mergedDeletedIds.Add(le.Id);
                            mergedTimestamps[le.Id] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        expertMap[le.Id] = le;
                    }
                }
            }
            merged.Experts = expertMap.Values.OrderBy(e => e.FullName).ToList();

            // ۴. ادغام کالاهای انبار (StorageItems) با تطبیق کد کالا و نام کالا
            var storageMap = new Dictionary<Guid, StorageItem>();
            foreach (var itm in primary.StorageItems)
            {
                if (!mergedDeletedIds.Contains(itm.Id))
                {
                    storageMap[itm.Id] = itm;
                }
            }
            foreach (var li in local.StorageItems)
            {
                if (mergedDeletedIds.Contains(li.Id))
                {
                    if (mergedTimestamps.TryGetValue(li.Id, out var delTime) && EnsureUtc(li.UpdatedAt) > delTime)
                    {
                        mergedDeletedIds.Remove(li.Id);
                        mergedTimestamps.Remove(li.Id);
                        storageMap[li.Id] = li;
                    }
                    continue;
                }

                if (storageMap.TryGetValue(li.Id, out var existing))
                {
                    if (EnsureUtc(li.UpdatedAt) > EnsureUtc(existing.UpdatedAt))
                    {
                        storageMap[li.Id] = li;
                    }
                }
                else
                {
                    string normLiCode = NormalizeDigits(li.Code);
                    string normLiName = NormalizePersianText(li.Name);

                    var existingMatch = storageMap.Values.FirstOrDefault(x =>
                        (!string.IsNullOrWhiteSpace(normLiCode) && string.Equals(NormalizeDigits(x.Code), normLiCode, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(NormalizePersianText(x.Name), normLiName, StringComparison.OrdinalIgnoreCase));

                    if (existingMatch != null)
                    {
                        if (EnsureUtc(li.UpdatedAt) > EnsureUtc(existingMatch.UpdatedAt))
                        {
                            storageMap.Remove(existingMatch.Id);
                            mergedDeletedIds.Add(existingMatch.Id);
                            mergedTimestamps[existingMatch.Id] = DateTime.UtcNow;
                            storageMap[li.Id] = li;
                        }
                        else
                        {
                            mergedDeletedIds.Add(li.Id);
                            mergedTimestamps[li.Id] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        storageMap[li.Id] = li;
                    }
                }
            }
            merged.StorageItems = storageMap.Values.OrderBy(s => s.Name).ToList();

            merged.DeletedIds = mergedDeletedIds.ToList();
            merged.DeletedTimestamps = mergedTimestamps;

            // محاسبه شماره فاکتور بعدی بدون پرش غیرعادی روی شماره‌های دستی بزرگ
            int maxNextNum = Math.Max(nextCandidate, Math.Max(primary.Settings?.NextInvoiceNumber ?? 1, local.Settings?.NextInvoiceNumber ?? 1));
            string activePrefix = NormalizeDigits(merged.Settings?.InvoicePrefix ?? "");

            foreach (var inv in merged.Invoices)
            {
                string norm = NormalizeDigits(inv.InvoiceNumber);
                if (!string.IsNullOrEmpty(activePrefix) && norm.StartsWith(activePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string numPart = norm.Substring(activePrefix.Length).Trim();
                    if (int.TryParse(numPart, out int parsed))
                    {
                        if (parsed >= maxNextNum && (parsed - maxNextNum < 5000))
                        {
                            maxNextNum = parsed + 1;
                        }
                    }
                }
                else if (string.IsNullOrEmpty(activePrefix))
                {
                    if (int.TryParse(norm, out int parsed))
                    {
                        if (parsed >= maxNextNum && (parsed - maxNextNum < 5000))
                        {
                            maxNextNum = parsed + 1;
                        }
                    }
                }
            }
            merged.Settings ??= new AppSettings();
            merged.Settings.NextInvoiceNumber = maxNextNum;

            // ادغام حساب‌های بانکی
            merged.Settings.BankAccounts = mergedSettings.BankAccounts != null
                ? new List<BankAccountInfo>(mergedSettings.BankAccounts)
                : new List<BankAccountInfo>();

            return merged;
        }

        private bool PerformSyncAndMergeInternal()
        {
            try
            {
                if (!IsCustomDirectoryConfigured) return false;

                var localRes = LoadFromFile(_localFilePath);
                var localModel = (localRes.Status == LoadStatus.Ok && localRes.Model != null) ? localRes.Model : _data;

                if (File.Exists(_primaryFilePath))
                {
                    var remoteRes = LoadFromFile(_primaryFilePath);
                    if (remoteRes.Status == LoadStatus.Failed)
                    {
                        IsNetworkConnected = false;
                        return false;
                    }

                    if (remoteRes.Status == LoadStatus.Ok && remoteRes.Model != null)
                    {
                        _data = MergeDatabases(localModel, remoteRes.Model);
                    }
                    else
                    {
                        _data = localModel;
                    }
                }
                else
                {
                    _data = localModel;
                }

                _isSavingInternal = true;
                try
                {
                    SaveToFileInternal(_localFilePath, _data);
                    bool primSaved = SaveToFileInternal(_primaryFilePath, _data);
                    if (primSaved)
                    {
                        IsNetworkConnected = true;
                        LastSyncTime = DateTime.UtcNow;
                        PendingLocalChangesCount = 0;
                        UpdateDiskFingerprint(_primaryFilePath);
                        return true;
                    }
                    else
                    {
                        IsNetworkConnected = false;
                        PendingLocalChangesCount++;
                        UpdateDiskFingerprint(_localFilePath);
                        return false;
                    }
                }
                finally
                {
                    _isSavingInternal = false;
                }
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
                try
                {
                    string fullNew = Path.GetFullPath(trimmed.TrimEnd('\\', '/'));
                    string fullLocal = Path.GetFullPath(_localDirectory.TrimEnd('\\', '/'));
                    if (string.Equals(fullNew, fullLocal, StringComparison.OrdinalIgnoreCase))
                    {
                        ResetToDefaultDatabaseDirectory();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = "مسیر وارد شده نامعتبر است: " + ex.Message;
                    return false;
                }

                if (!IsDirectoryAccessible(trimmed, 3000))
                {
                    errorMessage = "دسترسی به پوشه انتخاب شده یا مجوز ذخیره‌سازی در آن برقرار نشد. لطفاً درستی مسیر شبکه و دسترسی آن را بررسی فرمایید.";
                    return false;
                }

                string candidateFilePath = Path.Combine(trimmed, "database.json");
                if (File.Exists(candidateFilePath))
                {
                    var remoteRes = LoadFromFile(candidateFilePath);
                    if (remoteRes.Status == LoadStatus.Failed)
                    {
                        errorMessage = "امکان خواندن فایل پایگاه‌داده در مسیر انتخابی وجود ندارد (فایل قفل است یا ساختار نامعتبر دارد). جهت جلوگیری از رونویسی ناخواسته، عملیات متوقف شد.";
                        return false;
                    }

                    _primaryDirectory = trimmed;
                    _primaryFilePath = candidateFilePath;
                    SaveLocationConfig(trimmed);

                    if (remoteRes.Status == LoadStatus.Ok && remoteRes.Model != null)
                    {
                        _data = MergeDatabases(_data, remoteRes.Model);
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
                    _primaryDirectory = trimmed;
                    _primaryFilePath = candidateFilePath;
                    SaveLocationConfig(trimmed);
                    SaveToFileInternal(_primaryFilePath, _data);
                }

                IsNetworkConnected = true;
                LastSyncTime = DateTime.UtcNow;
                PendingLocalChangesCount = 0;
                UpdateDiskFingerprint(_primaryFilePath);
                SetupFileWatcher();
            }

            NotifyDataChanged();
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
            NotifyDataChanged();
        }

        public bool SyncNow(out string message)
        {
            bool success;
            lock (_lock)
            {
                if (!IsCustomDirectoryConfigured)
                {
                    message = "پایگاه داده در حال حاضر روی پوشه پیش‌فرض برنامه قرار دارد.";
                    return true;
                }

                success = PerformSyncAndMergeInternal();
                if (success)
                {
                    message = "همگام‌سازی و ادغام کامل با پوشه شبکه با موفقیت انجام شد.";
                }
                else
                {
                    message = "امکان برقراری ارتباط با پوشه شبکه وجود ندارد. داده‌ها در پایگاه داده ثانویه محلی بدون وقفه نگهداری می‌شوند.";
                }
            }

            if (success)
            {
                NotifyDataChanged();
            }
            return success;
        }

        // ==========================================
        // دسترسی به داده‌ها (کاملاً از حافظه، بدون I/O در UI thread)
        // ==========================================
        public List<Company> GetCompanies()
        {
            lock (_lock)
            {
                return _data.Companies;
            }
        }

        public List<Expert> GetExperts()
        {
            lock (_lock)
            {
                return _data.Experts;
            }
        }

        public List<ProformaInvoice> GetInvoices()
        {
            lock (_lock)
            {
                return _data.Invoices;
            }
        }

        public AppSettings GetSettings()
        {
            lock (_lock)
            {
                return _data.Settings;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            lock (_lock)
            {
                settings.UpdatedAt = DateTime.UtcNow;
                _data.Settings = settings;
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        public void UpdateSettings(Action<AppSettings> updateAction)
        {
            lock (_lock)
            {
                _data.Settings ??= new AppSettings();
                updateAction(_data.Settings);
                _data.Settings.UpdatedAt = DateTime.UtcNow;
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        public List<StorageItem> GetStorageItems()
        {
            lock (_lock)
            {
                return _data.StorageItems;
            }
        }

        // عملیات شرکت‌ها
        public void AddOrUpdateCompany(Company company)
        {
            lock (_lock)
            {
                company.UpdatedAt = DateTime.UtcNow;
                _data.DeletedIds.Remove(company.Id);
                _data.DeletedTimestamps.Remove(company.Id);

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
            NotifyDataChanged();
        }

        public void DeleteCompany(Guid companyId)
        {
            lock (_lock)
            {
                _data.Companies.RemoveAll(c => c.Id == companyId);
                if (!_data.DeletedIds.Contains(companyId))
                {
                    _data.DeletedIds.Add(companyId);
                }
                _data.DeletedTimestamps[companyId] = DateTime.UtcNow;
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        // عملیات کارشناسان
        public void AddOrUpdateExpert(Expert expert)
        {
            lock (_lock)
            {
                expert.UpdatedAt = DateTime.UtcNow;
                _data.DeletedIds.Remove(expert.Id);
                _data.DeletedTimestamps.Remove(expert.Id);

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
            NotifyDataChanged();
        }

        public void DeleteExpert(Guid expertId)
        {
            lock (_lock)
            {
                _data.Experts.RemoveAll(e => e.Id == expertId);
                if (!_data.DeletedIds.Contains(expertId))
                {
                    _data.DeletedIds.Add(expertId);
                }
                _data.DeletedTimestamps[expertId] = DateTime.UtcNow;
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        // عملیات پیش‌فاکتورها
        public void AddOrUpdateInvoice(ProformaInvoice invoice)
        {
            lock (_lock)
            {
                invoice.UpdatedAt = DateTime.UtcNow;
                _data.DeletedIds.Remove(invoice.Id);
                _data.DeletedTimestamps.Remove(invoice.Id);

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
            NotifyDataChanged();
        }

        public void DeleteInvoice(Guid invoiceId)
        {
            lock (_lock)
            {
                _data.Invoices.RemoveAll(i => i.Id == invoiceId);
                if (!_data.DeletedIds.Contains(invoiceId))
                {
                    _data.DeletedIds.Add(invoiceId);
                }
                _data.DeletedTimestamps[invoiceId] = DateTime.UtcNow;
                SaveInternal(_data);
            }
            NotifyDataChanged();
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

        public static string NormalizePersianText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (char c in input.Trim())
            {
                switch (c)
                {
                    case 'ي':
                    case 'ى':
                        sb.Append('ی');
                        break;
                    case 'ك':
                        sb.Append('ک');
                        break;
                    case '\u200C': // نیم‌فاصله
                    case '\u200B': // zero-width space
                    case '\uFEFF': // BOM
                        sb.Append(' ');
                        break;
                    case '۰': case '٠': sb.Append('0'); break;
                    case '۱': case '١': sb.Append('1'); break;
                    case '۲': case '٢': sb.Append('2'); break;
                    case '۳': case '٣': sb.Append('3'); break;
                    case '۴': case '٤': sb.Append('4'); break;
                    case '۵': case '٥': sb.Append('5'); break;
                    case '۶': case '٦': sb.Append('6'); break;
                    case '۷': case '٧': sb.Append('7'); break;
                    case '۸': case '٨': sb.Append('8'); break;
                    case '۹': case '٩': sb.Append('9'); break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        public bool IsInvoiceNumberInUse(string? invoiceNumber, Guid? excludeInvoiceId = null)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber)) return false;
            lock (_lock)
            {
                string target = NormalizeDigits(invoiceNumber);
                return _data.Invoices.Any(inv =>
                    inv.Id != excludeInvoiceId &&
                    !string.IsNullOrWhiteSpace(inv.InvoiceNumber) &&
                    string.Equals(NormalizeDigits(inv.InvoiceNumber), target, StringComparison.OrdinalIgnoreCase));
            }
        }

        public string PeekNextUniqueInvoiceNumber()
        {
            lock (_lock)
            {
                return CalculateNextUniqueInvoiceNumber(_data.Settings.InvoicePrefix, _data.Settings.NextInvoiceNumber, advance: false);
            }
        }

        public string GenerateNextUniqueInvoiceNumber()
        {
            lock (_lock)
            {
                string nextNum = CalculateNextUniqueInvoiceNumber(_data.Settings.InvoicePrefix, _data.Settings.NextInvoiceNumber, advance: true);
                SaveInternal(_data);
                return nextNum;
            }
        }

        private string CalculateNextUniqueInvoiceNumber(string? prefix, int startNumber, bool advance)
        {
            string pref = prefix ?? "";
            int currentNum = startNumber > 0 ? startNumber : 1;

            var existingNumbers = new HashSet<string>(
                _data.Invoices
                    .Where(i => !string.IsNullOrWhiteSpace(i.InvoiceNumber))
                    .Select(i => NormalizeDigits(i.InvoiceNumber)),
                StringComparer.OrdinalIgnoreCase
            );

            string normalizedPref = NormalizeDigits(pref);
            foreach (var num in existingNumbers)
            {
                if (!string.IsNullOrEmpty(normalizedPref) && num.StartsWith(normalizedPref, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = num.Substring(normalizedPref.Length).Trim();
                    if (int.TryParse(suffix, out int parsedVal))
                    {
                        if (parsedVal >= currentNum && (parsedVal - currentNum < 5000))
                        {
                            currentNum = parsedVal + 1;
                        }
                    }
                }
                else if (string.IsNullOrEmpty(normalizedPref))
                {
                    if (int.TryParse(num, out int parsedVal))
                    {
                        if (parsedVal >= currentNum && (parsedVal - currentNum < 5000))
                        {
                            currentNum = parsedVal + 1;
                        }
                    }
                }
            }

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
                string pref = _data.Settings.InvoicePrefix ?? "";
                string normalizedUsed = NormalizeDigits(usedNumber);
                string normalizedPref = NormalizeDigits(pref);

                if (!string.IsNullOrEmpty(normalizedPref) && normalizedUsed.StartsWith(normalizedPref, StringComparison.OrdinalIgnoreCase))
                {
                    string numPart = normalizedUsed.Substring(normalizedPref.Length).Trim();
                    if (int.TryParse(numPart, out int parsedNum))
                    {
                        if (parsedNum >= _data.Settings.NextInvoiceNumber && (parsedNum - _data.Settings.NextInvoiceNumber < 5000))
                        {
                            _data.Settings.NextInvoiceNumber = parsedNum + 1;
                            SaveInternal(_data);
                        }
                    }
                }
                else if (string.IsNullOrEmpty(normalizedPref))
                {
                    if (int.TryParse(normalizedUsed, out int parsedNum))
                    {
                        if (parsedNum >= _data.Settings.NextInvoiceNumber && (parsedNum - _data.Settings.NextInvoiceNumber < 5000))
                        {
                            _data.Settings.NextInvoiceNumber = parsedNum + 1;
                            SaveInternal(_data);
                        }
                    }
                }
            }
        }

        private string GenerateNextProductCode()
        {
            int maxNum = 100;
            foreach (var itm in _data.StorageItems)
            {
                if (!string.IsNullOrWhiteSpace(itm.Code))
                {
                    string norm = NormalizeDigits(itm.Code);
                    if (norm.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase))
                    {
                        string suffix = norm.Substring(4).Trim();
                        if (int.TryParse(suffix, out int parsed) && parsed > maxNum)
                        {
                            maxNum = parsed;
                        }
                    }
                }
            }

            var existingCodes = new HashSet<string>(
                _data.StorageItems.Where(s => !string.IsNullOrWhiteSpace(s.Code)).Select(s => NormalizeDigits(s.Code)),
                StringComparer.OrdinalIgnoreCase
            );

            int candidate = maxNum + 1;
            while (existingCodes.Contains(NormalizeDigits($"PRD-{candidate}")))
            {
                candidate++;
            }
            return $"PRD-{candidate}";
        }

        // عملیات انبار و کالاها (Storage Items)
        public void AddOrUpdateStorageItem(StorageItem item)
        {
            lock (_lock)
            {
                _data.DeletedIds.Remove(item.Id);
                _data.DeletedTimestamps.Remove(item.Id);

                var existing = _data.StorageItems.FirstOrDefault(p => p.Id == item.Id);
                if (existing != null)
                {
                    int index = _data.StorageItems.IndexOf(existing);
                    item.UpdatedAt = DateTime.UtcNow;
                    _data.StorageItems[index] = item;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(item.Code))
                    {
                        item.Code = GenerateNextProductCode();
                    }
                    item.CreatedAt = DateTime.UtcNow;
                    item.UpdatedAt = DateTime.UtcNow;
                    _data.StorageItems.Add(item);
                }
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        public void DeleteStorageItem(Guid itemId)
        {
            lock (_lock)
            {
                _data.StorageItems.RemoveAll(p => p.Id == itemId);
                if (!_data.DeletedIds.Contains(itemId))
                {
                    _data.DeletedIds.Add(itemId);
                }
                _data.DeletedTimestamps[itemId] = DateTime.UtcNow;
                SaveInternal(_data);
            }
            NotifyDataChanged();
        }

        public StorageItem EnsureStorageItemExists(string name, string unit, decimal defaultPrice, bool updatePriceIfDifferent = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return null!;
            lock (_lock)
            {
                var trimmed = name.Trim();
                var normName = NormalizePersianText(trimmed);
                var existing = _data.StorageItems.FirstOrDefault(p =>
                    string.Equals(NormalizePersianText(p.Name), normName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (updatePriceIfDifferent && defaultPrice > 0 && defaultPrice != existing.DefaultPrice)
                    {
                        existing.DefaultPrice = defaultPrice;
                        existing.UpdatedAt = DateTime.UtcNow;
                        SaveInternal(_data);
                    }
                    else if (existing.DefaultPrice <= 0 && defaultPrice > 0)
                    {
                        existing.DefaultPrice = defaultPrice;
                        existing.UpdatedAt = DateTime.UtcNow;
                        SaveInternal(_data);
                    }
                    return existing;
                }

                var newItem = new StorageItem
                {
                    Id = Guid.NewGuid(),
                    Code = GenerateNextProductCode(),
                    Name = trimmed,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "عدد" : unit.Trim(),
                    DefaultPrice = defaultPrice,
                    StockQuantity = 0,
                    Category = "عمومی",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _data.StorageItems.Add(newItem);
                SaveInternal(_data);
                return newItem;
            }
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

        // بازیابی واقعی بدون ادغام با رکوردهای حذف‌شده قبلی
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

                    _isSavingInternal = true;
                    try
                    {
                        SaveToFileInternal(_localFilePath, _data);
                        if (IsCustomDirectoryConfigured && IsNetworkConnected)
                        {
                            SaveToFileInternal(_primaryFilePath, _data);
                            UpdateDiskFingerprint(_primaryFilePath);
                        }
                        else
                        {
                            UpdateDiskFingerprint(_localFilePath);
                        }
                        PendingLocalChangesCount = 0;
                        LastSyncTime = DateTime.UtcNow;
                    }
                    finally
                    {
                        _isSavingInternal = false;
                    }
                }
            }
            NotifyDataChanged();
        }

        private DatabaseModel CreateSeedData()
        {
            var seed = new DatabaseModel
            {
                Companies = new List<Company>(),
                Experts = new List<Expert>(),
                Invoices = new List<ProformaInvoice>(),
                StorageItems = new List<StorageItem>(),
                Settings = new AppSettings
                {
                    InvoicePrefix = string.Empty,
                    NextInvoiceNumber = 1,
                    CurrencyUnit = "ریال"
                },
                DeletedIds = new List<Guid>(),
                DeletedTimestamps = new Dictionary<Guid, DateTime>(),
                SchemaVersion = 1
            };

            return seed;
        }
    }
}
