using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class ExportViewModel : ObservableObject
    {
        private readonly DataTable _table;
        private readonly string _suggestedName;
        private readonly ExportService _exportService;

        private ExportFormat _selectedFormat = ExportFormat.Csv;
        private string _filePath = string.Empty;
        private string _delimiter = ";";
        private string _textQualifier = "\"";
        private string _encodingName = "UTF-8-BOM";
        private bool _includeHeaders = true;
        private bool _selectedRowsOnly = false;
        private bool _isExporting;
        private string _statusMessage = string.Empty;
        private bool _isSuccess;
        private string _previewText = string.Empty;

        public DataTable Table => _table;
        public int TotalRowCount => _table.Rows.Count;

        public List<ExportFormat> AvailableFormats { get; } = new()
        {
            ExportFormat.Csv,
            ExportFormat.ExcelXml,
            ExportFormat.Json,
            ExportFormat.SqlInsert
        };

        public List<string> AvailableDelimiters { get; } = new() { ";", ",", "\t (Tab)", "|" };
        public List<string> AvailableEncodings { get; } = new() { "UTF-8-BOM", "UTF-8", "Windows-1252", "ISO-8859-1", "ASCII" };

        public ExportFormat SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                if (SetProperty(ref _selectedFormat, value))
                {
                    UpdateDefaultExtension();
                    UpdatePreview();
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string Delimiter
        {
            get => _delimiter;
            set
            {
                var clean = value.StartsWith("\t") ? "\t" : value;
                if (SetProperty(ref _delimiter, clean))
                {
                    UpdatePreview();
                }
            }
        }

        public string TextQualifier
        {
            get => _textQualifier;
            set
            {
                if (SetProperty(ref _textQualifier, value))
                {
                    UpdatePreview();
                }
            }
        }

        public string EncodingName
        {
            get => _encodingName;
            set => SetProperty(ref _encodingName, value);
        }

        public bool IncludeHeaders
        {
            get => _includeHeaders;
            set
            {
                if (SetProperty(ref _includeHeaders, value))
                {
                    UpdatePreview();
                }
            }
        }

        public bool SelectedRowsOnly
        {
            get => _selectedRowsOnly;
            set => SetProperty(ref _selectedRowsOnly, value);
        }

        public bool IsExporting
        {
            get => _isExporting;
            set => SetProperty(ref _isExporting, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsSuccess
        {
            get => _isSuccess;
            set => SetProperty(ref _isSuccess, value);
        }

        public string PreviewText
        {
            get => _previewText;
            set => SetProperty(ref _previewText, value);
        }

        public bool IsCsvFormat => SelectedFormat == ExportFormat.Csv;
        public bool IsSqlInsertFormat => SelectedFormat == ExportFormat.SqlInsert;

        public ICommand BrowseFileCommand { get; }
        public ICommand ExecuteExportCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        public event EventHandler? RequestClose;

        public void RequestDialogClose()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public ExportViewModel(DataTable table, string suggestedName, ExportService exportService)
        {
            _table = table;
            _suggestedName = string.IsNullOrWhiteSpace(suggestedName) ? "Export_Dados" : suggestedName;
            _exportService = exportService;

            var defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            FilePath = Path.Combine(defaultFolder, $"{_suggestedName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            BrowseFileCommand = new RelayCommand(ExecuteBrowseFile);
            ExecuteExportCommand = new AsyncRelayCommand(ExecuteExportAsync, () => !IsExporting && !string.IsNullOrWhiteSpace(FilePath));
            
            OpenFileCommand = new RelayCommand(() =>
            {
                if (File.Exists(FilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FilePath) { UseShellExecute = true });
                }
            });

            OpenFolderCommand = new RelayCommand(() =>
            {
                if (File.Exists(FilePath))
                {
                    var dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
                    }
                }
            });

            CopyToClipboardCommand = new RelayCommand(() =>
            {
                var options = BuildOptions();
                var csv = _exportService.FormatCsvString(_table, options);
                ClipboardHelper.SetText(csv);
                StatusMessage = "Dados formatados copiados para a Área de Transferência!";
                ToastService.Instance.ShowSuccess("Dados copiados para a área de transferência.", "Copiado");
            });

            UpdatePreview();
        }

        private void UpdateDefaultExtension()
        {
            var ext = SelectedFormat switch
            {
                ExportFormat.Csv => ".csv",
                ExportFormat.ExcelXml => ".xml",
                ExportFormat.Json => ".json",
                ExportFormat.SqlInsert => ".sql",
                _ => ".csv"
            };

            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                var dir = Path.GetDirectoryName(FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                FilePath = Path.Combine(dir, nameWithoutExt + ext);
            }
            OnPropertyChanged(nameof(IsCsvFormat));
            OnPropertyChanged(nameof(IsSqlInsertFormat));
        }

        private void ExecuteBrowseFile()
        {
            var sfd = new SaveFileDialog
            {
                FileName = Path.GetFileName(FilePath),
                Filter = SelectedFormat switch
                {
                    ExportFormat.Csv => "Arquivo CSV (*.csv)|*.csv|Todos os Arquivos (*.*)|*.*",
                    ExportFormat.ExcelXml => "Planilha Excel XML (*.xml;*.xls)|*.xml;*.xls|Todos os Arquivos (*.*)|*.*",
                    ExportFormat.Json => "Arquivo JSON (*.json)|*.json|Todos os Arquivos (*.*)|*.*",
                    ExportFormat.SqlInsert => "Script SQL (*.sql)|*.sql|Todos os Arquivos (*.*)|*.*",
                    _ => "Todos os Arquivos (*.*)|*.*"
                },
                DefaultExt = SelectedFormat == ExportFormat.ExcelXml ? "xml" : "csv"
            };

            if (sfd.ShowDialog() == true)
            {
                FilePath = sfd.FileName;
            }
        }

        private void UpdatePreview()
        {
            try
            {
                if (_table.Rows.Count == 0)
                {
                    PreviewText = "(Sem registros para pré-visualização)";
                    return;
                }

                var options = BuildOptions();
                var previewRows = _table.Rows.Cast<DataRow>().Take(5).ToList();
                PreviewText = _exportService.FormatCsvString(_table, options, previewRows);
            }
            catch
            {
                PreviewText = "(Pré-visualização indisponível)";
            }
        }

        private ExportOptions BuildOptions()
        {
            return new ExportOptions
            {
                Format = SelectedFormat,
                FilePath = FilePath,
                Delimiter = Delimiter,
                TextQualifier = TextQualifier,
                EncodingName = EncodingName,
                IncludeHeaders = IncludeHeaders,
                SelectedRowsOnly = SelectedRowsOnly,
                TableNameForInsert = _suggestedName
            };
        }

        private async Task ExecuteExportAsync()
        {
            IsExporting = true;
            IsSuccess = false;
            StatusMessage = "Exportando dados...";

            try
            {
                var options = BuildOptions();
                await _exportService.ExportDataTableAsync(_table, options);
                IsSuccess = true;
                StatusMessage = $"Exportação de {_table.Rows.Count:N0} linha(s) concluída com sucesso!";
            }
            catch (Exception ex)
            {
                IsSuccess = false;
                StatusMessage = $"Erro na exportação: {ex.Message}";
            }
            finally
            {
                IsExporting = false;
            }
        }
    }
}
