using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using TarazInvoice.Models;
using TarazInvoice.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;
using DataGrid = System.Windows.Controls.DataGrid;

namespace TarazInvoice
{
    public partial class MainWindow : FluentWindow
    {
        private readonly StorageService _storage;
        private readonly ExcelExportService _excelService;
        private readonly PrintPdfService _printService;

        // وضعیت فعلی ویرایش پیش‌فاکتور
        private ProformaInvoice? _currentEditingInvoice = null;
        private ObservableCollection<InvoiceItem> _editingItems = new ObservableCollection<InvoiceItem>();
        private ObservableCollection<BankAccountInfo> _editingBankAccounts = new ObservableCollection<BankAccountInfo>();
        private Wpf.Ui.Controls.TextBox? _currentGridTextBox = null;
        private InvoiceItem? _currentEditingInvoiceItem = null;
        private bool _isApplyingGridSuggestion = false;
        private bool _isGridItemUserTyping = false;
        private string _gridItemLoadedText = string.Empty;
        private System.Windows.Threading.DispatcherTimer? _networkSyncTimer = null;

        // وضعیت فعلی شرکت و کارشناس در حال ویرایش
        private Company? _currentEditingCompany = null;
        private Expert? _currentEditingExpert = null;

        // وضعیت فعلی کالای انبار در حال ویرایش
        private StorageItem? _currentEditingStorageItem = null;

        // فیلتر وضعیت چیپ‌ها
        private InvoiceStatus? _selectedFilterStatus = null;
        private bool _isNewInvoice = false;
        private bool _isDirty = false;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                var iconUri = new Uri("pack://application:,,,/TarazInvoice;component/Resources/logo.png", UriKind.Absolute);
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }

