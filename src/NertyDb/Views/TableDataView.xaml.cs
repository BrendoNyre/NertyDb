using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NertyDb.Data;
using NertyDb.Services;
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

            string headerText = isPk ? $"🔑 {colName}" : colName;

            if (ViewModel.ColumnDescriptions.TryGetValue(colName, out var desc) && !string.IsNullOrWhiteSpace(desc))
            {
                var textBlock = new TextBlock
                {
                    Text = headerText,
                    ToolTip = $"{colName}: {desc}"
                };
                e.Column.Header = textBlock;
            }
            else
            {
                e.Column.Header = headerText;
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

        private void MainDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (ViewModel == null) return;

            var selectedCells = MainDataGrid.SelectedCells;
            if (selectedCells.Count == 0)
            {
                ViewModel.SelectionStats.Calculate(Enumerable.Empty<object?>());
                return;
            }

            var values = new System.Collections.Generic.List<object?>(selectedCells.Count);
            foreach (var cell in selectedCells)
            {
                if (cell.Item is DataRowView rowView)
                {
                    var colHeader = cell.Column.Header;
                    string colName = cell.Column.SortMemberPath;
                    if (string.IsNullOrEmpty(colName) && colHeader is TextBlock tb)
                    {
                        colName = tb.Text.Replace("🔑 ", "").Trim();
                    }
                    else if (string.IsNullOrEmpty(colName) && colHeader is string s)
                    {
                        colName = s.Replace("🔑 ", "").Trim();
                    }

                    if (!string.IsNullOrEmpty(colName) && rowView.Row.Table.Columns.Contains(colName))
                    {
                        values.Add(rowView[colName]);
                    }
                }
            }

            ViewModel.SelectionStats.Calculate(values);
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

        private void MainDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ViewModel == null) return;

            var isCtrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            var isAlt = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt;

            // Copy (Ctrl+C) — intercept BEFORE WPF DataGrid default handler, which calls Clipboard.SetDataObject directly
            if (e.Key == System.Windows.Input.Key.C && isCtrl && !isAlt)
            {
                e.Handled = true;
                CopyMainGridCellsToClipboard();
                return;
            }

            // Operations below require editable mode
            if (ViewModel.IsReadOnly) return;

            // Paste (Ctrl+V)
            if (e.Key == System.Windows.Input.Key.V && isCtrl && !isAlt)
            {
                e.Handled = true;
                HandleGridPaste();
            }
            // Delete / Backspace on selected cells
            else if ((e.Key == System.Windows.Input.Key.Delete || e.Key == System.Windows.Input.Key.Back) && !isCtrl && !isAlt)
            {
                if (System.Windows.Input.Keyboard.FocusedElement is not TextBox)
                {
                    var selectedCells = MainDataGrid.SelectedCells.ToList();
                    if (selectedCells.Count > 0)
                    {
                        e.Handled = true;
                        foreach (var cell in selectedCells)
                        {
                            if (cell.Item is DataRowView rowView && cell.Column != null)
                            {
                                var colName = GetColumnName(cell.Column);
                                if (!string.IsNullOrEmpty(colName) && ViewModel.TableData.Columns.Contains(colName))
                                {
                                    rowView[colName] = DBNull.Value;
                                    ViewModel.OnCellEdited(rowView, colName, DBNull.Value);
                                }
                            }
                        }
                        RefreshRowColors();
                    }
                }
            }
            // Duplicate Row (Ctrl+Alt+Down)
            else if (e.Key == System.Windows.Input.Key.Down && isCtrl && isAlt)
            {
                e.Handled = true;
                ViewModel.DuplicateRowCommand.Execute(MainDataGrid.SelectedItem);
            }
        }

        /// <summary>
        /// Copia células selecionadas do MainDataGrid para a área de transferência usando ClipboardHelper (seguro).
        /// Funciona em modo leitura e edição. Colunas separadas por Tab, linhas por nova linha.
        /// </summary>
        private void CopyMainGridCellsToClipboard()
        {
            try
            {
                var selectedCells = MainDataGrid.SelectedCells.ToList();
                if (selectedCells.Count == 0) return;

                var ordered = selectedCells
                    .Where(c => c.Item is DataRowView && c.Column != null)
                    .Select(c => new
                    {
                        RowView = (DataRowView)c.Item,
                        RowIndex = ViewModel!.TableData.Rows.IndexOf(((DataRowView)c.Item).Row),
                        DisplayIndex = c.Column.DisplayIndex,
                        ColumnName = GetColumnName(c.Column)
                    })
                    .Where(c => !string.IsNullOrEmpty(c.ColumnName))
                    .OrderBy(c => c.RowIndex)
                    .ThenBy(c => c.DisplayIndex)
                    .ToList();

                if (ordered.Count == 0) return;

                var sb = new StringBuilder();
                int currentRowIdx = -1;
                bool firstCellInRow = true;

                foreach (var cell in ordered)
                {
                    if (cell.RowIndex != currentRowIdx)
                    {
                        if (currentRowIdx != -1) sb.AppendLine();
                        currentRowIdx = cell.RowIndex;
                        firstCellInRow = true;
                    }

                    if (!firstCellInRow) sb.Append('\t');
                    firstCellInRow = false;

                    var val = cell.RowView[cell.ColumnName];
                    sb.Append(val == DBNull.Value ? "" : val?.ToString() ?? "");
                }

                ClipboardHelper.SetText(sb.ToString());
            }
            catch { }
        }

        private void HandleGridPaste()
        {
            if (ViewModel == null || !ClipboardHelper.ContainsText()) return;

            var clipboardText = ClipboardHelper.GetText();
            if (string.IsNullOrEmpty(clipboardText)) return;

            var selectedCells = MainDataGrid.SelectedCells.ToList();
            if (selectedCells.Count == 0 && MainDataGrid.SelectedItem is DataRowView)
            {
                if (MainDataGrid.Columns.Count > 0)
                {
                    selectedCells.Add(new DataGridCellInfo(MainDataGrid.SelectedItem, MainDataGrid.Columns[0]));
                }
            }
            if (selectedCells.Count == 0) return;

            var rawLines = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lines = rawLines.ToList();
            if (lines.Count > 1 && string.IsNullOrEmpty(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var matrix = lines.Select(l => l.Split('\t')).ToArray();
            int matrixRows = matrix.Length;
            int matrixCols = matrix.Max(r => r.Length);

            // Single value pasted across multiple cells (Mass edit)
            if (matrixRows == 1 && matrixCols == 1 && selectedCells.Count > 1)
            {
                var singleVal = matrix[0][0];
                foreach (var cell in selectedCells)
                {
                    if (cell.Item is DataRowView rowView && cell.Column != null)
                    {
                        var colName = GetColumnName(cell.Column);
                        if (!string.IsNullOrEmpty(colName) && ViewModel.TableData.Columns.Contains(colName))
                        {
                            var col = ViewModel.TableData.Columns[colName]!;
                            var typedVal = SqlResultTabViewModel.ConvertValueToColumnType(singleVal, col);
                            rowView[colName] = typedVal ?? DBNull.Value;
                            ViewModel.OnCellEdited(rowView, colName, typedVal);
                        }
                    }
                }
                RefreshRowColors();
                return;
            }

            // Matrix paste
            var orderedCells = selectedCells
                .Where(c => c.Item is DataRowView && c.Column != null)
                .Select(c => new
                {
                    Cell = c,
                    RowView = (DataRowView)c.Item,
                    RowIndex = ViewModel.TableData.Rows.IndexOf(((DataRowView)c.Item).Row),
                    DisplayIndex = c.Column.DisplayIndex,
                    ColumnName = GetColumnName(c.Column)
                })
                .OrderBy(c => c.RowIndex)
                .ThenBy(c => c.DisplayIndex)
                .ToList();

            if (orderedCells.Count == 0) return;

            int minRow = orderedCells.Min(c => c.RowIndex);
            int minCol = orderedCells.Min(c => c.DisplayIndex);

            for (int r = 0; r < matrixRows; r++)
            {
                int targetRowIdx = minRow + r;
                if (targetRowIdx >= ViewModel.FilteredView.Count) break;

                var targetRowView = ViewModel.FilteredView[targetRowIdx];
                for (int c = 0; c < matrix[r].Length; c++)
                {
                    int targetColDisplayIdx = minCol + c;
                    var col = MainDataGrid.Columns.FirstOrDefault(x => x.DisplayIndex == targetColDisplayIdx);
                    if (col == null) continue;

                    var colName = GetColumnName(col);
                    if (!string.IsNullOrEmpty(colName) && ViewModel.TableData.Columns.Contains(colName))
                    {
                        var valStr = matrix[r][c];
                        var colDef = ViewModel.TableData.Columns[colName]!;
                        var typedVal = SqlResultTabViewModel.ConvertValueToColumnType(valStr, colDef);
                        targetRowView[colName] = typedVal ?? DBNull.Value;
                        ViewModel.OnCellEdited(targetRowView, colName, typedVal);
                    }
                }
            }

            RefreshRowColors();
        }

        private static string GetColumnName(DataGridColumn col)
        {
            var path = col.SortMemberPath;
            if (!string.IsNullOrEmpty(path)) return path;
            if (col.Header is TextBlock tb)
            {
                return tb.Text.Replace("🔑 ", "").Trim();
            }
            if (col.Header is string h)
            {
                return h.Replace("🔑 ", "").Trim();
            }
            return col.Header?.ToString() ?? string.Empty;
        }

        private void MainDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || ViewModel == null) return;

            if (e.Row.Item is DataRowView rowView)
            {
                var colName = GetColumnName(e.Column);
                if (string.IsNullOrEmpty(colName) || !ViewModel.TableData.Columns.Contains(colName)) return;

                object? newValue = null;
                if (e.EditingElement is TextBox tb)
                {
                    var text = tb.Text;
                    var col = ViewModel.TableData.Columns[colName]!;
                    newValue = SqlResultTabViewModel.ConvertValueToColumnType(text, col);
                }

                // Mass edit: if multiple cells were selected in this column, update all of them
                var selectedInCol = MainDataGrid.SelectedCells
                    .Where(c => c.Item is DataRowView && c.Column != null && GetColumnName(c.Column) == colName)
                    .Select(c => (DataRowView)c.Item)
                    .Distinct()
                    .ToList();

                if (selectedInCol.Count > 1)
                {
                    foreach (var rv in selectedInCol)
                    {
                        rv[colName] = newValue ?? DBNull.Value;
                        ViewModel.OnCellEdited(rv, colName, newValue);
                    }
                }
                else
                {
                    ViewModel.OnCellEdited(rowView, colName, newValue);
                }
                
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
                ClipboardHelper.SetText(val == DBNull.Value ? "" : val.ToString() ?? "");
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

            ClipboardHelper.SetText(sb.ToString());
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

            ClipboardHelper.SetText(sb.ToString());
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
