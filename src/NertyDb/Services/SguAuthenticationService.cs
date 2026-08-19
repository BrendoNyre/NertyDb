using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NertyDb.Data;
using NertyDb.Models;

namespace NertyDb.Services
{
    public class SguAuthenticationService
    {
        public static async Task<SguAuthResult> ValidateSguUserAsync(
            IDbDriver driver,
            ConnectionProfile profile,
            string database,
            string sguUsername,
            string? sguPassword = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sguUsername))
            {
                return SguAuthResult.Failure("Informe o usuário do SGU.");
            }

            var cleanUser = sguUsername.Trim();
            var escapedUser = cleanUser.Replace("'", "''").ToUpperInvariant();

            try
            {
                // 1. Primary SGU Architecture: R910ENT (Entities) + R910USU (User status) + R910MGP (Groups)
                var queryR910 = $@"
                    SELECT TOP 1 
                        e.CODENT AS Codigo,
                        e.NOMENT AS Login,
                        e.NOMEXB AS NomeExibicao,
                        u.NOMCOM AS NomeCompleto,
                        COALESCE(u.CONHAB, 1) AS Habilitado,
                        COALESCE(u.CONBLO, 0) AS Bloqueado,
                        COALESCE(grp.NOMEXB, 'Senior') AS Grupo
                    FROM R910ENT e WITH (NOLOCK)
                    LEFT JOIN R910USU u WITH (NOLOCK) ON u.CODENT = e.CODENT
                    LEFT JOIN R910MGP mgp WITH (NOLOCK) ON mgp.CODMBR = e.CODENT
                    LEFT JOIN R910ENT grp WITH (NOLOCK) ON grp.CODENT = mgp.CODGRP AND grp.TIPENT = 'G'
                    WHERE e.TIPENT = 'U' 
                      AND (UPPER(LTRIM(RTRIM(e.NOMENT))) = '{escapedUser}' 
                           OR UPPER(LTRIM(RTRIM(e.NOMEXB))) = '{escapedUser}' 
                           OR UPPER(LTRIM(RTRIM(u.NOMCOM))) = '{escapedUser}');";

                if (profile.DatabaseType == DatabaseType.Oracle)
                {
                    queryR910 = $@"
                        SELECT 
                            e.CODENT AS Codigo,
                            e.NOMENT AS Login,
                            e.NOMEXB AS NomeExibicao,
                            u.NOMCOM AS NomeCompleto,
                            NVL(u.CONHAB, 1) AS Habilitado,
                            NVL(u.CONBLO, 0) AS Bloqueado,
                            NVL(grp.NOMEXB, 'Senior') AS Grupo
                        FROM R910ENT e
                        LEFT JOIN R910USU u ON u.CODENT = e.CODENT
                        LEFT JOIN R910MGP mgp ON mgp.CODMBR = e.CODENT
                        LEFT JOIN R910ENT grp ON grp.CODENT = mgp.CODGRP AND grp.TIPENT = 'G'
                        WHERE e.TIPENT = 'U' 
                          AND (UPPER(LTRIM(RTRIM(e.NOMENT))) = '{escapedUser}' 
                               OR UPPER(LTRIM(RTRIM(e.NOMEXB))) = '{escapedUser}' 
                               OR UPPER(LTRIM(RTRIM(u.NOMCOM))) = '{escapedUser}')
                          AND ROWNUM = 1";
                }

                var res = await driver.ExecuteQueryAsync(profile, database, queryR910, timeoutSeconds: 10, cancellationToken: cancellationToken);
                
                int codUsu = 0;
                string nomUsu = cleanUser;
                string nomGrp = "Senior";
                bool userFound = false;

                if (!res.HasError && res.Tables.Count > 0 && res.Tables[0].Rows.Count > 0)
                {
                    var row = res.Tables[0].Rows[0];
                    codUsu = Convert.ToInt32(row["Codigo"] != DBNull.Value ? row["Codigo"] : 0);
                    nomUsu = row["NomeExibicao"]?.ToString()?.Trim() ?? row["Login"]?.ToString()?.Trim() ?? cleanUser;
                    var habilitado = Convert.ToInt32(row["Habilitado"] != DBNull.Value ? row["Habilitado"] : 1);
                    var bloqueado = Convert.ToInt32(row["Bloqueado"] != DBNull.Value ? row["Bloqueado"] : 0);
                    nomGrp = row["Grupo"]?.ToString()?.Trim() ?? "Senior";

                    if (habilitado == 0)
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está desabilitado no sistema (CONHAB = 0).");
                    }
                    if (bloqueado == 1)
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está bloqueado no sistema (CONBLO = 1).");
                    }

