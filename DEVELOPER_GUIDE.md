# 📘 Guia do Desenvolvedor — NertyDb

Este documento contém todas as instruções, detalhes de arquitetura, pré-requisitos e comandos necessários para clonar, configurar, compilar, testar e evoluir o projeto **NertyDb** em qualquer outro computador.

---

## 💻 1. Pré-requisitos do Ambiente

Para abrir e desenvolver o projeto em outro computador, você precisará de:

1. **Sistema Operacional**: Windows 10, Windows 11 ou Windows Server (64-bit). *(O WPF depende das APIs gráficas nativas do Windows).*
2. **.NET 8.0 SDK**: Versão `8.0.100` ou superior (com suporte ao runtime de desktop `Microsoft.WindowsDesktop.App`).
   - [Download oficial do .NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
   - Verifique a instalação no terminal: `dotnet --version`
3. **Git**: [Git for Windows](https://git-scm.com/)
4. **IDE / Editor (Escolha uma das opções)**:
   - **Visual Studio 2022** (Recomendado — Community, Professional ou Enterprise):
     - Selecione a carga de trabalho: *"Desenvolvimento para desktop com .NET"* (inclui WPF e ferramentas de diagnóstico XAML).
   - **Visual Studio Code**:
     - Instale as extensões: *C# Dev Kit*, *C#*, *XML Tools*.
   - **JetBrains Rider** (2023.3 ou superior).

---

## 🚀 2. Clonando e Executando o Projeto

### Passo 1: Clonar o Repositório
Abra o PowerShell ou Prompt de Comando na pasta desejada:
```bash
git clone https://github.com/BrendoNyre/NertyDb.git
cd NertyDb
```

### Passo 2: Restaurar as Dependências (NuGet)
```bash
dotnet restore
```

### Passo 3: Executar em Modo de Desenvolvimento (Debug)
```bash
dotnet run --project src/NertyDb/NertyDb.csproj
```
*Ou abra o arquivo de solução `NertyDb.sln` no Visual Studio e pressione **F5**.*

### Passo 4: Executar os Testes Automatizados
O projeto conta com **53 testes unitários** cobrindo todas as regras de negócio, drivers, geradores DML, criptografia e DBeaver Features:
```bash
dotnet test
```

---

## 📦 3. Como Gerar o Executável Único Portable (`.exe`)

Para compilar a versão final autossuficiente (single-file portable que não precisa de instalador nem de .NET pré-instalado no computador de destino):

### Opção A — Via Script Automatizado:
```powershell
.\build.ps1
```

### Opção B — Via Linha de Comando Direta do .NET CLI:
```bash
dotnet publish src/NertyDb/NertyDb.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

O binário final será gerado em:
```text
publish/NertyDb.exe
```

---

## 🏛️ 4. Arquitetura e Estrutura do Código-Fonte

O NertyDb segue o padrão de arquitetura **MVVM Puro (Model-View-ViewModel)** em .NET 8 WPF, com foco em alta performance, desacoplamento e zero dependências de terceiros no framework MVVM.

```text
NertyDb/
├── src/
│   └── NertyDb/
│       ├── App.xaml / App.xaml.cs            # Ponto de entrada WPF, tratamento global de exceções
│       │
│       ├── Data/                             # Camada de Acesso a Dados e Drivers
│       │   ├── IDbDriver.cs                  # Interface comum para drivers (SQL Server, Oracle, etc.)
│       │   ├── SqlServerDriver.cs            # Driver Microsoft SQL Server via Microsoft.Data.SqlClient
│       │   ├── OracleDriver.cs               # Driver Oracle via Oracle.ManagedDataAccess.Core
│       │   ├── DmlGenerator.cs               # Gerador atômico de DML (UPDATE/INSERT/DELETE) com PKs
│       │   └── QueryExecutionResult.cs       # Modelos de retorno de execução de queries
│       │
│       ├── Models/                           # Modelos de Domínio e Entidades
│       │   ├── ConnectionProfile.cs          # Perfil de conexão (Host, Porta, Banco, Tipo de Auth)
│       │   ├── TableMetadata.cs              # Metadados de tabelas, colunas, chaves primárias e FKs
│       │   ├── PendingChange.cs              # Registro de alterações pendentes na grade (Insert/Update/Delete)
│       │   ├── SchemaTreeNodes.cs            # Nós da árvore de navegação do banco
│       │   ├── AppSettings.cs                # Configurações do aplicativo (Tema, Limite padrão, etc.)
│       │   └── SelectionStats.cs             # Cálculos rápidos de seleção estilo Excel (Soma, Média, etc.)
│       │
│       ├── Services/                         # Serviços de Negócio e Infraestrutura
│       │   ├── SecurityService.cs            # Criptografia AES-256-GCM / DPAPI para senhas
│       │   ├── SguAuthenticationService.cs   # Validação de senhas do Senior SGU (R900PPL / R900PDT)
│       │   ├── MetadataCacheService.cs       # Cache e dicionário de tabelas/colunas Senior (R996/R998/R910)
│       │   ├── ExportService.cs              # Exportação para CSV, Excel (XLSX), JSON e SQL
│       │   ├── StorageService.cs             # Persistência de conexões e histórico local
│       │   ├── ToastService.cs               # Gerenciador de notificações flutuantes (Toasts)
│       │   ├── AppLogService.cs              # Sistema estruturado de logs e rastreabilidade
│       │   └── SeniorTemplates.cs            # Consultas e modelos SQL pré-configurados do Senior
│       │
│       ├── ViewModels/                       # Lógica de Apresentação (MVVM)
│       │   ├── MvvmBase.cs                   # ObservableObject, RelayCommand, AsyncRelayCommand
│       │   ├── MainViewModel.cs              # ViewModel principal (menu, status, conexões ativas)
│       │   ├── SqlEditorViewModel.cs         # Gerenciamento de abas SQL, execução assíncrona, PageSize
│       │   ├── SqlResultTabViewModel.cs      # Grade de resultados estilo DBeaver, edição em massa, PKs
│       │   ├── TableDataViewModel.cs         # Visualizador direto de tabelas com paginação e edição
│       │   ├── ConnectionManagerViewModel.cs # CRUD e teste de perfis de conexão com medição de ping
│       │   ├── PendingChangesViewModel.cs    # Janela de revisão do script DML antes do commit
│       │   └── ExportViewModel.cs            # Assistente de exportação de dados
│       │
│       ├── Views/                            # Interfaces de Usuário em XAML
│       │   ├── MainWindow.xaml               # Janela principal com Ribbon, Explorer e abas
│       │   ├── SqlEditorView.xaml            # Editor AvalonEdit com aba de resultados e toolbar inferior
│       │   ├── TableDataView.xaml            # Visualizador de dados da tabela
│       │   ├── ConnectionManagerDialog.xaml  # Modal de gerenciamento de conexões
│       │   ├── PendingChangesDialog.xaml     # Modal de revisão de script SQL
│       │   ├── ExportDialog.xaml             # Modal de assistente de exportação
│       │   ├── KeyboardShortcutsDialog.xaml  # Modal de atalhos de teclado (F1)
│       │   └── ToastContainer.xaml           # Container visual de toasts no canto da tela
│       │
│       └── Resources/                        # Recursos Visuais, Estilos e Sintaxe
│           ├── Themes/DarkTheme.xaml         # Paleta de cores do tema Escuro
│           ├── Themes/LightTheme.xaml        # Paleta de cores do tema Claro
│           ├── Styles/                       # Estilos customizados de botões, inputs, DataGrid
│           └── SqlHighlighting.xshd          # Definição léxica de realce SQL para AvalonEdit
│
├── tests/
│   └── NertyDb.Tests/                        # Testes Automatizados (xUnit)
│       ├── DBeaverFeaturesTests.cs           # Testes de duplicação, edição em massa, DML seguro, Toasts
│       ├── DmlGeneratorTests.cs              # Testes de geração de UPDATE/INSERT/DELETE seguros
│       ├── ExportServiceTests.cs             # Testes de exportação CSV, Excel e JSON
│       ├── SecurityServiceTests.cs           # Testes de criptografia AES-256 e DPAPI
│       └── SguAuthenticationTests.cs         # Testes de decodificação e validação de senhas SGU
│
├── publish/                                  # Diretório de saída do executável NertyDb.exe
├── build.ps1                                 # Script de automação de build e testes
├── DEVELOPER_GUIDE.md                        # Este guia de desenvolvimento
└── README.md                                 # Documentação geral do projeto
```

---

## 🔐 5. Armazenamento de Dados Locais e Segurança

* **Onde as conexões e configurações ficam salvas?**
  * As conexões cadastradas pelo usuário são salvas em:
    ```text
    %APPDATA%\NertyDb\connections.json
    ```
  * O arquivo é protegido por criptografia via `SecurityService.cs`.
* **Criptografia de Senhas**:
  * O sistema utiliza **AES-256-GCM** quando uma senha mestra estiver definida, ou fallback para **Windows DPAPI** (*Data Protection API*), garantindo que senhas de bancos de dados nunca fiquem em texto claro no disco.

---

## 🎯 6. Funcionalidades do Padrão DBeaver Implementadas

Se você for evoluir a grade de resultados ou o editor SQL, atente-se para os componentes já estabelecidos:

1. **Barra de Ferramentas Inferior**: Posicionada dentro da aba de resultados, logo abaixo do `DataGrid`.
2. **Duplicação Real de Linhas**: Método `ExecuteDuplicateRow` clona a linha, aplica destaque verde, ignora colunas Identity/Auto-Increment e registra no buffer local de pendências (`PendingChanges`).
3. **Edição em Massa e Colagem Matricial**:
   - `ApplyBulkCellValues`: Aplica valor digitado a todas as células selecionadas na mesma coluna.
   - `HandleGridPaste`: Suporta colagem de valor único sobre seleção múltipla e colagem de matriz tabular (TSV copiado do Excel/planilha).
4. **Identificação Segura de Chave Primária**:
   - Extrai schema e tabela via `ExtractTableInfo`.
   - Consulta metadados reais via `ResolveTableAndPkInfoAsync` para preencher `PrimaryKeyColumns`.
   - Queries complexas (`JOIN`, `GROUP BY`, `UNION`, `COUNT`, `SUM`) são marcadas automaticamente como **🔒 Somente Leitura** para evitar alterações corrompidas no banco.
5. **Limitador de Linhas (`maxRows`)**:
   - Controlado na barra inferior (50, 100, 200, 500, 1000, Sem limite).
   - O leitor do driver interrompe a leitura diretamente no `DbDataReader`.

---

## ❓ 7. Solução de Problemas Comuns (Troubleshooting)

### A) Erro ao compilar: *"The process cannot access the file NertyDb.exe because it is being used by another process"*
* **Causa**: O aplicativo `NertyDb.exe` já está aberto em segundo plano ou em execução.
* **Solução**: Feche a janela do NertyDb ou encerre o processo no terminal:
  ```powershell
  Get-Process NertyDb -ErrorAction SilentlyContinue | Stop-Process -Force
  ```

### B) Visual Studio não exibe o designer XAML
* **Causa**: Falta da carga de trabalho Desktop do .NET no instalador do Visual Studio.
* **Solução**: Abra o *Visual Studio Installer* → Clique em *Modificar* → Marque *"Desenvolvimento para desktop com .NET"* e confirme.

### C) Testes falhando por porta ou ambiente
* Todos os testes em `NertyDb.Tests` utilizam mocks, emulação de dados em memória (`DataTable`) e validações isoladas, não dependendo de conexões de rede ativas nem de servidores reais ligados.

---

## 🤝 8. Fluxo de Contribuição e Git

Para enviar alterações para o repositório oficial:

```bash
# 1. Crie uma branch para a sua feature/correção
git checkout -b minha-melhoria

# 2. Faça as alterações e rode os testes
dotnet test

# 3. Commit das mudanças
git add .
git commit -m "feat: descricao clara da mudanca"

# 4. Envie para o GitHub
git push origin minha-melhoria
```
