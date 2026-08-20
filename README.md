# ⚡ NertyDb — Cliente de Banco de Dados Senior & DBeaver-Inspired

[![Download NertyDb Portable](https://img.shields.io/badge/Download-NertyDb.exe_(Portable_v1.5.0)-blue?style=for-the-badge&logo=windows)](https://github.com/BrendoNyre/NertyDb/releases/download/v1.5.0/NertyDb-v1.5.0-Portable.exe)
[![GitHub Release](https://img.shields.io/github/v/release/BrendoNyre/NertyDb?style=for-the-badge&color=green)](https://github.com/BrendoNyre/NertyDb/releases/tag/v1.5.0)
[![Developer Guide](https://img.shields.io/badge/Documentação-Guia_do_Desenvolvedor-orange?style=for-the-badge)](./DEVELOPER_GUIDE.md)

> 🚀 **[Clique aqui para Baixar o Executável Direto (`NertyDb.exe` v1.5.0)](https://github.com/BrendoNyre/NertyDb/releases/tag/v1.5.0)** — *100% portable, executável único pronto para uso, sem instalador e sem necessidade de pré-instalar o .NET em nenhum computador.*

O **NertyDb** é um cliente de banco de dados desktop **100% portable para Windows** (executável único `.exe`, sem instalador e sem dependências externas no servidor do cliente), desenvolvido com foco nas rotinas de suporte, consulta e análise dos sistemas **Senior** (Gestão de Ponto, Controle de Ponto e Ronda, Acesso, HCM, ERP, etc.), com suporte nativo a **Microsoft SQL Server** e **Oracle Database**.

Ele combina a facilidade de logon com usuários do **Senior SGU**, o dicionário de dados oficial das tabelas Senior e a ergonomia avançada do **DBeaver** (edição inline, barra inferior de resultados, duplicação real, edição em massa e atalhos de teclado).

---

## 🎯 Principais Funcionalidades

### 1. Grade de Resultados no Padrão DBeaver (Core Feature)
- **Barra de Ferramentas Inferior:** Posicionada dentro da aba de resultados, logo abaixo do grid.
- **Duplicação Real de Linhas:** Clona a linha selecionada, preserva campos de PK editáveis, ignora colunas Identity/Auto-Increment e permite editar os dados antes de salvar (`Ctrl+Alt+Down`).
- **Edição em Massa e Colagem Matricial:**
  - Selecione várias células de uma coluna e digite para atualizar todas simultaneamente.
  - Copie células de planilhas (Excel/TSV) e cole (`Ctrl+V`) distribuindo os valores por linhas e colunas.
- **Exclusão Múltipla Segura:** Exclua múltiplos registros com marcação visual vermelha e confirmação em transação atômica.
- **Identificação Segura de Chave Primária (PK):** Extrai a PK real dos metadados do banco para gerar `UPDATE` e `DELETE` precisos (`WHERE pk1 = val1 AND pk2 = val2`).
- **Bloqueio Automático de Queries Complexas:** Consultas com `JOIN`, `GROUP BY`, `UNION` ou agregações são marcadas como **🔒 Somente Leitura** para evitar alterações corrompidas.
- **Limitador de Linhas Real:** Opções de 50, 100, 200, 500, 1000 ou Sem Limite direto na leitura do driver.

### 2. Autenticação Senior SGU Integrada
- Validação automática de senhas de usuários do SGU Senior direto nas tabelas de segurança (`R900PPL` e `R900PDT`).
- Suporte a grupos, permissões e múltiplos bancos sem precisar de usuário `sa` ou `SYSDBA`.

### 3. Dicionário de Metadados Senior
- Carregamento automático de descrições e títulos amigáveis de milhares de tabelas e campos do Senior (`R996TBL`, `R998TBL`, `R910TBL`, `R996FLD`, `R998FLD`, `R910CMP`).
- Tooltips ricos na árvore lateral e no cabeçalho das colunas.

### 4. Sistema de Notificações Toast & Logs Estruturados
- Alertas flutuantes no canto inferior direito para sucessos, avisos e erros com auto-dismiss.
- Aba **💬 Mensagens** com histórico de execuções, tempos de resposta e tags de componentes.

### 5. Editor SQL com Autocomplete e Modelos Senior
- Realce de sintaxe SQL com colorização temática e Autocomplete (`Ctrl+Espaço`).
- Execução com `F5` ou `Ctrl+Enter`, e botão de cancelamento assíncrono.
- Modelos prontos para Ponto (`R070ACC`), Apurações (`R070CON`), Colaboradores (`R034FUN`), Crachás (`R034CRA`) e Rondas (`R070OCO`).

### 6. Assistente de Exportação Completo
- Exportação para **CSV** (ANSI Windows-1252, UTF-8 BOM, ISO-8859-1), **Excel (XLSX)**, **JSON** e scripts **SQL INSERT**.

---

## ⌨️ Atalhos de Teclado (Padrão DBeaver)

| Atalho | Ação |
| :--- | :--- |
| **F5** ou **Ctrl + Enter** | Executa a query SQL selecionada ou todo o editor |
| **Ctrl + S** | Grava (Commit) as alterações pendentes da grade no banco |
| **Ctrl + Z** | Descarta (Rollback) todas as alterações pendentes da grade |
| **Insert** | Insere uma nova linha vazia na grade |
| **Ctrl + Alt + Seta Abaixo** | Duplica a linha selecionada na grade |
| **Delete** | Exclui as linhas selecionadas ou limpa as células para `NULL` |
| **Ctrl + Espaço** | Abre a lista de Autocomplete no Editor SQL |
| **Ctrl + / ou Ctrl + Shift + /** | Comenta / Descomenta linhas no Editor SQL |
| **Ctrl + Shift + F** | Formata e indenta o código SQL |
| **Ctrl + Shift + U** / **Ctrl + Shift + L** | Converte texto para MAIÚSCULAS / minúsculas |
| **F1** | Abre a janela de ajuda de atalhos de teclado |

---

## 📖 Como Abrir e Desenvolver em Outro Computador

Para instruções detalhadas de como clonar, compilar, debugar e rodar o projeto em outra máquina com Visual Studio, VS Code ou Rider, consulte o nosso guia dedicado:

👉 **[Consulte o Guia do Desenvolvedor (`DEVELOPER_GUIDE.md`)](./DEVELOPER_GUIDE.md)**

---

## 🏗️ Como Compilar o Executável Único Portable

```powershell
# Executar o script automatizado de compilação
.\build.ps1
```

O executável único compilado será gerado na pasta:
```text
publish/NertyDb.exe
```

---

## 🔒 Segurança e Privacidade

- **Criptografia Forte:** As credenciais de acesso são criptografadas com **AES-256-GCM** (com chave mestra) e fallback para **Windows DPAPI** (*Data Protection API*).
- **100% Offline:** O software não realiza nenhuma chamada para servidores externos ou serviços de telemetria, sendo totalmente seguro para ambientes corporativos e servidores de produção.

