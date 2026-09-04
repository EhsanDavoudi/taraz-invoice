# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Primary users are sales experts, commercial department staff, and sales managers in Iranian B2B companies who prepare, calculate, track, and issue commercial proforma invoices (پیش‌فاکتور) for corporate clients and counterparties.

## Product Purpose

تـراز (سامانه جامع صدور و مدیریت پیش‌فاکتور) provides a 100% offline, native, lightning-fast desktop management system for business proforma invoicing, customer account management, and multi-expert sales tracking, ensuring reliable calculations and professional documents without reliance on internet connectivity or cloud servers.

## Positioning

Unlike generic spreadsheet templates or bulky cloud ERPs, Taraz Invoice combines dedicated per-expert sales numbering, specialized Iranian commercial pricing logic (inverse 1.1 VAT base price calculation and item discounts), and confidential customer-facing export filtering (PDF/Excel) in a standalone, local-first Windows application.

## Operating Context

- Used in daily commercial sales workflows, quoting goods and services to prospective and existing business clients.
- Operates in Iranian business environments where Jalali (Shamsi) calendar dates, right-to-left (RTL) reading order, and Persian number-to-words currency conversions are essential.
- Fully localized in Persian with seamless support for official company identifiers (شناسه ملی، کد اقتصادی، شماره ثبت).
- Desktop workspace environment: fast keyboard navigation, data entry grids, instant calculation updates, and direct printing or document generation.

## Capabilities and Constraints

- **Architecture / Framework**: 100% offline Windows desktop application built with .NET 9 WPF (C# and XAML).
- **Per-Expert Numbering**: Independent sequential counters and customizable prefixes (e.g., `EXP-REZ-`, `EXP-MOH-`) allocated to individual sales experts for transparent attribution.
- **Pricing & Calculation Engine**:
  - Real-time calculations: Unit Price × Quantity, discount percentages, and unit discounts.
  - Inverse 1.1 VAT formula toggle (Base = Price / 1.1) tailored to Iranian market valuation practices.
  - Final customer-facing sell price and row totals dynamically updated.
- **Confidential Exports**: Customer-facing PDF and Excel exports strictly conceal internal cost formulas, raw prices, and discount percentages, revealing only row numbers, descriptions, quantities, final selling unit price, and row/grand totals.
- **Excel Template Engine**: Opens existing `.xlsx` company templates, preserves existing layout, logos, fonts, and styling, and fills placeholders (`{{INVOICE_NO}}`, `{{DATE}}`, `{{ITEMS}}`, etc.).
- **Local Storage & Backup**: Complete data persistence in local `Data/database.json` with instant single-click backup and restore, plus duplicate-invoice functionality.
- **Invoice Status Lifecycle**: 5 explicit states with visual color cues: Draft (پیش‌نویس), Sent (ارسال‌شده), Pending Review (در انتظار تأیید), Approved (تأیید شده), and Canceled (لغو / باطل شده).

## Brand Commitments

- **Name**: تـراز (سامانه جامع صدور و مدیریت پیش‌فاکتور).
- **Language**: Persian (Farsi), Right-to-Left (RTL) layout throughout.
- **Aesthetic Tone**: Clean, serious, reliable commercial software interface; high visual contrast, clear typographic hierarchy for Persian text (Vazirmatn / Segoe UI), and zero clutter.

## Evidence on Hand

- Native WPF implementation in `d:\Code\atrak-excel` (`MainWindow.xaml`, `Models/`, `Services/`).
- Local database storage engine in `Services/StorageService.cs` and `Data/database.json`.
- Comprehensive calculation logic implemented and verified in `Models/InvoiceItem.cs` and `TarazInvoice.Tests`.
- Production-ready Excel templating and PDF printing services (`Services/ExcelExportService.cs`, `Services/PrintPdfService.cs`).
- Documented feature set and usage instructions in `README.md`.

## Product Principles

1. **Local-First & Dependable**: 100% offline autonomy; never block the user on network access or remote server availability.
2. **Confidentiality by Default**: Internal pricing calculations and margin adjustments must never leak into customer deliverables.
3. **Frictionless Commercial Flow**: Fast data entry, automatic number incrementing per expert, and instant PDF/Excel generation.
4. **Cultural & Financial Accuracy**: Native Persian RTL layout, accurate Jalali calendar integration, and error-free Persian number-to-words currency output.

## Accessibility & Inclusion

- Native RTL (Right-to-Left) flow for all UI components, dialogs, tables, and document exports.
- High legibility Persian typography with scalable font sizing and clear contrast for long-duration office work.
- Full keyboard navigability across tabular data entry forms.
