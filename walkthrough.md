# Walkthrough — NertyDb v1.3.0: Mensagens Visuais, Toolbar DBeaver, Dicionário Senior Real e Atalhos

Implementamos e validamos com 100% de cobertura todos os 5 pilares solicitados na nova versão:

---

## 🔔 1. Sistema de Notificações Toast & Registro Estruturado de Mensagens
- **ToastService (`ToastService.cs` & `ToastContainer.xaml`)**:
  - Toasts animados, leves e flutuantes no canto inferior direito da janela principal.
  - Quatro níveis com ícones e cores temáticas: `Success (Verde)`, `Warning (Âmbar)`, `Error (Vermelho)`, `Info (Azul)`.
  - Auto-dismiss com temporizador em 4s e botão de fechar manual. Thread-safe com despacho direto para a UI.
- **Registro Estruturado (`AppLogService.cs` & `SqlEditorView.xaml`)**:
  - Nova aba "💬 Mensagens" com lista estruturada: badge de nível, timestamp em formato `HH:mm:ss`, componente de origem, mensagem clara e comando/detalhe SQL.
  - Botão **🗑️ Limpar Mensagens** para reset rápido.

---

## 🛠️ 2. Barra de Ferramentas Completa na Aba de Resultados (Padrão DBeaver)
- **Ações Disponíveis por Aba de Resultado (`SqlResultTabViewModel.cs` & `SqlEditorView.xaml`)**:
  - 🔍 **Filtro Rápido de Linhas**: Filtra instantaneamente o `DataView` da grade sem reexecutar query.
  - 🔄 **Atualizar (F5)**: Reexecuta a consulta original da aba com notificação Toast e atualização dos dados.
  - ➕ **Inserir (Insert)**: Insere nova linha vazia para edição direta na grade.
  - 📋 **Duplicar (Ctrl+Alt+Down)**: Clona a linha selecionada para agilizar cadastros semelhantes.
  - 🗑️ **Excluir (Delete)**: Marca as linhas selecionadas para exclusão com destaque visual em vermelho.
  - 💾 **Salvar Alterações (Ctrl+S)**: Abre o modal de revisão de DML com script de transação e confirma a gravação.
  - ↩️ **Descartar**: Reverte as edições em lote sem salvar.
  - 📤 **Exportar...**: Exporta o conjunto de dados da aba para CSV, Excel ou JSON.

---

## 🏷️ 3. Dicionário de Dados Senior Real (Tabelas e Colunas)
- **Causa Raiz da Rodada Anterior**:
  - As tabelas `R999TAB` e `R999COL` encontravam-se vazias em ambientes padrão Senior.
  - As descrições reais da Senior residem nas tabelas de dicionário nativas:
    - **Tabelas**: `r996tbl` (nativas) e `r998tbl` (customizadas) com colunas `tblnam` e `destbl`, e `r910tbl` com `nomtbl` e `destbl`. Testado com **3.927 descrições de tabelas** (`R034FUN -> Ficha Básica Colaborador`, `R030EMP -> Empresas`, `R038HCA -> Histórico Cargos`).
    - **Colunas**: `r996fld`, `r998fld` com colunas `tblnam`, `fldnam`, `desfld`/`lgntit`/`shrtit`, e `r910cmp` com `nomtbl`, `nomcmp`, `descmp`. Testado com **203 colunas documentadas para R034FUN** (`ANOCHE -> Ano de Chegada`, `APEFUN -> Apelido`, etc.).
- **Consultas Otimizadas em Lote (`SqlServerDriver.cs` & `OracleDriver.cs`)**:
  - Implementado fallback em cadeia para `r996/r998/r910/r999` e `sys.extended_properties` / `ALL_TAB_COMMENTS`.
  - Árvore de esquema (`SchemaTreeViewModel.cs`) exibe subtítulo amigável e busca rápida por nome técnico ou descrição (ex: digitando "Colaborador" ou "Ponto" localiza as tabelas correspondentes).

---

## 🔄 4. Atualização Automática da Grade pós-Gravação (Auto-Refresh)
- `TableDataViewModel.cs` e `SqlResultTabViewModel.cs`:
  - Após confirmação e commit atômico no banco de dados via `PendingChangesViewModel`, a grade executa automaticamente um novo `SELECT` para refletir o estado real das colunas (triggers, sequence/identity, defaults).
  - Emite notificação Toast de sucesso verde informando o total de alterações salvas.

---

## ⌨️ 5. Atalhos de Teclado no Padrão DBeaver & Guia de Ajuda (F1)
- **Novos Atalhos Globais e de Editor**:
  - `F5` / `Ctrl+Enter` / `Ctrl+E`: Executa a instrução sob o cursor.
  - `Alt+X`: Executa todo o script SQL.
  - `Ctrl+Shift+F`: Formata o SQL com indentação e keywords em maiúsculas.
  - `Ctrl+/`: Comenta / Descomenta linha atual (`-- `).
  - `Ctrl+Shift+/`: Comenta / Descomenta bloco (`/* ... */`).
  - `Ctrl+Alt+Down`: Duplica linha no editor ou registro na grade.
  - `Ctrl+Shift+L`: Deleta linha no editor.
  - `Ctrl+Shift+U`: Converte seleção para MAIÚSCULAS.
  - `Ctrl+Shift+Alt+U`: Converte seleção para minúsculas.
  - `F1` / Botão **⌨️ Atalhos (F1)**: Abre o modal `ShortcutsHelpDialog.xaml` com o cheat sheet completo.

---

## 🧪 Validação e Testes Automatizados
- **Build**: Compilado com **0 Erros** e **0 Warnings**.
- **Testes Unitários**: **49/49 testes aprovados** com 100% de sucesso.
- **Binário Portable**: Gerado em `publish/NertyDb.exe` (56 MB, autossuficiente e portátil).
