using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarazInvoice.Models
{
    public class InvoiceItem : INotifyPropertyChanged
    {
        private int _rowNumber = 1;
        private string _itemName = string.Empty;
        private decimal _quantity = 1;
        private decimal _rawPrice = 0;
        private decimal _discountPercent = 0;
        private decimal _discountAmount = 0;
        private bool _isDividedBy1Point1 = false;
        private decimal _finalUnitPrice = 0;
        private decimal _totalPrice = 0;
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

        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    RecalculateTotals();
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

        public decimal FinalUnitPrice
        {
            get => _finalUnitPrice;
            set
            {
                if (_finalUnitPrice != value)
                {
                    _finalUnitPrice = value;
                    OnPropertyChanged();
                    _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);
                    OnPropertyChanged(nameof(TotalPrice));
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

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        public void UpdateFromPercent()
        {
            _isCalculating = true;
            try
            {
                _discountAmount = Math.Round(_rawPrice * (_discountPercent / 100m), 0);
                OnPropertyChanged(nameof(DiscountAmount));
                RecalculateTotals();
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
                RecalculateTotals();
            }
            finally
            {
                _isCalculating = false;
            }
        }

        public void RecalculateTotals()
        {
            decimal priceAfterDiscount = _rawPrice - _discountAmount;
            if (priceAfterDiscount < 0) priceAfterDiscount = 0;

            if (_isDividedBy1Point1)
            {
                _finalUnitPrice = Math.Round(priceAfterDiscount / 1.1m, 0);
            }
            else
            {
                _finalUnitPrice = Math.Round(priceAfterDiscount, 0);
            }

            _totalPrice = Math.Round(_quantity * _finalUnitPrice, 0);

            OnPropertyChanged(nameof(FinalUnitPrice));
            OnPropertyChanged(nameof(TotalPrice));
        }

        public InvoiceItem Clone()
        {
            return new InvoiceItem
            {
                RowNumber = this.RowNumber,
                ItemName = this.ItemName,
                Quantity = this.Quantity,
                RawPrice = this.RawPrice,
                DiscountPercent = this.DiscountPercent,
                DiscountAmount = this.DiscountAmount,
                IsDividedBy1Point1 = this.IsDividedBy1Point1,
                FinalUnitPrice = this.FinalUnitPrice,
                TotalPrice = this.TotalPrice,
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
