using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarazInvoice.Models
{
    public class InvoiceItem : INotifyPropertyChanged
    {
        private int _rowNumber = 1;
        private string _itemName = string.Empty;
        private string _unit = "عدد";
        private decimal _quantity = 1;
        private decimal _rawPrice = 0;
        private decimal? _discountPercent = null;
        private decimal _discountAmount = 0;
        private bool _isDividedBy1Point1 = false;
        private decimal? _profitPercent = null;
        private decimal _profitAmount = 0;
        private decimal _finalUnitPrice = 0;
        private decimal _totalPrice = 0;
        private decimal _totalProfit = 0;
        private string _description = string.Empty;

        private bool _isCalculating = false;

        public int RowNumber
        {
            get => _rowNumber;
            set { if (_rowNumber != value) { _rowNumber = value; OnPropertyChanged(); } }
        }

        public string ItemName
        {
            get => _itemName;
            set { if (_itemName != value) { _itemName = value; OnPropertyChanged(); } }
        }

        public string Unit
        {
            get => _unit;
            set { if (_unit != value) { _unit = value; OnPropertyChanged(); } }
        }

        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
                    _totalProfit = Math.Round(_quantity * _profitAmount, 0);
                    OnPropertyChanged(nameof(TotalPrice));
                    OnPropertyChanged(nameof(TotalProfit));
                }
            }
        }

        public decimal RawPrice
        {
            get => _rawPrice;
            set
            {
                if (_rawPrice != value)
                {
                    _rawPrice = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromPercent();
                    }
                }
            }
        }

        public decimal? DiscountPercent
        {
            get => _discountPercent;
            set
            {
                if (_discountPercent != value)
                {
                    _discountPercent = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromPercent();
                    }
                }
            }
        }

        public decimal DiscountAmount
        {
            get => _discountAmount;
            set
            {
                if (_discountAmount != value)
                {
                    _discountAmount = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromAmount();
                    }
                }
            }
        }

        public bool IsDividedBy1Point1
        {
            get => _isDividedBy1Point1;
            set
            {
                if (_isDividedBy1Point1 != value)
                {
                    _isDividedBy1Point1 = value;
                    OnPropertyChanged();
                    RecalculateTotals();
                }
            }
        }

        public decimal? ProfitPercent
        {
            get => _profitPercent;
            set
            {
                if (_profitPercent != value)
                {
                    _profitPercent = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromProfitPercent();
                    }
                }
            }
        }

        public decimal ProfitAmount
        {
            get => _profitAmount;
            set
            {
                if (_profitAmount != value)
                {
                    _profitAmount = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromProfitAmount();
                    }
                }
            }
        }

        public decimal FinalUnitPrice
        {
            get => _finalUnitPrice;
            set
            {
                decimal rounded = RoundUpPrice(value);
                if (_finalUnitPrice != rounded)
                {
                    _finalUnitPrice = rounded;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromFinalUnitPrice();
                    }
                    else
                    {
                        _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
                        OnPropertyChanged(nameof(TotalPrice));
                    }
                }
            }
        }

        public decimal TotalPrice
        {
            get => _totalPrice;
            private set
            {
                if (_totalPrice != value)
                {
                    _totalPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal TotalProfit
        {
            get => _totalProfit;
            private set
            {
                if (_totalProfit != value)
                {
                    _totalProfit = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// قیمت پایه و تمام شده پس از اعمال تخفیف و/یا تقسیم بر ۱.۱ (قیمت خرید)
        /// </summary>
        public decimal BaseCost
        {
            get
            {
                decimal priceAfterDiscount = _rawPrice - _discountAmount;
                if (priceAfterDiscount < 0) priceAfterDiscount = 0;

                if (_isDividedBy1Point1)
                {
                    return Math.Round(priceAfterDiscount / 1.1m, 0);
                }
                return Math.Round(priceAfterDiscount, 0);
            }
            set
            {
                if (value < 0) value = 0;
                _isCalculating = true;
                try
                {
                    decimal targetAfterDiscount = _isDividedBy1Point1 ? Math.Round(value * 1.1m, 0) : value;
                    if (_rawPrice <= 0)
                    {
                        _rawPrice = targetAfterDiscount;
                        _discountAmount = 0;
                        _discountPercent = 0;
                    }
                    else
                    {
                        if (targetAfterDiscount <= _rawPrice)
                        {
                            _discountAmount = _rawPrice - targetAfterDiscount;
                            _discountPercent = Math.Round((_discountAmount / _rawPrice) * 100m, 2);
                        }
                        else
                        {
                            _rawPrice = targetAfterDiscount;
                            _discountAmount = 0;
                            _discountPercent = 0;
                        }
                    }
                    OnPropertyChanged(nameof(RawPrice));
                    OnPropertyChanged(nameof(DiscountAmount));
                    OnPropertyChanged(nameof(DiscountPercent));
                    OnPropertyChanged(nameof(BaseCost));
                    RecalculateTotalsInternal();
                }
                finally
                {
                    _isCalculating = false;
                }
            }
        }

        public void UpdateFromPercent()
        {
            _isCalculating = true;
            try
            {
                decimal pct = _discountPercent ?? 0m;
                _discountAmount = Math.Round(_rawPrice * (pct / 100m), 0);
                OnPropertyChanged(nameof(DiscountAmount));
                RecalculateTotalsInternal();
            }
            finally
            {
                _isCalculating = false;
            }
        }

        public void UpdateFromAmount()
        {
            _isCalculating = true;
            try
            {
                if (_rawPrice > 0)
                {
                    _discountPercent = Math.Round((_discountAmount / _rawPrice) * 100m, 2);
                }
                else
                {
                    _discountPercent = 0;
                }
                OnPropertyChanged(nameof(DiscountPercent));
                RecalculateTotalsInternal();
            }
            finally
            {
                _isCalculating = false;
            }
        }

        /// <summary>
        /// گرد کردن به بالا (Ceiling): اگر ۴ رقمی باشد با تقریب ۱۰۰ (۲ رقم) و اگر بیشتر از ۴ رقم باشد با تقریب ۱۰۰۰ (۳ رقم)
        /// </summary>
        public static decimal RoundUpPrice(decimal value)
        {
            if (value <= 0) return 0;

            decimal intPart = Math.Floor(value);
            if (intPart >= 1000m && intPart <= 9999m)
            {
                return Math.Ceiling(value / 100m) * 100m;
            }
            else if (intPart >= 10000m)
            {
                return Math.Ceiling(value / 1000m) * 1000m;
            }
            else
            {
                return Math.Ceiling(value);
            }
        }

        public void UpdateFromProfitPercent()
        {
            _isCalculating = true;
            try
            {
                decimal baseCost = BaseCost;
                decimal pct = _profitPercent ?? 0m;
                _profitAmount = Math.Round(baseCost * (pct / 100m), 0);
                _finalUnitPrice = RoundUpPrice(baseCost + _profitAmount);
                _profitAmount = _finalUnitPrice - baseCost;
                _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
                _totalProfit = Math.Round(_quantity * _profitAmount, 0);

                OnPropertyChanged(nameof(BaseCost));
                OnPropertyChanged(nameof(ProfitAmount));
                OnPropertyChanged(nameof(FinalUnitPrice));
                OnPropertyChanged(nameof(TotalPrice));
                OnPropertyChanged(nameof(TotalProfit));
            }
            finally
            {
                _isCalculating = false;
            }
        }

        public void UpdateFromProfitAmount()
        {
            _isCalculating = true;
            try
            {
                decimal baseCost = BaseCost;
                _finalUnitPrice = RoundUpPrice(baseCost + _profitAmount);
                _profitAmount = _finalUnitPrice - baseCost;
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
                _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
                _totalProfit = Math.Round(_quantity * _profitAmount, 0);

                OnPropertyChanged(nameof(BaseCost));
                OnPropertyChanged(nameof(ProfitPercent));
                OnPropertyChanged(nameof(ProfitAmount));
                OnPropertyChanged(nameof(FinalUnitPrice));
                OnPropertyChanged(nameof(TotalPrice));
                OnPropertyChanged(nameof(TotalProfit));
            }
            finally
            {
                _isCalculating = false;
            }
        }

        public void UpdateFromFinalUnitPrice()
        {
            _isCalculating = true;
            try
            {
                decimal baseCost = BaseCost;
                _finalUnitPrice = RoundUpPrice(_finalUnitPrice);
                _profitAmount = _finalUnitPrice - baseCost;
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
                _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
                _totalProfit = Math.Round(_quantity * _profitAmount, 0);

                OnPropertyChanged(nameof(BaseCost));
                OnPropertyChanged(nameof(ProfitAmount));
                OnPropertyChanged(nameof(ProfitPercent));
                OnPropertyChanged(nameof(FinalUnitPrice));
                OnPropertyChanged(nameof(TotalPrice));
                OnPropertyChanged(nameof(TotalProfit));
            }
            finally
            {
                _isCalculating = false;
            }
        }

        public void RecalculateTotals()
        {
            _isCalculating = true;
            try
            {
                RecalculateTotalsInternal();
            }
            finally
            {
                _isCalculating = false;
            }
        }

        private void RecalculateTotalsInternal()
        {
            decimal baseCost = BaseCost;

            if (_profitPercent.HasValue && _profitPercent.Value > 0)
            {
                _profitAmount = Math.Round(baseCost * (_profitPercent.Value / 100m), 0);
                _finalUnitPrice = RoundUpPrice(baseCost + _profitAmount);
                _profitAmount = _finalUnitPrice - baseCost;
            }
            else if (_profitAmount > 0)
            {
                _finalUnitPrice = RoundUpPrice(baseCost + _profitAmount);
                _profitAmount = _finalUnitPrice - baseCost;
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
            }
            else
            {
                _finalUnitPrice = RoundUpPrice(baseCost);
                _profitAmount = _finalUnitPrice - baseCost;
            }

            _totalPrice = RoundUpPrice(_quantity * _finalUnitPrice);
            _totalProfit = Math.Round(_quantity * _profitAmount, 0);

            OnPropertyChanged(nameof(BaseCost));
            OnPropertyChanged(nameof(ProfitPercent));
            OnPropertyChanged(nameof(ProfitAmount));
            OnPropertyChanged(nameof(FinalUnitPrice));
            OnPropertyChanged(nameof(TotalPrice));
            OnPropertyChanged(nameof(TotalProfit));
        }

        public InvoiceItem Clone()
        {
            return new InvoiceItem
            {
                RowNumber = this.RowNumber,
                ItemName = this.ItemName,
                Unit = this.Unit,
                Quantity = this.Quantity,
                RawPrice = this.RawPrice,
                DiscountPercent = this.DiscountPercent,
                DiscountAmount = this.DiscountAmount,
                IsDividedBy1Point1 = this.IsDividedBy1Point1,
                ProfitPercent = this.ProfitPercent,
                ProfitAmount = this.ProfitAmount,
                FinalUnitPrice = this.FinalUnitPrice,
                TotalPrice = this.TotalPrice,
                TotalProfit = this.TotalProfit,
                Description = this.Description
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
