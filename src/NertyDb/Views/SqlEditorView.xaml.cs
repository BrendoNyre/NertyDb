using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using NertyDb.Data;
using NertyDb.Editor;
using NertyDb.Services;
using NertyDb.ViewModels;

namespace NertyDb.Views
{
    public partial class SqlEditorView : UserControl
    {
        private CompletionWindow? _completionWindow;
        private bool _isUpdatingText;
        private readonly System.Windows.Threading.DispatcherTimer _completionDebounceTimer;

        public SqlEditorViewModel? ViewModel => DataContext as SqlEditorViewModel;

        public SqlEditorView()
        {
            InitializeComponent();
            LoadSyntaxHighlighting();

            _completionDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _completionDebounceTimer.Tick += (s, e) =>
            {
                _completionDebounceTimer.Stop();
                ShowCompletionWindow();
            };

            DataContextChanged += SqlEditorView_DataContextChanged;
            Editor.TextChanged += Editor_TextChanged;
            Editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
            Editor.TextArea.TextEntered += TextArea_TextEntered;
            Editor.KeyDown += Editor_KeyDown;
        }

        private static IHighlightingDefinition? _cachedSqlHighlighting;
        private static readonly object _highlightingLock = new();

        private void LoadSyntaxHighlighting()
        {
            if (_cachedSqlHighlighting != null)
            {
                Editor.SyntaxHighlighting = _cachedSqlHighlighting;
                return;
            }

            lock (_highlightingLock)
            {
                if (_cachedSqlHighlighting != null)
                {
                    Editor.SyntaxHighlighting = _cachedSqlHighlighting;
                    return;
                }

                try
                {
                    var assembly = typeof(SqlEditorView).Assembly;
                    using var stream = assembly.GetManifestResourceStream("NertyDb.Resources.SqlHighlighting.xshd");
                    if (stream != null)
                    {
                        using var reader = XmlReader.Create(stream);
                        _cachedSqlHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                        Editor.SyntaxHighlighting = _cachedSqlHighlighting;
                        return;
                    }
                }
                catch { }

                try
                {
                    _cachedSqlHighlighting = HighlightingManager.Instance.GetDefinition("C#");
                    Editor.SyntaxHighlighting = _cachedSqlHighlighting;
                }
                catch { }
            }
        }

        private void SqlEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SqlEditorViewModel oldVm)
            {
                oldVm.InsertTextRequested -= Vm_InsertTextRequested;
            }

