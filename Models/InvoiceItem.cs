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
        private decimal _discountPercent = 0;
        private decimal _discountAmount = 0;
        private bool _isDividedBy1Point1 = false;
        private decimal _profitPercent = 0;
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
                    _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
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

        public decimal DiscountPercent
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

        public decimal ProfitPercent
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
                if (_finalUnitPrice != value)
                {
                    _finalUnitPrice = value;
                    OnPropertyChanged();
                    if (!_isCalculating)
                    {
                        UpdateFromFinalUnitPrice();
                    }
                    else
                    {
                        _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
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
        /// قیمت پایه و تمام شده پس از اعمال تخفیف و/یا تقسیم بر ۱.۱
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
        }

        public void UpdateFromPercent()
        {
            _isCalculating = true;
            try
            {
                _discountAmount = Math.Round(_rawPrice * (_discountPercent / 100m), 0);
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

        public void UpdateFromProfitPercent()
        {
            _isCalculating = true;
            try
            {
                decimal baseCost = BaseCost;
                _profitAmount = Math.Round(baseCost * (_profitPercent / 100m), 0);
                _finalUnitPrice = baseCost + _profitAmount;
                _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
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
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
                _finalUnitPrice = baseCost + _profitAmount;
                _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
                _totalProfit = Math.Round(_quantity * _profitAmount, 0);

                OnPropertyChanged(nameof(BaseCost));
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

        public void UpdateFromFinalUnitPrice()
        {
            _isCalculating = true;
            try
            {
                decimal baseCost = BaseCost;
                _profitAmount = _finalUnitPrice - baseCost;
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
                _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
                _totalProfit = Math.Round(_quantity * _profitAmount, 0);

                OnPropertyChanged(nameof(BaseCost));
                OnPropertyChanged(nameof(ProfitAmount));
                OnPropertyChanged(nameof(ProfitPercent));
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

            if (_profitPercent > 0)
            {
                _profitAmount = Math.Round(baseCost * (_profitPercent / 100m), 0);
                _finalUnitPrice = baseCost + _profitAmount;
            }
            else if (_profitAmount > 0)
            {
                _profitPercent = (baseCost > 0) ? Math.Round((_profitAmount / baseCost) * 100m, 2) : 0;
                _finalUnitPrice = baseCost + _profitAmount;
            }
            else
            {
                _finalUnitPrice = baseCost;
                _profitAmount = 0;
                _profitPercent = 0;
            }

            _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
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
