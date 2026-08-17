# ⚡ NertyDb — Cliente de Banco de Dados Senior (Substituto do CBDS)

O **NertyDb** é um cliente de banco de dados desktop **100% portable para Windows** (executável único `.exe`, sem instalador e sem necessidade de pré-instalar o .NET ou qualquer dependência externa no servidor do cliente), desenvolvido com foco nas rotinas de suporte e análise dos sistemas **Senior** (Gestão de Ponto, Controle de Ponto e Ronda, Acesso e HCM).

Ele foi projetado para substituir o **CBDS** (nativo da Senior), superando todas as suas limitações arcaicas e trazendo a usabilidade, agilidade e ergonomia do **DBeaver** em um executável ultraleve e rápido.

---

## 🎯 Principais Funcionalidades

### 1. Conexão Rápida e Múltiplos Perfis
- Salva múltiplos perfis de clientes (Host/IP, Porta, Banco de Dados, Autenticação SQL Server ou Windows Integrada).
- Armazenamento seguro de senhas via **Windows DPAPI** (sem salvar senhas em texto puro).
- Teste de conexão instantâneo com medição de **latência em milissegundos** e detecção da versão do SQL Server.
- Suporte a múltiplas consultas e conexões simultâneas.
- **Portabilidade Total:** Todas as configurações, histórico e consultas favoritas são salvas localmente na pasta `data/` ao lado do `.exe` (não altera o Registro do Windows).

### 2. Navegador de Estrutura (Schema Explorer) com Busca Fuzzy
- Árvore lateral hierárquica: `Conexão → Banco → Schemas (dbo, etc.) → Tabelas e Views → Colunas e Chaves`.
- **Busca rápida fuzzy:** Encontre instantaneamente qualquer tabela ou coluna do Senior digitando termos curtos (ex: `r034`, `r070`, `ponto`, `batida`, `colab`).
- **Ações rápidas no clique direito:**
  - *Consultar Dados (Top 100)* com 1 duplo-clique.
  - *Contagem Rápida de Linhas (`COUNT_BIG`)*.
  - *Gerar SELECT*, *Gerar INSERT*, *Gerar CREATE TABLE*.
  - *Copiar Nome da Tabela*.

### 3. Grade de Dados com Edição Inline e Commit Explícito (Core Feature)
- **Visualização paginada:** Paginação sob demanda (50, 100, 500, 1000 ou todas as linhas) com ordenação por coluna e filtro rápido em memória.
- **Edição inline:** Duplo-clique em qualquer célula para alterar o valor diretamente no estilo Excel/planilha.
- **Inserção e Exclusão:**
  - Botão `+ Nova Linha`: Adiciona registro direto na grade.
  - Botão `🗑️ Excluir`: Marca registros selecionados para deleção.
- **Destaque visual de alterações (Visual Diff):**
  - Célula alterada: Fundo **Âmbar/Amarelo**.
  - Linha nova: Fundo **Verde**.
  - Linha excluída: Fundo **Vermelho** com texto tachado.
- **Revisão e Commit Seguro:**
  - O NertyDb **nunca grava nada sem confirmação**.
  - Ao clicar em `💾 Salvar (Commit)`, abre-se uma tela de revisão com os comandos SQL gerados (`UPDATE`, `INSERT`, `DELETE`) baseados nas **chaves primárias (PK)** da tabela.
  - Execução atômica dentro de transação (`BEGIN TRANSACTION ... COMMIT TRANSACTION`) com rollback automático em caso de erro.
  - Botão `↩️ Descartar (Rollback)` para desfazer todas as edições em memória com 1 clique.

### 4. Editor SQL com Autocomplete e Modelos Senior
- Realce de sintaxe SQL com colorização temática.
- Sugestão automática (Autocomplete) de tabelas e colunas do banco conectado (`Ctrl+Espaço`).
- Execução com `F5` ou `Ctrl+Enter`, e botão de cancelamento assíncrono para não travar a interface em queries pesadas.
- Abas de múltiplos resultados e console de mensagens com tempo de execução e linhas afetadas.
- **Modelos Senior integrados:**
  - Batidas não apuradas (`R070ACC`)
  - Apurações diárias consolidadas (`R070CON`)
  - Colaboradores ativos com filial e cargo (`R034FUN`)
  - Histórico de crachás (`R034CRA`)
  - Ocorrências de ronda (`R070OCO`)
  - Dicionário de tabelas Senior (`R999TAB`)
