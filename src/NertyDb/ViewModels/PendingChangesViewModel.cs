using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class PendingChangesViewModel : ObservableObject
    {
        private readonly IDbDriver _driver;
        private readonly Func<Task> _onSuccess;

        private bool _isExecuting;
        private string _statusMessage = string.Empty;
        private bool _hasError;
        private string? _errorMessage;
        private string _generatedSqlScript = string.Empty;

        public ConnectionProfile Connection { get; }
        public string Database { get; }
        public string Schema { get; }
        public string TableName { get; }

        public ObservableCollection<PendingChange> Changes { get; } = new();

        public int UpdatesCount => Changes.Count(c => c.Type == ChangeType.Update);
        public int InsertsCount => Changes.Count(c => c.Type == ChangeType.Insert);
        public int DeletesCount => Changes.Count(c => c.Type == ChangeType.Delete);
        public int TotalCount => Changes.Count;

        public string SummaryText => $"{TotalCount} alteração(ões) pendente(s) em {Schema}.{TableName}: {UpdatesCount} UPDATE(s), {InsertsCount} INSERT(s), {DeletesCount} DELETE(s)";

        public string GeneratedSqlScript
        {
            get => _generatedSqlScript;
            private set => SetProperty(ref _generatedSqlScript, value);
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public ICommand CommitCommand { get; }
        public ICommand CopyScriptCommand { get; }

        public event EventHandler? RequestClose;

        public PendingChangesViewModel(
            ConnectionProfile connection,
            string database,
            string schema,
            string tableName,
            List<PendingChange> changes,
            IDbDriver driver,
            Func<Task> onSuccess)
        {
            Connection = connection;
            Database = database;
            Schema = schema;
            TableName = tableName;
            _driver = driver;
            _onSuccess = onSuccess;

            foreach (var c in changes)
            {
                Changes.Add(c);
            }

            GeneratedSqlScript = DmlGenerator.GenerateTransactionScript(changes);

            CommitCommand = new AsyncRelayCommand(ExecuteCommitAsync, () => !IsExecuting && TotalCount > 0);
            
            CopyScriptCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(GeneratedSqlScript))
                {
                    ClipboardHelper.SetText(GeneratedSqlScript);
                    StatusMessage = "Script SQL copiado para a Área de Transferência!";
                    ToastService.Instance.ShowSuccess("Script SQL copiado com sucesso.", "Copiado");
                }
            });
        }

        private async Task ExecuteCommitAsync()
        {
            IsExecuting = true;
            HasError = false;
            ErrorMessage = null;
            StatusMessage = "Gravando alterações no banco de dados com transação atômica...";

            try
            {
                var result = await _driver.ExecuteDmlBatchAsync(Connection, Database, Changes.ToList());

                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await _onSuccess();
                    RequestClose?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    HasError = true;
                    ErrorMessage = result.Message;
                    StatusMessage = "Erro ao executar alterações no banco de dados.";
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
                StatusMessage = "Exceção ao gravar alterações.";
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }
}
