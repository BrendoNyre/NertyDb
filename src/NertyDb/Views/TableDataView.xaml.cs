using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NertyDb.Data;
using NertyDb.ViewModels;

namespace NertyDb.Views
{
    public partial class TableDataView : UserControl
    {
        public TableDataViewModel? ViewModel => DataContext as TableDataViewModel;

        public TableDataView()
        {
            InitializeComponent();
            DataContextChanged += TableDataView_DataContextChanged;
        }

        private void TableDataView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TableDataViewModel oldVm)
            {
                oldVm.DataReloaded -= Vm_DataReloaded;
                oldVm.VisualChangesUpdated -= Vm_VisualChangesUpdated;
            }

            if (e.NewValue is TableDataViewModel newVm)
            {
                newVm.DataReloaded += Vm_DataReloaded;
                newVm.VisualChangesUpdated += Vm_VisualChangesUpdated;
                UpdatePkText(newVm);
            }
        }

        private void Vm_DataReloaded(object? sender, EventArgs e)
        {
            if (ViewModel != null)
            {
                UpdatePkText(ViewModel);
                RefreshRowColors();
            }
        }

        private void Vm_VisualChangesUpdated(object? sender, EventArgs e)
        {
            RefreshRowColors();
        }

        private void UpdatePkText(TableDataViewModel vm)
        {
            if (vm.PrimaryKeyColumns.Count > 0)
            {
                TxtPkColumns.Text = $"[{string.Join(", ", vm.PrimaryKeyColumns)}]";
                TxtPkColumns.Foreground = (Brush)FindResource("AccentBrush");
            }
            else
            {
                TxtPkColumns.Text = "(Sem PK definida)";
                TxtPkColumns.Foreground = (Brush)FindResource("FgMutedBrush");
            }
        }

        private void MainDataGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (ViewModel == null) return;

            var colName = e.PropertyName;
            bool isPk = ViewModel.PrimaryKeyColumns.Contains(colName, StringComparer.OrdinalIgnoreCase);

            if (isPk)
            {
                e.Column.Header = $"🔑 {colName}";
            }

            // Set column editable state
            e.Column.IsReadOnly = ViewModel.IsReadOnly;

            // Custom null text display formatting for text columns
            if (e.Column is DataGridTextColumn textCol && e.PropertyType == typeof(string))
            {
                if (textCol.Binding is Binding b)
                {
                    b.TargetNullValue = "(NULL)";
                }
            }
        }

        private void MainDataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (ViewModel == null) return;

            if (ViewModel.IsReadOnly)
            {
                e.Cancel = true;
                if (!ViewModel.HasPrimaryKey)
                {
                    MessageBox.Show("Esta tabela/view não possui Chave Primária (PK) definida.\r\nA edição inline está desativada por segurança para evitar atualizações inconsistentes.", "Edição Desativada", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void MainDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || ViewModel == null) return;

            if (e.Row.Item is DataRowView rowView)
            {
                var colName = e.Column.Header?.ToString()?.Replace("🔑 ", "").Trim() ?? e.Column.SortMemberPath;
                object? newValue = null;

                if (e.EditingElement is TextBox tb)
                {
                    var text = tb.Text;
                    if (string.Equals(text, "(NULL)", StringComparison.OrdinalIgnoreCase))
                    {
                        newValue = DBNull.Value;
                    }
                    else
                    {
                        newValue = text;
                    }
                }

                // Dispatch to ViewModel
                ViewModel.OnCellEdited(rowView, colName, newValue);
                
                // Refresh visual styling
                Dispatcher.BeginInvoke(new Action(RefreshRowColors), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void MainDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            ApplyRowStyle(e.Row);
        }

        private void RefreshRowColors()
        {
            for (int i = 0; i < MainDataGrid.Items.Count; i++)
            {
                var row = (DataGridRow)MainDataGrid.ItemContainerGenerator.ContainerFromIndex(i);
                if (row != null)
                {
                    ApplyRowStyle(row);
                }
            }
        }

        private void ApplyRowStyle(DataGridRow row)
        {
            if (ViewModel == null || row.Item is not DataRowView rowView) return;

            var rowIndex = ViewModel.TableData.Rows.IndexOf(rowView.Row);
            if (rowIndex < 0) return;

            if (ViewModel.InsertedRowIndices.Contains(rowIndex))
            {
                row.Background = (Brush)FindResource("DiffInsertedBgBrush");
                row.Foreground = (Brush)FindResource("DiffInsertedFgBrush");
            }
            else if (ViewModel.DeletedRowIndices.Contains(rowIndex))
            {
                row.Background = (Brush)FindResource("DiffDeletedBgBrush");
                row.Foreground = (Brush)FindResource("DiffDeletedFgBrush");
            }
            else if (ViewModel.ModifiedCellsByRow.ContainsKey(rowIndex) && ViewModel.ModifiedCellsByRow[rowIndex].Count > 0)
            {
                row.Background = (Brush)FindResource("DiffModifiedBgBrush");
                row.Foreground = (Brush)FindResource("DiffModifiedFgBrush");
            }
            else
            {
                row.ClearValue(DataGridRow.BackgroundProperty);
                row.ClearValue(DataGridRow.ForegroundProperty);
            }
        }

        private void MainDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (ViewModel == null) return;
            e.Handled = true;
            var colName = e.Column.Header?.ToString()?.Replace("🔑 ", "").Trim() ?? e.Column.SortMemberPath;
            ViewModel.SortByColumn(colName);
        }

        private void CopyCell_Click(object sender, RoutedEventArgs e)
        {
            if (MainDataGrid.CurrentCell.Item is DataRowView rowView && MainDataGrid.CurrentCell.Column != null)
            {
                var colName = MainDataGrid.CurrentCell.Column.Header?.ToString()?.Replace("🔑 ", "").Trim() ?? MainDataGrid.CurrentCell.Column.SortMemberPath;
                var val = rowView.Row[colName];
                Clipboard.SetText(val == DBNull.Value ? "" : val.ToString() ?? "");
            }
        }

        private void CopyRowsCsv_Click(object sender, RoutedEventArgs e)
        {
            var selected = MainDataGrid.SelectedItems.OfType<DataRowView>().ToList();
            if (selected.Count == 0 && MainDataGrid.SelectedItem is DataRowView single)
            {
                selected.Add(single);
            }

            if (selected.Count == 0) return;

            var sb = new StringBuilder();
            var dt = selected.First().Row.Table;

            // Header
            sb.AppendLine(string.Join(";", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));

            // Rows
            foreach (var r in selected)
            {
                var vals = dt.Columns.Cast<DataColumn>().Select(c =>
                {
                    var val = r[c.ColumnName];
                    if (val == null || val == DBNull.Value) return "";
                    return val.ToString()?.Replace(";", ",") ?? "";
                });
                sb.AppendLine(string.Join(";", vals));
            }

            Clipboard.SetText(sb.ToString());
        }

        private void CopyRowsSql_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            var selected = MainDataGrid.SelectedItems.OfType<DataRowView>().ToList();
            if (selected.Count == 0 && MainDataGrid.SelectedItem is DataRowView single)
            {
                selected.Add(single);
            }

            if (selected.Count == 0) return;

            var sb = new StringBuilder();
            var dt = selected.First().Row.Table;
            var cols = dt.Columns.Cast<DataColumn>().Where(c => !c.AutoIncrement).ToList();
            var colNames = string.Join(", ", cols.Select(c => DmlGenerator.EscapeIdentifier(c.ColumnName)));

            foreach (var r in selected)
            {
                var valList = cols.Select(c => DmlGenerator.FormatLiteral(r[c.ColumnName]));
                sb.AppendLine($"INSERT INTO {DmlGenerator.EscapeIdentifier(ViewModel.Schema)}.{DmlGenerator.EscapeIdentifier(ViewModel.TableName)} ({colNames}) VALUES ({string.Join(", ", valList)});");
            }

            Clipboard.SetText(sb.ToString());
        }

        private void DiscardRow_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (MainDataGrid.SelectedItem is DataRowView rowView)
            {
                ViewModel.DiscardRow(rowView.Row);
            }
        }
    }
}