- Histórico automático das últimas consultas executadas com timestamp e status.

### 5. Assistente de Exportação Completo
- **Exportação para CSV:**
  - Delimitadores configuráveis (`;`, `,`, `\t (Tab)`, `|`).
  - Tratamento e escape automático de aspas, delimitadores e quebras de linha.
  - Suporte a codificações com acentuação brasileira: **UTF-8 com BOM**, **Windows-1252 (ANSI)** e **ISO-8859-1**.
- **Planilha Excel (SpreadsheetML / XML):** Abre diretamente no Microsoft Excel com cabeçalhos estilizados e tipos de dados corretos.
- **JSON:** Exportação estruturada em array de objetos.
- **Script SQL INSERT:** Gera arquivo `.sql` pronto para inserção em outros ambientes.
- Cópia rápida para a área de transferência (`Copiar Célula`, `Copiar Linhas como CSV`, `Copiar Linhas como INSERT`).

### 6. Usabilidade e Ergonomia
- Tema **Dark** e tema **Light** elegantes e de alto contraste.
- 100% offline, zero telemetria e sem dependência de internet.
- Inicialização em menos de 1 segundo.

---

## ⌨️ Principais Atalhos de Teclado

| Atalho | Ação |
| :--- | :--- |
| **F5** ou **Ctrl + Enter** | Executa a query SQL selecionada ou o script atual |
| **Ctrl + S** | Abre a revisão de alterações da grade para gravação (Commit) |
| **Ctrl + N** | Abre uma nova aba de consulta SQL |
| **Ctrl + O** | Abre o Gerenciador de Conexões |
| **Ctrl + Espaço** | Abre a lista de Autocomplete no Editor SQL |
| **Duplo-clique na Célula** | Edita o valor da célula inline |
| **Duplo-clique na Tabela** | Abre a grade de dados da tabela imediatamente |

---

## 🏗️ Como Compilar o Executável Único Portable

Para gerar o arquivo `NertyDb.exe` autônomo:

1. Abra o PowerShell na raiz do projeto `NertyDb`.
2. Execute o script de build automatizado:
   ```powershell
   .\build.ps1
   ```
3. O executável único compilado será gerado na pasta:
   ```
   publish/NertyDb.exe
   ```

Você pode copiar o `NertyDb.exe` diretamente para um pendrive, compartilhar via rede ou rodar em qualquer servidor Windows (Windows Server 2012 R2, 2016, 2019, 2022, Windows 10/11) sem instalar nada.

---

## 📁 Estrutura do Código-Fonte

```
NertyDb/
├── src/
│   └── NertyDb/
│       ├── Data/            # SqlServerDriver, DmlGenerator, IDbDriver, QueryResults
│       ├── Models/          # ConnectionProfile, TableMetadata, PendingChange, QueryModels
│       ├── Services/        # StorageService, ExportService, SeniorTemplates
│       ├── ViewModels/      # MVVM ViewModels (Main, TableData, SqlEditor, PendingChanges, Export, Connection)
│       ├── Views/           # Interfaces XAML (MainWindow, TableDataView, SqlEditorView, Dialogs)
│       └── Resources/       # Temas Dark/Light, Styles, SqlHighlighting.xshd
├── tests/
│   └── NertyDb.Tests/       # Testes unitários automatizados (DML, CSV/Excel/JSON, FuzzySearch, DPAPI)
├── publish/                 # Executável único portable final (NertyDb.exe)
├── build.ps1                # Script PowerShell de compilação
└── README.md                # Documentação do projeto
```

---

## 🔒 Segurança e Privacidade

- As senhas salvas são criptografadas com a API nativa do Windows (**DPAPI** - *Data Protection API*), garantindo que apenas a conta do usuário logado consiga descriptografar a chave.
- O software não faz nenhuma requisição externa para a internet, sendo seguro para uso em ambientes corporativos restritos e com políticas rígidas de compliance.