            if (e.NewValue is SqlEditorViewModel newVm)
            {
                newVm.InsertTextRequested += Vm_InsertTextRequested;
                _isUpdatingText = true;
                Editor.Text = newVm.SqlText ?? string.Empty;
                _isUpdatingText = false;
            }
        }

        private void Vm_InsertTextRequested(object? sender, string text)
        {
            Editor.Document.Insert(Editor.CaretOffset, text);
        }

        private void Editor_TextChanged(object? sender, EventArgs e)
        {
            if (!_isUpdatingText && ViewModel != null)
            {
                _isUpdatingText = true;
                ViewModel.SqlText = Editor.Text;
                _isUpdatingText = false;
            }
        }

        private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Enter || e.Key == Key.Return) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _completionDebounceTimer.Stop();
                if (_completionWindow != null)
                {
                    _completionWindow.Close();
                    _completionWindow = null;
                }
                ExecuteCurrentSqlStatement();
                return;
            }

            if (_completionWindow != null)
            {
                // CRITICAL RULE: Space or Escape must dismiss the completion window without applying any suggestion
                if (e.Key == Key.Space || e.Key == Key.Escape)
                {
                    _completionDebounceTimer.Stop();
                    _completionWindow.Close();
                    _completionWindow = null;
                    return;
                }

                // Tab or Enter: confirm and apply the selected suggestion
                if (e.Key == Key.Tab || e.Key == Key.Enter)
                {
                    _completionDebounceTimer.Stop();
                    if (_completionWindow.CompletionList.SelectedItem != null)
                    {
                        e.Handled = true;
                        _completionWindow.CompletionList.RequestInsertion(e);
                        _completionWindow.Close();
                        _completionWindow = null;
                    }
                    else
                    {
                        _completionWindow.Close();
                        _completionWindow = null;
                    }
                }
            }
        }

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0)
            {
                char c = e.Text[0];
                if (c == ' ' || c == ';' || c == ',' || c == '(' || c == ')' || c == '\n' || c == '\r')
                {
                    _completionDebounceTimer.Stop();
                    if (_completionWindow != null)
                    {
                        _completionWindow.Close();
                        _completionWindow = null;
                    }
                    return;
                }

                // Dot triggers immediate column lookup (0ms delay)
                if (c == '.')
                {
                    _completionDebounceTimer.Stop();
                    ShowCompletionWindow();
                    return;
                }

                // Letters / digits / underscores: debounce by 100ms to keep typing 100% fluid without UI stall
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    _completionDebounceTimer.Stop();
                    _completionDebounceTimer.Start();
                }
            }
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            var isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            if (e.Key == Key.F5 || (e.Key == Key.X && isAlt))
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Return) && isCtrl)
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
            else if (e.Key == Key.Space && isCtrl)
            {
                e.Handled = true;
                _completionDebounceTimer.Stop();
                ShowCompletionWindow();
            }
            else if (e.Key == Key.E && isCtrl)
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
            else if (e.Key == Key.F && isCtrl && isShift)
            {
                e.Handled = true;
                ViewModel?.FormatSqlCommand.Execute(null);
            }
            else if ((e.Key == Key.OemQuestion || e.Key == Key.Oem2 || e.Key == Key.Divide) && isCtrl && !isShift)
            {
                e.Handled = true;
                ToggleLineComment();
            }
            else if ((e.Key == Key.OemQuestion || e.Key == Key.Oem2 || e.Key == Key.Divide) && isCtrl && isShift)
            {
                e.Handled = true;
                ToggleBlockComment();
            }
            else if (e.Key == Key.Down && isCtrl && isAlt)
            {
                e.Handled = true;
                DuplicateCurrentLine();
            }
            else if (e.Key == Key.L && isCtrl && isShift)
            {
                e.Handled = true;
                DeleteCurrentLine();
            }
            else if (e.Key == Key.U && isCtrl && isShift && !isAlt)
            {
                e.Handled = true;
                ConvertSelectionCase(toUpper: true);
            }
            else if (e.Key == Key.U && isCtrl && isShift && isAlt)
            {
                e.Handled = true;
                ConvertSelectionCase(toUpper: false);
            }
        }

        private void ToggleLineComment()
        {
            var doc = Editor.Document;
            int startOffset = Editor.SelectionStart;
            int length = Editor.SelectionLength;

            var startLine = doc.GetLineByOffset(startOffset);
            var endLine = doc.GetLineByOffset(Math.Min(doc.TextLength, startOffset + length));

            using (doc.RunUpdate())
            {
                bool allCommented = true;
                for (int i = startLine.LineNumber; i <= endLine.LineNumber; i++)
                {
                    var line = doc.GetLineByNumber(i);
                    var text = doc.GetText(line.Offset, line.Length).TrimStart();
                    if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("--"))
                    {
                        allCommented = false;
                        break;
                    }
                }

                for (int i = startLine.LineNumber; i <= endLine.LineNumber; i++)
                {
                    var line = doc.GetLineByNumber(i);
                    var lineText = doc.GetText(line.Offset, line.Length);
                    if (allCommented)
                    {
                        int commentIdx = lineText.IndexOf("--");
                        if (commentIdx >= 0)
                        {
                            int removeLen = (commentIdx + 2 < lineText.Length && lineText[commentIdx + 2] == ' ') ? 3 : 2;
                            doc.Remove(line.Offset + commentIdx, removeLen);
                        }
                    }
                    else
                    {
                        doc.Insert(line.Offset, "-- ");
                    }
                }
            }
        }

        private void ToggleBlockComment()
        {
            var doc = Editor.Document;
            int startOffset = Editor.SelectionStart;
            int length = Editor.SelectionLength;
            if (length == 0) return;

            var selectedText = doc.GetText(startOffset, length);
            if (selectedText.StartsWith("/*") && selectedText.EndsWith("*/"))
            {
                var unwrapped = selectedText.Substring(2, selectedText.Length - 4).Trim();
                doc.Replace(startOffset, length, unwrapped);
            }
            else
            {
                doc.Replace(startOffset, length, $"/* {selectedText} */");
            }
        }

        private void DuplicateCurrentLine()
        {
            var doc = Editor.Document;
            var line = doc.GetLineByOffset(Editor.CaretOffset);
            var lineText = doc.GetText(line.Offset, line.Length);
            doc.Insert(line.EndOffset, Environment.NewLine + lineText);
        }

        private void DeleteCurrentLine()
        {
            var doc = Editor.Document;
            var line = doc.GetLineByOffset(Editor.CaretOffset);
            int removeOffset = line.Offset;
            int removeLength = line.TotalLength;
            doc.Remove(removeOffset, removeLength);
        }

        private void ConvertSelectionCase(bool toUpper)
        {
            if (Editor.SelectionLength == 0) return;
            var text = Editor.SelectedText;
            Editor.SelectedText = toUpper ? text.ToUpperInvariant() : text.ToLowerInvariant();
        }

        private void ClearMessages_Click(object sender, RoutedEventArgs e)
        {
            AppLogService.Instance.Clear();
        }

        private void ExecuteCurrentSqlStatement()
        {
            if (ViewModel == null) return;
            var sqlToRun = SqlStatementExtractor.ExtractStatementToExecute(Editor.Text, Editor.CaretOffset, Editor.SelectedText);
            ViewModel.ExecuteCommand.Execute(sqlToRun);
        }

        private void ShowCompletionWindow()
        {
            if (ViewModel == null || ViewModel.Connection == null) return;

            var fullText = Editor.Text;
            var caret = Editor.CaretOffset;
            if (caret < 0 || caret > fullText.Length) return;

            var context = SqlCompletionProvider.GetCompletionContext(
                fullText,
                caret,
                ViewModel.Connection,
                ViewModel.Database);

            if (context.Items.Count == 0)
            {
                if (_completionWindow != null)
                {
                    _completionWindow.Close();
                    _completionWindow = null;
                }
                return;
            }

            if (_completionWindow == null)
            {
                _completionWindow = new CompletionWindow(Editor.TextArea)
                {
                    Width = 320,
                    Height = 220,
                    CloseWhenCaretAtBeginning = true
                };

                _completionWindow.Closed += (s, args) => _completionWindow = null;
            }

            _completionWindow.StartOffset = context.TokenStartOffset;
            _completionWindow.EndOffset = caret;

            var completionData = _completionWindow.CompletionList.CompletionData;
            completionData.Clear();
            foreach (var item in context.Items)
            {
                completionData.Add(item);
            }

            if (!_completionWindow.IsVisible)
            {
                _completionWindow.Show();
            }
        }

        #region Editable Result DataGrid Handlers

        private void ResultGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid && grid.DataContext is SqlResultTabViewModel vm)
            {
                vm.VisualChangesUpdated -= (s, args) => RefreshGridVisuals(grid, vm);
                vm.VisualChangesUpdated += (s, args) => RefreshGridVisuals(grid, vm);

                vm.RowCreated -= (s, rowIndex) => ScrollRowIntoView(grid, rowIndex);
                vm.RowCreated += (s, rowIndex) => ScrollRowIntoView(grid, rowIndex);
            }
        }

        private void RefreshGridVisuals(DataGrid grid, SqlResultTabViewModel vm)
        {
            Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < grid.Items.Count; i++)
                {
                    if (grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        UpdateRowVisual(row, vm);
                    }
                }
            });
        }

        private void ScrollRowIntoView(DataGrid grid, int rowIndex)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (rowIndex >= 0 && rowIndex < grid.Items.Count)
                {
                    var item = grid.Items[rowIndex];
                    grid.SelectedItem = item;
                    grid.ScrollIntoView(item);
                }
            });
        }

        private void ResultGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not DataGrid grid || grid.DataContext is not SqlResultTabViewModel vm) return;

            var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            // Paste (Ctrl+V)
            if (e.Key == Key.V && isCtrl && !isAlt)
            {
                e.Handled = true;
                HandleGridPaste(grid, vm);
            }
            // Delete / Backspace on selected cells
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && !isCtrl && !isAlt)
            {
                if (Keyboard.FocusedElement is not TextBox)
                {
                    var selectedCells = grid.SelectedCells.ToList();
                    if (selectedCells.Count > 0)
                    {
                        e.Handled = true;
                        var cellsToClear = selectedCells
                            .Where(c => c.Item is DataRowView && c.Column != null)
                            .Select(c => ((DataRowView)c.Item, GetColumnName(c.Column)))
                            .Where(x => !string.IsNullOrEmpty(x.Item2))
                            .ToList();

                        vm.ApplyBulkCellValues(cellsToClear, null);
                        RefreshGridVisuals(grid, vm);
                    }
                }
            }
            // Duplicate Row (Ctrl+Alt+Down)
            else if (e.Key == Key.Down && isCtrl && isAlt)
            {
                e.Handled = true;
                vm.DuplicateRowCommand.Execute(grid.SelectedCells);
            }
            // Insert Row (Insert)
            else if (e.Key == Key.Insert)
            {
                e.Handled = true;
                vm.AddRowCommand.Execute(null);
            }
        }

        private void HandleGridPaste(DataGrid grid, SqlResultTabViewModel vm)
        {
            if (grid == null || vm == null || !Clipboard.ContainsText()) return;

            var clipboardText = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardText)) return;

            var selectedCells = grid.SelectedCells.ToList();
            if (selectedCells.Count == 0 && grid.SelectedItem is DataRowView)
            {
                if (grid.Columns.Count > 0)
                {
                    selectedCells.Add(new DataGridCellInfo(grid.SelectedItem, grid.Columns[0]));
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

            // Scenario A: Single value pasted across multiple selected cells (Mass edit)
            if (matrixRows == 1 && matrixCols == 1 && selectedCells.Count > 1)
            {
                var singleVal = matrix[0][0];
                var cellItems = selectedCells
                    .Where(c => c.Item is DataRowView && c.Column != null)
                    .Select(c => ((DataRowView)c.Item, GetColumnName(c.Column)))
                    .Where(x => !string.IsNullOrEmpty(x.Item2))
                    .ToList();

                vm.ApplyBulkCellValues(cellItems, singleVal);
                RefreshGridVisuals(grid, vm);
                return;
            }

            // Scenario B: Matrix / multi-value paste
            var orderedCells = selectedCells
                .Where(c => c.Item is DataRowView && c.Column != null)
                .Select(c => new
                {
                    Cell = c,
                    RowView = (DataRowView)c.Item,
                    RowIndex = vm.Data.Rows.IndexOf(((DataRowView)c.Item).Row),
                    DisplayIndex = c.Column.DisplayIndex,
                    ColumnName = GetColumnName(c.Column)
                })
                .OrderBy(c => c.RowIndex)
                .ThenBy(c => c.DisplayIndex)
                .ToList();

            if (orderedCells.Count == 0) return;

            int minRow = orderedCells.Min(c => c.RowIndex);
            int minCol = orderedCells.Min(c => c.DisplayIndex);

            // If matching vertical vector (e.g. 4 values pasted into 4 vertical cells)
            if (matrixCols == 1 && orderedCells.All(c => c.DisplayIndex == orderedCells[0].DisplayIndex) && orderedCells.Count == matrixRows)
            {
                for (int i = 0; i < orderedCells.Count; i++)
                {
                    var item = orderedCells[i];
                    var valStr = matrix[i][0];
                    if (vm.Data.Columns.Contains(item.ColumnName))
                    {
                        var col = vm.Data.Columns[item.ColumnName]!;
                        var typedVal = SqlResultTabViewModel.ConvertValueToColumnType(valStr, col);
                        item.RowView[item.ColumnName] = typedVal ?? DBNull.Value;
                        vm.OnCellEdited(item.RowView, item.ColumnName, typedVal);
                    }
                }
                vm.TriggerVisualUpdate();
                RefreshGridVisuals(grid, vm);
                return;
            }

            // General rectangular paste starting from top-left cell
            for (int r = 0; r < matrixRows; r++)
            {
                int targetRowIdx = minRow + r;
                if (targetRowIdx >= vm.FilteredView.Count) break;

                var targetRowView = vm.FilteredView[targetRowIdx];
                for (int c = 0; c < matrix[r].Length; c++)
                {
                    int targetColDisplayIdx = minCol + c;
                    var col = grid.Columns.FirstOrDefault(x => x.DisplayIndex == targetColDisplayIdx);
                    if (col == null) continue;

                    var colName = GetColumnName(col);
                    if (!string.IsNullOrEmpty(colName) && vm.Data.Columns.Contains(colName))
                    {
                        var valStr = matrix[r][c];
                        var colDef = vm.Data.Columns[colName]!;
                        var typedVal = SqlResultTabViewModel.ConvertValueToColumnType(valStr, colDef);
                        targetRowView[colName] = typedVal ?? DBNull.Value;
                        vm.OnCellEdited(targetRowView, colName, typedVal);
                    }
                }
            }

            vm.TriggerVisualUpdate();
            RefreshGridVisuals(grid, vm);
        }

        private static string GetColumnName(DataGridColumn col)
        {
            var path = col.SortMemberPath;
            if (!string.IsNullOrEmpty(path)) return path;
            if (col.Header is string h)
            {
                return h.Replace("🔑 ", "").Trim();
            }
            return col.Header?.ToString() ?? string.Empty;
        }

        private void ResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            e.Column.MinWidth = 80;
            e.Column.IsReadOnly = false;
        }

        private void ResultGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (sender is DataGrid dg && dg.DataContext is SqlResultTabViewModel vm)
            {
                if (vm.IsReadOnly)
                {
                    e.Cancel = true;
                }
            }
        }

        private void ResultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (sender is not DataGrid grid || grid.DataContext is not SqlResultTabViewModel vm) return;
            if (e.Row.Item is not DataRowView rowView) return;

            var colHeader = GetColumnName(e.Column);
            if (string.IsNullOrEmpty(colHeader) || !vm.Data.Columns.Contains(colHeader)) return;

            object? newValue = null;
            if (e.EditingElement is TextBox tb)
            {
                var text = tb.Text;
                var col = vm.Data.Columns[colHeader]!;
                newValue = SqlResultTabViewModel.ConvertValueToColumnType(text, col);
            }

            // Mass edit: if multiple cells were selected in this exact column, apply to all selected cells!
            var selectedCellsInCol = grid.SelectedCells
                .Where(c => c.Item is DataRowView && c.Column != null && GetColumnName(c.Column) == colHeader)
                .Select(c => ((DataRowView)c.Item, colHeader))
                .ToList();

            if (selectedCellsInCol.Count > 1)
            {
                vm.ApplyBulkCellValues(selectedCellsInCol, newValue);
            }
            else
            {
                vm.OnCellEdited(rowView, colHeader, newValue);
            }

            UpdateRowVisual(e.Row, vm);
            RefreshGridVisuals(grid, vm);
        }

        private void ResultGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (sender is DataGrid grid && grid.DataContext is SqlResultTabViewModel vm)
            {
                UpdateRowVisual(e.Row, vm);
            }
        }

        private void UpdateRowVisual(DataGridRow row, SqlResultTabViewModel vm)
        {
            if (row.Item is not DataRowView rowView) return;
            var rowIndex = vm.Data.Rows.IndexOf(rowView.Row);
            if (rowIndex < 0) return;

            if (vm.DeletedRowIndices.Contains(rowIndex))
            {
                row.Background = (Brush)FindResource("DiffDeletedBgBrush");
                row.Foreground = (Brush)FindResource("DiffDeletedFgBrush");
            }
            else if (vm.InsertedRowIndices.Contains(rowIndex))
            {
                row.Background = (Brush)FindResource("DiffInsertedBgBrush");
                row.Foreground = (Brush)FindResource("DiffInsertedFgBrush");
            }
            else if (vm.ModifiedCellsByRow.ContainsKey(rowIndex) && vm.ModifiedCellsByRow[rowIndex].Count > 0)
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

        private void DiscardResultRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is SqlResultTabViewModel vm)
            {
                var grid = FindParentDataGrid(mi);
                if (grid?.SelectedItem is DataRowView rowView)
                {
                    vm.DiscardRow(rowView.Row);
                }
            }
        }

        private void CopyResultCell_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
            {
                var grid = FindParentDataGrid(mi);
                if (grid?.CurrentCell.Item is DataRowView rowView && grid.CurrentCell.Column != null)
                {
                    var colHeader = grid.CurrentCell.Column.Header?.ToString();
                    if (!string.IsNullOrEmpty(colHeader))
                    {
                        var val = rowView[colHeader]?.ToString() ?? "";
                        Clipboard.SetText(val);
                    }
                }
            }
        }

        private void CopyResultRowsCsv_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is SqlResultTabViewModel vm)
            {
                var grid = FindParentDataGrid(mi);
                if (grid == null) return;
                var selected = grid.SelectedItems.OfType<DataRowView>().ToList();
                if (selected.Count == 0 && grid.SelectedItem is DataRowView single) selected.Add(single);
                if (selected.Count == 0) return;

                var sb = new StringBuilder();
                var cols = vm.Data.Columns.Cast<DataColumn>().ToList();
                sb.AppendLine(string.Join(";", cols.Select(c => $"\"{c.ColumnName}\"")));

                foreach (var r in selected)
                {
                    var line = string.Join(";", cols.Select(c => $"\"{r[c.ColumnName]?.ToString()?.Replace("\"", "\"\"")}\""));
                    sb.AppendLine(line);
                }

                Clipboard.SetText(sb.ToString());
            }
        }

        private void CopyResultRowsSql_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is SqlResultTabViewModel vm)
            {
                var grid = FindParentDataGrid(mi);
                if (grid == null) return;
                var selected = grid.SelectedItems.OfType<DataRowView>().ToList();
                if (selected.Count == 0 && grid.SelectedItem is DataRowView single) selected.Add(single);
                if (selected.Count == 0) return;

                var sb = new StringBuilder();
                var cols = vm.Data.Columns.Cast<DataColumn>().ToList();
                var colNames = string.Join(", ", cols.Select(c => DmlGenerator.EscapeIdentifier(c.ColumnName)));
                var tblName = string.IsNullOrEmpty(vm.TableName) ? "TABELA" : vm.TableName;

                foreach (var r in selected)
                {
                    var valList = cols.Select(c => DmlGenerator.FormatLiteral(r[c.ColumnName]));
                    sb.AppendLine($"INSERT INTO {DmlGenerator.EscapeIdentifier(vm.Schema)}.{DmlGenerator.EscapeIdentifier(tblName)} ({colNames}) VALUES ({string.Join(", ", valList)});");
                }

                Clipboard.SetText(sb.ToString());
            }
        }

        private static DataGrid? FindParentDataGrid(DependencyObject child)
        {
            if (child is ContextMenu cm && cm.PlacementTarget is DataGrid dg) return dg;
            if (child is ContextMenu cmenu && cmenu.PlacementTarget != null)
            {
                return FindVisualParent<DataGrid>(cmenu.PlacementTarget);
            }
            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private void ResultGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.DataContext is SqlResultTabViewModel tabVm)
            {
                var selectedCells = grid.SelectedCells;
                if (selectedCells.Count == 0)
                {
                    tabVm.SelectionStats.Calculate(Enumerable.Empty<object?>());
                    return;
                }

                var values = new System.Collections.Generic.List<object?>(selectedCells.Count);
                foreach (var cell in selectedCells)
                {
                    if (cell.Item is DataRowView rowView)
                    {
                        string colName = cell.Column.SortMemberPath;
                        if (string.IsNullOrEmpty(colName) && cell.Column.Header is string s)
                        {
                            colName = s.Replace("🔑 ", "").Trim();
                        }

                        if (!string.IsNullOrEmpty(colName) && rowView.Row.Table.Columns.Contains(colName))
                        {
                            values.Add(rowView[colName]);
                        }
                    }
                }

                tabVm.SelectionStats.Calculate(values);
            }
        }

        #endregion
    }
}
