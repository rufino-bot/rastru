using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

public class FakeSetorRepo : ISetorRepository
{
  private readonly List<Setor> _linhas;
  private int _proximoId;

  public FakeSetorRepo(params Setor[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(s => s.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  public Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(s => s.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`SetorRepository` real, `WHERE Nome = @p`). Duplicado-por-caixa (ex.: "solda" vs
  /// "Solda") nao e coberto neste nivel — precisa de um teste ponta a ponta contra o banco real.
  /// </summary>
  public Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(s => s.Nome == nome));

  public Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Setor>>(
          _linhas.Where(s => incluirInativos || s.Ativo).OrderBy(s => s.Nome).ToList());

  public Task AdicionarAsync(Setor setor, CancellationToken ct)
  {
    setor.Id = _proximoId++;
    _linhas.Add(setor);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

public class FakeMaterialRepo : IMaterialRepository
{
  private readonly List<Material> _linhas;
  private int _proximoId;

  public FakeMaterialRepo(params Material[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(m => m.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  public Task<Material?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(m => m.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`MaterialRepository` real, `WHERE Codigo = @p`) e de `UQ_Material_Codigo`.
  /// Duplicado-por-caixa (ex.: "ch-001" vs "CH-001") nao e coberto neste nivel — precisa de um
  /// teste ponta a ponta contra o banco real. NAO torne o fake case-insensitive: isso simularia
  /// o banco e esconderia a lacuna.
  /// </summary>
  public Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(m => m.Codigo == codigo));

  public Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Material>>(
          _linhas.Where(m => incluirInativos || m.Ativo).OrderBy(m => m.Codigo).ToList());

  public Task AdicionarAsync(Material material, CancellationToken ct)
  {
    material.Id = _proximoId++;
    _linhas.Add(material);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

public class FakePedidoRepo : IPedidoRepository
{
  private readonly List<Pedido> _linhas;
  private int _proximoId;

  public FakePedidoRepo(params Pedido[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(p => p.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  public Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(p => p.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`PedidoRepository` real, `WHERE Numero = @p`) e de `UQ_Pedido_Numero`.
  /// Duplicado-por-caixa (ex.: "ped-001" vs "PED-001") nao e coberto neste nivel — precisa de um
  /// teste ponta a ponta contra o banco real. NAO torne o fake case-insensitive: isso simularia
  /// o banco e esconderia a lacuna.
  /// </summary>
  public Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(p => p.Numero == numero));

  public Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Pedido>>(
          _linhas.OrderByDescending(p => p.DataAbertura).ToList());

  public Task AdicionarAsync(Pedido pedido, CancellationToken ct)
  {
    pedido.Id = _proximoId++;
    _linhas.Add(pedido);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

public class FakeComponenteRepo : IComponenteRepository
{
  private readonly List<Componente> _linhas;
  private int _proximoId;

  public FakeComponenteRepo(params Componente[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(c => c.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  /// <summary>O ultimo filtro que o caso de uso mandou — prova o que ele TRADUZIU, nao o retorno.</summary>
  public FiltroDeComponente? UltimoFiltro { get; private set; }

  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(c => c.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`ComponenteRepository` real, `WHERE Codigo = @p`) e de `UQ_Componente_Codigo`.
  /// Duplicado-por-caixa (ex.: "sup-01" vs "SUP-01") nao e coberto neste nivel — precisa de um
  /// teste ponta a ponta contra o banco real. NAO torne o fake case-insensitive: isso simularia
  /// o banco e esconderia a lacuna.
  /// </summary>
  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(c => c.Codigo == codigo));

  /// <summary>
  /// Espelha o repositorio real o suficiente para o caso de uso ser testavel, mas a fidelidade
  /// para aqui: a prova de que a paginacao acontece no SQL vive em `ComponenteMappingTests`.
  /// </summary>
  public Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct)
  {
    UltimoFiltro = filtro;

    var consulta = _linhas.Where(c => filtro.IncluirInativos || c.Ativo);
    if (!string.IsNullOrWhiteSpace(filtro.Busca))
    {
      var busca = filtro.Busca.Trim();
      consulta = consulta.Where(c => c.Codigo.Contains(busca) || c.Descricao.Contains(busca));
    }

    var filtradas = consulta.OrderBy(c => c.Codigo).ToList();
    var pagina = filtradas
        .Skip((filtro.Pagina - 1) * filtro.Tamanho)
        .Take(filtro.Tamanho)
        .ToList();

    return Task.FromResult<(IReadOnlyList<Componente>, int)>((pagina, filtradas.Count));
  }

  public Task AdicionarAsync(Componente componente, CancellationToken ct)
  {
    componente.Id = _proximoId++;
    _linhas.Add(componente);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

public class FakeAgrupamentoRepo : IAgrupamentoRepository
{
  private readonly List<Agrupamento> _linhas;
  private int _proximoId;

  public FakeAgrupamentoRepo(params Agrupamento[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(a => a.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  /// <summary>Ids que o teste quer fazer passar por "tem EstruturaItem" (tabela da Fase 2).</summary>
  public HashSet<int> ComEstrutura { get; } = [];

  public Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(a => a.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`AgrupamentoRepository` real, `WHERE PedidoId = @p0 AND Codigo = @p1`) e de
  /// `UQ_Agrupamento_PedidoCodigo`. Duplicado-por-caixa (ex.: "ag-01" vs "AG-01") nao e coberto
  /// neste nivel — precisa de um teste ponta a ponta contra o banco real. NAO torne o fake
  /// case-insensitive: isso simularia o banco e esconderia a lacuna. (`PedidoId` e int, entao a
  /// divergencia e so na parte textual da chave composta.)
  /// </summary>
  public Task<Agrupamento?> ObterPorPedidoECodigoAsync(
      int pedidoId, string codigo, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(a => a.PedidoId == pedidoId && a.Codigo == codigo));

  public Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(int pedidoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Agrupamento>>(
          _linhas.Where(a => a.PedidoId == pedidoId).OrderBy(a => a.Codigo).ToList());

  public Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct)
  {
    agrupamento.Id = _proximoId++;
    _linhas.Add(agrupamento);
    return Task.CompletedTask;
  }

  public Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct)
  {
    _linhas.Remove(agrupamento);
    return Task.CompletedTask;
  }

  public Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult(ComEstrutura.Contains(agrupamentoId));

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

/// <summary>
/// Fake em memoria do repositorio de receita padrao. Sem banco: o que se prova aqui e REGRA, e
/// regra que so passa com banco no ar e regra que ninguem roda. O que ele NAO cobre — a
/// atomicidade real do apaga-e-grava e o predicado do DELETE — e provado em
/// `ReceitaPadraoRepositoryTests` (Infrastructure), contra o SQL Server.
/// </summary>
public class FakeReceitaPadraoRepo : IReceitaPadraoRepository
{
  /// <summary>
  /// Ids de linha nascem ALTOS e fora da faixa dos ids de catalogo usados nos testes (1, 2, 10,
  /// 11, 20, 21) para que uma projecao que devolva o campo errado nao possa acertar por
  /// coincidencia numerica.
  /// </summary>
  private int _proximoId = 500;

  public List<Componente> Componentes { get; } = [];
  public List<Material> Materiais { get; } = [];
  public List<Setor> Setores { get; } = [];
  public List<ComponenteFilhoPadrao> Filhos { get; } = [];
  public List<ComponenteMaterialPadrao> MateriaisPadrao { get; } = [];
  public List<ComponenteRoteiroPadrao> Roteiro { get; } = [];

  /// <summary>
  /// Um contador POR TABELA, e nao um so: os tres `Substituir*Async` recebem tipos de entidade
  /// diferentes, entao chamar o errado nao compila — mas um teste que ARRANJA o cenario gravando
  /// num sub-recurso e depois afirma `== 1` querendo dizer "o outro gravou" e satisfeito pelo
  /// arranjo. E o mesmo formato do achado B11 da Fase 1A: assercao que casa com o cenario inteiro
  /// em vez de casar com o alvo.
  /// </summary>
  public int SubstituicoesDeFilhos { get; private set; }
  public int SubstituicoesDeMateriais { get; private set; }
  public int SubstituicoesDeRoteiro { get; private set; }

  /// <summary>
  /// Soma dos tres. Serve para "NAO gravou NADA" (que e o que os caminhos de recusa afirmam);
  /// para "gravou ISTO", use o contador da tabela.
  /// </summary>
  public int Substituicoes => SubstituicoesDeFilhos + SubstituicoesDeMateriais + SubstituicoesDeRoteiro;

  /// <summary>
  /// O `ct` de CADA chamada, na ordem em que chegaram. Sem isto, trocar o `ct` recebido por
  /// `CancellationToken.None` nas chamadas ao repositorio nao quebra teste nenhum — a propagacao
  /// do token fica verificada por leitura, e leitura nao roda no CI.
  /// </summary>
  public List<CancellationToken> TokensRecebidos { get; } = [];

  /// <summary>
  /// Faz a proxima gravacao de materiais OU de roteiro subir
  /// <see cref="ConflitoDeConcorrenciaException"/>, que e o que o repositorio real lanca quando o
  /// banco derruba o perdedor de duas substituicoes simultaneas (deadlock/lock timeout do range
  /// lock do SERIALIZABLE). Existe para provar a traducao para <c>TipoDeErro.Conflito</c> sem
  /// depender de uma corrida de banco.
  ///
  /// Vale para os TRES sub-recursos porque a transacao SERIALIZABLE e do repositorio, nao da
  /// tabela: quem grava roteiro ou filhos e derrubado pelo mesmo motivo que quem grava material.
  /// Enquanto so materiais lancava, um `catch` faltando no roteiro era invisivel — o teste nao
  /// tinha como chegar la; o mesmo valia para filhos ate a Task 5.
  /// </summary>
  public bool ConflitoNaProximaSubstituicao { get; set; }

  /// <summary>
  /// Faz a proxima leitura do roteiro (dentro de <c>ProjetarRoteiro</c>, chamada DEPOIS da
  /// gravacao) subir <see cref="ConflitoDeConcorrenciaException"/>. Existe para provar que o
  /// <c>try</c> de <c>SubstituirRoteiro</c> e ESTREITO — envolve so a gravacao, nao a releitura —
  /// entao essa excecao sobe CRUA e NAO vira <c>TipoDeErro.Conflito</c>. Pinado por
  /// <c>Falha_na_releitura_apos_gravar_o_roteiro_nao_vira_conflito</c>.
  /// </summary>
  public bool ConflitoNaProximaListagemDeRoteiro { get; set; }

  /// <summary>
  /// O terceiro gemeo, para materiais — o ultimo dos tres a existir. Enquanto ele faltava, alargar
  /// o <c>try</c> de <c>SubstituirMateriais</c> ate englobar a releitura, com
  /// <c>catch (Exception)</c>, sobrevivia a suite inteira: nao havia como um teste fazer a
  /// releitura de materiais estourar. Pinado por
  /// <c>Falha_na_releitura_apos_gravar_os_materiais_nao_vira_conflito</c>.
  /// </summary>
  public bool ConflitoNaProximaListagemDeMateriais { get; set; }

  /// <summary>
  /// O gemeo da flag acima para filhos, e por um motivo mais forte: em <c>SubstituirFilhos</c> o
  /// <c>try</c> convive com a leitura do grafo e a deteccao de ciclo, entao um <c>catch</c> largo
  /// transformaria <c>KeyNotFoundException</c>, <c>NullReference</c> e erro de montagem do grafo em
  /// 409 "tente de novo" — a pior resposta possivel, porque o usuario reenvia e falha de novo sem
  /// sintoma util. Pinado por <c>Falha_na_releitura_apos_gravar_os_filhos_nao_vira_conflito</c>.
  /// </summary>
  public bool ConflitoNaProximaListagemDeFilhos { get; set; }

  private Task<T> Anotado<T>(CancellationToken ct, T valor)
  {
    TokensRecebidos.Add(ct);
    return Task.FromResult(valor);
  }

  public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
      Anotado(ct, Componentes.SingleOrDefault(c => c.Id == id));

  public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(
      int componenteId, CancellationToken ct)
  {
    if (ConflitoNaProximaListagemDeFilhos)
      throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    return Anotado<IReadOnlyList<ComponenteFilhoPadrao>>(
        ct, Filhos.Where(f => f.ComponentePaiId == componenteId).ToList());
  }

  public Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(
      int componenteId, CancellationToken ct)
  {
    if (ConflitoNaProximaListagemDeMateriais)
      throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    return Anotado<IReadOnlyList<ComponenteMaterialPadrao>>(
        ct, MateriaisPadrao.Where(m => m.ComponenteId == componenteId).ToList());
  }

  public Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(
      int componenteId, CancellationToken ct)
  {
    if (ConflitoNaProximaListagemDeRoteiro)
      throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    return Anotado<IReadOnlyList<ComponenteRoteiroPadrao>>(
        ct, Roteiro.Where(r => r.ComponenteId == componenteId).OrderBy(r => r.Ordem).ToList());
  }

  public Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Anotado<IReadOnlyList<Componente>>(ct, Componentes.Where(c => ids.Contains(c.Id)).ToList());

  public Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Anotado<IReadOnlyList<Material>>(ct, Materiais.Where(m => ids.Contains(m.Id)).ToList());

  public Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Anotado<IReadOnlyList<Setor>>(ct, Setores.Where(s => ids.Contains(s.Id)).ToList());

  public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(CancellationToken ct) =>
      Anotado<IReadOnlyList<ComponenteFilhoPadrao>>(ct, Filhos.ToList());

  public Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct)
  {
    SubstituicoesDeFilhos++;
    TokensRecebidos.Add(ct);
    if (ConflitoNaProximaSubstituicao) throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    Filhos.RemoveAll(f => f.ComponentePaiId == componenteId);
    foreach (var nova in novas) nova.Id = _proximoId++;
    Filhos.AddRange(novas);
    return Task.CompletedTask;
  }

  public Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct)
  {
    SubstituicoesDeMateriais++;
    TokensRecebidos.Add(ct);
    if (ConflitoNaProximaSubstituicao) throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    // Apaga por PREDICADO no parametro, como o repositorio real. E grava as linhas COMO VIERAM:
    // se o caso de uso montar uma linha com ComponenteId errado, ela fica no fake e some da
    // leitura daquele componente, em vez de ser corrigida pelo fake.
    MateriaisPadrao.RemoveAll(m => m.ComponenteId == componenteId);
    // Imita a IDENTITY do banco: sem isto todo `Id` de linha ficaria 0 e uma projecao que
    // devolvesse 0 no lugar do Id passaria por acidente.
    foreach (var nova in novas) nova.Id = _proximoId++;
    MateriaisPadrao.AddRange(novas);
    return Task.CompletedTask;
  }

  public Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct)
  {
    SubstituicoesDeRoteiro++;
    TokensRecebidos.Add(ct);
    if (ConflitoNaProximaSubstituicao) throw new ConflitoDeConcorrenciaException(new Exception("simulado"));
    Roteiro.RemoveAll(r => r.ComponenteId == componenteId);
    foreach (var nova in novas) nova.Id = _proximoId++;
    Roteiro.AddRange(novas);
    return Task.CompletedTask;
  }
}
