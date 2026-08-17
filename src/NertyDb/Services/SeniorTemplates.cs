using System.Collections.Generic;
using NertyDb.Models;

namespace NertyDb.Services
{
    public static class SeniorTemplates
    {
        public static List<SeniorTemplate> GetBuiltInTemplates()
        {
            return new List<SeniorTemplate>
            {
                new SeniorTemplate
                {
                    Category = "Gestão de Ponto (R070)",
                    Title = "Batidas Não Apuradas / Não Processadas (R070ACC)",
                    Description = "Consulta marcações de ponto pendentes de apuração no coletor/relógio",
                    Sql = @"-- Consulta batidas de ponto recentes (R070ACC)
SELECT TOP (200) 
    NUMEMP AS Empresa,
    TIPCOL AS TipoColaborador,
    NUMCAD AS Matricula,
    DATACC AS DataBatida,
    HORACC AS HoraBatida,
    ORIBAT AS Origem,
    NUMREP AS NumeroREP,
    USUREP AS PIS,
    SITMAR AS SituacaoMarcacao
FROM R070ACC
ORDER BY DATACC DESC, HORACC DESC;"
                },
                new SeniorTemplate
                {
                    Category = "Gestão de Ponto (R070)",
                    Title = "Apurações de Ponto / Horas Calculadas (R070CON)",
                    Description = "Consulta apuração diária com horas trabalhadas, faltas, extras e situações",
                    Sql = @"-- Apurações diárias consolidadas (R070CON)
SELECT TOP (200)
    c.NUMEMP AS Empresa,
    c.TIPCOL AS TipoColab,
    c.NUMCAD AS Matricula,
    f.NOMFUN AS NomeFuncionario,
    c.DATAPU AS DataApuracao,
    c.CODESC AS CodEscala,
    c.CODHOR AS CodHorario,
    c.TOTTRB AS HorasTrabalhadas,
    c.TOTHEX AS HorasExtras,
    c.TOTNOT AS HorasNoturnas,
    c.TOTFAL AS HorasFalta
FROM R070CON c
LEFT JOIN R034FUN f ON f.NUMEMP = c.NUMEMP AND f.TIPCOL = c.TIPCOL AND f.NUMCAD = c.NUMCAD
ORDER BY c.DATAPU DESC, c.NUMCAD ASC;"
                },
                new SeniorTemplate
                {
                    Category = "Colaboradores (R034)",
                    Title = "Colaboradores Ativos com Cargo e Centro de Custo",
                    Description = "Listagem de funcionários ativos (R034FUN) com junção de cargos e postos",
                    Sql = @"-- Colaboradores ativos com filial e cargo
SELECT 
    f.NUMEMP AS Empresa,
    f.TIPCOL AS TipoColab,
    f.NUMCAD AS Matricula,
    f.NOMFUN AS Nome,
    f.DATADM AS DataAdmissao,
    f.SITAFA AS SituacaoAfastamento,
    f.CODCAR AS CodCargo,
    f.CODFIL AS Filial,
    f.CODCCU AS CentroCusto
FROM R034FUN f
WHERE f.SITAFA NOT IN (7) -- 7 = Demitido
ORDER BY f.NUMEMP, f.NOMFUN;"
                },
                new SeniorTemplate
                {
                    Category = "Colaboradores (R034)",
                    Title = "Histórico de Crachás do Colaborador (R034CRA)",
                    Description = "Consulta vínculos de crachás físicos/virtuais e data de validade",
                    Sql = @"-- Histórico de crachás
SELECT 
    c.NUMEMP,
    c.TIPCOL,
    c.NUMCAD,
    f.NOMFUN,
    c.NUMCRA AS NumeroCrachá,
    c.DATINI AS InicioValidade,
    c.DATFIM AS FimValidade,
    c.SITCRA AS SituacaoCracha
FROM R034CRA c
LEFT JOIN R034FUN f ON f.NUMEMP = c.NUMEMP AND f.TIPCOL = c.TIPCOL AND f.NUMCAD = c.NUMCAD
ORDER BY c.DATINI DESC;"
                },
                new SeniorTemplate
                {
                    Category = "Ronda e Portaria (R070)",
                    Title = "Eventos e Ocorrências de Ronda (R070OCO)",
                    Description = "Consulta ocorrências de ronda registradas pelos vigilantes",
                    Sql = @"-- Ocorrências de Ronda
SELECT TOP (200)
    o.NUMEMP,
    o.CODRON AS CodRonda,
    o.DATORO AS DataOcorrencia,
    o.HORORO AS HoraOcorrencia,
    o.CODOCO AS CodigoOcorrencia,
    o.OBSOCO AS Observacao
FROM R070OCO o
ORDER BY o.DATORO DESC, o.HORORO DESC;"
                },
                new SeniorTemplate
                {
                    Category = "Dicionário Senior",
                    Title = "Dicionário de Tabelas do Sistema Senior",
                    Description = "Lista tabelas e descrições do dicionário de dados nativo do Senior",
                    Sql = @"-- Busca no dicionário nativo Senior (se presente)
SELECT 
    TABLENAME AS NomeTabela,
    TABLEDESCRIPTION AS DescricaoTabela
FROM R999TAB
ORDER BY TABLENAME;"
                }
            };
        }
    }
}
