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

            if (_currentEditingInvoice != null && (ViewInvoiceEditor?.Visibility == Visibility.Visible || _isDirty))
            {
                SaveDraftSilently();
            }
        }

        private void Storage_DataChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    RefreshDropdownsAndActiveViews();
                }
                catch
                {
                    // خطا در خواندن همزمان نادیده گرفته می‌شود
                }
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
                if (experts.Count == 0) experts = allExperts;

                CmbEditorExpert.ItemsSource = null;
                CmbEditorExpert.ItemsSource = experts;
                if (curExpId.HasValue)
                {
                    CmbEditorExpert.SelectedItem = experts.FirstOrDefault(e => e.Id == curExpId.Value);
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
        }

        private void UpdateWindowTitle()
        {
            const string baseTitle = "تـراز (v0.2.0-beta) - سامانه جامع صدور و مدیریت پیش‌فاکتور";
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
                        RefreshDropdownsAndActiveViews();
                    }
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
                if (experts.Count == 0) experts = allExperts;
                CmbEditorExpert.ItemsSource = experts;
                if (curId.HasValue)
                {
                    CmbEditorExpert.SelectedItem = experts.FirstOrDefault(x => x.Id == curId.Value);
                }
            }
            catch { }
        }

        private void DgrInvoiceItems_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell && dep is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                {
                    cell.Focus();
                }
                DgrInvoiceItems.BeginEdit();
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

            // ذخیره خودکار پیش‌نویس در صورت خروج از صفحه ویرایشگر تا اطلاعات کاربر هرگز از بین نرود
            if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible && _isDirty && viewName != "Editor")
            {
                SaveDraftSilently();
                SetStatus("پیش‌فاکتور به صورت خودکار در وضعیت «پیش‌نویس» ذخیره گردید.");
            }

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
                    TxtPageTitle.Text = "مدیریت کارشناسان و شماره‌انداز مستقل";
                    TxtPageSubtitle.Text = "تنظیم کارشناسان فروش و شماره‌انداز مجزای پیش‌فاکتور هر کارشناس";
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
            var expert = _storage.GetExperts().FirstOrDefault(exp => exp.Id == sourceInvoice.ExpertId);
            string newNumber = expert != null ? expert.GetPreviewNextInvoiceNumber() : $"INV-{DateTime.Now:mmss}";

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
                RefreshInvoicesList();
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

            if (experts.Count == 0)
            {
                MessageBox.Show("لطفاً ابتدا حداقل یک کارشناس ثبت نمایید.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                SwitchToView("Experts");
                return;
            }

            var defaultExpert = experts.First();
            var defaultCompany = companies.First();
            var settings = _storage.GetSettings();

            var newInvoice = new ProformaInvoice
            {
                InvoiceNumber = defaultExpert.GetPreviewNextInvoiceNumber(),
                IssueDateShamsi = JalaliDateHelper.GetCurrentShamsiDate(),
                ValidUntilShamsi = JalaliDateHelper.GetShamsiDateAfterDays(7),
                CompanyId = defaultCompany.Id,
                CompanyName = defaultCompany.Name,
                CompanyPhone = defaultCompany.Phone,
                CompanyAddress = defaultCompany.Address,
                CompanyEconomicCode = defaultCompany.EconomicCode,
                CompanyNationalCode = defaultCompany.NationalCode,
                ExpertId = defaultExpert.Id,
                ExpertName = defaultExpert.FullName,
                ExpertPhone = defaultExpert.PhoneNumber,
                Status = InvoiceStatus.Draft,
                Notes = !string.IsNullOrWhiteSpace(settings.DefaultTerms) ? settings.DefaultTerms : "۱. فرایند تحویل بار پس از وصول پیش‌پرداخت در صورت عدم تغییر قیمت انجام خواهد شد.\n۲. در صورت تغییر قیمت، پیش‌فاکتور اصلاحی تقدیم می‌گردد.",
                PaymentTerms = !string.IsNullOrWhiteSpace(settings.PaymentTerms) ? settings.PaymentTerms : "نقد",
                DeliveryDays = !string.IsNullOrWhiteSpace(settings.DeliveryDays) ? settings.DeliveryDays : "سه روز کاری",
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
            CmbEditorExpert.ItemsSource = experts;
            var currentExp = experts.FirstOrDefault(e => e.Id == invoice.ExpertId);
            CmbEditorExpert.SelectedItem = currentExp;

            TxtEditorInvoiceNumber.Text = invoice.InvoiceNumber;
            TxtEditorIssueDate.Text = invoice.IssueDateShamsi;
            TxtEditorValidUntil.Text = invoice.ValidUntilShamsi;
            CmbEditorStatus.SelectedIndex = (int)invoice.Status;

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
            decimal grandTotal = _editingItems.Sum(i => i.TotalPrice);

            decimal vatPercent = 10m;
            if (TxtEditorVatPercent != null && decimal.TryParse(TxtEditorVatPercent.Text?.Trim(), out decimal parsedVat) && parsedVat >= 0)
            {
                vatPercent = parsedVat;
            }
            decimal vatAmount = Math.Round(grandTotal * (vatPercent / 100m), 0);
            decimal grandTotalWithVat = grandTotal + vatAmount;

            TxtSummaryRawTotal.Text = $"{rawTotal:N0} ریال";
            TxtSummaryDiscountTotal.Text = $"{discountTotal:N0} ریال";
            TxtSummaryProfitTotal.Text = $"{profitTotal:N0} ریال";
            decimal profitPct = grandTotal > 0 ? Math.Round((profitTotal / grandTotal) * 100m, 1) : 0;
            TxtSummaryProfitPercent.Text = $"{profitPct}% سود";

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
                if (filtered.Count == 0) filtered = allExperts;

                var curExp = CmbEditorExpert.SelectedItem as Expert;
                CmbEditorExpert.ItemsSource = filtered;
                if (curExp != null && filtered.Any(x => x.Id == curExp.Id))
                {
                    CmbEditorExpert.SelectedItem = filtered.First(x => x.Id == curExp.Id);
                }
                else
                {
                    CmbEditorExpert.SelectedItem = filtered.FirstOrDefault();
                }
            }
        }

        private void CmbEditorExpert_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEditorExpert.SelectedItem is Expert expert)
            {
                TxtExpertCounterHint.Text = $"(شماره بعدی: {expert.GetPreviewNextInvoiceNumber()})";
                if (_isNewInvoice)
                {
                    TxtEditorInvoiceNumber.Text = expert.GetPreviewNextInvoiceNumber();
                }
            }
            else
            {
                TxtExpertCounterHint.Text = "";
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

            if (CmbEditorExpert.SelectedItem is not Expert expert)
            {
                MessageBox.Show("لطفاً کارشناس صادرکننده را انتخاب نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

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

            _currentEditingInvoice.ExpertId = expert.Id;
            _currentEditingInvoice.ExpertName = expert.FullName;
            _currentEditingInvoice.ExpertPhone = expert.PhoneNumber;

            _currentEditingInvoice.InvoiceNumber = TxtEditorInvoiceNumber.Text.Trim();
            _currentEditingInvoice.IssueDateShamsi = TxtEditorIssueDate.Text?.Trim() ?? JalaliDateHelper.GetCurrentShamsiDate();
            _currentEditingInvoice.ValidUntilShamsi = TxtEditorValidUntil.Text?.Trim() ?? JalaliDateHelper.GetShamsiDateAfterDays(7);
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

            if (decimal.TryParse(TxtEditorVatPercent.Text?.Trim(), out decimal vat) && vat >= 0)
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

            if (_isNewInvoice)
            {
                // برای جلوگیری از تداخل شماره فاکتور هنگام استفاده همزمان چند کاربر روی شبکه
                _storage.CheckAndReloadIfDiskChanged();
                var freshExpert = _storage.GetExperts().FirstOrDefault(e => e.Id == expert.Id) ?? expert;

                if (_storage.GetInvoices().Any(inv => inv.InvoiceNumber == _currentEditingInvoice.InvoiceNumber && inv.Id != _currentEditingInvoice.Id))
                {
                    string newNum = freshExpert.GetPreviewNextInvoiceNumber();
                    _currentEditingInvoice.InvoiceNumber = newNum;
                    if (TxtEditorInvoiceNumber != null) TxtEditorInvoiceNumber.Text = newNum;
                }

                if (_currentEditingInvoice.InvoiceNumber == freshExpert.GetPreviewNextInvoiceNumber())
                {
                    freshExpert.ConsumeNextInvoiceNumber();
                    _storage.AddOrUpdateExpert(freshExpert);
                }
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

        private void SaveDraftSilently()
        {
            if (_currentEditingInvoice == null) return;
            try
            {
                if (CmbEditorCompany?.SelectedItem is Company company)
                {
                    _currentEditingInvoice.CompanyId = company.Id;
                    _currentEditingInvoice.CompanyName = company.Name;
                    _currentEditingInvoice.CompanyPhone = TxtEditorCompanyPhone?.Text?.Trim() ?? "";
                    _currentEditingInvoice.CompanyAddress = TxtEditorCompanyAddress?.Text?.Trim() ?? "";
                    _currentEditingInvoice.CompanyEconomicCode = company.EconomicCode;
                    _currentEditingInvoice.CompanyNationalCode = company.NationalCode;
                }

                if (CmbEditorExpert?.SelectedItem is Expert expert)
                {
                    _currentEditingInvoice.ExpertId = expert.Id;
                    _currentEditingInvoice.ExpertName = expert.FullName;
                    _currentEditingInvoice.ExpertPhone = expert.PhoneNumber;
                }

                if (TxtEditorInvoiceNumber != null) _currentEditingInvoice.InvoiceNumber = TxtEditorInvoiceNumber.Text.Trim();
                if (TxtEditorIssueDate != null) _currentEditingInvoice.IssueDateShamsi = TxtEditorIssueDate.Text?.Trim() ?? JalaliDateHelper.GetCurrentShamsiDate();
                if (TxtEditorValidUntil != null) _currentEditingInvoice.ValidUntilShamsi = TxtEditorValidUntil.Text?.Trim() ?? JalaliDateHelper.GetShamsiDateAfterDays(7);
                _currentEditingInvoice.Status = InvoiceStatus.Draft;
                if (TxtEditorCustomerPrefix != null) _currentEditingInvoice.CustomerLabelPrefix = TxtEditorCustomerPrefix.Text?.Trim() ?? "نام مشتری شرکت محترم ";
                if (TxtEditorSubject != null) _currentEditingInvoice.Subject = TxtEditorSubject.Text?.Trim() ?? "";
                if (TxtEditorAttachment != null) _currentEditingInvoice.Attachment = TxtEditorAttachment.Text?.Trim() ?? "ندارد";
                if (TxtEditorPageNumberText != null) _currentEditingInvoice.PageNumberText = TxtEditorPageNumberText.Text?.Trim() ?? "یک";
                _currentEditingInvoice.CurrencyUnit = "ریال";
                if (TxtEditorValidityText != null) _currentEditingInvoice.ValidityText = TxtEditorValidityText.Text?.Trim() ?? "";
                if (TxtEditorNotes != null) _currentEditingInvoice.Notes = TxtEditorNotes.Text?.Trim() ?? "";
                if (TxtEditorPaymentTerms != null) _currentEditingInvoice.PaymentTerms = TxtEditorPaymentTerms.Text?.Trim() ?? "نقد";
                if (TxtEditorDeliveryDays != null) _currentEditingInvoice.DeliveryDays = TxtEditorDeliveryDays.Text?.Trim() ?? "موجود";
                if (TxtEditorDeliveryLocation != null) _currentEditingInvoice.DeliveryLocation = TxtEditorDeliveryLocation.Text?.Trim() ?? "مشهد";
                if (TxtEditorSignatoryName != null) _currentEditingInvoice.SignatoryName = TxtEditorSignatoryName.Text?.Trim() ?? "محمد احمدی پور هدش";
                if (TxtEditorExpertRole != null) _currentEditingInvoice.ExpertRoleLabel = TxtEditorExpertRole.Text?.Trim() ?? "کارشناسی خرید:";

                if (TxtEditorVatPercent != null && decimal.TryParse(TxtEditorVatPercent.Text?.Trim(), out decimal vat) && vat >= 0)
                {
                    _currentEditingInvoice.VatPercent = vat;
                }

                if (ChkHighlightValidity != null) _currentEditingInvoice.HighlightValidity = ChkHighlightValidity.IsChecked == true;
                if (ChkHighlightPaymentTerms != null) _currentEditingInvoice.HighlightPaymentTerms = ChkHighlightPaymentTerms.IsChecked == true;
                if (ChkHighlightDeliveryDays != null) _currentEditingInvoice.HighlightDeliveryDays = ChkHighlightDeliveryDays.IsChecked == true;
                if (ChkHighlightDeliveryLocation != null) _currentEditingInvoice.HighlightDeliveryLocation = ChkHighlightDeliveryLocation.IsChecked == true;
                if (ChkHighlightNotes != null) _currentEditingInvoice.HighlightNotes = ChkHighlightNotes.IsChecked == true;
                if (ChkHighlightVat != null) _currentEditingInvoice.HighlightVat = ChkHighlightVat.IsChecked == true;

                _currentEditingInvoice.Items = _editingItems.ToList();
                _currentEditingInvoice.BankAccounts = _editingBankAccounts.ToList();
                _currentEditingInvoice.UpdatedAt = DateTime.Now;

                _storage.AddOrUpdateInvoice(_currentEditingInvoice);
                SetDirty(false);
            }
            catch
            {
                // نادیده گرفتن استثنای ذخیره موقت
            }
        }

        private void BtnApplyBulkDiscount_Click(object sender, RoutedEventArgs e)
        {
            string txt = TxtBulkDiscountPercent?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal discount) && discount >= 0)
            {
                if (_editingItems.Count == 0)
                {
                    MessageBox.Show("هیچ سطری در پیش‌فاکتور برای اعمال تخفیف وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var targets = DgrInvoiceItems.SelectedItems.OfType<InvoiceItem>().ToList();
                if (targets.Count == 0) targets = _editingItems.ToList();

                foreach (var item in targets)
                {
                    item.DiscountPercent = discount;
                }
                DgrInvoiceItems.Items.Refresh();
                RecalculateEditorSummaries();
                SetDirty(true);
                SetStatus($"تخفیف {discount}٪ به {targets.Count} سطر پیش‌فاکتور اعمال شد.");
            }
            else
            {
                MessageBox.Show("لطفاً یک درصد تخفیف معتبر وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnApplyBulkProfit_Click(object sender, RoutedEventArgs e)
        {
            string txt = TxtBulkProfitPercent?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal profit) && profit >= 0)
            {
                if (_editingItems.Count == 0)
                {
                    MessageBox.Show("هیچ سطری در پیش‌فاکتور برای اعمال سود وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var targets = DgrInvoiceItems.SelectedItems.OfType<InvoiceItem>().ToList();
                if (targets.Count == 0) targets = _editingItems.ToList();

                foreach (var item in targets)
                {
                    item.ProfitPercent = profit;
                }
                DgrInvoiceItems.Items.Refresh();
                RecalculateEditorSummaries();
                SetDirty(true);
                SetStatus($"سود {profit}٪ به {targets.Count} سطر پیش‌فاکتور اعمال شد.");
            }
            else
            {
                MessageBox.Show("لطفاً یک درصد سود معتبر وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnApplyBulkBoth_Click(object sender, RoutedEventArgs e)
        {
            if (_editingItems.Count == 0)
            {
                MessageBox.Show("هیچ سطری در پیش‌فاکتور وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string dTxt = TxtBulkDiscountPercent?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";
            string pTxt = TxtBulkProfitPercent?.Text?.Replace("٪", "").Replace("%", "").Trim() ?? "";

            bool hasDiscount = decimal.TryParse(dTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal discount) && discount >= 0;
            bool hasProfit = decimal.TryParse(pTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal profit) && profit >= 0;

            if (!hasDiscount && !hasProfit)
            {
                MessageBox.Show("لطفاً حداقل درصد تخفیف یا درصد سود را وارد نمایید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = DgrInvoiceItems.SelectedItems.OfType<InvoiceItem>().ToList();
            if (targets.Count == 0) targets = _editingItems.ToList();

            foreach (var item in targets)
            {
                if (hasDiscount) item.DiscountPercent = discount;
                if (hasProfit) item.ProfitPercent = profit;
            }

            DgrInvoiceItems.Items.Refresh();
            RecalculateEditorSummaries();
            SetDirty(true);
            SetStatus($"تنظیم یکجای تخفیف و سود روی {targets.Count} سطر پیش‌فاکتور اعمال گردید.");
        }

        private void BtnApplyBulk1Point1_Click(object sender, RoutedEventArgs e)
        {
            if (_editingItems.Count == 0)
            {
                MessageBox.Show("هیچ سطری در پیش‌فاکتور وجود ندارد.", "توجه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targets = DgrInvoiceItems.SelectedItems.OfType<InvoiceItem>().ToList();
            if (targets.Count == 0) targets = _editingItems.ToList();

            foreach (var item in targets)
            {
                item.IsDividedBy1Point1 = true;
            }
            DgrInvoiceItems.Items.Refresh();
            RecalculateEditorSummaries();
            SetDirty(true);
            SetStatus($"فرمول تقسیم بر ۱.۱ به {targets.Count} سطر پیش‌فاکتور اعمال شد.");
        }

        #region پیشنهادات هوشمند جستجوی کالا در جدول اقلام

        private void TxtGridItemName_Loaded(object sender, RoutedEventArgs e)
        {
            _currentGridTextBox = sender as Wpf.Ui.Controls.TextBox;
            if (_currentGridTextBox != null)
            {
                _currentGridTextBox.Focus();
                _currentGridTextBox.SelectAll();
            }
        }

        private void TxtGridItemName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox tb)
            {
                _currentGridTextBox = tb;
                string q = tb.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(q))
                {
                    if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                    return;
                }

                var matching = _storage.GetStorageItems()
                    .Where(x => (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Code) && x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(x.Category) && x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(25)
                    .ToList();

                if (matching.Any() && PopupGridSuggestions != null && LstGridSuggestions != null)
                {
                    LstGridSuggestions.ItemsSource = matching;
                    PopupGridSuggestions.PlacementTarget = tb;
                    PopupGridSuggestions.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    PopupGridSuggestions.IsOpen = true;
                }
                else
                {
                    if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                }
            }
        }

        private void TxtGridItemName_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (PopupGridSuggestions != null && PopupGridSuggestions.IsOpen)
            {
                if (e.Key == Key.Down)
                {
                    LstGridSuggestions?.Focus();
                    if (LstGridSuggestions != null && LstGridSuggestions.Items.Count > 0)
                    {
                        LstGridSuggestions.SelectedIndex = 0;
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    PopupGridSuggestions.IsOpen = false;
                    e.Handled = true;
                }
            }
        }

        private void LstGridSuggestions_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ApplyGridSuggestion();
        }

        private void LstGridSuggestions_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyGridSuggestion();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                _currentGridTextBox?.Focus();
                e.Handled = true;
            }
        }

        private void ApplyGridSuggestion()
        {
            if (LstGridSuggestions?.SelectedItem is StorageItem item && _currentGridTextBox != null)
            {
                if (_currentGridTextBox.DataContext is InvoiceItem invoiceItem)
                {
                    invoiceItem.ItemName = item.Name;
                    if (!string.IsNullOrEmpty(item.Unit)) invoiceItem.Unit = item.Unit;
                    if (item.DefaultPrice > 0) invoiceItem.RawPrice = item.DefaultPrice;
                    RecalculateEditorSummaries();
                    SetDirty(true);
                }
                else
                {
                    _currentGridTextBox.Text = item.Name;
                }
                if (PopupGridSuggestions != null) PopupGridSuggestions.IsOpen = false;
                LstGridSuggestions.SelectedItem = null;
                DgrInvoiceItems.CommitEdit(DataGridEditingUnit.Row, true);
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

        private void BtnSaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند و به صفحه فاکتورها پرتاب نمی‌شود
                SetStatus($"پیش‌فاکتور شماره {_currentEditingInvoice!.InvoiceNumber} با موفقیت ذخیره شد.");
            }
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
            if (SaveCurrentInvoiceFromEditor())
            {
                if (_currentEditingInvoice != null)
                {
                    try
                    {
                        _printService.PrintOrExportToPdf(_currentEditingInvoice);
                        SetStatus($"پیش‌فاکتور {_currentEditingInvoice.InvoiceNumber} تأیید و دستور چاپ ارسال گردید.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"خطا در چاپ پیش‌فاکتور:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                // کاربر در ادیتور پیش‌فاکتور باقی می‌ماند
            }
        }

        private void TxtQuickItemName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = TxtQuickItemName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q))
            {
                PopupQuickSuggestions.IsOpen = false;
                return;
            }

            var matching = _storage.GetStorageItems()
                .Where(x => (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(x.Code) && x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(x.Category) && x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Take(25)
                .ToList();

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
            if (_isDirty && _currentEditingInvoice != null)
            {
                SaveDraftSilently();
                SetStatus("پیش‌فاکتور به صورت «پیش‌نویس» ذخیره گردید.");
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
            TxtExpertRole.Text = "کارشناس فروش";
            TxtExpertPrefix.Text = "EXP-";
            TxtExpertNextNum.Text = "1001";
            TxtExpertPhone.Text = "";
            TxtExpertEmail.Text = "";
            if (ChkExpertAllCompanies != null) ChkExpertAllCompanies.IsChecked = true;
            if (LstExpertCompanies != null)
            {
                LstExpertCompanies.ItemsSource = _storage.GetCompanies().OrderBy(c => c.Name).ToList();
                LstExpertCompanies.SelectedItems.Clear();
                LstExpertCompanies.IsEnabled = false;
            }
            UpdateExpertPreview();
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
            TxtExpertPrefix.Text = expert.InvoicePrefix;
            TxtExpertNextNum.Text = expert.NextInvoiceNumber.ToString();
            TxtExpertPhone.Text = expert.PhoneNumber;
            TxtExpertEmail.Text = expert.Email;
            UpdateExpertPreview();

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

        private void TxtExpertNumbering_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateExpertPreview();
        }

        private void UpdateExpertPreview()
        {
            string prefix = TxtExpertPrefix?.Text?.Trim() ?? "";
            string numStr = TxtExpertNextNum?.Text?.Trim() ?? "1";
            if (TxtExpertNumberPreview != null)
            {
                TxtExpertNumberPreview.Text = $"پیش‌نمایش: {prefix}{numStr}";
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

            int.TryParse(TxtExpertNextNum.Text?.Trim() ?? "1001", out int nextNum);

            var expert = _currentEditingExpert ?? new Expert();
            expert.FullName = fullName;
            expert.RoleTitle = TxtExpertRole.Text?.Trim() ?? "کارشناس فروش";
            expert.InvoicePrefix = TxtExpertPrefix.Text?.Trim() ?? "EXP-";
            expert.NextInvoiceNumber = nextNum > 0 ? nextNum : 1;
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

        private void BtnDeleteStorageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is StorageItem item)
            {
                var res = MessageBox.Show($"آیا از حذف کالای «{item.Name}» از انبار مطمئن هستید؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _storage.DeleteStorageItem(item.Id);
                    RefreshStorageList();
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

            string priceText = TxtStoragePrice.Text?.Replace(",", "").Trim() ?? "0";
            if (decimal.TryParse(priceText, out decimal price))
            {
                item.DefaultPrice = price;
            }

            _storage.AddOrUpdateStorageItem(item);
            RefreshStorageList();
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

        private bool _isLoadingSettings = false;

        private void LoadSettingsTab()
        {
            _isLoadingSettings = true;
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
                if (TxtCompanyStampPath != null)
                {
                    TxtCompanyStampPath.Text = settings.CompanyStampPath;
                    LoadImagePreview(settings.CompanyStampPath, ImgStampPreview);
                }

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

                if (DgrBankAccounts != null)
                {
                    _settingsBankAccounts = new ObservableCollection<BankAccountInfo>(settings.BankAccounts ?? new List<BankAccountInfo>());
                    DgrBankAccounts.ItemsSource = _settingsBankAccounts;
                }
            }
            finally
            {
                _isLoadingSettings = false;
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

            SetStatus("حساب بانکی به لیست افزوده شد.");
        }

        private void BtnDeleteBankAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is BankAccountInfo acc)
            {
                _settingsBankAccounts.Remove(acc);
                SetStatus("حساب بانکی از لیست حذف شد.");
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
            _storage.Save();

            SetStatus("مشخصات فروشنده با موفقیت ذخیره شد.");
            MessageBox.Show("مشخصات شرکت فروشنده با موفقیت ذخیره شد.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void BtnSaveInvoiceAppearance_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            settings.CompanyLogoPath = TxtCompanyLogoPath.Text?.Trim() ?? "";
            settings.CompanyStampPath = TxtCompanyStampPath.Text?.Trim() ?? "";

            string selectedFontName = CmbFontCompanyName.SelectedItem?.ToString() ?? CmbFontCompanyName.Text;
            settings.CompanyNameFontFamily = !string.IsNullOrWhiteSpace(selectedFontName) ? selectedFontName.Trim() : "Vazirmatn";
            if (double.TryParse(TxtFontSizeCompanyName.Text?.Trim(), out double nameSize) && nameSize > 6)
            {
                settings.CompanyNameFontSize = nameSize;
            }

            string selectedFontTbl = CmbFontTable.SelectedItem?.ToString() ?? CmbFontTable.Text;
            settings.TableFontFamily = !string.IsNullOrWhiteSpace(selectedFontTbl) ? selectedFontTbl.Trim() : "Vazirmatn";
            if (double.TryParse(TxtFontSizeTable.Text?.Trim(), out double tblSize) && tblSize > 6)
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

            _storage.Save();
            UpdateSettingsPdfPreview();
            SetStatus("تنظیمات سربرگ، تصویر، مهر و فونت‌ها با موفقیت ذخیره شد.");
            MessageBox.Show("تنظیمات سربرگ، تصویر شرکت، مهر و فونت‌ها با موفقیت ذخیره گردید.", "موفقیت", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSettingsControlChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            UpdateSettingsPdfPreview();
        }

        private void OnSettingsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            UpdateSettingsPdfPreview();
        }

        private void BtnRefreshSettingsPdf_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsPdfPreview();
            SetStatus("پیش‌نمایش سند رسمی با موفقیت تازه‌سازی شد.");
        }

        public void UpdateSettingsPdfPreview()
        {
            if (SettingsPdfViewer == null) return;

            try
            {
                var previewSettings = new AppSettings
                {
                    SellerName = !string.IsNullOrWhiteSpace(TxtSellerName?.Text) ? TxtSellerName.Text.Trim() : "شرکت آریان نیرو گستر اترک",
                    SellerRegistrationNo = !string.IsNullOrWhiteSpace(TxtSellerRegistrationNo?.Text) ? TxtSellerRegistrationNo.Text.Trim() : "۸۸۶۵۴",
                    SellerNationalCode = TxtSellerNatCode?.Text?.Trim() ?? "14008765432",
                    SellerEconomicCode = TxtSellerEcoCode?.Text?.Trim() ?? "411567891234",
                    SellerPhone = !string.IsNullOrWhiteSpace(TxtSellerPhone?.Text) ? TxtSellerPhone.Text.Trim() : "۰۵۱۳۷۱۳۲۰۸۴",
                    SellerAddress = !string.IsNullOrWhiteSpace(TxtSellerAddress?.Text) ? TxtSellerAddress.Text.Trim() : "مشهد - خیابان سنایی",
                    CompanyLogoPath = TxtCompanyLogoPath?.Text?.Trim() ?? "",
                    CompanyStampPath = TxtCompanyStampPath?.Text?.Trim() ?? "",
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

                if (double.TryParse(TxtFontSizeCompanyName?.Text?.Trim(), out double cSize) && cSize > 6)
                    previewSettings.CompanyNameFontSize = cSize;
                if (double.TryParse(TxtFontSizeTable?.Text?.Trim(), out double tSize) && tSize > 6)
                    previewSettings.TableFontSize = tSize;
                if (decimal.TryParse(TxtVatPercent?.Text?.Trim(), out decimal vat) && vat >= 0)
                    previewSettings.VatPercent = vat;

                var sampleInv = new ProformaInvoice
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

                var doc = _printService.CreateInvoiceFlowDocument(sampleInv, previewSettings);
                SettingsPdfViewer.Document = doc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Preview error: " + ex);
            }
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

            if (double.TryParse(TxtFontSizeCompanyName?.Text?.Trim(), out double cSize) && cSize > 6)
                previewSettings.CompanyNameFontSize = cSize;
            if (double.TryParse(TxtFontSizeTable?.Text?.Trim(), out double tSize) && tSize > 6)
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
                var doc = _printService.CreateInvoiceFlowDocument(sampleInv, previewSettings);
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator, "پیش‌فاکتور نمونه رسمی");
                    SetStatus("پیش‌نمایش چاپ سند رسمی با تنظیمات جاری با موفقیت ارسال گردید.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در پیش‌نمایش چاپ:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        private void SetStatus(string message)
        {
            TxtStatusMessage.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }
    }
}
