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
