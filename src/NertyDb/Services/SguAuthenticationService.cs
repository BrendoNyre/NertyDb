using System;
using System.Data;
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

                var res = await driver.ExecuteQueryAsync(profile, database, queryR910, timeoutSeconds: 10, cancellationToken);
                
                if (!res.HasError && res.Tables.Count > 0 && res.Tables[0].Rows.Count > 0)
                {
                    var row = res.Tables[0].Rows[0];
                    var codUsu = Convert.ToInt32(row["Codigo"] != DBNull.Value ? row["Codigo"] : 0);
                    var nomUsu = row["NomeExibicao"]?.ToString()?.Trim() ?? row["Login"]?.ToString()?.Trim() ?? cleanUser;
                    var habilitado = Convert.ToInt32(row["Habilitado"] != DBNull.Value ? row["Habilitado"] : 1);
                    var bloqueado = Convert.ToInt32(row["Bloqueado"] != DBNull.Value ? row["Bloqueado"] : 0);
                    var nomGrp = row["Grupo"]?.ToString()?.Trim() ?? "Senior";

                    if (habilitado == 0)
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está desabilitado no sistema (CONHAB = 0).");
                    }
                    if (bloqueado == 1)
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está bloqueado no sistema (CONBLO = 1).");
                    }

                    return SguAuthResult.Success(codUsu, nomUsu, nomGrp);
                }

                // 2. Secondary fallback for legacy modules: R999USU
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

                var res999 = await driver.ExecuteQueryAsync(profile, database, queryR999, timeoutSeconds: 10, cancellationToken);
                if (!res999.HasError && res999.Tables.Count > 0 && res999.Tables[0].Rows.Count > 0)
                {
                    var row = res999.Tables[0].Rows[0];
                    var codUsu = Convert.ToInt32(row["CODUSU"] != DBNull.Value ? row["CODUSU"] : 0);
                    var nomUsu = row["NOMUSU"]?.ToString()?.Trim() ?? cleanUser;

                    return SguAuthResult.Success(codUsu, nomUsu, "SGU");
                }

                // If SGU tables are not present (e.g. non-Senior database or empty schema), allow connection with warning
                if (res.HasError && !string.IsNullOrEmpty(res.ErrorMessage) && (res.ErrorMessage.Contains("R910ENT", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("R910USU", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("table or view does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    return SguAuthResult.Success(0, cleanUser, "Base Externa / Sem SGU");
                }

                return SguAuthResult.Failure($"Usuário SGU '{cleanUser}' não encontrado no cadastro de entidades/usuários Senior (R910ENT/R910USU/R999USU).");
            }
            catch (Exception ex)
            {
                App.LogException("SguAuthenticationService.ValidateSguUserAsync", ex);
                return SguAuthResult.Failure($"Erro ao validar usuário no SGU: {ex.Message}");
            }
        }
    }
}
