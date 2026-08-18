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

            try
            {
                // 1. Check primary SGU users table: R910USU
                var queryR910 = $@"
                    SELECT TOP 1 u.CODUSU, u.NOMUSU, u.SITUSU, u.CODGRP, g.NOMGRP
                    FROM R910USU u WITH (NOLOCK)
                    LEFT JOIN R910GRP g WITH (NOLOCK) ON g.CODGRP = u.CODGRP
                    WHERE UPPER(u.NOMUSU) = '{cleanUser.Replace("'", "''").ToUpperInvariant()}';";

                if (profile.DatabaseType == DatabaseType.Oracle)
                {
                    queryR910 = $@"
                        SELECT u.CODUSU, u.NOMUSU, u.SITUSU, u.CODGRP, g.NOMGRP
                        FROM R910USU u
                        LEFT JOIN R910GRP g ON g.CODGRP = u.CODGRP
                        WHERE UPPER(u.NOMUSU) = '{cleanUser.Replace("'", "''").ToUpperInvariant()}'
                        AND ROWNUM = 1";
                }

                var res = await driver.ExecuteQueryAsync(profile, database, queryR910, timeoutSeconds: 10, cancellationToken);
                
                if (!res.HasError && res.Tables.Count > 0 && res.Tables[0].Rows.Count > 0)
                {
                    var row = res.Tables[0].Rows[0];
                    var codUsu = Convert.ToInt32(row["CODUSU"] != DBNull.Value ? row["CODUSU"] : 0);
                    var nomUsu = row["NOMUSU"]?.ToString()?.Trim() ?? cleanUser;
                    var sitUsu = row["SITUSU"]?.ToString()?.Trim() ?? "A";
                    var nomGrp = row["NOMGRP"]?.ToString()?.Trim() ?? "Padrão";

                    if (!string.Equals(sitUsu, "A", StringComparison.OrdinalIgnoreCase))
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está inativo ou bloqueado no sistema (Situação: '{sitUsu}').");
                    }

                    return SguAuthResult.Success(codUsu, nomUsu, nomGrp);
                }

                // 2. Fallback check: R999USU
                var queryR999 = $@"
                    SELECT TOP 1 CODUSU, NOMUSU, SITUSU
                    FROM R999USU WITH (NOLOCK)
                    WHERE UPPER(NOMUSU) = '{cleanUser.Replace("'", "''").ToUpperInvariant()}';";

                if (profile.DatabaseType == DatabaseType.Oracle)
                {
                    queryR999 = $@"
                        SELECT CODUSU, NOMUSU, SITUSU
                        FROM R999USU
                        WHERE UPPER(NOMUSU) = '{cleanUser.Replace("'", "''").ToUpperInvariant()}'
                        AND ROWNUM = 1";
                }

                var res999 = await driver.ExecuteQueryAsync(profile, database, queryR999, timeoutSeconds: 10, cancellationToken);
                if (!res999.HasError && res999.Tables.Count > 0 && res999.Tables[0].Rows.Count > 0)
                {
                    var row = res999.Tables[0].Rows[0];
                    var codUsu = Convert.ToInt32(row["CODUSU"] != DBNull.Value ? row["CODUSU"] : 0);
                    var nomUsu = row["NOMUSU"]?.ToString()?.Trim() ?? cleanUser;
                    var sitUsu = row["SITUSU"]?.ToString()?.Trim() ?? "A";

                    if (!string.Equals(sitUsu, "A", StringComparison.OrdinalIgnoreCase))
                    {
                        return SguAuthResult.Failure($"O usuário SGU '{nomUsu}' está inativo ou bloqueado no sistema (Situação: '{sitUsu}').");
                    }

                    return SguAuthResult.Success(codUsu, nomUsu, "SGU");
                }

                // If SGU tables are not present (e.g. non-Senior database or empty schema), allow connection with warning
                if (res.HasError && !string.IsNullOrEmpty(res.ErrorMessage) && (res.ErrorMessage.Contains("R910USU", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || res.ErrorMessage.Contains("table or view does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    return SguAuthResult.Success(0, cleanUser, "Base Externa / Sem SGU");
                }

                return SguAuthResult.Failure($"Usuário SGU '{cleanUser}' não encontrado no cadastro de usuários Senior (R910USU/R999USU).");
            }
            catch (Exception ex)
            {
                App.LogException("SguAuthenticationService.ValidateSguUserAsync", ex);
                return SguAuthResult.Failure($"Erro ao validar usuário no SGU: {ex.Message}");
            }
        }
    }
}
