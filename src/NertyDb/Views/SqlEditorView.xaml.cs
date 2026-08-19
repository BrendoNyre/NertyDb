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
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Return) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
            else if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _completionDebounceTimer.Stop();
                ShowCompletionWindow();
            }
            else if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                ExecuteCurrentSqlStatement();
            }
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
                // Allow editing always
            }
        }

        private void ResultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (sender is not DataGrid grid || grid.DataContext is not SqlResultTabViewModel vm) return;
            if (e.Row.Item is not DataRowView rowView) return;

            var colHeader = e.Column.Header?.ToString();
            if (string.IsNullOrEmpty(colHeader)) return;

            object? newValue = null;
            if (e.EditingElement is TextBox tb)
            {
                var text = tb.Text;
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    newValue = DBNull.Value;
                }
                else
                {
                    var col = vm.Data.Columns[colHeader];
                    if (col != null)
                    {
                        try
                        {
                            var targetType = Nullable.GetUnderlyingType(col.DataType) ?? col.DataType;
                            newValue = Convert.ChangeType(text, targetType);
                        }
                        catch
                        {
                            newValue = text;
                        }
                    }
                    else
                    {
                        newValue = text;
                    }
                }
            }

            vm.OnCellEdited(rowView, colHeader, newValue);
            UpdateRowVisual(e.Row, vm);
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