            _storage = new StorageService();
            _storage.DataChanged += Storage_DataChanged;
            _storage.ConnectionStatusChanged += Storage_ConnectionStatusChanged;
            _excelService = new ExcelExportService(_storage);
            _printService = new PrintPdfService(_storage);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Activated += MainWindow_Activated;
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            try
            {
                if (_storage.CheckAndReloadIfDiskChanged())
                {
                    RefreshDropdownsAndActiveViews();
                }
            }
            catch { }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_networkSyncTimer != null)
            {
                _networkSyncTimer.Stop();
            }
        }

        private void Storage_DataChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    RefreshAll();
                }
                catch
                {
                    // خطا در خواندن همزمان نادیده گرفته می‌شود
                }
            });
        }

        private void Storage_ConnectionStatusChanged(bool connected, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateDbStatusUI();
                SetStatus(message);
            });
        }

        private void RefreshDropdownsAndActiveViews()
        {
            // بروزرسانی شرکت‌ها در ادیتور با حفظ انتخاب
            if (CmbEditorCompany != null)
            {
                var curCompId = (CmbEditorCompany.SelectedItem as Company)?.Id;
                var companies = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                CmbEditorCompany.ItemsSource = null;
                CmbEditorCompany.ItemsSource = companies;
                if (curCompId.HasValue)
                {
                    CmbEditorCompany.SelectedItem = companies.FirstOrDefault(c => c.Id == curCompId.Value);
                }
            }

            // بروزرسانی کارشناسان در ادیتور با حفظ انتخاب و فیلتر متناسب با شرکت
            if (CmbEditorExpert != null)
            {
                var curExpId = (CmbEditorExpert.SelectedItem as Expert)?.Id;
                var comp = CmbEditorCompany?.SelectedItem as Company;
                var allExperts = _storage.GetExperts().OrderBy(e => e.FullName).ToList();
                var experts = (comp != null) ? allExperts.Where(x => x.BelongsToCompany(comp.Id)).ToList() : allExperts;

                var expertList = new List<Expert>
                {
                    new Expert { Id = Guid.Empty, FullName = "(بدون کارشناس)", RoleTitle = "" }
                };
                expertList.AddRange(experts);

                CmbEditorExpert.ItemsSource = null;
                CmbEditorExpert.ItemsSource = expertList;
                if (curExpId.HasValue && curExpId.Value != Guid.Empty)
                {
                    CmbEditorExpert.SelectedItem = expertList.FirstOrDefault(e => e.Id == curExpId.Value) ?? expertList[0];
                }
                else
                {
                    CmbEditorExpert.SelectedIndex = 0;
                }
            }

            // بروزرسانی منوهای فیلتر شرکت و کارشناس در لیست پیش‌فاکتورها
            if (CmbFilterCompany != null)
            {
                var curComp = CmbFilterCompany.SelectedItem;
                CmbFilterCompany.Items.Clear();
                CmbFilterCompany.Items.Add("همه شرکت‌ها");
                var companies = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                foreach (var c in companies) CmbFilterCompany.Items.Add(c);
                if (curComp is Company oldC)
                {
                    var match = companies.FirstOrDefault(c => c.Id == oldC.Id);
                    CmbFilterCompany.SelectedItem = match ?? (object)"همه شرکت‌ها";
                }
                else
                {
                    CmbFilterCompany.SelectedIndex = 0;
                }
            }

            if (CmbFilterExpert != null)
            {
                var curExp = CmbFilterExpert.SelectedItem;
                CmbFilterExpert.Items.Clear();
                CmbFilterExpert.Items.Add("همه کارشناسان");
                var experts = _storage.GetExperts().OrderBy(e => e.FullName).ToList();
                foreach (var ex in experts) CmbFilterExpert.Items.Add(ex);
                if (curExp is Expert oldE)
                {
                    var match = experts.FirstOrDefault(e => e.Id == oldE.Id);
                    CmbFilterExpert.SelectedItem = match ?? (object)"همه کارشناسان";
                }
                else
                {
                    CmbFilterExpert.SelectedIndex = 0;
                }
            }

            // بروزرسانی صفحات فعال در کاربری همزمان تحت شبکه
            if (ViewInvoices != null && ViewInvoices.Visibility == Visibility.Visible)
            {
                RefreshInvoicesList();
            }
            else if (ViewCompanies != null && ViewCompanies.Visibility == Visibility.Visible)
            {
                RefreshCompaniesList();
            }
            else if (ViewExperts != null && ViewExperts.Visibility == Visibility.Visible)
            {
                RefreshExpertsList();
            }
            else if (ViewStorage != null && ViewStorage.Visibility == Visibility.Visible)
            {
                RefreshStorageList();
            }
            else if (ViewReports != null && ViewReports.Visibility == Visibility.Visible)
            {
                RefreshReportsList();
            }
        }

        private void SetDirty(bool dirty)
        {
            _isDirty = dirty;
            UpdateWindowTitle();
            if (IcoTopSaveStatus != null && TxtTopSaveStatus != null && BrdTopSaveStatus != null)
            {
                if (dirty)
                {
                    IcoTopSaveStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Edit24;
                    IcoTopSaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                    TxtTopSaveStatus.Text = "تغییرات ذخیره‌نشده (Ctrl + S)";
                    TxtTopSaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                    BrdTopSaveStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                    BrdTopSaveStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                }
                else
                {
                    IcoTopSaveStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                    IcoTopSaveStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    TxtTopSaveStatus.Text = _isNewInvoice ? "آماده (Ctrl + S)" : $"ذخیره شد ({DateTime.Now:HH:mm:ss}) - Ctrl+S";
                    TxtTopSaveStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    BrdTopSaveStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                    BrdTopSaveStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                }
            }
        }

        private void UpdateWindowTitle()
        {
            const string baseTitle = "تـراز (نسخه ۱.۰.۱) - سامانه جامع صدور و مدیریت پیش‌فاکتور";
            if (_isDirty && ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible)
            {
                Title = $"* {baseTitle} (ویرایش پیش‌فاکتور - تغییرات ذخیره‌نشده)";
            }
            else
            {
                Title = baseTitle;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtCurrentDate.Text = "امروز: " + JalaliDateHelper.GetCurrentShamsiDate();
            
            InitCombos();
            RefreshAll();
            LoadSettingsTab();

            // فعال‌سازی تایمر همگام‌سازی زنده تحت شبکه (مشابه عملکرد React Query و Invalidation خودکار)
            _networkSyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _networkSyncTimer.Tick += (s, ev) =>
            {
                try
                {
                    if (_storage.CheckAndReloadIfDiskChanged())
                    {
                        RefreshAll();
                    }
                    UpdateDbStatusUI();
                }
                catch { }
            };
            _networkSyncTimer.Start();

            // اتصال رویدادهای باز شدن کمبوباکس برای بارگذاری بلادرنگ داده‌های شبکه
            if (CmbEditorCompany != null)
            {
                CmbEditorCompany.DropDownOpened += CmbEditorCompany_DropDownOpened;
            }
            if (CmbEditorExpert != null)
            {
                CmbEditorExpert.DropDownOpened += CmbEditorExpert_DropDownOpened;
            }

            // ورود با یک کلیک به حالت ویرایش در جدول اقلام
            if (DgrInvoiceItems != null)
            {
                DgrInvoiceItems.PreviewMouseLeftButtonDown += DgrInvoiceItems_PreviewMouseLeftButtonDown;
            }

            SwitchToView("Invoices");
        }

        private void CmbEditorCompany_DropDownOpened(object? sender, EventArgs e)
        {
            try
            {
                _storage.CheckAndReloadIfDiskChanged();
                var curId = (CmbEditorCompany.SelectedItem as Company)?.Id;
                var companies = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                CmbEditorCompany.ItemsSource = companies;
                if (curId.HasValue)
                {
                    CmbEditorCompany.SelectedItem = companies.FirstOrDefault(c => c.Id == curId.Value);
                }
            }
            catch { }
        }

        private void CmbEditorExpert_DropDownOpened(object? sender, EventArgs e)
        {
            try
            {
                _storage.CheckAndReloadIfDiskChanged();
                var curId = (CmbEditorExpert.SelectedItem as Expert)?.Id;
                var comp = CmbEditorCompany.SelectedItem as Company;
                var allExperts = _storage.GetExperts().OrderBy(ex => ex.FullName).ToList();
                var experts = (comp != null) ? allExperts.Where(x => x.BelongsToCompany(comp.Id)).ToList() : allExperts;

                var expertList = new List<Expert>
                {
                    new Expert { Id = Guid.Empty, FullName = "(بدون کارشناس)", RoleTitle = "" }
                };
                expertList.AddRange(experts);

                CmbEditorExpert.ItemsSource = expertList;
                if (curId.HasValue && curId.Value != Guid.Empty)
                {
                    CmbEditorExpert.SelectedItem = expertList.FirstOrDefault(x => x.Id == curId.Value) ?? expertList[0];
                }
                else
                {
                    CmbEditorExpert.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void DgrInvoiceItems_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (PopupGridSuggestions != null && PopupGridSuggestions.IsOpen)
            {
                PopupGridSuggestions.IsOpen = false;
            }

            var dep = (DependencyObject)e.OriginalSource;

            // اگر کلیک روی تکست‌باکس در حال ویرایش، دکمه‌های جدول (حذف، کپی)، چک‌باکس یا اسکرول‌بار باشد، دخالت نمی‌کنیم
            var walk = dep;
            while (walk != null && walk is not DataGridCell && walk != DgrInvoiceItems)
            {
                if (walk is System.Windows.Controls.TextBox ||
                    walk is Wpf.Ui.Controls.TextBox ||
                    walk is System.Windows.Controls.Primitives.ButtonBase ||
                    walk is Wpf.Ui.Controls.Button ||
                    walk is CheckBox ||
                    walk is System.Windows.Controls.Primitives.ScrollBar)
                {
                    return;
                }
                walk = VisualTreeHelper.GetParent(walk);
            }

            while (dep != null && dep is not DataGridCell && dep is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                try
                {
                    DgrInvoiceItems.CommitEdit(DataGridEditingUnit.Cell, true);
                    DgrInvoiceItems.CommitEdit(DataGridEditingUnit.Row, true);
                }
                catch { }

                if (cell.DataContext is InvoiceItem item)
                {
                    DgrInvoiceItems.SelectedItem = item;
                }
                cell.Focus();
                DgrInvoiceItems.CurrentCell = new DataGridCellInfo(cell);

                // ورود به ویرایش سلول به شکل دیفر شده تا روتینگ ماوس و انتخاب سطر بدون گلیچ پایان یابد
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!cell.IsEditing && DgrInvoiceItems.CurrentCell.IsValid)
                    {
                        DgrInvoiceItems.BeginEdit();
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void InitCombos()
        {
            if (CmbEditorStatus != null)
            {
                CmbEditorStatus.Items.Clear();
                foreach (InvoiceStatus s in Enum.GetValues(typeof(InvoiceStatus)))
                {
                    CmbEditorStatus.Items.Add(s.ToPersianString());
                }
                CmbEditorStatus.SelectedIndex = 0;
            }

            // فیلتر ماه صفحه اصلی
            if (CmbFilterMonth != null)
            {
                CmbFilterMonth.Items.Clear();
                CmbFilterMonth.Items.Add("همه ماه‌ها");
                foreach (var m in JalaliDateHelper.PersianMonths)
                {
                    CmbFilterMonth.Items.Add(m);
                }
                CmbFilterMonth.SelectedIndex = 0;
            }

            // فیلترهای بخش گزارشات
            if (CmbReportMonth != null)
            {
                CmbReportMonth.Items.Clear();
                CmbReportMonth.Items.Add("همه ماه‌ها");
                foreach (var m in JalaliDateHelper.PersianMonths)
                {
                    CmbReportMonth.Items.Add(m);
                }
                CmbReportMonth.SelectedIndex = 0;
            }

            if (CmbReportYear != null)
            {
                CmbReportYear.Items.Clear();
                CmbReportYear.Items.Add("همه سال‌ها");
                CmbReportYear.Items.Add("1405");
                CmbReportYear.Items.Add("1404");
                CmbReportYear.Items.Add("1403");
                CmbReportYear.Items.Add("1402");
                CmbReportYear.SelectedIndex = 0;
            }

            if (CmbReportStatus != null)
            {
                CmbReportStatus.Items.Clear();
                CmbReportStatus.Items.Add("همه وضعیت‌ها");
                foreach (InvoiceStatus s in Enum.GetValues(typeof(InvoiceStatus)))
                {
                    CmbReportStatus.Items.Add(s.ToPersianString());
                }
                CmbReportStatus.SelectedIndex = 0;
            }

            // واحدهای استاندارد اندازه‌گیری انبار و کالاها
            string[] units = new[] { "عدد", "دستگاه", "متر", "کیلوگرم", "شاخه", "بسته", "ست", "لیتر", "قالب", "کارتن" };
            if (CmbStorageUnit != null)
            {
                CmbStorageUnit.Items.Clear();
                foreach (var u in units) CmbStorageUnit.Items.Add(u);
                CmbStorageUnit.SelectedIndex = 0;
            }
            if (CmbQuickItemUnit != null)
            {
                CmbQuickItemUnit.Items.Clear();
                foreach (var u in units) CmbQuickItemUnit.Items.Add(u);
                CmbQuickItemUnit.SelectedIndex = 0;
            }

            // بارگذاری تمام فونت‌های محلی سیستم کاربر (Local System Fonts) با اولویت قلم‌های محبوب فارسی
            var allSystemFonts = new List<string>();
            string[] popularPersian = new[] { "Vazirmatn", "B Nazanin", "B Titr", "B Yekan", "B Mitra", "B Zar", "IRANSans", "IranNastaliq", "Tahoma", "Segoe UI", "Arial", "Calibri", "Aptos" };

            try
            {
                var installedFonts = System.Windows.Media.Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .ToList();

                // ابتدا قلم‌های پرکاربرد فارسی
                foreach (var p in popularPersian)
                {
                    if (p == "Vazirmatn" || installedFonts.Any(f => f.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!allSystemFonts.Contains(p, StringComparer.OrdinalIgnoreCase))
                            allSystemFonts.Add(p);
                    }
                }

                // سپس تمام فونت‌های نصب شده روی ویندوز سیستم محلی به ترتیب الفبا
                foreach (var f in installedFonts.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!allSystemFonts.Contains(f, StringComparer.OrdinalIgnoreCase))
                        allSystemFonts.Add(f);
                }
            }
            catch
            {
                allSystemFonts.AddRange(popularPersian);
            }

            if (CmbFontCompanyName != null)
            {
                CmbFontCompanyName.Items.Clear();
                foreach (var f in allSystemFonts) CmbFontCompanyName.Items.Add(f);
                if (CmbFontCompanyName.Items.Count > 0) CmbFontCompanyName.SelectedIndex = 0;
            }
            if (CmbFontTable != null)
            {
                CmbFontTable.Items.Clear();
                foreach (var f in allSystemFonts) CmbFontTable.Items.Add(f);
                if (CmbFontTable.Items.Count > 0) CmbFontTable.SelectedIndex = 0;
            }
        }

        #region سایدبار و جابجایی بین صفحات

        // قلم‌موهای ایستا برای حالت‌های مختلف سایدبار جهت کارایی بالا
        private static readonly SolidColorBrush NavActiveBgBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)); // Cobalt 50
        private static readonly SolidColorBrush NavActiveBorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)); // Blue 200
        private static readonly SolidColorBrush NavActiveIconBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)); // Blue 600
        private static readonly SolidColorBrush NavActiveTextBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)); // Blue 700

        private static readonly SolidColorBrush NavHoverBgBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)); // Slate 100
        private static readonly SolidColorBrush NavHoverBorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)); // Slate 200
        private static readonly SolidColorBrush NavHoverIconBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)); // Slate 800
        private static readonly SolidColorBrush NavHoverTextBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)); // Slate 900

        private static readonly SolidColorBrush NavInactiveBgBrush = Brushes.Transparent;
        private static readonly SolidColorBrush NavInactiveBorderBrush = Brushes.Transparent;
        private static readonly SolidColorBrush NavInactiveIconBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // Slate 500
        private static readonly SolidColorBrush NavInactiveTextBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)); // Slate 700

        private string _currentActiveView = "Invoices";

        static MainWindow()
        {
            NavActiveBgBrush.Freeze();
            NavActiveBorderBrush.Freeze();
            NavActiveIconBrush.Freeze();
            NavActiveTextBrush.Freeze();

            NavHoverBgBrush.Freeze();
            NavHoverBorderBrush.Freeze();
            NavHoverIconBrush.Freeze();
            NavHoverTextBrush.Freeze();

            NavInactiveIconBrush.Freeze();
            NavInactiveTextBrush.Freeze();
        }

        private void NavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                if (tag == "Editor")
                {
                    if (_currentEditingInvoice != null && _currentEditingInvoice.Status == InvoiceStatus.Draft)
                    {
                        SwitchToView("Editor");
                    }
                    else
                    {
                        // اگر پیش‌فاکتور نیمه‌کاره‌ای (پیش‌نویس) وجود دارد، آن را بارگذاری می‌کند؛ در غیر این صورت فرم خالی باز می‌شود
                        var lastDraft = _storage.GetInvoices().LastOrDefault(i => i.Status == InvoiceStatus.Draft);
                        if (lastDraft != null)
                        {
                            OpenInvoiceEditor(lastDraft, isNew: false);
                        }
                        else
                        {
                            BtnNewInvoice_Click(sender, e);
                        }
                    }
                }
                else
                {
                    SwitchToView(tag);
                }
            }
        }

        private (Wpf.Ui.Controls.SymbolIcon? Icon, TextBlock? Text) GetNavElements(string tag)
        {
            return tag switch
            {
                "Invoices" => (NavIconInvoices, NavTextInvoices),
                "Editor" => (NavIconEditor, NavTextEditor),
                "Storage" => (NavIconStorage, NavTextStorage),
                "Reports" => (NavIconReports, NavTextReports),
                "Companies" => (NavIconCompanies, NavTextCompanies),
                "Experts" => (NavIconExperts, NavTextExperts),
                "Settings" => (NavIconSettings, NavTextSettings),
                _ => (null, null)
            };
        }

        private void UpdateSidebarActiveState(string activeView)
        {
            _currentActiveView = activeView;

            SetNavItemState(NavCardInvoices, NavIconInvoices, NavTextInvoices, activeView == "Invoices");
            SetNavItemState(NavCardEditor, NavIconEditor, NavTextEditor, activeView == "Editor");
            SetNavItemState(NavCardStorage, NavIconStorage, NavTextStorage, activeView == "Storage");
            SetNavItemState(NavCardReports, NavIconReports, NavTextReports, activeView == "Reports");
            SetNavItemState(NavCardCompanies, NavIconCompanies, NavTextCompanies, activeView == "Companies");
            SetNavItemState(NavCardExperts, NavIconExperts, NavTextExperts, activeView == "Experts");
            SetNavItemState(NavCardSettings, NavIconSettings, NavTextSettings, activeView == "Settings");
        }

        private void SetNavItemState(Border? card, Wpf.Ui.Controls.SymbolIcon? icon, TextBlock? text, bool isActive)
        {
            if (card == null || icon == null || text == null) return;

            if (isActive)
            {
                card.Background = NavActiveBgBrush;
                card.BorderBrush = NavActiveBorderBrush;
                card.BorderThickness = new Thickness(1);
                icon.Foreground = NavActiveIconBrush;
                text.Foreground = NavActiveTextBrush;
                text.FontWeight = FontWeights.Bold;
            }
            else
            {
                card.Background = NavInactiveBgBrush;
                card.BorderBrush = NavInactiveBorderBrush;
                card.BorderThickness = new Thickness(1);
                icon.Foreground = NavInactiveIconBrush;
                text.Foreground = NavInactiveTextBrush;
                text.FontWeight = FontWeights.Normal;
            }
        }

        private void NavCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border card && card.Tag is string tag)
            {
                if (tag != _currentActiveView)
                {
                    card.Background = NavHoverBgBrush;
                    card.BorderBrush = NavHoverBorderBrush;
                    var (icon, text) = GetNavElements(tag);
                    if (icon != null) icon.Foreground = NavHoverIconBrush;
                    if (text != null) text.Foreground = NavHoverTextBrush;
                }
            }
        }

        private void NavCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border card && card.Tag is string tag)
            {
                if (tag != _currentActiveView)
                {
                    card.Background = NavInactiveBgBrush;
                    card.BorderBrush = NavInactiveBorderBrush;
                    var (icon, text) = GetNavElements(tag);
                    if (icon != null) icon.Foreground = NavInactiveIconBrush;
                    if (text != null) text.Foreground = NavInactiveTextBrush;
                }
            }
        }

        private void SwitchToView(string viewName)
        {
            if (ViewInvoices == null) return;

            if (ViewInvoices != null) ViewInvoices.Visibility = Visibility.Collapsed;
            if (ViewInvoiceEditor != null) ViewInvoiceEditor.Visibility = Visibility.Collapsed;
            if (ViewStorage != null) ViewStorage.Visibility = Visibility.Collapsed;
            if (ViewReports != null) ViewReports.Visibility = Visibility.Collapsed;
            if (ViewCompanies != null) ViewCompanies.Visibility = Visibility.Collapsed;
            if (ViewExperts != null) ViewExperts.Visibility = Visibility.Collapsed;
            if (ViewSettings != null) ViewSettings.Visibility = Visibility.Collapsed;

            // بروزرسانی حالت فعال سایدبار
            UpdateSidebarActiveState(viewName);

            switch (viewName)
            {
                case "Invoices":
                    if (ViewInvoices != null) ViewInvoices.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "فهرست و کارتابل پیش‌فاکتورها";
                    TxtPageSubtitle.Text = "رهگیری وضعیت فروش، خروجی تمیز اکسل برای مشتریان و چاپ مستقیم رسمی";
                    RefreshInvoicesList();
                    break;

                case "Editor":
                    if (_currentEditingInvoice == null)
                    {
                        var lastDraft = _storage.GetInvoices().LastOrDefault(i => i.Status == InvoiceStatus.Draft);
                        if (lastDraft != null)
                        {
                            OpenInvoiceEditor(lastDraft, isNew: false);
                        }
                        else
                        {
                            BtnNewInvoice_Click(this, new RoutedEventArgs());
                            return;
                        }
                    }
                    if (ViewInvoiceEditor != null) ViewInvoiceEditor.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = _isNewInvoice ? "صدور پیش‌فاکتور جدید" : $"ویرایش پیش‌فاکتور {_currentEditingInvoice?.InvoiceNumber}";
                    TxtPageSubtitle.Text = "محاسبات تخفیف و فرمول ۱.۱ به صورت خودکار محاسبه و خلاصه مالی بروزرسانی می‌شود";
                    break;

                case "Storage":
                    if (ViewStorage != null) ViewStorage.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "انبار و مدیریت کالاها و قطعات";
                    TxtPageSubtitle.Text = "مشاهده، تعریف و قیمت‌گذاری کالاها جهت تکمیل خودکار و سریع اقلام پیش‌فاکتور";
                    RefreshStorageList();
                    break;

                case "Reports":
                    if (ViewReports != null) ViewReports.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "گزارشات و تحلیل جامع فروش";
                    TxtPageSubtitle.Text = "فیلتر و گزارش‌گیری پیشرفته بر اساس ماه، سال، کارشناس، شرکت و وضعیت تسویه و ارسال";
                    RefreshReportsList();
                    break;

                case "Companies":
                    if (ViewCompanies != null) ViewCompanies.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "مدیریت شرکت‌ها (طرف حساب)";
                    TxtPageSubtitle.Text = "ثبت، ویرایش و مشاهده مشخصات شرکت‌های خریدار و مشتریان";
                    RefreshCompaniesList();
                    break;

                case "Experts":
                    if (ViewExperts != null) ViewExperts.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "مدیریت کارشناسان خرید";
                    TxtPageSubtitle.Text = "تنظیم کارشناسان خرید و شرکت‌های مجاز";
                    RefreshExpertsList();
                    break;

                case "Settings":
                    if (ViewSettings != null) ViewSettings.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = "قالب اکسل و تنظیمات سامانه";
                    TxtPageSubtitle.Text = "معرفی فایل قالب اختصاصی اکسل، مشخصات شرکت فروشنده و پشتیبان‌گیری";
                    LoadSettingsTab();
                    break;
            }
        }

        #region تنظیم تناسبات ستون‌های جداول (Proportional Column Auto-Sizing)

        private void DataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                // پس از پایان درگ تغییر اندازه ستون، مقدار ستون به پیکسل ثابت تبدیل می‌شود.
                // با تبدیل مجدد ستون‌ها به وزن‌های ستاره‌ای (Star)، تمام ستون‌ها به صورت هماهنگ مقیاس‌پذیر می‌مانند
                // و تغییر اندازه ستون‌ها روی تمام ستون‌های دیگر سرشکن می‌شود، نه فقط ستون قبلی یا بعدی.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NormalizeColumnsToStar(dg);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void NormalizeColumnsToStar(DataGrid dg)
        {
            var visibleColumns = dg.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
            if (visibleColumns.Count == 0) return;

            // فقط در صورتی که حداقل یک ستون از حالت درگ تغییر اندازه به پیکسل قطعی تبدیل شده باشد
            if (visibleColumns.Any(c => c.Width.IsAbsolute))
            {
                foreach (var col in visibleColumns)
                {
                    if (col.ActualWidth > 0)
                    {
                        col.Width = new DataGridLength(col.ActualWidth, DataGridLengthUnitType.Star);
                    }
                }
            }
        }

        #endregion

        private void RefreshAll()
        {
            RefreshInvoicesList();
            RefreshCompaniesList();
            RefreshExpertsList();
            RefreshStorageList();
            RefreshDropdownsAndActiveViews();
            UpdateStats();
        }

        private void UpdateStats()
        {
            var invoices = _storage.GetInvoices();
            var companies = _storage.GetCompanies();
            var experts = _storage.GetExperts();

            decimal totalAmount = invoices.Sum(i => i.GrandTotal);
            decimal totalProfit = invoices.Sum(i => i.TotalProfit);
            var approvedInvoices = invoices.Where(i => i.Status == InvoiceStatus.Approved).ToList();
            decimal approvedAmount = approvedInvoices.Sum(i => i.GrandTotal);
            int pendingCount = invoices.Count(i => i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Sent);

            if (TxtKpiTotalAmount != null) TxtKpiTotalAmount.Text = $"{totalAmount:N0} ریال";
            if (TxtKpiTotalCount != null) TxtKpiTotalCount.Text = $"تعداد: {invoices.Count} فاکتور صادرشده";

            if (TxtKpiTotalProfit != null) TxtKpiTotalProfit.Text = $"{totalProfit:N0} ریال";
            if (TxtKpiProfitMargin != null)
            {
                decimal margin = totalAmount > 0 ? Math.Round((totalProfit / totalAmount) * 100m, 1) : 0;
                TxtKpiProfitMargin.Text = $"میانگین حاشیه سود: {margin}٪";
            }

            if (TxtKpiApprovedAmount != null) TxtKpiApprovedAmount.Text = $"{approvedAmount:N0} ریال";
            if (TxtKpiApprovedCount != null) TxtKpiApprovedCount.Text = $"تعداد: {approvedInvoices.Count} تأیید نهایی";

            if (TxtKpiPendingCount != null) TxtKpiPendingCount.Text = $"{pendingCount} پیش‌فاکتور";

            if (TxtKpiCompaniesCount != null) TxtKpiCompaniesCount.Text = $"{companies.Count} شرکت";
            if (TxtKpiExpertsCount != null) TxtKpiExpertsCount.Text = $"{experts.Count} کارشناس فعال";
        }

        #endregion

        #region فهرست پیش‌فاکتورها و فیلترهای سریع چیپ

        private void ChipStatus_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == ChipStatusAll) _selectedFilterStatus = null;
            else if (sender == ChipStatusPreparingGoods) _selectedFilterStatus = InvoiceStatus.PreparingGoods;
            else if (sender == ChipStatusSent) _selectedFilterStatus = InvoiceStatus.Sent;
            else if (sender == ChipStatusNotSent) _selectedFilterStatus = InvoiceStatus.NotSent;
            else if (sender == ChipStatusPaid) _selectedFilterStatus = InvoiceStatus.Paid;
            else if (sender == ChipStatusUnpaid) _selectedFilterStatus = InvoiceStatus.Unpaid;
            else if (sender == ChipStatusApproved) _selectedFilterStatus = InvoiceStatus.Approved;
            else if (sender == ChipStatusDraft) _selectedFilterStatus = InvoiceStatus.Draft;
            else if (sender == ChipStatusPending) _selectedFilterStatus = InvoiceStatus.Pending;
            else if (sender == ChipStatusCanceled) _selectedFilterStatus = InvoiceStatus.Canceled;

            RefreshInvoicesList();
        }

        private void RefreshInvoicesList()
        {
            if (DgrInvoices == null) return;

            var invoices = _storage.GetInvoices().OrderByDescending(i => i.CreatedAt).ToList();

            // فیلتر جستجوی متنی
            string search = TxtSearchInvoice?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                invoices = invoices.Where(i =>
                    (i.InvoiceNumber != null && i.InvoiceNumber.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (i.CompanyName != null && i.CompanyName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (i.ExpertName != null && i.ExpertName.Contains(search, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            // فیلتر وضعیت چیپ‌ها
            if (_selectedFilterStatus.HasValue)
            {
                invoices = invoices.Where(i => i.Status == _selectedFilterStatus.Value).ToList();
            }

            // فیلتر کارشناس
            if (CmbFilterExpert?.SelectedItem is Expert exp)
            {
                invoices = invoices.Where(i => i.ExpertId == exp.Id).ToList();
            }

            // فیلتر ماه صدور
            if (CmbFilterMonth != null && CmbFilterMonth.SelectedIndex > 0)
            {
                int monthIndex = CmbFilterMonth.SelectedIndex;
                invoices = invoices.Where(i => JalaliDateHelper.GetMonthNumber(i.IssueDateShamsi) == monthIndex).ToList();
            }

            // فیلتر شرکت (طرف حساب)
            if (CmbFilterCompany != null && CmbFilterCompany.SelectedItem is Company comp)
            {
                invoices = invoices.Where(i => i.CompanyId == comp.Id).ToList();
            }

            DgrInvoices.ItemsSource = invoices;
            UpdateStats();
        }

        private void TxtSearchInvoice_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshInvoicesList();
        }

        private void CmbFilterExpert_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshInvoicesList();
        }

        private void CmbFilterMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshInvoicesList();
        }

        private void CmbFilterCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshInvoicesList();
        }

        private void BtnRefreshInvoices_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSearchInvoice != null) TxtSearchInvoice.Text = "";
            if (ChipStatusAll != null) ChipStatusAll.IsChecked = true;
            _selectedFilterStatus = null;
            if (CmbFilterExpert != null && CmbFilterExpert.Items.Count > 0) CmbFilterExpert.SelectedIndex = 0;
            if (CmbFilterMonth != null && CmbFilterMonth.Items.Count > 0) CmbFilterMonth.SelectedIndex = 0;
            if (CmbFilterCompany != null && CmbFilterCompany.Items.Count > 0) CmbFilterCompany.SelectedIndex = 0;
            RefreshInvoicesList();
            SetStatus("لیست پیش‌فاکتورها بروزرسانی شد.");
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                ExportInvoiceToExcel(invoice);
            }
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                ExportInvoiceToPdfWithDialog(invoice);
            }
        }

        private bool ExportInvoiceToPdfWithDialog(ProformaInvoice invoice, AppSettings? overrideSettings = null)
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "ذخیره پیش‌فاکتور به صورت فایل PDF",
                    Filter = "فایل‌های PDF (*.pdf)|*.pdf",
                    FileName = PrintPdfService.GetSafeInvoiceFileName(invoice, "pdf"),
                    DefaultExt = ".pdf",
                    AddExtension = true
                };

                if (sfd.ShowDialog() == true)
                {
                    string targetPath = sfd.FileName;
                    _printService.SaveInvoiceAsPdfFile(invoice, targetPath, overrideSettings);

                    SetStatus($"فایل PDF پیش‌فاکتور {invoice.InvoiceNumber} با موفقیت در «{System.IO.Path.GetFileName(targetPath)}» ذخیره شد.");

                    // باز کردن خودکار فایل پی‌دی‌اف پس از ذخیره جهت بررسی کاربر
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(targetPath)
                        {
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception exOpen)
                    {
                        System.Diagnostics.Debug.WriteLine("Cannot auto-open PDF: " + exOpen.Message);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در ذخیره فایل PDF:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        private void ExportInvoiceToExcel(ProformaInvoice invoice)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = PrintPdfService.GetSafeInvoiceFileName(invoice, "xlsx")
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportInvoice(invoice, sfd.FileName);
                    SetStatus($"فایل اکسل مشتری با موفقیت ذخیره شد: {sfd.FileName}");

                    var res = MessageBox.Show(
                        "فایل اکسل تمیز مشتری با موفقیت صادر گردید.\nآیا مایل به باز کردن فایل در اکسل هستید؟",
                        "صدور موفقیت‌آمیز اکسل",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در صدور فایل اکسل:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnPrintPdf_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                try
                {
                    _printService.PrintOrExportToPdf(invoice);
                    SetStatus($"دستور چاپ یا صدور PDF پیش‌فاکتور {invoice.InvoiceNumber} صادر شد.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در چاپ یا ذخیره PDF:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnApproveAndPrintRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                try
                {
                    invoice.Status = InvoiceStatus.Approved;
                    invoice.UpdatedAt = DateTime.Now;
                    _storage.AddOrUpdateInvoice(invoice);
                    RefreshInvoicesList();
                    SetStatus($"پیش‌فاکتور {invoice.InvoiceNumber} تأیید شد و ارسال به چاپگر انجام شد.");
                    _printService.PrintOrExportToPdf(invoice);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در تأیید و چاپ پیش‌فاکتور:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDuplicateInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice sourceInvoice)
            {
                DuplicateInvoice(sourceInvoice);
            }
        }

        private void DuplicateInvoice(ProformaInvoice sourceInvoice)
        {
            string newNumber = _storage.PeekNextUniqueInvoiceNumber();

            var newInvoice = sourceInvoice.CloneForNew(newNumber);
            OpenInvoiceEditor(newInvoice, isNew: true);
            SetStatus($"نسخه کپی با شماره جدید {newNumber} ایجاد شد.");
        }

        private void BtnEditInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                OpenInvoiceEditor(invoice, isNew: false);
            }
        }

        private void BtnChangeStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                var values = (InvoiceStatus[])Enum.GetValues(typeof(InvoiceStatus));
                int currentIndex = Array.IndexOf(values, invoice.Status);
                int next = (currentIndex + 1) % values.Length;
                invoice.Status = values[next];
                invoice.UpdatedAt = DateTime.Now;
                _storage.AddOrUpdateInvoice(invoice);

                RefreshInvoicesList();
                SetStatus($"وضعیت پیش‌فاکتور {invoice.InvoiceNumber} به «{invoice.Status.ToPersianString()}» تغییر یافت.");
            }
        }

        private void BtnDeleteInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is ProformaInvoice invoice)
            {
                DeleteInvoice(invoice);
            }
        }

        private void DeleteInvoice(ProformaInvoice invoice)
        {
            var result = MessageBox.Show(
                $"آیا از حذف پیش‌فاکتور شماره {invoice.InvoiceNumber} متعلق به {invoice.CompanyName} اطمینان دارید؟",
                "تأیید حذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _storage.DeleteInvoice(invoice.Id);
                RefreshAll();
                SetStatus($"پیش‌فاکتور {invoice.InvoiceNumber} حذف شد.");
            }
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MenuItem_Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement mi && mi.DataContext is ProformaInvoice inv)
            {
                OpenInvoiceEditor(inv, isNew: false);
            }
        }

        private void MenuItem_Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement mi && mi.DataContext is ProformaInvoice sourceInvoice)
            {
                DuplicateInvoice(sourceInvoice);
            }
        }

        private void MenuItem_SetStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement mi && mi.DataContext is ProformaInvoice invoice && mi.Tag is string statusStr)
            {
                if (Enum.TryParse<InvoiceStatus>(statusStr, out var newStatus))
                {
                    if (invoice.Status != newStatus)
                    {
                        invoice.Status = newStatus;
                        invoice.UpdatedAt = DateTime.Now;
                        _storage.AddOrUpdateInvoice(invoice);
                        RefreshInvoicesList();
                        SetStatus($"وضعیت پیش‌فاکتور {invoice.InvoiceNumber} به «{newStatus.ToPersianString()}» تغییر یافت.");
                    }
                }
            }
        }

        private void MenuItem_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement mi && mi.DataContext is ProformaInvoice invoice)
            {
                DeleteInvoice(invoice);
            }
        }

        private void DgrInvoices_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DgrInvoices.SelectedItem is ProformaInvoice invoice)
            {
                OpenInvoiceEditor(invoice, isNew: false);
            }
        }



        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;

            // Ctrl + S: ذخیره پیش‌فاکتور در حالت ویرایش
            if (e.Key == System.Windows.Input.Key.S && (modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible)
                {
                    BtnSaveInvoice_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            // Ctrl + N: ایجاد پیش‌فاکتور جدید
            else if (e.Key == System.Windows.Input.Key.N && (modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                BtnNewInvoice_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl + P: چاپ / PDF
            else if (e.Key == System.Windows.Input.Key.P && (modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible && _currentEditingInvoice != null)
                {
                    BtnSaveAndPrintPdf_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (ViewInvoices != null && ViewInvoices.Visibility == Visibility.Visible && DgrInvoices.SelectedItem is ProformaInvoice inv)
                {
                    _printService.PrintOrExportToPdf(inv);
                    e.Handled = true;
                }
            }
            // Esc: بستن پنجره‌های بازشونده (مانند لیست پیشنهادات کالا) بدون خروج ناخواسته از پیش‌فاکتور
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (PopupQuickSuggestions != null && PopupQuickSuggestions.IsOpen)
                {
                    PopupQuickSuggestions.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                if (PopupGridSuggestions != null && PopupGridSuggestions.IsOpen)
                {
                    PopupGridSuggestions.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible)
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        #endregion

        #region فرم صدور و ویرایش پیش‌فاکتور

        private void BtnNewInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible && _isDirty)
            {
                var confirm = MessageBox.Show(
                    "تغییرات ذخیره‌نشده‌ای در پیش‌فاکتور جاری وجود دارد.\nآیا مایل به ذخیره تغییرات قبل از ایجاد پیش‌فاکتور جدید هستید؟",
                    "تغییرات ذخیره‌نشده",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    if (!SaveCurrentInvoiceFromEditor()) return;
                }
                else if (confirm == MessageBoxResult.No)
                {
                    SetDirty(false);
                }
                else
                {
                    return;
                }
            }

            var experts = _storage.GetExperts();
            var companies = _storage.GetCompanies();

            if (companies.Count == 0)
            {
                MessageBox.Show("لطفاً ابتدا حداقل یک شرکت ثبت نمایید.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                SwitchToView("Companies");
                return;
            }

            var defaultExpert = experts.FirstOrDefault();
            var defaultCompany = companies.First();
            var settings = _storage.GetSettings();

            var newInvoice = new ProformaInvoice
            {
                InvoiceNumber = _storage.PeekNextUniqueInvoiceNumber(),
                IssueDateShamsi = JalaliDateHelper.GetCurrentShamsiDate(),
                ValidUntilShamsi = JalaliDateHelper.GetShamsiDateAfterDays(7),
                CompanyId = defaultCompany.Id,
                CompanyName = defaultCompany.Name,
                CompanyPhone = defaultCompany.Phone,
                CompanyAddress = defaultCompany.Address,
                CompanyEconomicCode = defaultCompany.EconomicCode,
                CompanyNationalCode = defaultCompany.NationalCode,
                ExpertId = defaultExpert?.Id ?? Guid.Empty,
                ExpertName = defaultExpert?.FullName ?? "",
                ExpertPhone = defaultExpert?.PhoneNumber ?? "",
                Status = InvoiceStatus.Draft,
                Notes = !string.IsNullOrWhiteSpace(settings.DefaultTerms) ? settings.DefaultTerms : "۱. فرایند تحویل بار پس از وصول پیش‌پرداخت در صورت عدم تغییر قیمت انجام خواهد شد.\n۲. در صورت تغییر قیمت، پیش‌فاکتور اصلاحی تقدیم می‌گردد.",
                PaymentTerms = !string.IsNullOrWhiteSpace(settings.PaymentTerms) ? settings.PaymentTerms : "نقد",
                DeliveryDays = !string.IsNullOrWhiteSpace(settings.DeliveryDays) ? settings.DeliveryDays : "موجود",
                DeliveryLocation = !string.IsNullOrWhiteSpace(settings.DeliveryLocation) ? settings.DeliveryLocation : "مشهد",
                VatPercent = settings.VatPercent >= 0 ? settings.VatPercent : 10m,
                Items = new List<InvoiceItem>()
            };

            OpenInvoiceEditor(newInvoice, isNew: true);
        }

        private void OpenInvoiceEditor(ProformaInvoice invoice, bool isNew)
        {
            _currentEditingInvoice = invoice;
            _isNewInvoice = isNew;

            var settings = _storage.GetSettings();
            var companies = _storage.GetCompanies();
            CmbEditorCompany.ItemsSource = companies;
            CmbEditorCompany.SelectedItem = companies.FirstOrDefault(c => c.Id == invoice.CompanyId);

            var experts = _storage.GetExperts();
            var expertList = new List<Expert>
            {
                new Expert { Id = Guid.Empty, FullName = "(بدون کارشناس)", RoleTitle = "" }
            };
            expertList.AddRange(experts);
            CmbEditorExpert.ItemsSource = expertList;
            if (invoice.ExpertId != Guid.Empty && !string.IsNullOrWhiteSpace(invoice.ExpertName))
            {
                CmbEditorExpert.SelectedItem = expertList.FirstOrDefault(e => e.Id == invoice.ExpertId) ?? expertList[0];
            }
            else
            {
                CmbEditorExpert.SelectedIndex = 0;
            }

            TxtEditorInvoiceNumber.Text = invoice.InvoiceNumber;
            TxtEditorIssueDate.Text = invoice.IssueDateShamsi;
            CmbEditorStatus.SelectedIndex = (int)invoice.Status;
            UpdateApproveButtonVisibility();

            TxtEditorCompanyPhone.Text = invoice.CompanyPhone;
            TxtEditorCompanyAddress.Text = invoice.CompanyAddress;
            TxtEditorCompanyEcoCode.Text = !string.IsNullOrEmpty(invoice.CompanyEconomicCode) ? invoice.CompanyEconomicCode : invoice.CompanyNationalCode;

            // اطلاعات سربرگ، پیشوند مشتری و واحد پول
            TxtEditorCustomerPrefix.Text = !string.IsNullOrWhiteSpace(invoice.CustomerLabelPrefix) ? invoice.CustomerLabelPrefix : (!string.IsNullOrWhiteSpace(settings.CustomerLabelPrefix) ? settings.CustomerLabelPrefix : "نام مشتری شرکت محترم ");
            TxtEditorSubject.Text = invoice.Subject ?? "";
            TxtEditorAttachment.Text = !string.IsNullOrWhiteSpace(invoice.Attachment) ? invoice.Attachment : "ندارد";
            TxtEditorPageNumberText.Text = !string.IsNullOrWhiteSpace(invoice.PageNumberText) ? invoice.PageNumberText : "یک";

            string currUnit = !string.IsNullOrWhiteSpace(invoice.CurrencyUnit) ? invoice.CurrencyUnit : (!string.IsNullOrWhiteSpace(settings.CurrencyUnit) ? settings.CurrencyUnit : "ریال");
            CmbEditorCurrencyUnit.SelectedIndex = 0;

            // شرایط و توضیحات اختصاصی این پیش‌فاکتور
            TxtEditorNotes.Text = invoice.Notes;
            TxtEditorPaymentTerms.Text = !string.IsNullOrWhiteSpace(invoice.PaymentTerms) ? invoice.PaymentTerms : (!string.IsNullOrWhiteSpace(settings.PaymentTerms) ? settings.PaymentTerms : "نقد");
            TxtEditorDeliveryDays.Text = !string.IsNullOrWhiteSpace(invoice.DeliveryDays) ? invoice.DeliveryDays : (!string.IsNullOrWhiteSpace(settings.DeliveryDays) ? settings.DeliveryDays : "موجود");
            TxtEditorDeliveryLocation.Text = !string.IsNullOrWhiteSpace(invoice.DeliveryLocation) ? invoice.DeliveryLocation : (!string.IsNullOrWhiteSpace(settings.DeliveryLocation) ? settings.DeliveryLocation : "مشهد");
            TxtEditorVatPercent.Text = (invoice.VatPercent >= 0 ? invoice.VatPercent : (settings.VatPercent >= 0 ? settings.VatPercent : 10m)).ToString("G29");

            TxtEditorValidityText.Text = !string.IsNullOrWhiteSpace(invoice.ValidityText) ? invoice.ValidityText : (!string.IsNullOrWhiteSpace(settings.DefaultValidityText) ? settings.DefaultValidityText : "اعتبار این پیش فاکتورتا پایان روز می باشد.");
            TxtEditorSignatoryName.Text = !string.IsNullOrWhiteSpace(invoice.SignatoryName) ? invoice.SignatoryName : (!string.IsNullOrWhiteSpace(settings.SignatoryName) ? settings.SignatoryName : "محمد احمدی پور هدش");
            TxtEditorExpertRole.Text = !string.IsNullOrWhiteSpace(invoice.ExpertRoleLabel) ? invoice.ExpertRoleLabel : (!string.IsNullOrWhiteSpace(settings.ExpertRoleLabel) ? settings.ExpertRoleLabel : "کارشناسی خرید:");

            // وضعیت‌های هایلایت
            ChkHighlightValidity.IsChecked = invoice.HighlightValidity;
            ChkHighlightPaymentTerms.IsChecked = invoice.HighlightPaymentTerms;
            ChkHighlightDeliveryDays.IsChecked = invoice.HighlightDeliveryDays;
            ChkHighlightDeliveryLocation.IsChecked = invoice.HighlightDeliveryLocation;
            ChkHighlightNotes.IsChecked = invoice.HighlightNotes;
            ChkHighlightVat.IsChecked = invoice.HighlightVat;

            // حساب‌های بانکی اختصاصی این پیش‌فاکتور
            var defaultAccounts = invoice.BankAccounts != null && invoice.BankAccounts.Count > 0
                ? invoice.BankAccounts
                : (settings.BankAccounts ?? new List<BankAccountInfo>());

            _editingBankAccounts = new ObservableCollection<BankAccountInfo>(
                defaultAccounts.Select(a => new BankAccountInfo
                {
                    BankName = a.BankName,
                    AccountNumber = a.AccountNumber,
                    ShebaNumber = a.ShebaNumber,
                    CardNumber = a.CardNumber,
                    OwnerName = a.OwnerName
                }));
            if (DgrEditorBankAccounts != null)
            {
                DgrEditorBankAccounts.ItemsSource = _editingBankAccounts;
            }

            // اقلام
            _editingItems = new ObservableCollection<InvoiceItem>();
            foreach (var item in invoice.Items)
            {
                var clone = item.Clone();
                clone.PropertyChanged += Item_PropertyChanged;
                _editingItems.Add(clone);
            }

            DgrInvoiceItems.ItemsSource = _editingItems;
            RecalculateEditorSummaries();
            SetDirty(false);

            SwitchToView("Editor");
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InvoiceItem.TotalPrice) ||
                e.PropertyName == nameof(InvoiceItem.FinalUnitPrice) ||
                e.PropertyName == nameof(InvoiceItem.TotalProfit) ||
                e.PropertyName == nameof(InvoiceItem.ProfitAmount) ||
                e.PropertyName == nameof(InvoiceItem.ProfitPercent) ||
                e.PropertyName == nameof(InvoiceItem.Quantity) ||
                e.PropertyName == nameof(InvoiceItem.DiscountAmount) ||
                e.PropertyName == nameof(InvoiceItem.DiscountPercent) ||
                e.PropertyName == nameof(InvoiceItem.RawPrice) ||
                e.PropertyName == nameof(InvoiceItem.IsDividedBy1Point1))
            {
                RecalculateEditorSummaries();
            }
            SetDirty(true);
        }

        private void RecalculateEditorSummaries()
        {
            decimal rawTotal = _editingItems.Sum(i => i.Quantity * i.RawPrice);
            decimal discountTotal = _editingItems.Sum(i => i.Quantity * (i.DiscountAmount));
            decimal profitTotal = _editingItems.Sum(i => i.TotalProfit);
            decimal grandTotal = InvoiceItem.RoundUpPrice(_editingItems.Sum(i => i.TotalPrice));

            decimal vatPercent = 10m;
            if (TxtEditorVatPercent != null && TryParsePercent(TxtEditorVatPercent.Text, out decimal parsedVat))
            {
                vatPercent = parsedVat;
            }
            decimal rawVat = Math.Round(grandTotal * (vatPercent / 100m), 0);
            decimal grandTotalWithVat = (vatPercent > 0)
                ? InvoiceItem.RoundUpPrice(grandTotal + rawVat)
                : grandTotal;
            decimal vatAmount = (vatPercent > 0) ? (grandTotalWithVat - grandTotal) : 0;

            TxtSummaryRawTotal.Text = $"{grandTotal:N0} ریال";
            TxtSummaryDiscountTotal.Text = $"{discountTotal:N0} ریال";
            TxtSummaryProfitTotal.Text = $"{profitTotal:N0} ریال";
            decimal totalCost = _editingItems.Sum(i => i.Quantity * i.BaseCost);
            decimal profitPct = (totalCost > 0)
                ? Math.Round((profitTotal / totalCost) * 100m, 2)
                : (grandTotal > 0 ? Math.Round((profitTotal / grandTotal) * 100m, 2) : 0m);
            TxtSummaryProfitPercent.Text = $"{profitPct:G29}% سود";

            if (TxtSummaryGrandTotal != null)
            {
                TxtSummaryGrandTotal.Text = $"{grandTotalWithVat:N0} ریال";
            }
            if (TxtSummaryGrandTotalWithVat != null)
            {
                TxtSummaryGrandTotalWithVat.Text = $"{grandTotalWithVat:N0} ریال";
            }
            if (TxtSummaryGrandTotalWords != null)
            {
                TxtSummaryGrandTotalWords.Text = JalaliDateHelper.NumberToWords((long)grandTotalWithVat) + " ریال";
            }
            if (TxtSummarySubtotalHint != null)
            {
                TxtSummarySubtotalHint.Text = $"جمع اقلام: {grandTotal:N0} ریال | ارزش افزوده ({vatPercent:G29}٪): {vatAmount:N0} ریال";
            }
            if (TxtSummaryVatRateBadge != null)
            {
                TxtSummaryVatRateBadge.Text = $"+ {vatPercent:G29}٪ ارزش افزوده";
            }
        }

        private void TxtEditorVatPercent_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalculateEditorSummaries();
            SetDirty(true);
        }

        private void CmbEditorCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEditorCompany.SelectedItem is Company company)
            {
                TxtEditorCompanyPhone.Text = company.Phone;
                TxtEditorCompanyAddress.Text = company.Address;
                TxtEditorCompanyEcoCode.Text = company.NationalOrEconomicCode;

                if (TxtEditorCustomerPrefix != null)
                {
                    TxtEditorCustomerPrefix.Text = (company.CompanyType == CompanyType.Real)
                        ? "جناب آقای / سرکار خانم "
                        : "نام مشتری شرکت محترم ";
                }

                // فیلتر کارشناسان متناسب با انتساب به این شرکت (کارشناسان اختصاصی یا عمومی)
                var allExperts = _storage.GetExperts();
                var filtered = allExperts.Where(exp => exp.BelongsToCompany(company.Id)).ToList();

                var expertList = new List<Expert>
                {
                    new Expert { Id = Guid.Empty, FullName = "(بدون کارشناس)", RoleTitle = "" }
                };
                expertList.AddRange(filtered);

                var curExp = CmbEditorExpert.SelectedItem as Expert;
                CmbEditorExpert.ItemsSource = expertList;
                if (curExp != null && curExp.Id != Guid.Empty && filtered.Any(x => x.Id == curExp.Id))
                {
                    CmbEditorExpert.SelectedItem = expertList.First(x => x.Id == curExp.Id);
                }
                else if (filtered.Count > 0)
                {
                    CmbEditorExpert.SelectedItem = filtered.First();
                }
                else
                {
                    CmbEditorExpert.SelectedIndex = 0;
                }
            }
        }

        private void CmbEditorExpert_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEditorExpert.SelectedItem is Expert expert)
            {
                if (_isNewInvoice && string.IsNullOrWhiteSpace(TxtEditorInvoiceNumber?.Text))
                {
                    TxtEditorInvoiceNumber!.Text = _storage.PeekNextUniqueInvoiceNumber();
                }
            }
        }

        private void TxtItemName_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                tb.Focus();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    tb.Focus();
                    tb.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new InvoiceItem
            {
                RowNumber = _editingItems.Count + 1,
                ItemName = "",
                Quantity = 1,
                RawPrice = 0,
                DiscountPercent = null,
                ProfitPercent = null,
                IsDividedBy1Point1 = false
            };
            newItem.PropertyChanged += Item_PropertyChanged;
            _editingItems.Add(newItem);
            RecalculateEditorSummaries();
            SetDirty(true);

            // هدایت خودکار کاربر به سطر جدید و شروع فوری ویرایش نام محصول
            DgrInvoiceItems.SelectedItem = newItem;
            DgrInvoiceItems.ScrollIntoView(newItem);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DgrInvoiceItems.Focus();
                if (DgrInvoiceItems.Columns.Count > 1)
                {
                    DgrInvoiceItems.CurrentCell = new DataGridCellInfo(newItem, DgrInvoiceItems.Columns[1]);
                    DgrInvoiceItems.BeginEdit();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is InvoiceItem item)
            {
                _editingItems.Remove(item);
                for (int i = 0; i < _editingItems.Count; i++)
                {
                    _editingItems[i].RowNumber = i + 1;
                }
                RecalculateEditorSummaries();
                SetDirty(true);
            }
        }

        private void BtnDuplicateItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is InvoiceItem item)
            {
                var clone = item.Clone();
                clone.RowNumber = _editingItems.Count + 1;
                clone.PropertyChanged += Item_PropertyChanged;
                _editingItems.Add(clone);
                RecalculateEditorSummaries();
                SetDirty(true);

                DgrInvoiceItems.SelectedItem = clone;
                DgrInvoiceItems.ScrollIntoView(clone);
            }
        }

        private bool SaveCurrentInvoiceFromEditor()
        {
            if (_currentEditingInvoice == null) return false;

            if (CmbEditorCompany.SelectedItem is not Company company)
            {
                MessageBox.Show("لطفاً شرکت خریدار را انتخاب نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var expert = CmbEditorExpert?.SelectedItem as Expert;

            if (string.IsNullOrWhiteSpace(TxtEditorInvoiceNumber.Text))
            {
                MessageBox.Show("شماره پیش‌فاکتور نمی‌تواند خالی باشد.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_editingItems.Count == 0)
            {
                MessageBox.Show("پیش‌فاکتور باید حداقل دارای یک قلم کالا باشد.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // بررسی اجباری بودن درصد تخفیف و سود برای تمام سطرها (حتی ۰٪)
            var incompleteItem = _editingItems.FirstOrDefault(it =>
                string.IsNullOrWhiteSpace(it.ItemName) || !it.DiscountPercent.HasValue || !it.ProfitPercent.HasValue);

            if (incompleteItem != null)
            {
                if (string.IsNullOrWhiteSpace(incompleteItem.ItemName))
                {
                    MessageBox.Show($"شرح کالا برای ردیف {incompleteItem.RowNumber} وارد نشده است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"تعیین درصد تخفیف و درصد سود برای تمامی ردیف‌ها الزامی است (حتی مقدار ۰٪ باید صراحتاً وارد گردد).\nلطفاً ردیف {incompleteItem.RowNumber} را تکمیل فرمایید.", "تکمیل درصد تخفیف و سود", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                DgrInvoiceItems.SelectedItem = incompleteItem;
                DgrInvoiceItems.ScrollIntoView(incompleteItem);
                return false;
            }

            _currentEditingInvoice.CompanyId = company.Id;
            _currentEditingInvoice.CompanyName = company.Name;
            _currentEditingInvoice.CompanyPhone = TxtEditorCompanyPhone.Text?.Trim() ?? "";
            _currentEditingInvoice.CompanyAddress = TxtEditorCompanyAddress.Text?.Trim() ?? "";
            _currentEditingInvoice.CompanyEconomicCode = company.EconomicCode;
            _currentEditingInvoice.CompanyNationalCode = company.NationalCode;

            if (expert != null && expert.Id != Guid.Empty && expert.FullName != "(بدون کارشناس)")
            {
                _currentEditingInvoice.ExpertId = expert.Id;
                _currentEditingInvoice.ExpertName = expert.FullName;
                _currentEditingInvoice.ExpertPhone = expert.PhoneNumber;
            }
            else
            {
                _currentEditingInvoice.ExpertId = Guid.Empty;
                _currentEditingInvoice.ExpertName = "";
                _currentEditingInvoice.ExpertPhone = "";
            }

            _currentEditingInvoice.InvoiceNumber = TxtEditorInvoiceNumber.Text.Trim();
            _currentEditingInvoice.IssueDateShamsi = TxtEditorIssueDate.Text?.Trim() ?? JalaliDateHelper.GetCurrentShamsiDate();
            _currentEditingInvoice.ValidUntilShamsi = _currentEditingInvoice.IssueDateShamsi;
            _currentEditingInvoice.Status = (InvoiceStatus)CmbEditorStatus.SelectedIndex;

            _currentEditingInvoice.CustomerLabelPrefix = TxtEditorCustomerPrefix.Text?.Trim() ?? "نام مشتری شرکت محترم ";
            _currentEditingInvoice.Subject = TxtEditorSubject.Text?.Trim() ?? "";
            _currentEditingInvoice.Attachment = TxtEditorAttachment.Text?.Trim() ?? "ندارد";
            _currentEditingInvoice.PageNumberText = TxtEditorPageNumberText.Text?.Trim() ?? "یک";
            _currentEditingInvoice.CurrencyUnit = "ریال";

            _currentEditingInvoice.ValidityText = TxtEditorValidityText.Text?.Trim() ?? "اعتبار این پیش فاکتورتا پایان روز می باشد.";
            _currentEditingInvoice.Notes = TxtEditorNotes.Text?.Trim() ?? "";
            _currentEditingInvoice.PaymentTerms = TxtEditorPaymentTerms.Text?.Trim() ?? "نقد";
            _currentEditingInvoice.DeliveryDays = TxtEditorDeliveryDays.Text?.Trim() ?? "موجود";
            _currentEditingInvoice.DeliveryLocation = TxtEditorDeliveryLocation.Text?.Trim() ?? "مشهد";
            _currentEditingInvoice.SignatoryName = TxtEditorSignatoryName.Text?.Trim() ?? "محمد احمدی پور هدش";
            _currentEditingInvoice.ExpertRoleLabel = TxtEditorExpertRole.Text?.Trim() ?? "کارشناسی خرید:";

            if (TryParsePercent(TxtEditorVatPercent?.Text, out decimal vat))
            {
                _currentEditingInvoice.VatPercent = vat;
            }

            _currentEditingInvoice.HighlightValidity = ChkHighlightValidity.IsChecked == true;
            _currentEditingInvoice.HighlightPaymentTerms = ChkHighlightPaymentTerms.IsChecked == true;
            _currentEditingInvoice.HighlightDeliveryDays = ChkHighlightDeliveryDays.IsChecked == true;
            _currentEditingInvoice.HighlightDeliveryLocation = ChkHighlightDeliveryLocation.IsChecked == true;
            _currentEditingInvoice.HighlightNotes = ChkHighlightNotes.IsChecked == true;
            _currentEditingInvoice.HighlightVat = ChkHighlightVat.IsChecked == true;

            _currentEditingInvoice.Items = _editingItems.ToList();
            _currentEditingInvoice.BankAccounts = _editingBankAccounts.ToList();
            _currentEditingInvoice.UpdatedAt = DateTime.Now;

            // بررسی و تضمین یکتایی شماره پیش‌فاکتور (هیچ دو پیش‌فاکتوری شماره یکسان نخواهند داشت)
            _storage.CheckAndReloadIfDiskChanged();
            if (string.IsNullOrWhiteSpace(_currentEditingInvoice.InvoiceNumber) ||
                _storage.IsInvoiceNumberInUse(_currentEditingInvoice.InvoiceNumber, _currentEditingInvoice.Id))
            {
                string uniqueNum = _storage.GenerateNextUniqueInvoiceNumber();
                _currentEditingInvoice.InvoiceNumber = uniqueNum;
                if (TxtEditorInvoiceNumber != null) TxtEditorInvoiceNumber.Text = uniqueNum;
            }
            else if (_isNewInvoice)
            {
                _storage.RegisterUsedInvoiceNumber(_currentEditingInvoice.InvoiceNumber);
            }

            // ثبت خودکار اقلام در انبار در صورتی که قبلاً ثبت نشده باشند
            foreach (var item in _editingItems)
            {
                if (!string.IsNullOrWhiteSpace(item.ItemName))
                {
                    _storage.EnsureStorageItemExists(item.ItemName, item.Unit, item.RawPrice);
                }
            }

            _storage.AddOrUpdateInvoice(_currentEditingInvoice);
            _isNewInvoice = false;
            SetDirty(false);

            RefreshAll();
            SetStatus($"پیش‌فاکتور شماره {_currentEditingInvoice.InvoiceNumber} با موفقیت ذخیره شد.");
            return true;
        }

        private static bool TryParsePercent(string? text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                           .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
                           .Replace("%", "").Replace("٪", "").Replace(",", "").Replace("،", "").Replace(" ", "").Trim();

            s = s.Replace('/', '.').Replace('٫', '.');

            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private void CommitGridEdits()
        {
            try
            {
                DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Cell, true);
                DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
        }

        private List<InvoiceItem> GetBulkTargets()
        {
            if (CmbBulkTarget?.SelectedIndex == 1)
            {
                var selected = DgrInvoiceItems.SelectedItems.OfType<InvoiceItem>().ToList();
                if (selected.Count > 0) return selected;
            }
            return _editingItems.ToList();
        }

        private void TxtPercent_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == "/" || e.Text == "٫" || e.Text == "،")
            {
                e.Handled = true;
                if (sender is System.Windows.Controls.TextBox tb)
                {
                    int caret = tb.SelectionStart;
                    tb.Text = tb.Text.Remove(caret, tb.SelectionLength).Insert(caret, ".");
                    tb.SelectionStart = caret + 1;
                }
                else if (sender is Wpf.Ui.Controls.TextBox uiTb)
                {
                    int caret = uiTb.SelectionStart;
                    uiTb.Text = uiTb.Text.Remove(caret, uiTb.SelectionLength).Insert(caret, ".");
                    uiTb.SelectionStart = caret + 1;
                }
            }
        }

        private void BtnApplyBulkDiscount_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            if (TryParsePercent(TxtBulkDiscountPercent?.Text, out decimal discount))
            {
                if (_editingItems.Count == 0)
                {
                    MessageBox.Show("هیچ سطری در پیش‌فاکتور برای اعمال تخفیف وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var targets = GetBulkTargets();
                foreach (var item in targets)
                {
                    item.DiscountPercent = discount;
                    item.UpdateFromPercent();
                }

                RecalculateEditorSummaries();
                SetDirty(true);
                SetStatus($"تخفیف {discount:G29}٪ به {targets.Count} سطر پیش‌فاکتور اعمال شد.");
            }
            else
            {
                MessageBox.Show("لطفاً یک درصد تخفیف معتبر وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnApplyBulkProfit_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            if (TryParsePercent(TxtBulkProfitPercent?.Text, out decimal profit))
            {
                if (_editingItems.Count == 0)
                {
                    MessageBox.Show("هیچ سطری در پیش‌فاکتور برای اعمال سود وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var targets = GetBulkTargets();
                foreach (var item in targets)
                {
                    item.ProfitPercent = profit;
                    item.UpdateFromProfitPercent();
                }

                RecalculateEditorSummaries();
                SetDirty(true);
                SetStatus($"سود {profit:G29}٪ به {targets.Count} سطر پیش‌فاکتور اعمال شد.");
            }
            else
            {
                MessageBox.Show("لطفاً یک درصد سود معتبر وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnApplyBulkBoth_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            if (_editingItems.Count == 0)
            {
                MessageBox.Show("هیچ سطری در پیش‌فاکتور وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasDiscount = TryParsePercent(TxtBulkDiscountPercent?.Text, out decimal discount);
            bool hasProfit = TryParsePercent(TxtBulkProfitPercent?.Text, out decimal profit);

            if (!hasDiscount && !hasProfit)
            {
                MessageBox.Show("لطفاً حداقل درصد تخفیف یا درصد سود معتبر را وارد نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = GetBulkTargets();
            foreach (var item in targets)
            {
                if (hasDiscount) item.DiscountPercent = discount;
                if (hasProfit) item.ProfitPercent = profit;
                item.RecalculateTotals();
            }

            RecalculateEditorSummaries();
            SetDirty(true);
            SetStatus($"تنظیم یکجای تخفیف و سود روی {targets.Count} سطر پیش‌فاکتور اعمال گردید.");
        }

        private void BtnApplyBulk1Point1_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            if (_editingItems.Count == 0)
            {
                MessageBox.Show("هیچ سطری در پیش‌فاکتور وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targets = GetBulkTargets();
            bool targetValue = targets.Any(i => !i.IsDividedBy1Point1);

            foreach (var item in targets)
            {
                item.IsDividedBy1Point1 = targetValue;
                item.RecalculateTotals();
            }

            RecalculateEditorSummaries();
            SetDirty(true);
            string actionDesc = targetValue ? "فعال‌سازی" : "غیرفعال‌سازی";
            SetStatus($"{actionDesc} فرمول تقسیم بر ۱.۱ روی {targets.Count} سطر پیش‌فاکتور اعمال شد.");
        }

        #region پیشنهادات هوشمند جستجوی کالا در جدول اقلام

        private void TxtGridItemName_Loaded(object sender, RoutedEventArgs e)
        {
            _currentGridTextBox = sender as Wpf.Ui.Controls.TextBox;
            if (_currentGridTextBox != null)
            {
                _currentGridTextBox.FlowDirection = FlowDirection.RightToLeft;
                _currentGridTextBox.TextAlignment = TextAlignment.Left;
                if (_currentGridTextBox.DataContext is InvoiceItem ii)
                {
                    _currentEditingInvoiceItem = ii;
                    _gridItemLoadedText = ii.ItemName ?? "";
                }
                else
                {
                    _gridItemLoadedText = _currentGridTextBox.Text ?? "";
                }
                _isGridItemUserTyping = false;
                _currentGridTextBox.Focus();
                _currentGridTextBox.SelectAll();
            }
        }

        private void TxtGridItemName_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _isGridItemUserTyping = true;
        }

        private void TxtGridItemName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingGridSuggestion) return;

            if (sender is Wpf.Ui.Controls.TextBox tb)
            {
                _currentGridTextBox = tb;
                if (tb.DataContext is InvoiceItem ii)
                {
                    _currentEditingInvoiceItem = ii;
                }

                // در زمان لود اولیه سلول و پیش از فوکوس یا تعامل، پاپ‌آپ باز نمی‌شود
                if (!tb.IsKeyboardFocusWithin && !_isGridItemUserTyping && tb.Text == _gridItemLoadedText)
                {
                    if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                    return;
                }

                ShowGridSuggestions(tb, tb.Text);
            }
        }

        private void ShowGridSuggestions(Wpf.Ui.Controls.TextBox tb, string? query)
        {
            if (PopupGridSuggestions == null || LstGridSuggestions == null) return;

            var allItems = _storage.GetStorageItems();
            if (allItems == null || allItems.Count == 0)
            {
                PopupGridSuggestions.IsOpen = false;
                return;
            }

            string q = query?.Trim() ?? "";
            List<StorageItem> matching;
            if (string.IsNullOrWhiteSpace(q))
            {
                matching = allItems.Take(30).ToList();
            }
            else
            {
                matching = allItems
                    .Where(x => (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Code) && x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Category) && x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(30)
                    .ToList();
            }

            if (matching.Any())
            {
                LstGridSuggestions.ItemsSource = matching;
                LstGridSuggestions.SelectedIndex = -1;
                PopupGridSuggestions.PlacementTarget = tb;
                PopupGridSuggestions.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                PopupGridSuggestions.IsOpen = true;
            }
            else
            {
                PopupGridSuggestions.IsOpen = false;
            }
        }

        private void TxtGridItemName_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space)
            {
                _isGridItemUserTyping = true;
            }

            if (PopupGridSuggestions != null && PopupGridSuggestions.IsOpen && LstGridSuggestions != null)
            {
                if (e.Key == Key.Down)
                {
                    if (LstGridSuggestions.Items.Count > 0)
                    {
                        int next = LstGridSuggestions.SelectedIndex + 1;
                        if (next >= LstGridSuggestions.Items.Count) next = 0;
                        LstGridSuggestions.SelectedIndex = next;
                        LstGridSuggestions.ScrollIntoView(LstGridSuggestions.SelectedItem);
                    }
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    if (LstGridSuggestions.Items.Count > 0)
                    {
                        int prev = LstGridSuggestions.SelectedIndex - 1;
                        if (prev < 0) prev = LstGridSuggestions.Items.Count - 1;
                        LstGridSuggestions.SelectedIndex = prev;
                        LstGridSuggestions.ScrollIntoView(LstGridSuggestions.SelectedItem);
                    }
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Enter)
                {
                    if (LstGridSuggestions.SelectedItem is StorageItem item)
                    {
                        ApplyGridSuggestion(item);
                        e.Handled = true;
                        return;
                    }
                    else
                    {
                        PopupGridSuggestions.IsOpen = false;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    PopupGridSuggestions.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Tab)
                {
                    if (LstGridSuggestions.SelectedItem is StorageItem item)
                    {
                        ApplyGridSuggestion(item);
                    }
                    else
                    {
                        PopupGridSuggestions.IsOpen = false;
                    }
                }
            }
            else if (e.Key == Key.Down && (PopupGridSuggestions == null || !PopupGridSuggestions.IsOpen))
            {
                if (sender is Wpf.Ui.Controls.TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    _isGridItemUserTyping = true;
                    TxtGridItemName_TextChanged(tb, null!);
                    e.Handled = true;
                }
            }
        }

        private void LstGridSuggestions_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not ListBoxItem && dep != LstGridSuggestions)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (dep is ListBoxItem lbi && lbi.DataContext is StorageItem item)
            {
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyGridSuggestion(item);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void LstGridSuggestions_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (LstGridSuggestions?.SelectedItem is StorageItem item)
                {
                    ApplyGridSuggestion(item);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                _currentGridTextBox?.Focus();
                e.Handled = true;
            }
        }

        private void ApplyGridSuggestion(StorageItem? selectedItem = null)
        {
            var item = selectedItem ?? LstGridSuggestions?.SelectedItem as StorageItem;
            if (item == null) return;

            var invoiceItem = (_currentGridTextBox?.DataContext as InvoiceItem)
                           ?? _currentEditingInvoiceItem
                           ?? (DgrInvoiceItems?.SelectedItem as InvoiceItem);

            if (invoiceItem != null)
            {
                invoiceItem.ItemName = item.Name;
                if (!string.IsNullOrEmpty(item.Unit)) invoiceItem.Unit = item.Unit;
                if (item.DefaultPrice > 0) invoiceItem.RawPrice = item.DefaultPrice;
                RecalculateEditorSummaries();
                SetDirty(true);
            }

            _isApplyingGridSuggestion = true;
            try
            {
                if (_currentGridTextBox != null)
                {
                    _currentGridTextBox.Text = item.Name;
                }

                if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                if (LstGridSuggestions != null) LstGridSuggestions.SelectedItem = null;

                try
                {
                    DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Cell, true);
                    DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Row, true);
                }
                catch { }
            }
            finally
            {
                _isApplyingGridSuggestion = false;
            }
        }

        #endregion

        #region مدیریت حساب‌های بانکی اختصاصی در فرم ویرایش پیش‌فاکتور

        private void BtnAddEditorBankAccount_Click(object sender, RoutedEventArgs e)
        {
            string bName = TxtEditorNewBankName?.Text?.Trim() ?? "";
            string accNum = TxtEditorNewBankAccountNumber?.Text?.Trim() ?? "";
            string sheba = TxtEditorNewBankSheba?.Text?.Trim() ?? "";
            string card = TxtEditorNewBankCard?.Text?.Trim() ?? "";
            string owner = TxtEditorNewBankOwner?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(bName) && string.IsNullOrEmpty(accNum) && string.IsNullOrEmpty(sheba) && string.IsNullOrEmpty(card))
            {
                MessageBox.Show("لطفاً مشخصات حساب بانکی را وارد نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _editingBankAccounts.Add(new BankAccountInfo
            {
                BankName = bName,
                AccountNumber = accNum,
                ShebaNumber = sheba,
                CardNumber = card,
                OwnerName = owner
            });

            if (TxtEditorNewBankName != null) TxtEditorNewBankName.Text = "";
            if (TxtEditorNewBankAccountNumber != null) TxtEditorNewBankAccountNumber.Text = "";
            if (TxtEditorNewBankSheba != null) TxtEditorNewBankSheba.Text = "";
            if (TxtEditorNewBankCard != null) TxtEditorNewBankCard.Text = "";
            if (TxtEditorNewBankOwner != null) TxtEditorNewBankOwner.Text = "";

            SetDirty(true);
            SetStatus("حساب بانکی به این پیش‌فاکتور افزوده شد.");
        }

        private void BtnDeleteEditorBankAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is BankAccountInfo acc)
            {
                _editingBankAccounts.Remove(acc);
                SetDirty(true);
                SetStatus("حساب بانکی از این پیش‌فاکتور حذف شد.");
            }
        }

        private void BtnResetEditorBankAccounts_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            _editingBankAccounts.Clear();
            if (settings.BankAccounts != null)
            {
                foreach (var a in settings.BankAccounts)
                {
                    _editingBankAccounts.Add(new BankAccountInfo
                    {
                        BankName = a.BankName,
                        AccountNumber = a.AccountNumber,
                        ShebaNumber = a.ShebaNumber,
                        CardNumber = a.CardNumber,
                        OwnerName = a.OwnerName
                    });
                }
            }
            SetDirty(true);
            SetStatus("حساب‌های بانکی پیش‌فاکتور به اطلاعات تنظیمات اصلی بازنشانی گردید.");
        }

        #endregion

        private async void BtnSaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            await SaveInvoiceWithLoadingAnimationAsync();
        }

        private async Task<bool> SaveInvoiceWithLoadingAnimationAsync()
        {
            if (BtnTopSaveInvoice != null) BtnTopSaveInvoice.IsEnabled = false;
            if (PrgSaveLoading != null) PrgSaveLoading.Visibility = Visibility.Visible;
            if (IcoTopSaveStatus != null) IcoTopSaveStatus.Visibility = Visibility.Collapsed;
            if (TxtTopSaveStatus != null)
            {
                TxtTopSaveStatus.Text = "در حال ذخیره‌سازی...";
                TxtTopSaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            }
            if (BrdTopSaveStatus != null)
            {
                BrdTopSaveStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                BrdTopSaveStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFDBFE"));
            }

            try
            {
                DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Cell, true);
                DgrInvoiceItems?.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            // تأخیر ملایم جهت نمایش انیمیشن لودینگ و ثبت روان
            await Task.Delay(200);

            bool success = SaveCurrentInvoiceFromEditor();

            if (PrgSaveLoading != null) PrgSaveLoading.Visibility = Visibility.Collapsed;
            if (IcoTopSaveStatus != null) IcoTopSaveStatus.Visibility = Visibility.Visible;
            if (BtnTopSaveInvoice != null) BtnTopSaveInvoice.IsEnabled = true;

            if (success)
            {
                if (IcoTopSaveStatus != null)
                {
                    IcoTopSaveStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                    IcoTopSaveStatus.Foreground = (Brush)FindResource("SuccessBrush");
                }
                if (TxtTopSaveStatus != null)
                {
                    TxtTopSaveStatus.Text = _isNewInvoice ? "آماده (Ctrl + S)" : $"ذخیره شد ({DateTime.Now:HH:mm:ss}) - Ctrl+S";
                    TxtTopSaveStatus.Foreground = (Brush)FindResource("SuccessBrush");
                }
                if (BrdTopSaveStatus != null)
                {
                    BrdTopSaveStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                    BrdTopSaveStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                }
            }
            else
            {
                if (IcoTopSaveStatus != null)
                {
                    IcoTopSaveStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning24;
                    IcoTopSaveStatus.Foreground = (Brush)FindResource("DangerBrush");
                }
                if (TxtTopSaveStatus != null)
                {
                    TxtTopSaveStatus.Text = "خطا در ذخیره (اطلاعات ناقص)";
                    TxtTopSaveStatus.Foreground = (Brush)FindResource("DangerBrush");
                }
                if (BrdTopSaveStatus != null)
                {
                    BrdTopSaveStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                    BrdTopSaveStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
                }
            }

            return success;
        }

        private void BtnSaveAndExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                ExportInvoiceToExcel(_currentEditingInvoice!);
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند
            }
        }

        private void BtnSaveAndExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                if (_currentEditingInvoice != null)
                {
                    ExportInvoiceToPdfWithDialog(_currentEditingInvoice);
                }
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند
            }
        }

        private void BtnSaveAndPrintPdf_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                _printService.PrintOrExportToPdf(_currentEditingInvoice!);
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند
            }
        }

        private void BtnApproveAndPrint_Click(object sender, RoutedEventArgs e)
        {
            if (CmbEditorStatus != null)
            {
                CmbEditorStatus.SelectedItem = InvoiceStatus.Approved.ToPersianString();
            }
            if (_currentEditingInvoice != null)
            {
                _currentEditingInvoice.Status = InvoiceStatus.Approved;
            }
            UpdateApproveButtonVisibility();
            if (SaveCurrentInvoiceFromEditor())
            {
                if (_currentEditingInvoice != null)
                {
                    try
                    {
                        _printService.PrintOrExportToPdf(_currentEditingInvoice);
                        SetStatus($"پیش‌فاکتور {_currentEditingInvoice.InvoiceNumber} تأیید و پیش‌نمایش چاپ ارسال گردید.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"خطا در چاپ پیش‌فاکتور:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند
            }
        }

        private void CmbEditorStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateApproveButtonVisibility();
        }

        private void UpdateApproveButtonVisibility()
        {
            if (BtnTopApproveAndPrint == null) return;

            bool isApproved = false;
            if (CmbEditorStatus != null && CmbEditorStatus.SelectedItem != null)
            {
                string? selected = CmbEditorStatus.SelectedItem.ToString();
                if (selected == InvoiceStatus.Approved.ToPersianString() ||
                    (CmbEditorStatus.SelectedIndex >= 0 && (InvoiceStatus)CmbEditorStatus.SelectedIndex == InvoiceStatus.Approved))
                {
                    isApproved = true;
                }
            }
            else if (_currentEditingInvoice != null && _currentEditingInvoice.Status == InvoiceStatus.Approved)
            {
                isApproved = true;
            }

            BtnTopApproveAndPrint.Visibility = isApproved ? Visibility.Collapsed : Visibility.Visible;
        }

        private void TxtQuickItemName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = TxtQuickItemName.Text?.Trim() ?? "";
            var allItems = _storage.GetStorageItems();
            List<StorageItem> matching;
            if (string.IsNullOrWhiteSpace(q))
            {
                matching = allItems.Take(30).ToList();
            }
            else
            {
                matching = allItems
                    .Where(x => (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Code) && x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Category) && x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(30)
                    .ToList();
            }

            if (matching.Any())
            {
                LstQuickSuggestions.ItemsSource = matching;
                PopupQuickSuggestions.IsOpen = true;
            }
            else
            {
                PopupQuickSuggestions.IsOpen = false;
            }
        }

        private void LstQuickSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstQuickSuggestions.SelectedItem is StorageItem item)
            {
                TxtQuickItemName.Text = item.Name;
                if (!string.IsNullOrEmpty(item.Unit)) CmbQuickItemUnit.Text = item.Unit;
                if (item.DefaultPrice > 0) TxtQuickItemPrice.Text = item.DefaultPrice.ToString("N0");
                PopupQuickSuggestions.IsOpen = false;
                LstQuickSuggestions.SelectedItem = null;
            }
        }

        private void BtnQuickAddItem_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtQuickItemName.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("لطفاً نام کالا را وارد نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtQuickItemName.Focus();
                return;
            }

            if (!decimal.TryParse(TxtQuickItemQty.Text?.Trim(), out decimal qty) || qty <= 0)
            {
                qty = 1;
            }

            string unit = !string.IsNullOrWhiteSpace(CmbQuickItemUnit.Text) ? CmbQuickItemUnit.Text.Trim() : "عدد";
            string priceText = TxtQuickItemPrice.Text?.Replace(",", "").Trim() ?? "0";
            if (!decimal.TryParse(priceText, out decimal price) || price < 0)
            {
                price = 0;
            }

            string dTxt = TxtQuickItemDiscount?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";
            string pTxt = TxtQuickItemProfit?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";

            if (string.IsNullOrWhiteSpace(dTxt) || !decimal.TryParse(dTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal discount) || discount < 0)
            {
                MessageBox.Show("ورود درصد تخفیف الزامی است (حتی مقدار ۰٪ را وارد نمایید).", "اطلاعات ناقص", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtQuickItemDiscount?.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(pTxt) || !decimal.TryParse(pTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal profit) || profit < 0)
            {
                MessageBox.Show("ورود درصد سود الزامی است (حتی مقدار ۰٪ را وارد نمایید).", "اطلاعات ناقص", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtQuickItemProfit?.Focus();
                return;
            }

            var newItem = new InvoiceItem
            {
                RowNumber = _editingItems.Count + 1,
                ItemName = name,
                Unit = unit,
                Quantity = qty,
                RawPrice = price,
                DiscountPercent = discount,
                ProfitPercent = profit,
                IsDividedBy1Point1 = false
            };
            newItem.PropertyChanged += Item_PropertyChanged;
            _editingItems.Add(newItem);
            RecalculateEditorSummaries();
            SetDirty(true);

            // بررسی وضعیت کالا در انبار: در صورتی که کالا در انبار موجود باشد، قیمت آن در انبار فقط در صورتی تغییر می‌کند که کاربر قیمت را تغییر داده باشد
            var existingStorageItem = _storage.GetStorageItems().FirstOrDefault(p =>
                string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existingStorageItem != null)
            {
                if (price > 0 && price != existingStorageItem.DefaultPrice)
                {
                    existingStorageItem.DefaultPrice = price;
                    existingStorageItem.UpdatedAt = DateTime.Now;
                    if (!string.IsNullOrWhiteSpace(unit)) existingStorageItem.Unit = unit;
                    _storage.Save();
                }
            }
            else
            {
                // افزودن کالای جدید به انبار
                _storage.EnsureStorageItemExists(name, unit, price);
            }

            // پاکسازی فیلدهای ورود سریع
            TxtQuickItemName.Text = "";
            TxtQuickItemQty.Text = "1";
            TxtQuickItemPrice.Text = "0";
            if (TxtQuickItemDiscount != null) TxtQuickItemDiscount.Text = "";
            if (TxtQuickItemProfit != null) TxtQuickItemProfit.Text = "";
            TxtQuickItemName.Focus();

            SetStatus($"کالای «{name}» به اقلام پیش‌فاکتور افزوده شد.");
        }

        private void BtnCancelEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty)
            {
                var confirm = MessageBox.Show(
                    "تغییراتی در پیش‌فاکتور اعمال شده که هنوز ذخیره نشده است.\nآیا از انصراف و بازگشت بدون ذخیره اطمینان دارید؟",
                    "تأیید انصراف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
                SetDirty(false);
            }
            SwitchToView("Invoices");
        }

        private void BtnQuickAddCompany_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("Companies");
        }

        #endregion

        #region مدیریت شرکت‌ها

        private void RefreshCompaniesList()
        {
            if (DgrCompanies == null) return;
            var companies = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
            DgrCompanies.ItemsSource = companies;

            if (CmbFilterCompany != null)
            {
                CmbFilterCompany.Items.Clear();
                CmbFilterCompany.Items.Add("همه شرکت‌ها");
                foreach (var c in companies) CmbFilterCompany.Items.Add(c);
                CmbFilterCompany.SelectedIndex = 0;
            }

            if (CmbReportCompany != null)
            {
                CmbReportCompany.Items.Clear();
                CmbReportCompany.Items.Add("همه شرکت‌ها");
                foreach (var c in companies) CmbReportCompany.Items.Add(c);
                CmbReportCompany.SelectedIndex = 0;
            }
        }

        private void CmbCompanyType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCompanyType == null || LblCompanyFormName == null || LblCompanyCode == null || LblCompanyReg == null) return;

            bool isReal = (CmbCompanyType.SelectedIndex == 1);
            if (isReal)
            {
                LblCompanyFormName.Text = "نام و نام خانوادگی شخص طرف حساب *:";
                LblCompanyCode.Text = "کد ملی / شناسه ملی:";
                LblCompanyReg.Text = "شماره شناسنامه / مدرک:";
            }
            else
            {
                LblCompanyFormName.Text = "نام شرکت یا سازمان طرف حساب *:";
                LblCompanyCode.Text = "شناسه ملی / کد اقتصادی:";
                LblCompanyReg.Text = "شماره ثبت شرکت:";
            }
        }

        private void BtnNewCompany_Click(object sender, RoutedEventArgs e)
        {
            _currentEditingCompany = null;
            TxtCompanyFormTitle.Text = "ثبت طرف حساب / شرکت جدید";
            if (CmbCompanyType != null) CmbCompanyType.SelectedIndex = 0;
            TxtCompanyName.Text = "";
            TxtCompanyEconomicCode.Text = "";
            if (TxtCompanyMobile != null) TxtCompanyMobile.Text = "";
            TxtCompanyRegistrationNo.Text = "";
            TxtCompanyPhone.Text = "";
            TxtCompanyAddress.Text = "";
            TxtCompanyDescription.Text = "";
            TxtCompanyName.Focus();
        }

        private void DgrCompanies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgrCompanies.SelectedItem is Company company)
            {
                PopulateCompanyForm(company);
            }
        }

        private void PopulateCompanyForm(Company company)
        {
            _currentEditingCompany = company;
            TxtCompanyFormTitle.Text = $"ویرایش طرف حساب: {company.Name}";
            if (CmbCompanyType != null)
            {
                CmbCompanyType.SelectedIndex = (company.CompanyType == CompanyType.Real) ? 1 : 0;
            }
            TxtCompanyName.Text = company.Name;
            TxtCompanyEconomicCode.Text = company.NationalOrEconomicCode;
            if (TxtCompanyMobile != null) TxtCompanyMobile.Text = company.Mobile;
            TxtCompanyRegistrationNo.Text = company.RegistrationNo;
            TxtCompanyPhone.Text = company.Phone;
            TxtCompanyAddress.Text = company.Address;
            TxtCompanyDescription.Text = company.Description;
        }

        private void BtnEditCompany_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is Company company)
            {
                PopulateCompanyForm(company);
            }
        }

        private void BtnSaveCompany_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtCompanyName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("نام شرکت یا شخص طرف حساب الزامی است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var company = _currentEditingCompany ?? new Company();
            company.Name = name;
            company.CompanyType = (CmbCompanyType != null && CmbCompanyType.SelectedIndex == 1) ? CompanyType.Real : CompanyType.Legal;
            string eco = TxtCompanyEconomicCode.Text?.Trim() ?? "";
            company.NationalOrEconomicCode = eco;
            company.RegistrationNo = TxtCompanyRegistrationNo.Text?.Trim() ?? "";
            company.Phone = TxtCompanyPhone.Text?.Trim() ?? "";
            company.Mobile = TxtCompanyMobile?.Text?.Trim() ?? "";
            company.Address = TxtCompanyAddress.Text?.Trim() ?? "";
            company.Description = TxtCompanyDescription.Text?.Trim() ?? "";

            _storage.AddOrUpdateCompany(company);
            RefreshAll();
            BtnNewCompany_Click(sender, e);
            SetStatus($"اطلاعات طرف حساب {company.Name} با موفقیت ذخیره شد.");
        }

        private void BtnClearCompanyForm_Click(object sender, RoutedEventArgs e)
        {
            BtnNewCompany_Click(sender, e);
        }

        private void BtnDeleteCompany_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is Company company)
            {
                var res = MessageBox.Show($"آیا از حذف شرکت «{company.Name}» مطمئن هستید؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _storage.DeleteCompany(company.Id);
                    RefreshAll();
                    BtnNewCompany_Click(sender, e);
                    SetStatus($"شرکت {company.Name} حذف شد.");
                }
            }
        }

        #endregion

        #region مدیریت کارشناسان و شماره‌گذاری اختصاصی

        private void RefreshExpertsList()
        {
            if (DgrExperts == null) return;
            var experts = _storage.GetExperts().OrderBy(e => e.FullName).ToList();
            DgrExperts.ItemsSource = experts;

            if (CmbFilterExpert != null)
            {
                CmbFilterExpert.Items.Clear();
                CmbFilterExpert.Items.Add("همه کارشناسان");
                foreach (var exp in experts)
                {
                    CmbFilterExpert.Items.Add(exp);
                }
                CmbFilterExpert.SelectedIndex = 0;
            }

            if (CmbReportExpert != null)
            {
                CmbReportExpert.Items.Clear();
                CmbReportExpert.Items.Add("همه کارشناسان");
                foreach (var exp in experts)
                {
                    CmbReportExpert.Items.Add(exp);
                }
                CmbReportExpert.SelectedIndex = 0;
            }
        }

        private void BtnNewExpert_Click(object sender, RoutedEventArgs e)
        {
            _currentEditingExpert = null;
            TxtExpertFormTitle.Text = "ثبت کارشناس جدید";
            TxtExpertFullName.Text = "";
            TxtExpertRole.Text = "کارشناس خرید";
            TxtExpertPhone.Text = "";
            TxtExpertEmail.Text = "";
            if (ChkExpertAllCompanies != null) ChkExpertAllCompanies.IsChecked = true;
            if (LstExpertCompanies != null)
            {
                LstExpertCompanies.ItemsSource = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                LstExpertCompanies.SelectedItems.Clear();
                LstExpertCompanies.IsEnabled = false;
            }
            TxtExpertFullName.Focus();
        }

        private void ChkExpertAllCompanies_Changed(object sender, RoutedEventArgs e)
        {
            if (LstExpertCompanies != null && ChkExpertAllCompanies != null)
            {
                LstExpertCompanies.IsEnabled = (ChkExpertAllCompanies.IsChecked != true);
            }
        }

        private void DgrExperts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgrExperts.SelectedItem is Expert expert)
            {
                PopulateExpertForm(expert);
            }
        }

        private void PopulateExpertForm(Expert expert)
        {
            _currentEditingExpert = expert;
            TxtExpertFormTitle.Text = $"ویرایش کارشناس: {expert.FullName}";
            TxtExpertFullName.Text = expert.FullName;
            TxtExpertRole.Text = expert.RoleTitle;
            TxtExpertPhone.Text = expert.PhoneNumber;
            TxtExpertEmail.Text = expert.Email;

            if (ChkExpertAllCompanies != null && LstExpertCompanies != null)
            {
                var companies = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                LstExpertCompanies.ItemsSource = companies;

                bool isAll = (expert.AssignedCompanyIds == null || expert.AssignedCompanyIds.Count == 0);
                ChkExpertAllCompanies.IsChecked = isAll;
                LstExpertCompanies.IsEnabled = !isAll;

                LstExpertCompanies.SelectedItems.Clear();
                if (!isAll && expert.AssignedCompanyIds != null)
                {
                    foreach (var c in companies)
                    {
                        if (expert.AssignedCompanyIds.Contains(c.Id))
                        {
                            LstExpertCompanies.SelectedItems.Add(c);
                        }
                    }
                }
            }
        }

        private void BtnEditExpert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is Expert expert)
            {
                PopulateExpertForm(expert);
            }
        }

        private void BtnSaveExpert_Click(object sender, RoutedEventArgs e)
        {
            string fullName = TxtExpertFullName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(fullName))
            {
                MessageBox.Show("نام کارشناس الزامی است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var expert = _currentEditingExpert ?? new Expert();
            expert.FullName = fullName;
            expert.RoleTitle = TxtExpertRole.Text?.Trim() ?? "کارشناس خرید";
            expert.PhoneNumber = TxtExpertPhone.Text?.Trim() ?? "";
            expert.Email = TxtExpertEmail.Text?.Trim() ?? "";

            if (ChkExpertAllCompanies != null && ChkExpertAllCompanies.IsChecked == true)
            {
                expert.AssignedCompanyIds.Clear();
                expert.AssignedCompanyNames.Clear();
            }
            else if (LstExpertCompanies != null)
            {
                var selected = LstExpertCompanies.SelectedItems.Cast<Company>().ToList();
                expert.AssignedCompanyIds = selected.Select(c => c.Id).ToList();
                expert.AssignedCompanyNames = selected.Select(c => c.Name).ToList();
            }

            _storage.AddOrUpdateExpert(expert);
            RefreshAll();
            BtnNewExpert_Click(sender, e);
            SetStatus($"اطلاعات کارشناس {expert.FullName} با شماره‌گذاری اختصاصی ذخیره شد.");
        }

        private void BtnClearExpertForm_Click(object sender, RoutedEventArgs e)
        {
            BtnNewExpert_Click(sender, e);
        }

        private void BtnDeleteExpert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is Expert expert)
            {
                var res = MessageBox.Show($"آیا از حذف کارشناس «{expert.FullName}» اطمینان دارید؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _storage.DeleteExpert(expert.Id);
                    RefreshAll();
                    BtnNewExpert_Click(sender, e);
                    SetStatus($"کارشناس {expert.FullName} حذف شد.");
                }
            }
        }

        #endregion

        #region انبار و مدیریت کالاها

        private void RefreshStorageList()
        {
            if (DgrStorage == null) return;

            var items = _storage.GetStorageItems().OrderBy(x => x.Name).ToList();
            string search = TxtSearchStorage?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                items = items.Where(x =>
                    (x.Name != null && x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (x.Code != null && x.Code.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (x.Category != null && x.Category.Contains(search, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            DgrStorage.ItemsSource = items;

            // KPI های انبار
            if (TxtStorageKpiTotalCount != null) TxtStorageKpiTotalCount.Text = $"{items.Count} قلم کالا";
            if (TxtStorageKpiCategories != null)
            {
                var cats = items.Where(x => !string.IsNullOrEmpty(x.Category)).Select(x => x.Category).Distinct().Take(3);
                TxtStorageKpiCategories.Text = cats.Any() ? string.Join("، ", cats) : "عمومی";
            }
        }

        private void TxtSearchStorage_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshStorageList();
        }

        private void BtnNewStorageItem_Click(object sender, RoutedEventArgs e)
        {
            _currentEditingStorageItem = null;
            TxtStorageFormTitle.Text = "تعریف کالای جدید در انبار";
            TxtStorageCode.Text = $"PRD-{_storage.GetStorageItems().Count + 101}";
            TxtStorageName.Text = "";
            CmbStorageUnit.Text = "عدد";
            TxtStoragePrice.Text = "0";
            TxtStorageCategory.Text = "";
            TxtStorageDesc.Text = "";
            TxtStorageName.Focus();
        }

        private void PopulateStorageForm(StorageItem item)
        {
            _currentEditingStorageItem = item;
            TxtStorageFormTitle.Text = $"ویرایش کالا: {item.Name}";
            TxtStorageCode.Text = item.Code;
            TxtStorageName.Text = item.Name;
            CmbStorageUnit.Text = item.Unit;
            TxtStoragePrice.Text = item.DefaultPrice.ToString("N0");
            TxtStorageCategory.Text = item.Category;
            TxtStorageDesc.Text = item.Description;
        }

        private void DgrStorage_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgrStorage.SelectedItem is StorageItem item)
            {
                PopulateStorageForm(item);
            }
        }

        private void BtnEditStorageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is StorageItem item)
            {
                PopulateStorageForm(item);
            }
        }

        private static decimal ParseDecimalFlexible(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            string s = text.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                           .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
                           .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                           .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9')
                           .Replace(",", "").Replace("،", "").Replace(" ", "").Replace("ریال", "").Trim();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                return val;
            return 0m;
        }

        private void BtnDeleteStorageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is StorageItem item)
            {
                var res = MessageBox.Show($"آیا از حذف کالای «{item.Name}» از انبار مطمئن هستید؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _storage.DeleteStorageItem(item.Id);
                    RefreshAll();
                    BtnNewStorageItem_Click(sender, e);
                    SetStatus($"کالای {item.Name} از انبار حذف شد.");
                }
            }
        }

        private void BtnSaveStorageItem_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtStorageName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("نام کالا الزامی است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStorageName.Focus();
                return;
            }

            var item = _currentEditingStorageItem ?? new StorageItem();
            item.Code = TxtStorageCode.Text?.Trim() ?? "";
            item.Name = name;
            item.Unit = !string.IsNullOrWhiteSpace(CmbStorageUnit.Text) ? CmbStorageUnit.Text.Trim() : "عدد";
            item.Category = TxtStorageCategory.Text?.Trim() ?? "";
            item.Description = TxtStorageDesc.Text?.Trim() ?? "";
            item.DefaultPrice = ParseDecimalFlexible(TxtStoragePrice.Text);

            _storage.AddOrUpdateStorageItem(item);
            RefreshAll();
            BtnNewStorageItem_Click(sender, e);
            SetStatus($"کالای {item.Name} با موفقیت در انبار ذخیره شد.");
        }

        private void BtnClearStorageForm_Click(object sender, RoutedEventArgs e)
        {
            BtnNewStorageItem_Click(sender, e);
        }

        private void BtnExportStorageExcel_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"اقلام_انبار_{DateTime.Now:yyyyMMdd}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportStorage(_storage.GetStorageItems(), sfd.FileName);
                    SetStatus($"فهرست اقلام انبار در فایل اکسل ذخیره شد: {sfd.FileName}");
                    var res = MessageBox.Show("فایل اقلام انبار صادر شد. آیا مایل به باز کردن آن هستید؟", "موفقیت", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در صدور اکسل انبار:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region گزارشات و تحلیل فروش

        private void RefreshReportsList()
        {
            if (DgrReports == null) return;

            var invoices = _storage.GetInvoices().OrderByDescending(i => i.CreatedAt).ToList();

            // فیلتر ماه
            if (CmbReportMonth != null && CmbReportMonth.SelectedIndex > 0)
            {
                int m = CmbReportMonth.SelectedIndex;
                invoices = invoices.Where(i => JalaliDateHelper.GetMonthNumber(i.IssueDateShamsi) == m).ToList();
            }

            // فیلتر سال
            if (CmbReportYear != null && CmbReportYear.SelectedIndex > 0 && CmbReportYear.SelectedItem is string yrStr && int.TryParse(yrStr, out int yr))
            {
                invoices = invoices.Where(i => JalaliDateHelper.GetYearNumber(i.IssueDateShamsi) == yr).ToList();
            }

            // فیلتر کارشناس
            if (CmbReportExpert != null && CmbReportExpert.SelectedItem is Expert exp)
            {
                invoices = invoices.Where(i => i.ExpertId == exp.Id).ToList();
            }

            // فیلتر شرکت
            if (CmbReportCompany != null && CmbReportCompany.SelectedItem is Company comp)
            {
                invoices = invoices.Where(i => i.CompanyId == comp.Id).ToList();
            }

            // فیلتر وضعیت
            if (CmbReportStatus != null && CmbReportStatus.SelectedIndex > 0)
            {
                var status = (InvoiceStatus)(CmbReportStatus.SelectedIndex - 1);
                invoices = invoices.Where(i => i.Status == status).ToList();
            }

            DgrReports.ItemsSource = invoices;

            // محاسبات و KPI های گزارش
            int count = invoices.Count;
            decimal totalAmount = invoices.Sum(i => i.GrandTotal);
            decimal totalProfit = invoices.Sum(i => i.TotalProfit);
            decimal profitMargin = totalAmount > 0 ? Math.Round((totalProfit / totalAmount) * 100m, 1) : 0;
            decimal paidAmount = invoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.GrandTotal);
            decimal unpaidAmount = invoices.Where(i => i.Status == InvoiceStatus.Unpaid).Sum(i => i.GrandTotal);

            if (TxtReportKpiCount != null) TxtReportKpiCount.Text = $"{count} پیش‌فاکتور";
            if (TxtReportKpiTotalAmount != null) TxtReportKpiTotalAmount.Text = $"{totalAmount:N0} ریال";
            if (TxtReportKpiTotalProfit != null) TxtReportKpiTotalProfit.Text = $"{totalProfit:N0} ریال";
            if (TxtReportKpiProfitMargin != null) TxtReportKpiProfitMargin.Text = $"میانگین حاشیه سود: {profitMargin}٪";
            if (TxtReportKpiPaidAmount != null) TxtReportKpiPaidAmount.Text = $"واریز شده: {paidAmount:N0} ریال";
            if (TxtReportKpiUnpaidAmount != null) TxtReportKpiUnpaidAmount.Text = $"واریز نشده: {unpaidAmount:N0} ریال";
        }

        private void ReportFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            RefreshReportsList();
        }

        private void BtnResetReportFilter_Click(object sender, RoutedEventArgs e)
        {
            if (CmbReportMonth != null) CmbReportMonth.SelectedIndex = 0;
            if (CmbReportYear != null) CmbReportYear.SelectedIndex = 0;
            if (CmbReportExpert != null) CmbReportExpert.SelectedIndex = 0;
            if (CmbReportCompany != null) CmbReportCompany.SelectedIndex = 0;
            if (CmbReportStatus != null) CmbReportStatus.SelectedIndex = 0;
            RefreshReportsList();
            SetStatus("فیلترهای گزارش به حالت پیش‌فرض بازنشانی شدند.");
        }

        private void BtnExportReportExcel_Click(object sender, RoutedEventArgs e)
        {
            var currentList = DgrReports.ItemsSource as List<ProformaInvoice> ?? _storage.GetInvoices();
            var sfd = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"گزارش_فروش_تاراز_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportReport(currentList, sfd.FileName, "گزارش پیش‌فاکتورها و فروش");
                    SetStatus($"گزارش اکسل با موفقیت ذخیره شد: {sfd.FileName}");
                    var res = MessageBox.Show("فایل گزارش با موفقیت ایجاد شد. مایل به باز کردن آن هستید؟", "موفقیت", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در صدور اکسل گزارش:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnPrintReport_Click(object sender, RoutedEventArgs e)
        {
            var currentList = DgrReports.ItemsSource as List<ProformaInvoice> ?? _storage.GetInvoices();
            string filterSummary = $"تعداد: {currentList.Count} پیش‌فاکتور | جمع کل: {currentList.Sum(i => i.GrandTotal):N0} ریال";
            try
            {
                _printService.PrintReport(currentList, "گزارش جامع پیش‌فاکتورها و کارتابل فروش", filterSummary);
                SetStatus("دستور چاپ گزارش پیش‌فاکتورها ارسال شد.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در چاپ گزارش:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgrReports_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgrReports.SelectedItem is ProformaInvoice inv)
            {
                OpenInvoiceEditor(inv, isNew: false);
            }
        }

        #endregion

        #region تب تنظیمات پیش‌فاکتور و برنامه

        private void LoadSettingsTab()
        {
            try
            {
                var settings = _storage.GetSettings();
                if (TxtSellerName == null) return;

                TxtSellerName.Text = settings.SellerName;
                if (TxtSellerRegistrationNo != null) TxtSellerRegistrationNo.Text = settings.SellerRegistrationNo;
                TxtSellerNatCode.Text = settings.SellerNationalCode;
                TxtSellerEcoCode.Text = settings.SellerEconomicCode;
                TxtSellerPhone.Text = settings.SellerPhone;
                TxtSellerAddress.Text = settings.SellerAddress;

                // بارگذاری تصاویر لوگو، مهر و فونت‌ها
                if (TxtCompanyLogoPath != null)
                {
                    TxtCompanyLogoPath.Text = settings.CompanyLogoPath;
                    LoadImagePreview(settings.CompanyLogoPath, ImgLogoPreview);
                }
                if (TxtLogoWidth != null) TxtLogoWidth.Text = (settings.LogoWidth > 0 ? settings.LogoWidth : 68).ToString();
                if (TxtLogoHeight != null) TxtLogoHeight.Text = (settings.LogoHeight > 0 ? settings.LogoHeight : 68).ToString();

                if (TxtCompanyStampPath != null)
                {
                    TxtCompanyStampPath.Text = settings.CompanyStampPath;
                    LoadImagePreview(settings.CompanyStampPath, ImgStampPreview);
                }
                if (TxtStampWidth != null) TxtStampWidth.Text = (settings.StampWidth > 0 ? settings.StampWidth : 140).ToString();
                if (TxtStampHeight != null) TxtStampHeight.Text = (settings.StampHeight > 0 ? settings.StampHeight : 75).ToString();

                if (CmbFontCompanyName != null)
                {
                    string font = !string.IsNullOrEmpty(settings.CompanyNameFontFamily) ? settings.CompanyNameFontFamily : "Vazirmatn";
                    if (!CmbFontCompanyName.Items.Contains(font))
                    {
                        CmbFontCompanyName.Items.Add(font);
                    }
                    CmbFontCompanyName.SelectedItem = font;
                    CmbFontCompanyName.Text = font;
                }
                if (TxtFontSizeCompanyName != null)
                {
                    TxtFontSizeCompanyName.Text = settings.CompanyNameFontSize > 0 ? settings.CompanyNameFontSize.ToString("0") : "18";
                }

                if (CmbFontTable != null)
                {
                    string font = !string.IsNullOrEmpty(settings.TableFontFamily) ? settings.TableFontFamily : "Vazirmatn";
                    if (!CmbFontTable.Items.Contains(font))
                    {
                        CmbFontTable.Items.Add(font);
                    }
                    CmbFontTable.SelectedItem = font;
                    CmbFontTable.Text = font;
                }
                if (TxtFontSizeTable != null)
                {
                    TxtFontSizeTable.Text = settings.TableFontSize > 0 ? settings.TableFontSize.ToString("0") : "11";
                }

                if (TxtPaymentTerms != null) TxtPaymentTerms.Text = !string.IsNullOrEmpty(settings.PaymentTerms) ? settings.PaymentTerms : "نقد";
                if (TxtDeliveryDays != null) TxtDeliveryDays.Text = !string.IsNullOrEmpty(settings.DeliveryDays) ? settings.DeliveryDays : "موجود";
                if (TxtDeliveryLocation != null) TxtDeliveryLocation.Text = !string.IsNullOrEmpty(settings.DeliveryLocation) ? settings.DeliveryLocation : "مشهد";
                if (TxtVatPercent != null) TxtVatPercent.Text = settings.VatPercent.ToString("0");

                if (TxtSignatoryName != null) TxtSignatoryName.Text = !string.IsNullOrEmpty(settings.SignatoryName) ? settings.SignatoryName : "محمد احمدی پور هدش";
                if (TxtExpertRoleLabel != null) TxtExpertRoleLabel.Text = !string.IsNullOrEmpty(settings.ExpertRoleLabel) ? settings.ExpertRoleLabel : "کارشناسی خرید:";
                if (TxtDefaultValidityText != null) TxtDefaultValidityText.Text = !string.IsNullOrEmpty(settings.DefaultValidityText) ? settings.DefaultValidityText : "اعتبار این پیش فاکتور تا پایان روز کاری میباشد.";
                if (CmbSettingsCurrencyUnit != null) CmbSettingsCurrencyUnit.SelectedIndex = 0;

                // تنظیمات پیشوند و شمارنده شماره پیش‌فاکتور
                if (TxtSettingsInvoicePrefix != null)
                {
                    TxtSettingsInvoicePrefix.Text = !string.IsNullOrWhiteSpace(settings.InvoicePrefix) ? settings.InvoicePrefix : "ANA 405-";
                }
                if (TxtSettingsNextInvoiceNumber != null)
                {
                    TxtSettingsNextInvoiceNumber.Text = (settings.NextInvoiceNumber > 0 ? settings.NextInvoiceNumber : 1001).ToString();
                }

                if (DgrBankAccounts != null)
                {
                    _settingsBankAccounts = new ObservableCollection<BankAccountInfo>(settings.BankAccounts ?? new List<BankAccountInfo>());
                    DgrBankAccounts.ItemsSource = _settingsBankAccounts;
                }

                if (TxtDatabaseDirectory != null)
                {
                    TxtDatabaseDirectory.Text = _storage.PrimaryDatabaseDirectory ?? "";
                }
                UpdateDbStatusUI();
            }
            finally
            {
            }

            UpdateSettingsPdfPreview();
        }

        private ObservableCollection<BankAccountInfo> _settingsBankAccounts = new ObservableCollection<BankAccountInfo>();

        private void BtnAddBankAccount_Click(object sender, RoutedEventArgs e)
        {
            string bName = TxtNewBankName?.Text?.Trim() ?? "";
            string accNum = TxtNewBankAccountNumber?.Text?.Trim() ?? "";
            string sheba = TxtNewBankSheba?.Text?.Trim() ?? "";
            string card = TxtNewBankCard?.Text?.Trim() ?? "";
            string owner = TxtNewBankOwner?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(bName) && string.IsNullOrEmpty(accNum) && string.IsNullOrEmpty(sheba) && string.IsNullOrEmpty(card))
            {
                MessageBox.Show("لطفاً مشخصات حساب بانکی را وارد نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var acc = new BankAccountInfo
            {
                BankName = bName,
                AccountNumber = accNum,
                ShebaNumber = sheba,
                CardNumber = card,
                OwnerName = owner
            };
            _settingsBankAccounts.Add(acc);

            if (TxtNewBankName != null) TxtNewBankName.Text = "";
            if (TxtNewBankAccountNumber != null) TxtNewBankAccountNumber.Text = "";
            if (TxtNewBankSheba != null) TxtNewBankSheba.Text = "";
            if (TxtNewBankCard != null) TxtNewBankCard.Text = "";
            if (TxtNewBankOwner != null) TxtNewBankOwner.Text = "";

            var settings = _storage.GetSettings();
            settings.BankAccounts = _settingsBankAccounts.ToList();
            if (_settingsBankAccounts.Count > 0)
            {
                var firstAcc = _settingsBankAccounts[0];
                settings.BankAccountNumber = $"{firstAcc.ShebaNumber} {firstAcc.BankName}".Trim();
                settings.BankCardNumber = $"{firstAcc.CardNumber} {firstAcc.OwnerName}".Trim();
            }
            else
            {
                settings.BankAccountNumber = "";
                settings.BankCardNumber = "";
            }
            _storage.SaveSettings(settings);

            SetStatus("حساب بانکی به لیست افزوده و در پایگاه‌داده ذخیره شد.");
        }

        private void BtnDeleteBankAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is BankAccountInfo acc)
            {
                _settingsBankAccounts.Remove(acc);

                var settings = _storage.GetSettings();
                settings.BankAccounts = _settingsBankAccounts.ToList();
                if (_settingsBankAccounts.Count > 0)
                {
                    var firstAcc = _settingsBankAccounts[0];
                    settings.BankAccountNumber = $"{firstAcc.ShebaNumber} {firstAcc.BankName}".Trim();
                    settings.BankCardNumber = $"{firstAcc.CardNumber} {firstAcc.OwnerName}".Trim();
                }
                else
                {
                    settings.BankAccountNumber = "";
                    settings.BankCardNumber = "";
                }
                _storage.SaveSettings(settings);

                SetStatus("حساب بانکی از لیست حذف و در پایگاه‌داده ذخیره شد.");
            }
        }
        private void BtnSaveSellerSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            settings.SellerName = TxtSellerName.Text?.Trim() ?? "";
            settings.SellerRegistrationNo = TxtSellerRegistrationNo?.Text?.Trim() ?? "۸۸۶۵۴";
            settings.SellerNationalCode = TxtSellerNatCode.Text?.Trim() ?? "";
            settings.SellerEconomicCode = TxtSellerEcoCode.Text?.Trim() ?? "";
            settings.SellerPhone = TxtSellerPhone.Text?.Trim() ?? "";
            settings.SellerAddress = TxtSellerAddress.Text?.Trim() ?? "";
            SaveInvoiceNumberingSettings();
            _storage.SaveSettings(settings);

            SetStatus("مشخصات فروشنده با موفقیت در پایگاه‌داده ذخیره شد.");
            MessageBox.Show("مشخصات شرکت فروشنده با موفقیت در پایگاه‌داده ذخیره شد.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveInvoiceNumberingSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            SaveInvoiceNumberingSettings();
            _storage.SaveSettings(settings);
            SetStatus("تنظیمات پیشوند و شماره‌گذاری پیش‌فاکتور در پایگاه‌داده ذخیره شد.");
            MessageBox.Show("تنظیمات پیشوند و شماره پیش‌فاکتور در پایگاه‌داده ذخیره شد.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveInvoiceNumberingSettings()
        {
            var settings = _storage.GetSettings();
            if (TxtSettingsInvoicePrefix != null)
            {
                settings.InvoicePrefix = TxtSettingsInvoicePrefix.Text?.Trim() ?? "ANA 405-";
            }
            if (TxtSettingsNextInvoiceNumber != null)
            {
                string norm = StorageService.NormalizeDigits(TxtSettingsNextInvoiceNumber.Text);
                if (int.TryParse(norm, out int nextNum) && nextNum > 0)
                {
                    settings.NextInvoiceNumber = nextNum;
                }
            }
        }

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON Backup Files (*.json)|*.json",
                FileName = $"Taraz_Invoice_Backup_{DateTime.Now:yyyyMMdd_HHmm}.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _storage.BackupTo(sfd.FileName);
                    SetStatus($"نسخه پشتیبان با موفقیت در مسیر {sfd.FileName} ذخیره شد.");
                    MessageBox.Show("نسخه پشتیبان از تمام شرکت‌ها، کارشناسان و فاکتورها ذخیره شد.", "پشتیبان‌گیری موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در تهیه پشتیبان:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Backup Files (*.json)|*.json",
                Title = "انتخاب فایل پشتیبان جهت بازیابی"
            };

            if (ofd.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    "هشدار: با بازیابی اطلاعات از فایل پشتیبان، داده‌های فعلی جایگزین خواهند شد.\nآیا ادامه می‌دهید؟",
                    "تأیید بازیابی",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        _storage.RestoreFrom(ofd.FileName);
                        RefreshAll();
                        LoadSettingsTab();
                        SetStatus("داده‌ها با موفقیت از فایل پشتیبان بازیابی شدند.");
                        MessageBox.Show("بازیابی داده‌ها با موفقیت انجام شد.", "بازیابی موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"خطا در بازیابی اطلاعات:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void UpdateDbStatusUI()
        {
            try
            {
                if (TxtDbCardStatus == null) return;

                string localPath = _storage.LocalDatabasePath;
                if (TxtDbSecondaryPath != null)
                {
                    TxtDbSecondaryPath.Text = $"مسیر دیتابیس ثانویه (پشتیبان محلی در پوشه اصلی): {localPath}";
                }

                if (!_storage.IsCustomDirectoryConfigured)
                {
                    TxtDbCardStatus.Text = "پایگاه‌داده محلی پیش‌فرض برنامه فعال است (تک‌کاربره).";
                    if (IcoDbCardStatus != null)
                    {
                        IcoDbCardStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                        IcoDbCardStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    }
                    if (TxtDbStatusBottom != null)
                    {
                        TxtDbStatusBottom.Text = "پایگاه‌داده: محلی پیش‌فرض";
                    }
                    if (IcoDbStatus != null)
                    {
                        IcoDbStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Database20;
                        IcoDbStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    }
                }
                else if (_storage.IsNetworkConnected)
                {
                    TxtDbCardStatus.Text = $"متصل به پوشه شبکه/اصلی ({_storage.PrimaryDatabaseDirectory}) - دیتابیس ثانویه محلی آماده و فعال.";
                    if (IcoDbCardStatus != null)
                    {
                        IcoDbCardStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                        IcoDbCardStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    }
                    if (TxtDbStatusBottom != null)
                    {
                        TxtDbStatusBottom.Text = "پایگاه‌داده: متصل به شبکه (همگام با محلی)";
                    }
                    if (IcoDbStatus != null)
                    {
                        IcoDbStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Database20;
                        IcoDbStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    }
                }
                else
                {
                    TxtDbCardStatus.Text = $"عدم دسترسی به پوشه شبکه ({_storage.PrimaryDatabaseDirectory}) - در حال کار با دیتابیس ثانویه محلی (تغییرات بدون وقفه ذخیره شده و پس از اتصال ادغام می‌شوند).";
                    if (IcoDbCardStatus != null)
                    {
                        IcoDbCardStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning20;
                        IcoDbCardStatus.Foreground = (Brush)FindResource("WarningBrush");
                    }
                    if (TxtDbStatusBottom != null)
                    {
                        TxtDbStatusBottom.Text = "پایگاه‌داده: آفلاین (ذخیره در دیتابیس ثانویه)";
                    }
                    if (IcoDbStatus != null)
                    {
                        IcoDbStatus.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning20;
                        IcoDbStatus.Foreground = (Brush)FindResource("WarningBrush");
                    }
                }
            }
            catch { }
        }

        private void BtnBrowseDatabaseDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "انتخاب پوشه پایگاه‌داده (محلی یا پوشه اشتراکی شبکه)",
                    Multiselect = false
                };
                if (!string.IsNullOrWhiteSpace(TxtDatabaseDirectory?.Text) && Directory.Exists(TxtDatabaseDirectory.Text))
                {
                    dialog.InitialDirectory = TxtDatabaseDirectory.Text;
                }
                if (dialog.ShowDialog() == true)
                {
                    if (TxtDatabaseDirectory != null)
                    {
                        TxtDatabaseDirectory.Text = dialog.FolderName;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در باز کردن دیالوگ انتخاب پوشه:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveDatabaseDir_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtDatabaseDirectory?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                _storage.ResetToDefaultDatabaseDirectory();
                UpdateDbStatusUI();
                SetStatus("پایگاه‌داده به پوشه پیش‌فرض برنامه بازگردانده شد.");
                RefreshAll();
                MessageBox.Show("مسیر پایگاه‌داده به پوشه پیش‌فرض برنامه (Data/database.json) بازگردانده شد.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool success = _storage.SetCustomDatabaseDirectory(path, out string errMsg);
            UpdateDbStatusUI();
            if (success)
            {
                SetStatus("پوشه پایگاه‌داده با موفقیت تنظیم و همگام‌سازی شد.");
                RefreshAll();
                MessageBox.Show("پوشه پایگاه‌داده با موفقیت تنظیم شد و اطلاعات با پایگاه‌داده ثانویه محلی ادغام و همگام‌سازی گردیدند.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(errMsg, "خطا در تنظیم پوشه", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSyncDatabaseNow_Click(object sender, RoutedEventArgs e)
        {
            bool success = _storage.SyncNow(out string msg);
            UpdateDbStatusUI();
            RefreshAll();
            SetStatus(msg);
            if (success)
            {
                MessageBox.Show(msg, "همگام‌سازی موفق", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(msg, "وضعیت اتصال", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnResetDefaultDatabaseDir_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "آیا مایلید مسیر پایگاه‌داده به پوشه پیش‌فرض برنامه بازگردد؟\n\nکلیه داده‌های شما در پایگاه‌داده ثانویه محلی محفوظ است.",
                "تأیید بازگشت به پیش‌فرض",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                _storage.ResetToDefaultDatabaseDirectory();
                if (TxtDatabaseDirectory != null) TxtDatabaseDirectory.Text = "";
                UpdateDbStatusUI();
                RefreshAll();
                SetStatus("پایگاه‌داده به پوشه پیش‌فرض برنامه بازگردانده شد.");
                MessageBox.Show("پایگاه‌داده با موفقیت به پوشه پیش‌فرض برنامه بازگردانده شد.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LoadImagePreview(string? path, System.Windows.Controls.Image? img)
        {
            if (img == null) return;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    img.Source = bmp;
                }
                else
                {
                    img.Source = null;
                }
            }
            catch
            {
                img.Source = null;
            }
        }

        private void BtnBrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                Title = "انتخاب فایل تصویر لوگوی شرکت"
            };

            if (ofd.ShowDialog() == true)
            {
                TxtCompanyLogoPath.Text = ofd.FileName;
                LoadImagePreview(ofd.FileName, ImgLogoPreview);
            }
        }

        private void BtnClearLogo_Click(object sender, RoutedEventArgs e)
        {
            TxtCompanyLogoPath.Text = "";
            ImgLogoPreview.Source = null;
        }

        private void BtnBrowseStamp_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                Title = "انتخاب تصویر مهر و امضای رسمی شرکت"
            };

            if (ofd.ShowDialog() == true)
            {
                TxtCompanyStampPath.Text = ofd.FileName;
                LoadImagePreview(ofd.FileName, ImgStampPreview);
            }
        }

        private void BtnClearStamp_Click(object sender, RoutedEventArgs e)
        {
            TxtCompanyStampPath.Text = "";
            ImgStampPreview.Source = null;
        }

        private static double ParseDoubleSafe(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string normalized = text.Trim()
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
            return double.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) ? val : fallback;
        }

        private void BtnSaveInvoiceAppearance_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            settings.CompanyLogoPath = TxtCompanyLogoPath.Text?.Trim() ?? "";
            settings.CompanyStampPath = TxtCompanyStampPath.Text?.Trim() ?? "";

            double lw = ParseDoubleSafe(TxtLogoWidth?.Text, 68);
            if (lw > 0) settings.LogoWidth = lw;
            double lh = ParseDoubleSafe(TxtLogoHeight?.Text, 68);
            if (lh > 0) settings.LogoHeight = lh;

            double sw = ParseDoubleSafe(TxtStampWidth?.Text, 140);
            if (sw > 0) settings.StampWidth = sw;
            double sh = ParseDoubleSafe(TxtStampHeight?.Text, 75);
            if (sh > 0) settings.StampHeight = sh;

            string selectedFontName = CmbFontCompanyName.SelectedItem?.ToString() ?? CmbFontCompanyName.Text;
            settings.CompanyNameFontFamily = !string.IsNullOrWhiteSpace(selectedFontName) ? selectedFontName.Trim() : "Vazirmatn";
            double nameSize = ParseDoubleSafe(TxtFontSizeCompanyName.Text, 0);
            if (nameSize > 6)
            {
                settings.CompanyNameFontSize = nameSize;
            }

            string selectedFontTbl = CmbFontTable.SelectedItem?.ToString() ?? CmbFontTable.Text;
            settings.TableFontFamily = !string.IsNullOrWhiteSpace(selectedFontTbl) ? selectedFontTbl.Trim() : "Vazirmatn";
            double tblSize = ParseDoubleSafe(TxtFontSizeTable.Text, 0);
            if (tblSize > 6)
            {
                settings.TableFontSize = tblSize;
            }

            settings.BankAccounts = _settingsBankAccounts.ToList();
            if (_settingsBankAccounts.Count > 0)
            {
                var firstAcc = _settingsBankAccounts[0];
                settings.BankAccountNumber = $"{firstAcc.ShebaNumber} {firstAcc.BankName}".Trim();
                settings.BankCardNumber = $"{firstAcc.CardNumber} {firstAcc.OwnerName}".Trim();
            }
            else
            {
                settings.BankAccountNumber = "";
                settings.BankCardNumber = "";
            }
            settings.PaymentTerms = TxtPaymentTerms.Text?.Trim() ?? "نقد";
            settings.DeliveryDays = TxtDeliveryDays.Text?.Trim() ?? "موجود";
            settings.DeliveryLocation = TxtDeliveryLocation.Text?.Trim() ?? "مشهد";

            if (TxtSignatoryName != null) settings.SignatoryName = TxtSignatoryName.Text?.Trim() ?? "محمد احمدی پور هدش";
            if (TxtExpertRoleLabel != null) settings.ExpertRoleLabel = TxtExpertRoleLabel.Text?.Trim() ?? "کارشناسی خرید:";
            if (TxtDefaultValidityText != null) settings.DefaultValidityText = TxtDefaultValidityText.Text?.Trim() ?? "اعتبار این پیش فاکتورتا پایان روز می باشد.";
            settings.CurrencyUnit = "ریال";

            if (decimal.TryParse(TxtVatPercent.Text?.Trim(), out decimal vat) && vat >= 0)
            {
                settings.VatPercent = vat;
            }

            SaveInvoiceNumberingSettings();
            _storage.SaveSettings(settings);
            RefreshAll();
            SetStatus("تنظیمات سربرگ، مقاصد، تصاویر و فونت‌ها در پایگاه‌داده ذخیره شد.");
            MessageBox.Show("تنظیمات سربرگ، تصویر شرکت، محل‌های تحویل و فونت‌ها با موفقیت در پایگاه‌داده ذخیره گردید.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSettingsControlChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void OnSettingsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void BtnRefreshSettingsPdf_Click(object sender, RoutedEventArgs e)
        {
        }

        public void UpdateSettingsPdfPreview()
        {
        }

        private void BuildCurrentSettingsSampleInvoice(out AppSettings previewSettings, out ProformaInvoice sampleInv)
        {
            previewSettings = new AppSettings
            {
                SellerName = !string.IsNullOrWhiteSpace(TxtSellerName?.Text) ? TxtSellerName.Text.Trim() : "شرکت آریان نیرو گستر اترک",
                SellerRegistrationNo = !string.IsNullOrWhiteSpace(TxtSellerRegistrationNo?.Text) ? TxtSellerRegistrationNo.Text.Trim() : "۸۸۶۵۴",
                SellerNationalCode = TxtSellerNatCode?.Text?.Trim() ?? "14008765432",
                SellerEconomicCode = TxtSellerEcoCode?.Text?.Trim() ?? "411567891234",
                SellerPhone = !string.IsNullOrWhiteSpace(TxtSellerPhone?.Text) ? TxtSellerPhone.Text.Trim() : "۰۵۱۳۷۱۳۲۰۸۴",
                SellerAddress = !string.IsNullOrWhiteSpace(TxtSellerAddress?.Text) ? TxtSellerAddress.Text.Trim() : "مشهد - خیابان سنایی",
                CompanyLogoPath = TxtCompanyLogoPath?.Text?.Trim() ?? "",
                CompanyStampPath = TxtCompanyStampPath?.Text?.Trim() ?? "",
                LogoWidth = ParseDoubleSafe(TxtLogoWidth?.Text, 68),
                LogoHeight = ParseDoubleSafe(TxtLogoHeight?.Text, 68),
                StampWidth = ParseDoubleSafe(TxtStampWidth?.Text, 140),
                StampHeight = ParseDoubleSafe(TxtStampHeight?.Text, 75),
                CompanyNameFontFamily = !string.IsNullOrWhiteSpace(CmbFontCompanyName?.Text) ? CmbFontCompanyName.Text.Trim() : "Vazirmatn",
                TableFontFamily = !string.IsNullOrWhiteSpace(CmbFontTable?.Text) ? CmbFontTable.Text.Trim() : "Vazirmatn",
                DefaultValidityText = !string.IsNullOrWhiteSpace(TxtDefaultValidityText?.Text) ? TxtDefaultValidityText.Text.Trim() : "اعتبار این پیش فاکتور تا پایان روز کاری میباشد.",
                PaymentTerms = !string.IsNullOrWhiteSpace(TxtPaymentTerms?.Text) ? TxtPaymentTerms.Text.Trim() : "نقد",
                DeliveryDays = !string.IsNullOrWhiteSpace(TxtDeliveryDays?.Text) ? TxtDeliveryDays.Text.Trim() : "موجود",
                DeliveryLocation = !string.IsNullOrWhiteSpace(TxtDeliveryLocation?.Text) ? TxtDeliveryLocation.Text.Trim() : "مشهد",
                SignatoryName = !string.IsNullOrWhiteSpace(TxtSignatoryName?.Text) ? TxtSignatoryName.Text.Trim() : "محمد احمدی پور هدش",
                ExpertRoleLabel = !string.IsNullOrWhiteSpace(TxtExpertRoleLabel?.Text) ? TxtExpertRoleLabel.Text.Trim() : "کارشناسی خرید:",
                CurrencyUnit = "ریال",
                BankAccounts = _settingsBankAccounts?.ToList() ?? new List<BankAccountInfo>()
            };

            double cSize = ParseDoubleSafe(TxtFontSizeCompanyName?.Text, 0);
            if (cSize > 6)
                previewSettings.CompanyNameFontSize = cSize;
            double tSize = ParseDoubleSafe(TxtFontSizeTable?.Text, 0);
            if (tSize > 6)
                previewSettings.TableFontSize = tSize;
            if (decimal.TryParse(TxtVatPercent?.Text?.Trim(), out decimal vat) && vat >= 0)
                previewSettings.VatPercent = vat;

            sampleInv = new ProformaInvoice
            {
                InvoiceNumber = "ANA 405-0000284",
                IssueDateShamsi = "۱۴۰۵/۰۷/۲۵",
                Attachment = "ندارد",
                PageNumberText = "یک",
                CompanyName = "شرکت محترم مهام صنعت نیشابور",
                Subject = "قیمت پیشنهادی و سایر شرایط فروش به شرح زیر اعلام میگردد:",
                CustomerLabelPrefix = "نام مشتری: شرکت محترم",
                SignatoryName = previewSettings.SignatoryName,
                ExpertName = "محمد احمدی پور هدش",
                ExpertRoleLabel = previewSettings.ExpertRoleLabel,
                ValidityText = previewSettings.DefaultValidityText,
                HighlightValidity = true,
                PaymentTerms = previewSettings.PaymentTerms,
                DeliveryDays = previewSettings.DeliveryDays,
                DeliveryLocation = previewSettings.DeliveryLocation,
                VatPercent = previewSettings.VatPercent,
                BankAccounts = previewSettings.BankAccounts,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem { RowNumber = 1, ItemName = "داکت شیاردار ۲/۵×۴ رعد (شاخه ۲ متری)", Quantity = 60, Unit = "متر", RawPrice = 1924000 },
                    new InvoiceItem { RowNumber = 2, ItemName = "داکت شیاردار ۴×۶ رعد (شاخه ۲ متری)", Quantity = 40, Unit = "متر", RawPrice = 3364000 }
                }
            };
            foreach (var it in sampleInv.Items) it.UpdateFromPercent();
        }

        private void BtnExportSamplePdf_Click(object sender, RoutedEventArgs e)
        {
            BuildCurrentSettingsSampleInvoice(out AppSettings previewSettings, out ProformaInvoice sampleInv);
            ExportInvoiceToPdfWithDialog(sampleInv, previewSettings);
        }

        private void BtnTestPrintSample_Click(object sender, RoutedEventArgs e)
        {
            BuildCurrentSettingsSampleInvoice(out AppSettings previewSettings, out ProformaInvoice sampleInv);

            try
            {
                _printService.PrintOrExportToPdf(sampleInv, previewSettings);
                SetStatus("پیش‌نمایش چاپ سند رسمی با تنظیمات جاری با موفقیت باز گردید.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در پیش‌نمایش چاپ:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        private void SetStatus(string message)
        {
            if (TxtStatusMessage != null)
            {
                TxtStatusMessage.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
                if (TxtStatusSeparator != null) TxtStatusSeparator.Visibility = Visibility.Visible;
            }
        }
    }
}