                    userFound = true;
                }

                // 2. Secondary fallback for legacy modules: R999USU
                if (!userFound)
                {
                    var queryR999 = $@"
                        SELECT TOP 1 CODUSU, NOMUSU
                        FROM R999USU WITH (NOLOCK)
                        WHERE UPPER(LTRIM(RTRIM(NOMUSU))) = '{escapedUser}';";

                    if (profile.DatabaseType == DatabaseType.Oracle)
                    {
                        queryR999 = $@"
                            SELECT CODUSU, NOMUSU
                            FROM R999USU
                            WHERE UPPER(LTRIM(RTRIM(NOMUSU))) = '{escapedUser}'
                            AND ROWNUM = 1";
                    }

                    var res999 = await driver.ExecuteQueryAsync(profile, database, queryR999, timeoutSeconds: 10, cancellationToken: cancellationToken);
                    if (!res999.HasError && res999.Tables.Count > 0 && res999.Tables[0].Rows.Count > 0)
                    {
                        var row = res999.Tables[0].Rows[0];
                        codUsu = Convert.ToInt32(row["CODUSU"] != DBNull.Value ? row["CODUSU"] : 0);
                        nomUsu = row["NOMUSU"]?.ToString()?.Trim() ?? cleanUser;
                        userFound = true;
                    }
                }

                // If SGU tables are not present (e.g. non-Senior database or empty schema), allow connection with warning
                if (!userFound)
                {
                    if (res.HasError && !string.IsNullOrEmpty(res.ErrorMessage) && (res.ErrorMessage.Contains("R910ENT", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("R910USU", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("table or view does not exist", StringComparison.OrdinalIgnoreCase)))
                    {
                        return SguAuthResult.Success(0, cleanUser, "Base Externa / Sem SGU");
                    }

                    return SguAuthResult.Failure($"Usuário SGU '{cleanUser}' não encontrado no cadastro de entidades/usuários Senior (R910ENT/R910USU/R999USU).");
                }

                // 3. Validate SGU User Password against Senior Security binary stream (R900PPL + R900PDT)
                var queryPdt = $@"
                    SELECT pdt.DAT1, pdt.DAT2 
                    FROM R900PPL ppl WITH (NOLOCK)
                    JOIN R900PDT pdt WITH (NOLOCK) ON pdt.PERID = ppl.PERID
                    WHERE UPPER(LTRIM(RTRIM(ppl.PERNAM))) = '{escapedUser}'
                    ORDER BY pdt.DATSEQ;";

                if (profile.DatabaseType == DatabaseType.Oracle)
                {
                    queryPdt = $@"
                        SELECT pdt.DAT1, pdt.DAT2 
                        FROM R900PPL ppl
                        JOIN R900PDT pdt ON pdt.PERID = ppl.PERID
                        WHERE UPPER(LTRIM(RTRIM(ppl.PERNAM))) = '{escapedUser}'
                        ORDER BY pdt.DATSEQ";
                }

                var resPdt = await driver.ExecuteQueryAsync(profile, database, queryPdt, timeoutSeconds: 10, cancellationToken: cancellationToken);
                if (!resPdt.HasError && resPdt.Tables.Count > 0 && resPdt.Tables[0].Rows.Count > 0)
                {
                    var dataStrings = new List<string>();
                    foreach (DataRow r in resPdt.Tables[0].Rows)
                    {
                        var d1 = r["DAT1"]?.ToString();
                        var d2 = r["DAT2"]?.ToString();
                        if (!string.IsNullOrEmpty(d1)) dataStrings.Add(d1);
                        if (!string.IsNullOrEmpty(d2)) dataStrings.Add(d2);
                    }

                    byte[] userStream = SeniorCryptoService.DecodeUserData(dataStrings);
                    bool isPasswordValid = SeniorCryptoService.ValidateSguPassword(cleanUser, sguPassword ?? string.Empty, userStream);

                    if (!isPasswordValid)
                    {
                        return SguAuthResult.Failure($"Senha inválida para o usuário SGU '{cleanUser}'. Verifique a senha e tente novamente.");
                    }
                }

                return SguAuthResult.Success(codUsu, nomUsu, nomGrp);
            }
            catch (Exception ex)
            {
                App.LogException("SguAuthenticationService.ValidateSguUserAsync", ex);
                return SguAuthResult.Failure($"Erro ao validar usuário no SGU: {ex.Message}");
            }
        }
    }
}
