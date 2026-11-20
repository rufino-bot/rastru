# Fase 6A — Ajustes da conferência (F1, F2, F3) — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consertar os três achados da conferência da Fase 6A no navegador (2026-11-19): o Panorama sem cache lento
(F2), a trilha da tela do Pedido poluída (F1) e a tabela do Panorama passando de 1024 px (F3).

**Architecture:** F2 troca o cache de chave global (`IMemoryCache` invalidado por qualquer linha nova do livro) por um
cache **incremental** singleton, que guarda as linhas por nó e lê só as novas, refazendo o pareamento só dos nós
tocados. F1 reescreve a `TrilhaDoNo` como uma linha do Roteiro (horizontal de `md` para cima, vertical abaixo) com a
lista do que está parado embaixo. F3 aperta espaçamentos e larguras da tabela do Panorama e pinta o fundo do `<html>`.

**Tech Stack:** .NET (C#), ASP.NET Core, EF Core Database First, SQL Server, xUnit; React + TypeScript (Vite),
Tailwind v4 (container queries nativas), Vitest.

**Spec:** `docs/superpowers/specs/2026-11-19-fase-6a-visao-ao-vivo-design.md`. As decisões deste plano são do usuário,
na conferência de 2026-11-19, registradas em `.superpowers/sdd/2026-11-19-fase-6a/progress.md` (ledger privado).
**Base:** `866aa17` na branch `claude/gracious-cerf-y5sakh`.

## Global Constraints

- Tudo de `docs/superpowers/plans/2026-11-19-fase-6a.md` («Global Constraints») continua valendo: nomes de domínio em
  português, **sem mudança de schema** (C15), o servidor manda datas e `agora` (C12), **cor** neutra em idade,
  "parado", complicações e "mais parado" (C9), leituras de todo perfil autenticado (C13).
- Ciclo de cada task: `dotnet build Rastreamento.slnx -warnaserror` (0 avisos) e `dotnet test Rastreamento.slnx -m:1`
  quando toca backend; `npm test -- --run --maxWorkers=2` **e** `npm run build` em `web/` quando toca front.
- Testes de tela: `// @vitest-environment jsdom`, `afterEach(cleanup)`, `web/src/testes/api.ts`; `data-testid` só
  pela regra do `CLAUDE.md`.
- Citação em comentário e prosa **pelo nome**, nunca `arquivo:NN` (`CLAUDE.md`, «Convenção de citação»). Comentário
  de código não cita o ledger.
- As três tasks são independentes entre si; a ordem abaixo é só a de execução. A seção «11. Emendas da conferência
  (2026-11-19)» da spec nasce na Task A1 e as outras acrescentam a ela.

---

### Task A1: Cache incremental das idades (F2)

**Por quê (medido em 2026-11-19, massa de `db/massa-carga-6a.sql`):** com o livro mudando entre chamadas, o
`GET /panorama` da 6A tem mediana de **1,010 s** contra **0,680 s** da `main` (+48%), na borda de C14; com o livro
parado, 0,706 s. A chave do cache atual é o `MAX(Movimentacao.Id)` **global**: qualquer apontamento da fábrica invalida
tudo, e a falta é o caso comum. A leitura completa do livro (~67 mil linhas, entidade inteira) leva ~139 ms no banco;
o resto dos ~0,33 s é materialização e pareamento.

**O desenho (decisão do usuário: caminho "cache incremental"):**

- O pareamento de `CalculoDaIdade` é **por nó**: tudo é chaveado por `EstruturaItemId`, e o estorno cai sempre no nó
  da linha estornada (`NovoMovimento.De` recebe o `estruturaItemId`; no banco de dev, 0 estornos em nó diferente do
  original). Então refazer só os nós tocados dá o mesmo resultado que refazer tudo.
- O livro é só de inclusão: o que mudou desde a última leitura são linhas de `Id` maior.
- **A janela de releitura.** A escrita é `SERIALIZABLE`, a leitura é *read committed* com bloqueio (RCSI desligado) e
  o `Id` é `IDENTITY`: duas escritas simultâneas podem fazer commit fora de ordem (o 101 visível antes do 100). Ler só
  `Id > último` perderia o 100 para sempre. Por isso a leitura incremental relê as últimas
  `JanelaDeReleitura` (1.000) posições de `Id` antes do último lido e descarta o que já conhece pelo `Id`. Uma
  transação ficar aberta enquanto mil outras linhas entram é impossível na prática (as transações da execução são
  curtas, com no máximo 3 tentativas).
- **A reconstrução completa** a cada `ReconstrucaoCompleta` (10 min) é a rede de segurança: limita o tempo de vida de
  qualquer erro que o incremental tenha.
- O cache é um **singleton** com uma trava (`SemaphoreSlim`): duas requisições não atualizam o retrato ao mesmo tempo.
  O repositório (scoped) entra por parâmetro, nunca guardado no singleton.
- O `IMemoryCache` sai: era só deste uso (`AddMemoryCache` em `Program.cs` e o pacote
  `Microsoft.Extensions.Caching.Memory` no `.csproj` do Application). Confira com
  `grep -rn "IMemoryCache\|AddMemoryCache\|Caching.Memory" src/ --include=*.cs --include=*.csproj` antes de tirar; se
  aparecer outro consumidor, o pacote fica.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (dois métodos novos)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Application/Execucao/CalculoDaIdade.cs` (`IdadesDoLivro.Substituir`)
- Create: `src/Rastreamento.Application/Panorama/CacheDasIdades.cs`
- Modify: `src/Rastreamento.Application/Panorama/ConsultaDoPanoramaUseCase.cs` (usa o `CacheDasIdades`; sai o `IMemoryCache`)
- Modify: `src/Rastreamento.Api/Program.cs` (`AddSingleton<CacheDasIdades>()`; sai o `AddMemoryCache()`)
- Modify: `src/Rastreamento.Application/Rastreamento.Application.csproj` (sai o pacote, se não houver outro consumidor)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`
- Create: `tests/Rastreamento.Application.Tests/Panorama/CacheDasIdadesTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Panorama/ConsultaDoPanoramaUseCaseTests.cs` (os quatro testes de cache)
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/LeiturasDaIdadeTests.cs`
- Modify: `docs/superpowers/specs/2026-11-19-fase-6a-visao-ao-vivo-design.md` (cria a seção 11, item F2)
- Modify: `CLAUDE.md` e `specs/0*.md` só onde descrevem o cache do Panorama (procure com
  `grep -rn -i "cache" CLAUDE.md specs/ | grep -i "panorama\|idade\|D7"`; em 2026-11-19 a busca não achava nada nos dois
  além da spec da 6A, mas remeça)

**Interfaces:**
- Produces:
  - `IExecucaoRepository.ListarMovimentacoesAteAsync(IReadOnlyCollection<int> estruturaItemIds, int ateId, CancellationToken ct) : Task<IReadOnlyList<Movimentacao>>` — as linhas dos nós com `Id <= ateId`, em ordem de `Id`.
  - `IExecucaoRepository.ListarMovimentacoesDoIntervaloAsync(int depoisDeId, int ateId, CancellationToken ct) : Task<IReadOnlyList<Movimentacao>>` — as linhas de **qualquer** nó com `depoisDeId < Id <= ateId`, em ordem de `Id`.
  - `IdadesDoLivro.Substituir(IdadesDoLivro anteriores, IdadesDoLivro refeitas, IReadOnlySet<int> nosRefeitos, IReadOnlySet<int> nosVivos) : IdadesDoLivro` (`internal static`).
  - `CacheDasIdades.ObterAsync(IReadOnlyList<NoDaIdade> nos, IExecucaoRepository execucao, CancellationToken ct) : Task<IdadesDoLivro>`; constantes `JanelaDeReleitura = 1000` e `ReconstrucaoCompleta = TimeSpan.FromMinutes(10)`.
- O `ListarMovimentacoesAsync(ids, ct)` existente **fica**: o `ConsultaDoAndamentoUseCase` o usa e não muda.

- [ ] **Step 1: Os dois métodos do repositório, com teste de Infrastructure que falha.** Em `LeiturasDaIdadeTests`,
  no padrão dos testes que já estão lá (escopo pelas próprias linhas, nunca contagem global — ver `CLAUDE.md`,
  «Paralelismo do xUnit contra um banco só»):
  - `Linhas_ate_um_id_param_no_id_e_filtram_os_nos`: grava 3 linhas em dois nós e uma num terceiro; pede os dois nós
    com `ateId` igual ao `Id` da segunda linha; espera só as linhas desses nós com `Id <= ateId`, em ordem de `Id`.
  - `Intervalo_de_ids_traz_qualquer_no_e_respeita_os_dois_limites`: grava 4 linhas (em nós diferentes); pede o
    intervalo `(Id da 1ª, Id da 3ª]`; espera a 2ª e a 3ª, e nenhuma linha de fora do conjunto que o teste gravou
    (filtre o resultado pelos nós do teste antes de afirmar, porque outro teste pode gravar no mesmo intervalo).
  Declare os dois métodos na interface e implemente:

```csharp
  public async Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesAteAsync(
      IReadOnlyCollection<int> estruturaItemIds, int ateId, CancellationToken ct)
  {
    if (estruturaItemIds.Count == 0) return [];
    var lista = estruturaItemIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.Id <= ateId && lista.Contains(m.EstruturaItemId)).OrderBy(m => m.Id).ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoIntervaloAsync(
      int depoisDeId, int ateId, CancellationToken ct) =>
      await _db.Movimentacoes.AsNoTracking()
          .Where(m => m.Id > depoisDeId && m.Id <= ateId).OrderBy(m => m.Id).ToListAsync(ct);
```

  No `FakeExecucaoRepo`, os dois métodos sobre `Movimentacoes`, e mais: `HashSet<int> Ocultas` (linhas que nenhuma
  leitura do livro vê, nem `MaiorIdDoLivroAsync`: simulam um commit atrasado), e os contadores
  `LeiturasDoLivroParaIdade` (passa a contar `ListarMovimentacoesAteAsync`) e `LeiturasDoIntervalo`.
  Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~LeiturasDaIdadeTests"` → os dois novos passam.

- [ ] **Step 2: `IdadesDoLivro.Substituir`.** Em `CalculoDaIdade.cs`, dentro de `IdadesDoLivro`:

```csharp
  /// <summary>
  /// As idades de `anteriores` para os nos vivos que nao foram refeitos, mais todas as de `refeitas`: o cache
  /// incremental refaz so os nos tocados, e o pareamento e por no (cada linha e cada estorno ficam no proprio no).
  /// </summary>
  internal static IdadesDoLivro Substituir(
      IdadesDoLivro anteriores, IdadesDoLivro refeitas, IReadOnlySet<int> nosRefeitos, IReadOnlySet<int> nosVivos)
  {
    bool Mantem(int no) => nosVivos.Contains(no) && !nosRefeitos.Contains(no);
    var partes = anteriores._partes.Where(kv => Mantem(kv.Key))
        .Concat(refeitas._partes)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    var permanencias = anteriores._permanencias.Where(kv => Mantem(kv.Key.No))
        .Concat(refeitas._permanencias)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    return new IdadesDoLivro(partes, permanencias);
  }
```

- [ ] **Step 3: Testes do `CacheDasIdades` que falham**, em `CacheDasIdadesTests.cs`, com o `FakeExecucaoRepo` e um
  `FakeTimeProvider` (o que os outros testes do Application já usam para relógio; se não houver, um `TimeProvider`
  de teste com `GetUtcNow` ajustável). Um auxiliar compara dois `IdadesDoLivro` nó a nó: as `Partes(id)` iguais
  (os `record` comparam por valor) e a `PermanenciaMedia(id, ordem)` igual para cada ordem de 1 a 4. O "esperado" é
  sempre `CalculoDaIdade.Calcular` sobre o livro inteiro visível, do zero.
  - `Primeira_chamada_le_o_livro_inteiro` — `LeiturasDoLivroParaIdade == 1` e o resultado igual ao do zero.
  - `Sem_linha_nova_nao_le_o_livro_de_novo` — duas chamadas: `LeiturasDoLivroParaIdade == 1`.
  - `Linha_nova_entra_sem_leitura_completa` — depois da primeira chamada, um `Termino` num nó; a segunda chamada não
    soma em `LeiturasDoLivroParaIdade`, soma 1 em `LeiturasDoIntervalo` e dá o mesmo que do zero.
  - `Estorno_novo_refaz_o_no` — um `Inicio`, chamada, o estorno dele, chamada: igual ao do zero (a parte some).
  - `Linha_que_comita_atrasada_dentro_da_janela_entra` — linhas 100 e 101 no fake, a 100 em `Ocultas`; chamada
    (o `MAX` dá 101, a 100 não aparece); tira a 100 de `Ocultas`; chamada: a 100 entra e o resultado é igual ao do zero.
  - `Quantidade_alterada_refaz_o_no` — a `Quantidade` do `NoDaIdade` muda (o "a iniciar" de C4): sem leitura completa,
    igual ao do zero.
  - `No_novo_le_so_as_linhas_dele` — um nó que não estava na primeira chamada entra na segunda: uma leitura
    `ListarMovimentacoesAteAsync` **só com o id dele** (o fake guarda os ids da última leitura) e o resultado igual ao do zero.
  - `No_que_saiu_some_das_idades` — o nó sai da lista: `Partes(id)` vazia.
  - `Depois_de_dez_minutos_reconstroi_do_zero` — o relógio anda `ReconstrucaoCompleta`: `LeiturasDoLivroParaIdade == 2`.
  - `Sequencia_de_escritas_da_o_mesmo_que_do_zero` — 3 nós com Roteiro de 2 passos, 30 operações fixas (escritas no
    teste, não aleatórias: início, término, entrega e estorno, em lotes parciais e com `DataHora` repetida em pares,
    para o desempate por `Id`) e uma chamada depois de cada uma: todas iguais ao do zero.
  Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CacheDasIdadesTests"` → falha (a classe não existe).

- [ ] **Step 4: O `CacheDasIdades`.**

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// O cache das idades do Panorama (D7 da spec da Fase 6A, emenda F2 da conferencia): guarda as linhas do livro por no
/// e o maior `Id` lido, e a cada chamada le so as linhas novas, refazendo o pareamento so dos nos que elas tocam. O
/// livro e so de inclusao, e o pareamento e por no, entao o resultado e o mesmo de refazer tudo. Singleton: o
/// repositorio entra por parametro a cada chamada.
/// </summary>
public sealed class CacheDasIdades
{
  /// <summary>
  /// Quantas posicoes de `Id` antes da ultima lida a leitura incremental rele. A escrita e serializavel e o `Id` e
  /// `IDENTITY`: duas escritas simultaneas podem fazer commit fora de ordem, e a linha de `Id` menor aparece depois.
  /// </summary>
  public const int JanelaDeReleitura = 1000;

  /// <summary>De quanto em quanto tempo o cache le tudo de novo: limita a vida de qualquer erro do incremental.</summary>
  public static readonly TimeSpan ReconstrucaoCompleta = TimeSpan.FromMinutes(10);

  private sealed record Retrato(
      int AteId, DateTime FeitoEm, IReadOnlyDictionary<int, NoDaIdade> Nos,
      IReadOnlyDictionary<int, List<Movimentacao>> LinhasPorNo, IReadOnlySet<int> IdsLidos, IdadesDoLivro Idades);

  private readonly SemaphoreSlim _trava = new(1, 1);
  private readonly TimeProvider _relogio;
  private Retrato? _retrato;

  public CacheDasIdades(TimeProvider relogio) => _relogio = relogio;

  public async Task<IdadesDoLivro> ObterAsync(
      IReadOnlyList<NoDaIdade> nos, IExecucaoRepository execucao, CancellationToken ct)
  {
    await _trava.WaitAsync(ct);
    try
    {
      var ateId = await execucao.MaiorIdDoLivroAsync(ct);
      var agora = _relogio.GetUtcNow().UtcDateTime;
      var anterior = _retrato;
      _retrato = anterior is null || agora - anterior.FeitoEm >= ReconstrucaoCompleta
          ? await CompletoAsync(nos, ateId, agora, execucao, ct)
          : await IncrementalAsync(anterior, nos, ateId, execucao, ct);
      return _retrato.Idades;
    }
    finally
    {
      _trava.Release();
    }
  }

  private static async Task<Retrato> CompletoAsync(
      IReadOnlyList<NoDaIdade> nos, int ateId, DateTime agora, IExecucaoRepository execucao, CancellationToken ct)
  {
    var linhas = await execucao.ListarMovimentacoesAteAsync(nos.Select(n => n.Id).ToList(), ateId, ct);
    var porNo = linhas.GroupBy(m => m.EstruturaItemId).ToDictionary(g => g.Key, g => g.ToList());
    return new Retrato(
        ateId, agora, nos.ToDictionary(n => n.Id), porNo, linhas.Select(m => m.Id).ToHashSet(),
        CalculoDaIdade.Calcular(nos, linhas));
  }

  private static async Task<Retrato> IncrementalAsync(
      Retrato anterior, IReadOnlyList<NoDaIdade> nos, int ateId, IExecucaoRepository execucao, CancellationToken ct)
  {
    var vivos = nos.ToDictionary(n => n.Id);
    var porNo = anterior.LinhasPorNo.Where(kv => vivos.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    var ids = new HashSet<int>(anterior.IdsLidos);
    var refeitos = new HashSet<int>();

    // No que entrou: as linhas dele inteiras. No cuja quantidade (ou criacao) mudou: refeito com as linhas que ja tem.
    var novos = nos.Where(n => !anterior.Nos.ContainsKey(n.Id)).Select(n => n.Id).ToList();
    if (novos.Count > 0)
      foreach (var m in await execucao.ListarMovimentacoesAteAsync(novos, ateId, ct))
        Acrescentar(m);
    refeitos.UnionWith(novos);
    refeitos.UnionWith(nos.Where(n => anterior.Nos.TryGetValue(n.Id, out var antes) && antes != n).Select(n => n.Id));

    // As linhas novas, relendo a janela para pegar o commit atrasado; o que ja foi lido sai pelo `Id`.
    var depoisDe = Math.Max(0, anterior.AteId - JanelaDeReleitura);
    foreach (var m in await execucao.ListarMovimentacoesDoIntervaloAsync(depoisDe, ateId, ct))
      if (vivos.ContainsKey(m.EstruturaItemId) && Acrescentar(m))
        refeitos.Add(m.EstruturaItemId);

    var idades = refeitos.Count == 0 && vivos.Count == anterior.Nos.Count
        ? anterior.Idades
        : IdadesDoLivro.Substituir(
            anterior.Idades,
            CalculoDaIdade.Calcular(
                refeitos.Select(id => vivos[id]).ToList(),
                refeitos.SelectMany(id => porNo.GetValueOrDefault(id) ?? []).ToList()),
            refeitos,
            vivos.Keys.ToHashSet());
    return new Retrato(Math.Max(ateId, anterior.AteId), anterior.FeitoEm, vivos, porNo, ids, idades);

    bool Acrescentar(Movimentacao m)
    {
      if (!ids.Add(m.Id)) return false;
      if (!porNo.TryGetValue(m.EstruturaItemId, out var lista)) porNo[m.EstruturaItemId] = lista = [];
      else if (ReferenceEquals(lista, anterior.LinhasPorNo.GetValueOrDefault(m.EstruturaItemId)))
        porNo[m.EstruturaItemId] = lista = [.. lista];   // nao muta a lista do retrato anterior
      lista.Add(m);
      return true;
    }
  }
}
```

  Atenção ao que este código assume e que o teste tem de cobrir: `ids` guarda os `Id` de **todo** nó lido, inclusive
  os que saíram (o que não faz mal: uma linha de nó que saiu não volta a ser contada); o `AteId` nunca anda para trás
  (`Math.Max`); e o retrato anterior nunca é mutado (uma requisição que já o leu não vê lista mudando).

- [ ] **Step 5: Ligar no use case e no DI.** No `ConsultaDoPanoramaUseCase`, o construtor troca `IMemoryCache cache`
  por `CacheDasIdades idades`, e `IdadesAsync` fica:

```csharp
  private async Task<IdadesDoLivro> IdadesAsync(
      IReadOnlyList<ContextoDoNo> todos, Dictionary<int, HashSet<int>> nosDaPeca, CancellationToken ct)
  {
    var pedidos = nosDaPeca.Values.SelectMany(n => n).ToHashSet();
    var nos = todos.Where(x => pedidos.Contains(x.No.Id)).ToList();
    var criacoes = await _execucao.ListarCriacaoDosAgrupamentosAsync(nos.Select(x => x.AgrupamentoId).Distinct().ToList(), ct);
    return await _idades.ObterAsync(
        nos.Select(x => new NoDaIdade(x.No.Id, x.No.Quantidade, criacoes.GetValueOrDefault(x.AgrupamentoId))).ToList(),
        _execucao, ct);
  }
```

  Sai o `ChaveDasIdades`. Atualize o `<summary>` da classe e o de `IdadesAsync` para o cache novo. Em `Program.cs`:
  `builder.Services.AddSingleton<CacheDasIdades>();` no lugar de `AddMemoryCache()` (o `TimeProvider` já está no DI:
  confira). Os quatro testes de cache de `ConsultaDoPanoramaUseCaseTests` passam a montar o use case com um
  `CacheDasIdades` (um por teste) e a afirmar: `Cache_reusa_as_idades_sem_linha_nova` — uma leitura completa;
  `Linha_nova_no_livro_refaz_as_idades` — continua **uma** leitura completa e o `ParadoDesde` reflete a linha nova
  (renomeie para `Linha_nova_no_livro_entra_sem_leitura_completa`); `Quantidade_alterada_refaz_as_idades` — uma
  leitura completa e o "a iniciar" novo no resultado; `Cache_nao_congela_o_agora` — sem mudança de sentido.
  Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~Panorama"` → verde.

- [ ] **Step 6: Mutação** (o procedimento que achou as lacunas deste projeto). Uma de cada vez, rode
  `CacheDasIdadesTests` e anote no relatório qual teste morreu; desfaça cada uma:
  (a) `JanelaDeReleitura` = 0; (b) `Acrescentar` sem o `if (!ids.Add(...))` (linha da janela contada duas vezes);
  (c) `refeitos` sem os nós de quantidade mudada; (d) `Substituir` mantendo os nós refeitos de `anteriores` em vez de
  trocar; (e) `ReconstrucaoCompleta` nunca disparando. Mutação que não mata nenhum teste é lacuna: escreva o teste.

- [ ] **Step 7: Suíte e medição.** `dotnet build Rastreamento.slnx -warnaserror` (0 avisos) e
  `dotnet test Rastreamento.slnx -m:1` inteiro. Depois, com a API no ar e a massa carregada, meça a chamada **sem
  cache antigo** (livro mudando) com este script (Python, `python -I`), que grava um `Inicio` de 1 unidade e o estorna
  alternadamente num Item de `CARGA-6A-*` com saldo a iniciar e no primeiro passo do Roteiro dele (descubra os dois ids
  no banco antes; em 2026-11-19 eram o nó 29731 e o Setor 25982):

```python
import json, statistics, time, urllib.request
BASE, ITEM, SETOR = "http://localhost:5169/api", 29731, 25982
def chamar(url, metodo="GET", corpo=None, token=None):
    req = urllib.request.Request(url, method=metodo, data=json.dumps(corpo).encode() if corpo is not None else None)
    req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", f"Bearer {token}")
    t0 = time.perf_counter()
    with urllib.request.urlopen(req) as r: dados = r.read()
    return time.perf_counter() - t0, dados
def login(u, s): return json.loads(chamar(f"{BASE}/auth/login", "POST", {"nomeUsuario": u, "senha": s})[1])["accessToken"]
adm, op = login("admin", "Admin@123"), login("operador", "Operador@123")
for _ in range(3): chamar(f"{BASE}/panorama", token=adm)
tempos, inicio = [], None
for i in range(12):
    if inicio is None:
        inicio = json.loads(chamar(f"{BASE}/estrutura/{ITEM}/inicios", "POST", {"setorId": SETOR, "quantidade": 1}, op)[1])["id"]
    else:
        chamar(f"{BASE}/movimentacoes/{inicio}/estorno", "POST", None, op); inicio = None
    tempos.append(chamar(f"{BASE}/panorama", token=adm)[0])
assert inicio is None
print(f"mediana {statistics.median(tempos):.3f} s  min {min(tempos):.3f}  max {max(tempos):.3f}")
```

  Registre a mediana no relatório. **Alvo: abaixo de 0,8 s** (era 1,010 s; a `main` faz 0,680 s). Não chegou: pare e
  reporte com o número, antes de qualquer outra otimização.

- [ ] **Step 8: Docs.** Na spec da 6A, depois da seção 10, crie:

```markdown
## 11. Emendas da conferência (2026-11-19)

- **F2 — o cache de D7 passa a ser incremental.** Medido na conferência: com a chave no maior `Id` global do livro,
  qualquer apontamento invalidava o cache inteiro, e a chamada sem cache levava 1,01 s contra 0,68 s da `main`. O
  `CacheDasIdades` (singleton) guarda as linhas por nó e o maior `Id` lido; a cada chamada lê só as linhas novas,
  relendo as últimas 1.000 posições de `Id` para pegar o commit fora de ordem, e refaz o pareamento só dos nós
  tocados. A cada 10 min ele lê tudo de novo. Sai o `IMemoryCache`.
```

  E corrija a seção 5.4 (Desempenho) e a linha de D7 só no que ficou falso: onde dizem "válido enquanto o maior `Id`
  da `Movimentacao` não muda", acrescente "(até a emenda F2, seção 11)". Não reescreva a decisão original.

- [ ] **Step 9: Commit** — `perf(6a): cache incremental das idades do Panorama`.

---

### Task A2: A trilha como linha do Roteiro (F1)

**Por quê:** na conferência, o usuário achou o cartão da trilha "muito poluído": o título `{código} {descrição} ·
{quantidade}` sem rótulo, os blocos em `flex-wrap` com largura do próprio texto (quebra irregular), e números soltos
("20", "coleta 20"). **Decisão do usuário: o desenho B**, com estas regras (todas dele ou aprovadas por ele):

- **Título:** `CARGA-6A-C01 · P01 · Peça · qtd 40` — código em `font-mono`, descrição, e em `text-tinta-fraca` o tipo
  do nó ("Peça" no topo da seção, "Item" na recursão) e a quantidade. *(O mock aprovado dizia "40 un."; o plano usa
  "qtd 40" porque a quantidade pode ser decimal, em kg ou metro, e "un." mentiria — avise o usuário no relatório.)*
- **A linha do Roteiro** (de `md` para cima): um ponto por bloco `Passo` da trilha, na ordem, e a **Expedição** sempre
  como último ponto (cheio com anel quando há bloco `Expedicao` na trilha; vazio sem ele). A linha ocupa **a largura
  inteira do cartão**, e cada ponto é `flex-1`: com menos passos o espaço se redistribui igualmente.
  - Ponto `Passado`: cheio, `bg-tinta-fraca`. `Atual`: cheio `bg-tinta`, com anel (`ring-2 ring-tinta ring-offset-2
    ring-offset-superficie`). `Futuro`: vazio, `border border-borda-campo`. Um traço `bg-borda-forte` (ou o token de
    borda mais forte que existir; nada fora de `index.css`) liga os pontos.
  - **O rótulo depende da largura do ponto**, por container query (Tailwind v4: `@container` no `<li>`): com 7 rem ou
    mais, o nome do Setor inteiro (`truncate`); abaixo de 7 rem, o número do passo antes do nome; abaixo de 4,5 rem, só
    o número — **exceto** no ponto `Atual`, que mantém número e nome (truncado). O nome inteiro está sempre no nome
    acessível do `<li>` (`aria-label`, "{ordem} · {Setor}: {situação}") e no `title`.
  - Nó **sem** bloco `Passo` (sem Roteiro): não há linha, só a lista de baixo (Review Focus da 6A).
- **A lista "onde está agora"** (de `md` para cima, embaixo da linha): uma linha por bloco `Atual` da trilha, na ordem
  do servidor — `Passo`: "{Setor} — {q} no Setor · há N" e/ou "{c} prontas, esperando coleta · há N"; `AIniciar`:
  "A iniciar — {q} · há N"; `Montagem`: "Aguardando montagem · {Setor} — {q} · há N"; `Conserto`: "Em conserto ·
  {Setor} — {q} · há N"; `Expedicao`: "Expedição — {q} · há N". O bloco mais parado do Pedido ganha a moldura de 2 px
  `border-tinta` e o texto "mais parado" (C9 e C10, sem cor). Depois, numa linha `text-tinta-fraca`, os passos já
  passados: "Já passaram: Corte (menos de 1 h), Dobra (2 d)" — cada um com a própria `permanenciaMediaSegundos`, sem
  parênteses quando nula. Montagem e conserto **não** viram ponto na linha: só aparecem nesta lista.
- **Celular** (abaixo de `md`): uma lista vertical única, sem a linha horizontal nem a lista de cima: passos `Passado`
  consecutivos numa linha só ("1–3 · Corte laser, Rebarbação, Dobra · já passaram"), cada `Atual` com o detalhe dele
  (o mesmo texto da lista de cima), `Futuro` consecutivos numa linha só ("5–8 · ainda não chegaram"), e a Expedição por
  último; os blocos `AIniciar`, `Montagem` e `Conserto` atuais entram na posição em que o servidor os manda.
- As duas formas (larga e celular) ficam no DOM e o CSS esconde uma (`hidden md:block` / `md:hidden`), como a tabela e
  os cartões do Panorama; os testes procuram dentro da lista certa pelo nome acessível.
- Complicações e "Itens (N)" (a `ListaRecolhivel` que abre sozinha) continuam como estão.

**Files:**
- Create: `web/src/andamento/LinhaDoRoteiro.tsx` (a linha horizontal) e `web/src/andamento/textoDoBloco.ts` (o texto de
  um bloco atual e o agrupamento do celular, funções puras) + testes
- Modify: `web/src/andamento/TrilhaDoNo.tsx` (+ teste), `web/src/andamento/SecaoOndeEstaTravando.tsx` (passa
  `nivel="Peca"`)
- Modify: `CLAUDE.md` (bullet «O andamento»), a spec da 6A (seção 11, item F1), `specs/04-fluxos-de-usuario.md` só
  se descrever a trilha bloco a bloco (procure "passou ·" e "ainda não chegou")

**Interfaces:**
- Consumes: `BlocoDaTrilhaDto`, `AndamentoDoNoDto`, `MaisParadoDto` de `web/src/api/andamento.ts` (sem mudança);
  `duracaoLegivel`, `idadeDesde` de `web/src/panorama/idade.ts`; `formatarQuantidade` de `web/src/execucao/formatacao`.
- Produces:
  - `textoDoBloco(b: BlocoDaTrilhaDto, agora: string): string` — o texto de um bloco `Atual` (sem o "mais parado").
  - `gruposDoCelular(trilha: BlocoDaTrilhaDto[]): GrupoDoCelular[]` com
    `type GrupoDoCelular = { tipo: 'passados' | 'futuros'; blocos: BlocoDaTrilhaDto[] } | { tipo: 'atual'; bloco: BlocoDaTrilhaDto }`.
  - `<LinhaDoRoteiro trilha={BlocoDaTrilhaDto[]} />` e `<TrilhaDoNo no agora maisParado nivel={'Peca' | 'Item'} />`.

- [ ] **Step 1: Testes que falham.**
  - `textoDoBloco.test.ts`: `passo_com_quantidade_e_coleta` ("Solda — 20 prontas, esperando coleta · há 9 d" quando
    só há coleta; as duas partes quando há as duas); `a_iniciar`; `montagem_e_conserto_com_setor`; `expedicao`;
    `grupos_juntam_passados_e_futuros_consecutivos` (5 passos P,P,A,F,F → passados[1,2], atual, futuros[4,5]);
    `grupos_nao_juntam_atual_com_passado`.
  - `LinhaDoRoteiro.test.tsx`: `um_ponto_por_passo_e_a_expedicao_por_ultimo`; `expedicao_vazia_sem_bloco_de_expedicao`;
    `nome_inteiro_no_nome_acessivel` (o `<li>` tem `aria-label` com ordem, Setor e situação); `sem_passo_nao_ha_linha`
    (o componente não renderiza nada).
  - `TrilhaDoNo.test.tsx` (reescreva os 6 de hoje para o desenho novo, mantendo o que eles protegiam):
    `titulo_com_tipo_e_quantidade` ("Peça · qtd 40"); `lista_do_agora_so_com_blocos_atuais`;
    `passados_numa_linha_com_permanencia_de_cada_um`; `mais_parado_marcado_sem_cor_de_estado` (moldura `border-tinta`,
    texto "mais parado", nenhuma classe `positivo`/`negativo`/`atencao`/`atraso`);
    `itens_abrem_quando_o_mais_parado_e_um_item`; `complicacoes em texto neutro abaixo da trilha`;
    `celular_agrupa_passados_e_futuros` (dentro da lista do celular, pelo nome acessível dela).
  Run: `cd web && npx vitest run src/andamento` → falham.
- [ ] **Step 2: Implementar** `textoDoBloco.ts`, `LinhaDoRoteiro.tsx` e a `TrilhaDoNo` nova. Container query no
  Tailwind v4: `@container` no `<li>` (com `min-w-0 flex-1`), e variantes `@max-[7rem]:` e `@max-[4.5rem]:` nos `span`
  do número e do nome. Nada de `Date.now()`; nada de cor de estado.
- [ ] **Step 3: Rodar e ver passar**; `npm test -- --run --maxWorkers=2` inteiro e `npm run build`.
- [ ] **Step 4: Docs.** O bullet «O andamento» do `CLAUDE.md` descreve a trilha nova (a linha, a lista, o celular, a
  regra do rótulo por largura e que montagem e conserto ficam fora da linha). Na seção 11 da spec, o item:

```markdown
- **F1 — a trilha vira uma linha do Roteiro.** O usuário achou o cartão de blocos poluído e escolheu, entre três
  desenhos, a linha do Roteiro: um ponto por passo e a Expedição por último, ocupando sempre a largura do cartão, com o
  rótulo encolhendo pela largura de cada ponto (nome, número e nome, só número; o ponto atual mantém o nome), e embaixo
  só o que tem peça agora, por extenso, com os passos já passados numa linha. No celular, vertical, com passados e
  futuros consecutivos agrupados. Montagem e conserto continuam blocos da trilha (C10), mas aparecem só na lista, não
  na linha. A borda tracejada do passo futuro de C10 vira o ponto vazio.
```

- [ ] **Step 5: Commit** — `fix(6a): a trilha como linha do Roteiro`.

**Conferência visual (do controlador, depois da review; não substitui a do usuário):** no navegador, um Pedido
`CARGA-6A-*` em 1280 px, em 1024 px e no preset `mobile`, e um nó com 8 passos ou mais. A massa só tem Roteiros de 4
passos: para o caso longo, use um Pedido do `seed-demo.sql` com Roteiro maior, se houver, ou registre que não foi visto.

---

### Task A3: A tabela do Panorama cabe em 1024 px (F3)

**Por quê:** em 1024 px a tabela tinha 1.056 px num contêiner de 961 (as duas colunas da 6A somam 204 px), a página
rolava de lado (nenhum ancestral tem `overflow`, de propósito: prenderia o `thead` `sticky`) e o `<html>` sem fundo
aparecia como faixa branca. **Decisão do usuário: o aperto do protótipo**, medido no navegador por CSS injetado:
largura mínima de **1.056 para 956 px** (5 px de folga). Os valores abaixo são exatamente os do protótipo.

**Files:**
- Modify: `web/src/components/CabecalhoOrdenavel.tsx` (+ teste) — o `<th>` de `px-3 py-2` para `px-2 py-2 text-sm`
  (único consumidor: a `PanoramaPage`, 6 linhas; confira com `grep -rn "<CabecalhoOrdenavel" web/src --include=*.tsx |
  grep -v "\.test\."` antes de mudar a primitiva; se houver outro consumidor, pare e reporte)
- Modify: `web/src/panorama/PanoramaPage.tsx` — `CLASSES_DE_COLUNA` de `px-3 py-2 …` para `px-2 py-2 text-sm …`
- Modify: `web/src/panorama/LinhaDoPanorama.tsx` — toda `td` de `p-2` para `px-1.5 py-2`; a do desenho de `w-28` para
  `w-[4.5rem]`, com a miniatura num contêiner `max-w-16`; o conteúdo de "Complicação" e de "Embalado" num
  `<div className="max-w-[5.75rem]">`
- Modify: `web/src/index.css` — `@layer base { html { background-color: var(--color-fundo); } }`
- Create: `web/src/tema/fundoDaPagina.test.ts` — guarda, no molde de `idiomaDaPagina.test.ts`: lê o `index.css` de disco
  e reprova se não houver regra de `html` com `background-color: var(--color-fundo)`
- Modify: `CLAUDE.md` (bullet «O Panorama»: uma frase sobre a largura mínima e o fundo do `<html>`); a spec da 6A
  (seção 11, item F3)

- [ ] **Step 1: Testes que falham:** `fundoDaPagina.test.ts`; no teste do `CabecalhoOrdenavel`,
  `cabecalho_compacto` (o `<th>` tem `px-2` e `text-sm`). jsdom não mede layout: a largura se confere no navegador
  (abaixo), não no teste.
- [ ] **Step 2: Implementar** as mudanças de **Files**.
- [ ] **Step 3:** `npm test -- --run --maxWorkers=2` inteiro e `npm run build`.
- [ ] **Step 4: Docs.** Na seção 11 da spec:

```markdown
- **F3 — a tabela do Panorama volta a caber em 1024 px.** As duas colunas da 6A levaram a largura mínima da tabela a
  1.056 px num espaço de 961, e a página rolava de lado mostrando o `<html>` sem fundo. Cabeçalhos com 14 px e
  espaçamento lateral de 8 px, células com 6 px, a coluna do desenho com 72 px e o conteúdo de "Complicação" e
  "Embalado" até 92 px: mínimo de 956 px, medido no navegador com a massa de C14. O `<html>` ganha o fundo da página,
  para uma tabela que estoure no futuro mostrar rolagem e não faixa branca. A folga é pequena (5 px): o mínimo é ditado
  pelo texto que não quebra, como o número do Pedido.
```

- [ ] **Step 5: Commit** — `fix(6a): a tabela do Panorama cabe em 1024 px`.

**Medição (do controlador, depois da review):** no navegador em 1024 × 1080, com a massa, a largura mínima da tabela
(forçando `width: 1px` nela e lendo `getBoundingClientRect().width`) tem de ser **no máximo 961 px**, e
`document.documentElement.scrollWidth` igual ao `clientWidth`. Registre os dois números no ledger.

---

## Depois das três tasks

A conferência do usuário já foi feita; os ajustes não pedem conferência nova obrigatória, mas o controlador mostra ao
usuário a trilha nova (F1) no navegador antes da review de branch, porque é desenho que ele escolheu por mock. Depois:
a **review de branch inteira** (base `origin/main` `accd69e`, no modelo mais capaz, sem o ledger das Tasks 1–9 da
nuvem, que não chegou — o revisor recebe o diff e a spec), fix wave, re-review e PR.
