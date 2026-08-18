# Walkthrough — Validação Real de Senha do Usuário Senior (SGU)

Implementamos a engenharia reversa e a validação criptográfica real da senha do usuário do **SGU (Sistema Senior / CBDS)** sobre as tabelas `R900PPL` e `R900PDT`, eliminando a falha de segurança onde qualquer senha arbitrária era aceita.

---

## 🔍 Descoberta & Engenharia Reversa da Arquitetura Senior

Inspecionamos os pacotes do framework Senior (`g6-security-senior.jar`, `com.senior.security.impl.SeniorLogon`, `G5BinaryHelper`, `PersonLoader`):

1. **Armazenamento de Credenciais**:
   - `R900PPL`: Contém `perid` (ID da entidade) e `pernam` (login em maiúsculo).
   - `R900PDT`: Contém a stream binária do usuário particionada em blocos Base-64/6-bit customizados (`dat1` e `dat2` com `CHARBASE = 33` e `CHARBITS = 6`).
2. **Algoritmo de Hash & Criptografia da Senha**:
   - Calculado via `SeniorCryptoService.EncryptUserPassword(username, password)`.
   - Gera um checksum Little-Endian de 32 bits a partir do nome do usuário em uppercase (`CheckSumPassword`).
   - Aplica a inversão bit-a-bit e rotação circular bit-shift do vetor de bytes da senha informada pelo analista.
3. **Validação**:
   - Decodifica a stream de dados do usuário (`DecodeUserData`).
   - Extrai os bytes de senha criptografada armazenada (`ExtractEncryptedPasswordFromUserStream`).
   - Compara por igualdade de sequência binária (`SequenceEqual`) contra o hash gerado a partir da senha digitada.

---

## 🛠️ Alterações Realizadas

### 1. [`SeniorCryptoService.cs`](file:///c:/Users/brendo.oliveira/Documents/NertyDb/src/NertyDb/Services/SeniorCryptoService.cs)
- Implementados os métodos:
  - `EncryptUserPassword(string username, string password)`: Gera o vetor criptográfico exato do Senior.
  - `DecodeUserData(IEnumerable<string> dataStrings)`: Decodifica os fragmentos `dat1`/`dat2` da `R900PDT`.
  - `ExtractEncryptedPasswordFromUserStream(byte[] userStream)`: Lê o cabeçalho binário do usuário e extrai o hash da senha.
  - `ValidateSguPassword(string username, string enteredPassword, byte[] storedUserStream)`: Valida se a senha informada confere com a base.

### 2. [`SguAuthenticationService.cs`](file:///c:/Users/brendo.oliveira/Documents/NertyDb/src/NertyDb/Services/SguAuthenticationService.cs)
- Adicionada consulta à `R900PPL` + `R900PDT` para carregar o registro criptográfico do usuário.
- Se a senha informada estiver incorreta, a autenticação falha imediatamente com a mensagem:
  > *"Senha inválida para o usuário SGU '{username}'. Verifique a senha e tente novamente."*

### 3. [`SeniorConfigAndSguTests.cs`](file:///c:/Users/brendo.oliveira/Documents/NertyDb/tests/NertyDb.Tests/SeniorConfigAndSguTests.cs)
- Adicionados testes automatizados cobrindo vetores criptográficos conhecidos e validações contra usuários reais.

---

## 🧪 Resultados dos Testes

| Teste | Usuário | Senha | Resultado Esperado | Resultado Obtido |
| :--- | :--- | :--- | :--- | :--- |
| Senha Correta | `senior` | `senior` | ✅ Sucesso | **✅ Sucesso (Autenticado)** |
| Senha Incorreta | `senior` | `tt` | ❌ Falha de Senha | **❌ Falha: Senha inválida** |
| Senha Correta | `carolina` | `1234` | ✅ Sucesso | **✅ Sucesso (Autenticado)** |
| Senha Incorreta | `carolina` | `errada` | ❌ Falha de Senha | **❌ Falha: Senha inválida** |
| Usuário Inexistente | `inexistente` | `123` | ❌ Usuário não encontrado | **❌ Falha: Usuário não encontrado** |

- **Testes Automatizados**: **40 aprovados / 0 falhas**.
- **Binário Gerado**: `.\publish\NertyDb.exe` (53,32 MB, single-file self-contained).
