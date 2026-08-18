using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public enum SchemaNodeType
    {
        Connection,
        Database,
        FolderTables,
        FolderViews,
        Table,
        View,
        FolderColumns,
        Column,
        FolderKeys,
        Key,
        FolderIndexes,
        Index
    }

    public class SchemaNode : ObservableObject
    {
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isVisible = true;
        private bool _childrenLoaded;

        public SchemaNodeType NodeType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SubTitle { get; set; } = string.Empty;
        public string Schema { get; set; } = "dbo";
        public string Database { get; set; } = string.Empty;
        public ConnectionProfile? Connection { get; set; }
        public object? Tag { get; set; }
        public SchemaNode? Parent { get; set; }

        public ObservableCollection<SchemaNode> Children { get; } = new();

        public bool ChildrenLoaded
        {
            get => _childrenLoaded;
            set => SetProperty(ref _childrenLoaded, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    if (value && !_childrenLoaded && (NodeType == SchemaNodeType.Table || NodeType == SchemaNodeType.View))
                    {
                        _ = LoadTableColumnsAsync();
                    }
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public bool HasChildren => Children.Count > 0;

        public string Icon => NodeType switch
        {
            SchemaNodeType.Connection => "🔌",
            SchemaNodeType.Database => "🗄️",
            SchemaNodeType.FolderTables => "📁",
            SchemaNodeType.FolderViews => "📂",
            SchemaNodeType.Table => "📊",
            SchemaNodeType.View => "👁️",
            SchemaNodeType.FolderColumns => "📑",
            SchemaNodeType.Column => (Tag is ColumnMetadata c && c.IsPrimaryKey) ? "🔑" : "🔹",
            SchemaNodeType.FolderKeys => "🔑",
            SchemaNodeType.Key => "🔗",
            SchemaNodeType.FolderIndexes => "⚡",
            SchemaNodeType.Index => "📇",
            _ => "📄"
        };

        public async Task LoadTableColumnsAsync()
        {
            if (_childrenLoaded || Connection == null) return;
            _childrenLoaded = true;

            try
            {
                var driver = DbDriverFactory.GetDriver(Connection);
                var details = await MetadataCacheService.Instance.GetTableDetailsAsync(Connection, Database, Schema, Title, driver);

                Children.Clear();

                // 1. Folder Columns
                var colFolder = new SchemaNode
                {
                    NodeType = SchemaNodeType.FolderColumns,
                    Title = "Colunas",
                    SubTitle = $"({details.Columns.Count})",
                    Connection = Connection,
                    Database = Database,
                    Schema = Schema,
                    Parent = this,
                    IsExpanded = true
                };

                foreach (var col in details.Columns)
                {
                    var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                    var pkStr = col.IsPrimaryKey ? " [PK]" : "";
                    colFolder.Children.Add(new SchemaNode
                    {
                        NodeType = SchemaNodeType.Column,
                        Title = col.Name,
                        SubTitle = $"{col.FullTypeDescription}, {nullStr}{pkStr}",
                        Connection = Connection,
                        Database = Database,
                        Schema = Schema,
                        Tag = col,
                        Parent = colFolder
                    });
                }
                Children.Add(colFolder);

                // 2. Folder Primary Keys / Keys
                if (details.PrimaryKeyColumns.Count > 0)
                {
                    var keyFolder = new SchemaNode
                    {
                        NodeType = SchemaNodeType.FolderKeys,
                        Title = "Chave Primária",
                        SubTitle = $"({details.PrimaryKeyColumns.Count})",
                        Connection = Connection,
                        Database = Database,
                        Schema = Schema,
                        Parent = this
                    };
                    foreach (var pkCol in details.PrimaryKeyColumns)
                    {
                        keyFolder.Children.Add(new SchemaNode
                        {
                            NodeType = SchemaNodeType.Key,
                            Title = pkCol,
                            SubTitle = "Chave Primária (PK)",
                            Connection = Connection,
                            Database = Database,
                            Schema = Schema,
                            Parent = keyFolder
                        });
                    }
                    Children.Add(keyFolder);
                }

                // 3. Foreign Keys
                if (details.ForeignKeys.Count > 0)
                {
                    var fkFolder = new SchemaNode
                    {
                        NodeType = SchemaNodeType.FolderKeys,
                        Title = "Chaves Estrangeiras (FK)",
                        SubTitle = $"({details.ForeignKeys.Count})",
                        Connection = Connection,
                        Database = Database,
                        Schema = Schema,
                        Parent = this
                    };
                    foreach (var fk in details.ForeignKeys)
                    {
                        fkFolder.Children.Add(new SchemaNode
                        {
                            NodeType = SchemaNodeType.Key,
                            Title = $"{fk.ColumnName} -> {fk.ReferencedTable}.{fk.ReferencedColumn}",
                            SubTitle = fk.ConstraintName,
                            Connection = Connection,
                            Database = Database,
                            Schema = Schema,
                            Parent = fkFolder
                        });
                    }
                    Children.Add(fkFolder);
                }

                // 4. Indexes
                if (details.Indexes.Count > 0)
                {
                    var idxFolder = new SchemaNode
                    {
                        NodeType = SchemaNodeType.FolderIndexes,
                        Title = "Índices",
                        SubTitle = $"({details.Indexes.Count})",
                        Connection = Connection,
                        Database = Database,
                        Schema = Schema,
                        Parent = this
                    };
                    foreach (var idx in details.Indexes)
                    {
                        idxFolder.Children.Add(new SchemaNode
                        {
                            NodeType = SchemaNodeType.Index,
                            Title = idx.Name,
                            SubTitle = $"{idx.TypeDescription} ({string.Join(", ", idx.Columns)})",
                            Connection = Connection,
                            Database = Database,
                            Schema = Schema,
                            Parent = idxFolder
                        });
                    }
                    Children.Add(idxFolder);
                }
            }
            catch { }
        }
    }

    public class SchemaTreeViewModel : ObservableObject
    {
        private readonly Action<ConnectionProfile, string, string, string> _onOpenTable;
        private readonly Action<ConnectionProfile, string, string> _onNewQueryWithSql;
        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _filterCts;

        public ObservableCollection<SchemaNode> RootNodes { get; } = new();
        public ObservableCollection<SchemaNode> FilteredNodes { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    DebounceApplyFilter();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenTableCommand { get; }
        public ICommand CountRowsCommand { get; }
        public ICommand GenerateSelectCommand { get; }
        public ICommand GenerateInsertCommand { get; }
        public ICommand GenerateCreateTableCommand { get; }
        public ICommand CopyNameCommand { get; }

        public SchemaTreeViewModel(
            Action<ConnectionProfile, string, string, string> onOpenTable,
            Action<ConnectionProfile, string, string> onNewQueryWithSql)
        {
            _onOpenTable = onOpenTable;
            _onNewQueryWithSql = onNewQueryWithSql;

            RefreshCommand = new AsyncRelayCommand(async (node) =>
            {
                if (node is SchemaNode sn && sn.Connection != null)
                {
                    MetadataCacheService.Instance.ClearCache(sn.Connection.Id);
                    await LoadDatabaseStructureAsync(sn.Connection, sn.Database);
                }
            });

            OpenTableCommand = new RelayCommand((node) =>
            {
                if (node is SchemaNode sn && (sn.NodeType == SchemaNodeType.Table || sn.NodeType == SchemaNodeType.View) && sn.Connection != null)
                {
                    _onOpenTable(sn.Connection, sn.Database, sn.Schema, sn.Title);
                }
            });

            CountRowsCommand = new RelayCommand((node) =>
            {
                if (node is SchemaNode sn && sn.NodeType == SchemaNodeType.Table && sn.Connection != null)
                {
                    var sql = sn.Connection.DatabaseType == DatabaseType.Oracle
                        ? $"SELECT COUNT(1) AS TotalLinhas FROM \"{sn.Schema}\".\"{sn.Title}\";"
                        : $"SELECT COUNT_BIG(1) AS [TotalLinhas] FROM [{sn.Schema}].[{sn.Title}];";
                    _onNewQueryWithSql(sn.Connection, sn.Database, sql);
                }
            });

            GenerateSelectCommand = new RelayCommand((node) =>
            {
                if (node is SchemaNode sn && sn.Connection != null)
                {
                    var sql = sn.Connection.DatabaseType == DatabaseType.Oracle
                        ? $"SELECT * FROM \"{sn.Schema}\".\"{sn.Title}\" WHERE ROWNUM <= 100;"
                        : $"SELECT TOP (100) * \r\nFROM [{sn.Schema}].[{sn.Title}]\r\nORDER BY 1 DESC;";
                    _onNewQueryWithSql(sn.Connection, sn.Database, sql);
                }
            });

            GenerateInsertCommand = new RelayCommand((node) =>
            {
                if (node is SchemaNode sn && sn.Connection != null)
                {
                    var sql = sn.Connection.DatabaseType == DatabaseType.Oracle
                        ? $"-- Modelo de INSERT para {sn.Schema}.{sn.Title}\r\nINSERT INTO \"{sn.Schema}\".\"{sn.Title}\" (\r\n    /* Colunas */\r\n)\r\nVALUES (\r\n    /* Valores */\r\n);"
                        : $"-- Modelo de INSERT para {sn.Schema}.{sn.Title}\r\nINSERT INTO [{sn.Schema}].[{sn.Title}] (\r\n    /* Colunas */\r\n)\r\nVALUES (\r\n    /* Valores */\r\n);";
                    _onNewQueryWithSql(sn.Connection, sn.Database, sql);
                }
            });

            GenerateCreateTableCommand = new AsyncRelayCommand(async (node) =>
            {
                if (node is SchemaNode sn && sn.Connection != null)
                {
                    var driver = DbDriverFactory.GetDriver(sn.Connection);
                    var details = await MetadataCacheService.Instance.GetTableDetailsAsync(sn.Connection, sn.Database, sn.Schema, sn.Title, driver);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"-- Definição de tabela: {sn.Schema}.{sn.Title}");
                    sb.AppendLine($"CREATE TABLE [{sn.Schema}].[{sn.Title}] (");
                    for (int i = 0; i < details.Columns.Count; i++)
                    {
                        var col = details.Columns[i];
                        var comma = i < details.Columns.Count - 1 ? "," : "";
                        var nullability = col.IsNullable ? "NULL" : "NOT NULL";
                        var identity = col.IsIdentity ? " IDENTITY(1,1)" : "";
                        var def = !string.IsNullOrEmpty(col.DefaultValue) ? $" DEFAULT {col.DefaultValue}" : "";
                        sb.AppendLine($"    [{col.Name}] {col.FullTypeDescription}{identity} {nullability}{def}{comma}");
                    }
                    if (details.PrimaryKeyColumns.Count > 0)
                    {
                        sb.AppendLine($"    ,CONSTRAINT [PK_{sn.Title}] PRIMARY KEY ({string.Join(", ", details.PrimaryKeyColumns.Select(c => $"[{c}]"))})");
                    }
                    sb.AppendLine(");");
                    _onNewQueryWithSql(sn.Connection, sn.Database, sb.ToString());
                }
            });

            CopyNameCommand = new RelayCommand((node) =>
            {
                if (node is SchemaNode sn)
                {
                    var formatted = sn.Connection?.DatabaseType == DatabaseType.Oracle
                        ? $"\"{sn.Schema}\".\"{sn.Title}\""
                        : $"[{sn.Schema}].[{sn.Title}]";

                    System.Windows.Clipboard.SetText(sn.NodeType == SchemaNodeType.Table || sn.NodeType == SchemaNodeType.View
                        ? formatted
                        : sn.Title);
                }
            });
        }

        public async Task LoadDatabaseStructureAsync(ConnectionProfile profile, string selectedDatabase)
        {
            IsLoading = true;
            StatusMessage = $"Carregando estrutura de {profile.Name} ({selectedDatabase})...";

            try
            {
                var driver = DbDriverFactory.GetDriver(profile);
                var tables = await MetadataCacheService.Instance.GetTablesAsync(profile, selectedDatabase, driver, forceRefresh: true);

                var connNode = RootNodes.FirstOrDefault(n => n.Connection?.Id == profile.Id);
                if (connNode == null)
                {
                    connNode = new SchemaNode
                    {
                        NodeType = SchemaNodeType.Connection,
                        Title = profile.Name,
                        SubTitle = $"{profile.DatabaseType} • {profile.Server}",
                        Connection = profile,
                        Database = selectedDatabase,
                        IsExpanded = true
                    };
                    RootNodes.Add(connNode);
                }

                connNode.Children.Clear();

                var dbNode = new SchemaNode
                {
                    NodeType = SchemaNodeType.Database,
                    Title = selectedDatabase,
                    SubTitle = $"{tables.Count} objetos",
                    Connection = profile,
                    Database = selectedDatabase,
                    Parent = connNode,
                    IsExpanded = true
                };
                connNode.Children.Add(dbNode);

                var tablesFolder = new SchemaNode
                {
                    NodeType = SchemaNodeType.FolderTables,
                    Title = "Tabelas",
                    SubTitle = $"({tables.Count(t => !t.IsView)})",
                    Connection = profile,
                    Database = selectedDatabase,
                    Parent = dbNode,
                    IsExpanded = true
                };

                var viewsFolder = new SchemaNode
                {
                    NodeType = SchemaNodeType.FolderViews,
                    Title = "Views",
                    SubTitle = $"({tables.Count(t => t.IsView)})",
                    Connection = profile,
                    Database = selectedDatabase,
                    Parent = dbNode
                };

                foreach (var t in tables)
                {
                    var itemNode = new SchemaNode
                    {
                        NodeType = t.IsView ? SchemaNodeType.View : SchemaNodeType.Table,
                        Title = t.Name,
                        Schema = t.Schema,
                        SubTitle = t.IsView ? t.Schema : $"{t.Schema} ({(t.RowCount >= 0 ? $"{t.RowCount:N0} lins" : "")})",
                        Connection = profile,
                        Database = selectedDatabase,
                        Tag = t,
                        Parent = t.IsView ? viewsFolder : tablesFolder,
                        IsExpanded = false
                    };

                    // Add dummy child so WPF treeview displays the expand arrow [+]
                    itemNode.Children.Add(new SchemaNode { Title = "Carregando colunas...", Parent = itemNode });

                    if (t.IsView)
                        viewsFolder.Children.Add(itemNode);
                    else
                        tablesFolder.Children.Add(itemNode);
                }

                dbNode.Children.Add(tablesFolder);
                if (viewsFolder.Children.Count > 0)
                {
                    dbNode.Children.Add(viewsFolder);
                }

                DebounceApplyFilter();
                StatusMessage = $"{tables.Count} tabelas e views carregadas com sucesso.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erro ao listar objetos: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void DebounceApplyFilter()
        {
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var term = _searchText?.Trim() ?? string.Empty;

                    // If not empty, debounce by 120ms to allow typing
                    if (!string.IsNullOrEmpty(term))
                    {
                        await Task.Delay(120, token);
                    }

                    if (token.IsCancellationRequested) return;

                    List<SchemaNode> results = new();

                    if (string.IsNullOrEmpty(term))
                    {
                        results = RootNodes.ToList();
                    }
                    else
                    {
                        foreach (var root in RootNodes)
                        {
                            if (token.IsCancellationRequested) return;
                            var filtered = FilterNodeFast(root, term, token);
                            if (filtered != null)
                            {
                                results.Add(filtered);
                            }
                        }
                    }

                    if (token.IsCancellationRequested) return;

                    var app = System.Windows.Application.Current;
                    if (app != null)
                    {
                        await app.Dispatcher.InvokeAsync(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            FilteredNodes.Clear();
                            foreach (var node in results)
                            {
                                FilteredNodes.Add(node);
                            }
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, token);
        }

        private SchemaNode? FilterNodeFast(SchemaNode node, string term, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            // 1. Table or View leaf nodes
            if (node.NodeType == SchemaNodeType.Table || node.NodeType == SchemaNodeType.View)
            {
                bool matches = IsFuzzyMatch(node.Title, term) || IsFuzzyMatch(node.Schema, term);
                if (!matches) return null;

                var tableCopy = new SchemaNode
                {
                    NodeType = node.NodeType,
                    Title = node.Title,
                    SubTitle = node.SubTitle,
                    Schema = node.Schema,
                    Database = node.Database,
                    Connection = node.Connection,
                    Tag = node.Tag,
                    IsExpanded = false, // CRITICAL: keep table collapsed so columns are not queried en masse
                    ChildrenLoaded = node.ChildrenLoaded
                };

                if (!node.ChildrenLoaded)
                {
                    tableCopy.Children.Add(new SchemaNode { Title = "Carregando colunas...", Parent = tableCopy });
                }
                else
                {
                    foreach (var child in node.Children)
                    {
                        tableCopy.Children.Add(child);
                    }
                }

                return tableCopy;
            }

            // 2. Container nodes (Connection, Database, FolderTables, FolderViews)
            var matchedChildren = new List<SchemaNode>();
            foreach (var child in node.Children)
            {
                if (token.IsCancellationRequested) return null;
                var filteredChild = FilterNodeFast(child, term, token);
                if (filteredChild != null)
                {
                    matchedChildren.Add(filteredChild);
                }
            }

            bool selfMatches = IsFuzzyMatch(node.Title, term);

            if (selfMatches || matchedChildren.Count > 0)
            {
                var containerCopy = new SchemaNode
                {
                    NodeType = node.NodeType,
                    Title = node.Title,
                    SubTitle = (node.NodeType == SchemaNodeType.FolderTables || node.NodeType == SchemaNodeType.FolderViews)
                        ? $"({matchedChildren.Count})"
                        : node.SubTitle,
                    Schema = node.Schema,
                    Database = node.Database,
                    Connection = node.Connection,
                    Tag = node.Tag,
                    IsExpanded = true, // Expand container folders so matched tables are visible
                    ChildrenLoaded = true
                };

                foreach (var mc in matchedChildren)
                {
                    mc.Parent = containerCopy;
                    containerCopy.Children.Add(mc);
                }

                return containerCopy;
            }

            return null;
        }

        public static bool IsFuzzyMatch(string? text, string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return true;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Direct case-insensitive substring match (fast path)
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;

            // Subsequence match with zero memory allocations
            ReadOnlySpan<char> textSpan = text.AsSpan();
            ReadOnlySpan<char> patternSpan = pattern.AsSpan();

            int textIndex = 0;
            int patternIndex = 0;

            while (textIndex < textSpan.Length && patternIndex < patternSpan.Length)
            {
                if (char.ToLowerInvariant(textSpan[textIndex]) == char.ToLowerInvariant(patternSpan[patternIndex]))
                {
                    patternIndex++;
                }
                textIndex++;
            }

            return patternIndex == patternSpan.Length;
        }
    }
}
