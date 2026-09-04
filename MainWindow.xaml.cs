using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

        // وضعیت فعلی شرکت و کارشناس در حال ویرایش
        private Company? _currentEditingCompany = null;
        private Expert? _currentEditingExpert = null;

        // فیلتر وضعیت چیپ‌ها
        private InvoiceStatus? _selectedFilterStatus = null;
        private bool _isNewInvoice = false;
        private bool _isDirty = false;

        public MainWindow()
        {
            InitializeComponent();

            _storage = new StorageService();
            _excelService = new ExcelExportService(_storage);
            _printService = new PrintPdfService(_storage);

            Loaded += MainWindow_Loaded;
        }

        private void SetDirty(bool dirty)
        {
            _isDirty = dirty;
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            const string baseTitle = "تـراز - سامانه جامع صدور و مدیریت پیش‌فاکتور";
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
            SwitchToView("Invoices");
        }

        private void InitCombos()
        {
            CmbEditorStatus.Items.Clear();
            foreach (InvoiceStatus s in Enum.GetValues(typeof(InvoiceStatus)))
            {
                CmbEditorStatus.Items.Add(s.ToPersianString());
            }
            CmbEditorStatus.SelectedIndex = 0;
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
                if (tag == "Editor" && _currentEditingInvoice == null)
                {
                    BtnNewInvoice_Click(sender, e);
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

            // بررسی تغییرات ذخیره‌نشده در صورت خروج از صفحه ویرایشگر
            if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible && _isDirty && viewName != "Editor")
            {
                var confirm = MessageBox.Show(
                    "تغییرات ذخیره‌نشده‌ای در پیش‌فاکتور جاری وجود دارد.\nآیا مایل به ذخیره تغییرات قبل از خروج از این صفحه هستید؟",
                    "تغییرات ذخیره‌نشده",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    if (!SaveCurrentInvoiceFromEditor())
                    {
                        return; // خطا در اعتبارسنجی؛ جلوگیری از خروج
                    }
                }
                else if (confirm == MessageBoxResult.No)
                {
                    SetDirty(false); // انصراف و نادیده گرفتن تغییرات
                }
                else
                {
                    return; // انصراف از تغییر صفحه
                }
            }

            if (ViewInvoices != null) ViewInvoices.Visibility = Visibility.Collapsed;
            if (ViewInvoiceEditor != null) ViewInvoiceEditor.Visibility = Visibility.Collapsed;
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
                    if (ViewInvoiceEditor != null) ViewInvoiceEditor.Visibility = Visibility.Visible;
                    TxtPageTitle.Text = _isNewInvoice ? "صدور پیش‌فاکتور جدید" : $"ویرایش پیش‌فاکتور {_currentEditingInvoice?.InvoiceNumber}";
                    TxtPageSubtitle.Text = "محاسبات تخفیف و فرمول ۱.۱ به صورت خودکار محاسبه و خلاصه مالی بروزرسانی می‌شود";
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
            var approvedInvoices = invoices.Where(i => i.Status == InvoiceStatus.Approved).ToList();
            decimal approvedAmount = approvedInvoices.Sum(i => i.GrandTotal);
            int pendingCount = invoices.Count(i => i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Sent);

            if (TxtKpiTotalAmount != null) TxtKpiTotalAmount.Text = $"{totalAmount:N0} تومان";
            if (TxtKpiTotalCount != null) TxtKpiTotalCount.Text = $"تعداد: {invoices.Count} فاکتور صادرشده";

            if (TxtKpiApprovedAmount != null) TxtKpiApprovedAmount.Text = $"{approvedAmount:N0} تومان";
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
            else if (sender == ChipStatusDraft) _selectedFilterStatus = InvoiceStatus.Draft;
            else if (sender == ChipStatusSent) _selectedFilterStatus = InvoiceStatus.Sent;
            else if (sender == ChipStatusPending) _selectedFilterStatus = InvoiceStatus.Pending;
            else if (sender == ChipStatusApproved) _selectedFilterStatus = InvoiceStatus.Approved;
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

        private void BtnRefreshInvoices_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchInvoice.Text = "";
            ChipStatusAll.IsChecked = true;
            if (CmbFilterExpert.Items.Count > 0) CmbFilterExpert.SelectedIndex = 0;
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

        private void ExportInvoiceToExcel(ProformaInvoice invoice)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"پیش‌فاکتور_{invoice.InvoiceNumber}_{invoice.CompanyName}.xlsx"
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
                int next = ((int)invoice.Status + 1) % 5;
                invoice.Status = (InvoiceStatus)next;
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

        private void ChkAdvancedPricing_Toggle(object sender, RoutedEventArgs e)
        {
            bool isAdvanced = ChkAdvancedPricing?.IsChecked == true;
            var vis = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
            if (ColRawPrice != null) ColRawPrice.Visibility = vis;
            if (ColDiscountPercent != null) ColDiscountPercent.Visibility = vis;
            if (ColDiscountAmount != null) ColDiscountAmount.Visibility = vis;
            if (Col1Point1 != null) Col1Point1.Visibility = vis;
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
            // Esc: بازگشت / انصراف
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (ViewInvoiceEditor != null && ViewInvoiceEditor.Visibility == Visibility.Visible)
                {
                    BtnCancelEditor_Click(this, new RoutedEventArgs());
                    e.Handled = true;
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
                Notes = _storage.GetSettings().DefaultTerms,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        RowNumber = 1,
                        ItemName = "",
                        Quantity = 1,
                        RawPrice = 0,
                        DiscountPercent = 0,
                        IsDividedBy1Point1 = false
                    }
                }
            };

            OpenInvoiceEditor(newInvoice, isNew: true);
        }

        private void OpenInvoiceEditor(ProformaInvoice invoice, bool isNew)
        {
            _currentEditingInvoice = invoice;
            _isNewInvoice = isNew;

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
            TxtEditorNotes.Text = invoice.Notes;

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
                e.PropertyName == nameof(InvoiceItem.Quantity) ||
                e.PropertyName == nameof(InvoiceItem.DiscountAmount))
            {
                RecalculateEditorSummaries();
            }
            SetDirty(true);
        }

        private void RecalculateEditorSummaries()
        {
            decimal rawTotal = _editingItems.Sum(i => i.Quantity * i.RawPrice);
            decimal discountTotal = _editingItems.Sum(i => i.Quantity * i.DiscountAmount);
            decimal grandTotal = _editingItems.Sum(i => i.TotalPrice);

            TxtSummaryRawTotal.Text = $"{rawTotal:N0} تومان";
            TxtSummaryDiscountTotal.Text = $"{discountTotal:N0} تومان";
            TxtSummaryGrandTotal.Text = $"{grandTotal:N0} تومان";
            TxtSummaryGrandTotalWords.Text = JalaliDateHelper.NumberToWords((long)grandTotal) + " تومان";
        }

        private void CmbEditorCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEditorCompany.SelectedItem is Company company)
            {
                TxtEditorCompanyPhone.Text = company.Phone;
                TxtEditorCompanyAddress.Text = company.Address;
                TxtEditorCompanyEcoCode.Text = !string.IsNullOrEmpty(company.EconomicCode) ? company.EconomicCode : company.NationalCode;
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
                DiscountPercent = 0,
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
            _currentEditingInvoice.Notes = TxtEditorNotes.Text?.Trim() ?? "";

            _currentEditingInvoice.Items = _editingItems.ToList();
            _currentEditingInvoice.UpdatedAt = DateTime.Now;

            if (_isNewInvoice)
            {
                if (_currentEditingInvoice.InvoiceNumber == expert.GetPreviewNextInvoiceNumber())
                {
                    expert.ConsumeNextInvoiceNumber();
                    _storage.AddOrUpdateExpert(expert);
                }
            }

            _storage.AddOrUpdateInvoice(_currentEditingInvoice);
            _isNewInvoice = false;
            SetDirty(false);

            RefreshAll();
            SetStatus($"پیش‌فاکتور شماره {_currentEditingInvoice.InvoiceNumber} با موفقیت ذخیره شد.");
            return true;
        }

        private void BtnSaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                SwitchToView("Invoices");
            }
        }

        private void BtnSaveAndExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                ExportInvoiceToExcel(_currentEditingInvoice!);
                SwitchToView("Invoices");
            }
        }

        private void BtnSaveAndPrintPdf_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentInvoiceFromEditor())
            {
                _printService.PrintOrExportToPdf(_currentEditingInvoice!);
                SwitchToView("Invoices");
            }
        }

        private void BtnCancelEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty)
            {
                var confirm = MessageBox.Show(
                    "آیا از لغو ویرایش و نادیده گرفتن تغییرات ذخیره‌نشده اطمینان دارید؟",
                    "تأیید انصراف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
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
        }

        private void BtnNewCompany_Click(object sender, RoutedEventArgs e)
        {
            _currentEditingCompany = null;
            TxtCompanyFormTitle.Text = "ثبت شرکت جدید";
            TxtCompanyName.Text = "";
            TxtCompanyEconomicCode.Text = "";
            TxtCompanyNationalCode.Text = "";
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
            TxtCompanyFormTitle.Text = $"ویرایش اطلاعات: {company.Name}";
            TxtCompanyName.Text = company.Name;
            TxtCompanyEconomicCode.Text = company.EconomicCode;
            TxtCompanyNationalCode.Text = company.NationalCode;
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
                MessageBox.Show("نام شرکت الزامی است.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var company = _currentEditingCompany ?? new Company();
            company.Name = name;
            company.EconomicCode = TxtCompanyEconomicCode.Text?.Trim() ?? "";
            company.NationalCode = TxtCompanyNationalCode.Text?.Trim() ?? "";
            company.RegistrationNo = TxtCompanyRegistrationNo.Text?.Trim() ?? "";
            company.Phone = TxtCompanyPhone.Text?.Trim() ?? "";
            company.Address = TxtCompanyAddress.Text?.Trim() ?? "";
            company.Description = TxtCompanyDescription.Text?.Trim() ?? "";

            _storage.AddOrUpdateCompany(company);
            RefreshAll();
            BtnNewCompany_Click(sender, e);
            SetStatus($"اطلاعات شرکت {company.Name} با موفقیت ذخیره شد.");
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
            UpdateExpertPreview();
            TxtExpertFullName.Focus();
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

        #region تب قالب اکسل و تنظیمات

        private void LoadSettingsTab()
        {
            var settings = _storage.GetSettings();
            if (TxtTemplatePath == null) return;

            TxtTemplatePath.Text = !string.IsNullOrEmpty(settings.CustomTemplatePath) ? settings.CustomTemplatePath : "(قالب استاندارد و پیش‌فرض برنامه فعال است)";
            TxtSellerName.Text = settings.SellerName;
            TxtSellerNatCode.Text = settings.SellerNationalCode;
            TxtSellerEcoCode.Text = settings.SellerEconomicCode;
            TxtSellerPhone.Text = settings.SellerPhone;
            TxtSellerAddress.Text = settings.SellerAddress;
        }

        private void BtnBrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Title = "انتخاب فایل قالب اکسل پیش‌فاکتور"
            };

            if (ofd.ShowDialog() == true)
            {
                var settings = _storage.GetSettings();
                settings.CustomTemplatePath = ofd.FileName;
                _storage.Save();
                TxtTemplatePath.Text = ofd.FileName;
                SetStatus($"قالب سفارشی اکسل انتخاب شد: {Path.GetFileName(ofd.FileName)}");
            }
        }

        private void BtnClearTemplate_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            settings.CustomTemplatePath = "";
            _storage.Save();
            TxtTemplatePath.Text = "(قالب استاندارد و پیش‌فرض برنامه فعال است)";
            SetStatus("قالب سفارشی غیرفعال شد و به قالب استاندارد بازگردانده شد.");
        }

        private void BtnCreateSampleTemplate_Click(object sender, RoutedEventArgs e)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string targetPath = Path.Combine(desktopPath, "قالب_نمونه_پیش‌فاکتور_تاراز.xlsx");

            try
            {
                _excelService.GenerateSampleTemplateFile(targetPath);
                SetStatus($"فایل نمونه قالب در دسکتاپ ایجاد شد: {targetPath}");

                var res = MessageBox.Show(
                    $"فایل نمونه قالب اکسل با تمام تگ‌ها در دسکتاپ شما ایجاد شد:\n{targetPath}\n\nآیا مایلید هم‌اکنون این فایل را به عنوان قالب فعال سیستم انتخاب نمایید؟",
                    "ایجاد قالب نمونه",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    var settings = _storage.GetSettings();
                    settings.CustomTemplatePath = targetPath;
                    _storage.Save();
                    TxtTemplatePath.Text = targetPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در ایجاد فایل قالب:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveSellerSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _storage.GetSettings();
            settings.SellerName = TxtSellerName.Text?.Trim() ?? "";
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

        #endregion

        private void SetStatus(string message)
        {
            TxtStatusMessage.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }
    }
}