using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NertyDb.ViewModels
{
    public class SelectionStatsViewModel : ObservableObject
    {
        private bool _isExpanded = true;
        private int _totalCount;
        private int _nonNullCount;
        private int _distinctCount;
        private bool _isNumeric;
        private decimal _sum;
        private decimal _average;
        private string _minString = string.Empty;
        private string _maxString = string.Empty;
        private string _formattedSummary = string.Empty;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool HasSelection => _totalCount > 0;

        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        public int NonNullCount
        {
            get => _nonNullCount;
            private set => SetProperty(ref _nonNullCount, value);
        }

        public int DistinctCount
        {
            get => _distinctCount;
            private set => SetProperty(ref _distinctCount, value);
        }

        public bool IsNumeric
        {
            get => _isNumeric;
            private set => SetProperty(ref _isNumeric, value);
        }

        public decimal Sum
        {
            get => _sum;
            private set => SetProperty(ref _sum, value);
        }

        public decimal Average
        {
            get => _average;
            private set => SetProperty(ref _average, value);
        }

        public string MinString
        {
            get => _minString;
            private set => SetProperty(ref _minString, value);
        }

        public string MaxString
        {
            get => _maxString;
            private set => SetProperty(ref _maxString, value);
        }

        public string FormattedSummary
        {
            get => _formattedSummary;
            private set => SetProperty(ref _formattedSummary, value);
        }

        private static readonly CultureInfo PtBr = new("pt-BR");

        public void Calculate(IEnumerable<object?> rawValues)
        {
            var valuesList = rawValues.ToList();
            TotalCount = valuesList.Count;

            if (TotalCount == 0)
            {
                NonNullCount = 0;
                DistinctCount = 0;
                IsNumeric = false;
                Sum = 0;
                Average = 0;
                MinString = string.Empty;
                MaxString = string.Empty;
                FormattedSummary = string.Empty;
                OnPropertyChanged(nameof(HasSelection));
                return;
            }

            var validValues = valuesList
                .Where(v => v != null && v != DBNull.Value)
                .Select(v => v!)
                .ToList();

            NonNullCount = validValues.Count;
            DistinctCount = validValues.Select(v => v.ToString()).Distinct().Count();

            if (NonNullCount == 0)
            {
                IsNumeric = false;
                Sum = 0;
                Average = 0;
                MinString = "NULL";
                MaxString = "NULL";
                FormattedSummary = $"Contagem: {TotalCount} (Todos NULL)";
                OnPropertyChanged(nameof(HasSelection));
                return;
            }

            // Check if all non-null values are numeric
            var numericList = new List<decimal>();
            bool allNumeric = true;

            foreach (var val in validValues)
            {
                if (val is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                {
                    numericList.Add(Convert.ToDecimal(val));
                }
                else if (val is string str && decimal.TryParse(str.Trim(), NumberStyles.Any, PtBr, out var parsed))
                {
                    numericList.Add(parsed);
                }
                else if (val is string strEn && decimal.TryParse(strEn.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedEn))
                {
                    numericList.Add(parsedEn);
                }
                else
                {
                    allNumeric = false;
                    break;
                }
            }

            if (allNumeric && numericList.Count > 0)
            {
                IsNumeric = true;
                Sum = numericList.Sum();
                Average = numericList.Average();
                var minNum = numericList.Min();
                var maxNum = numericList.Max();

                MinString = minNum.ToString("N2", PtBr);
                MaxString = maxNum.ToString("N2", PtBr);

                FormattedSummary = $"∑ Soma: {Sum.ToString("N2", PtBr)}   |   x̄ Média: {Average.ToString("N2", PtBr)}   |   Min: {MinString}   |   Max: {MaxString}   |   Contagem: {NonNullCount:N0}   |   Distintos: {DistinctCount:N0}";
            }
            else
            {
                IsNumeric = false;
                Sum = 0;
                Average = 0;

                // Dates or Strings
                var dates = validValues.OfType<DateTime>().ToList();
                if (dates.Count == validValues.Count)
                {
                    MinString = dates.Min().ToString("dd/MM/yyyy HH:mm:ss");
                    MaxString = dates.Max().ToString("dd/MM/yyyy HH:mm:ss");
                    FormattedSummary = $"Contagem: {NonNullCount:N0}   |   Distintos: {DistinctCount:N0}   |   De: {MinString}   |   Até: {MaxString}";
                }
                else
                {
                    var stringValues = validValues.Select(v => v.ToString() ?? "").OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();
                    MinString = stringValues.FirstOrDefault() ?? "";
                    MaxString = stringValues.LastOrDefault() ?? "";
                    FormattedSummary = $"Contagem: {NonNullCount:N0}   |   Distintos: {DistinctCount:N0}";
                }
            }

            OnPropertyChanged(nameof(HasSelection));
        }
    }
}
