using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Expedicao;
using Rastreamento.Application.Tests.Separacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Uma fabrica em memoria para os casos de uso da Fase 3: dois Pedidos `Aberto`, cada um com um
/// Agrupamento (o segundo so para quem precisa de nos em Pedidos diferentes, como o lote), cinco
/// Setores (um inativo) e tres usuarios. Os nos se descrevem como "No {id}", o que deixa as
/// mensagens conferiveis por texto. Cada task acrescenta aqui a fabrica do caso de uso que cria.
/// </summary>
internal sealed class CenarioDeExecucao
{
  public const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4, Inativo = 9;
  public const int Operador = 10, Movimentador = 11, Pcp = 12, Qualidade = 13, Gestao = 14;
  public const int PedidoId = 1, AgrupamentoId = 1;
  public const int PedidoId2 = 2, AgrupamentoId2 = 2;

  /// <summary>Quem le a fila nos testes: o Operador ve so os seus registros; o PCP ve todos.</summary>
  public static readonly QuemLe ComoOperador = new(Operador, false);
  public static readonly QuemLe ComoPcp = new(Pcp, true);

  public FakeEstruturaRepo Estruturas { get; } = new();
  public FakeExecucaoRepo Execucao { get; }
  public FakeSetorRepo Setores { get; }
  public FakeReceitaPadraoRepo Catalogo { get; } = new();

  /// <summary>A separacao de material sobre o MESMO planejado da arvore (`Estruturas.Materiais`).</summary>
  public FakeSeparacaoRepo Separacao { get; }

  /// <summary>
  /// Os registros do fim do caminho da Peca (afericao, contagem, liberacao, remessa). Participa da transacao do
  /// `Execucao`, como o `DbContext` compartilhado do codigo real.
  /// </summary>
  public FakeExpedicaoRepo Expedicao { get; }

  public CenarioDeExecucao()
  {
    Separacao = new FakeSeparacaoRepo(Estruturas);
    Execucao = new FakeExecucaoRepo(Estruturas);
    Expedicao = new FakeExpedicaoRepo(Execucao, Estruturas);
    var setores = new[]
    {
      new Setor { Id = Corte, Nome = "Corte", Ativo = true },
      new Setor { Id = Dobra, Nome = "Dobra", Ativo = true },
      new Setor { Id = Solda, Nome = "Solda", Ativo = true, Atividade = "solda" },
      new Setor { Id = Pintura, Nome = "Pintura", Ativo = true },
      new Setor { Id = Inativo, Nome = "Serra antiga", Ativo = false },
    };
    Setores = new FakeSetorRepo(setores);
    Catalogo.Setores.AddRange(setores);
    Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-01");
    Execucao.StatusDoPedido[PedidoId] = "Aberto";
    Execucao.ClienteDoPedido[PedidoId] = "Cliente do cenário";
    Execucao.Agrupamentos[AgrupamentoId2] = ("AG-02", PedidoId2, "PED-02");
    Execucao.StatusDoPedido[PedidoId2] = "Aberto";
    Execucao.ClienteDoPedido[PedidoId2] = "Outro cliente do cenário";
    Execucao.Usuarios[Operador] = "Operador do Corte";
    Execucao.Usuarios[Movimentador] = "Movimentador";
    Execucao.Usuarios[Pcp] = "PCP";
    Execucao.Usuarios[Qualidade] = "Qualidade";
    Execucao.Usuarios[Gestao] = "Gestão";
  }

  /// <summary>Um no com Roteiro de um passo por Setor, `Ordem` 1, 2, 3... Sem Setor: sem Roteiro.</summary>
  public int No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      NoDoAgrupamento(AgrupamentoId, id, pai, quantidade, razao, setores);

  /// <summary>
  /// Como <see cref="No"/>, num Agrupamento escolhido. Metodo a parte, e nao um parametro opcional de
  /// <see cref="No"/>: um opcional antes do `params` engoliria o primeiro Setor de toda chamada existente.
  /// </summary>
  public int NoDoAgrupamento(int agrupamento, int id, int? pai, decimal quantidade, decimal? razao, params int[] setores)
  {
    Estruturas.Itens.Add(new EstruturaItem
    {
      Id = id, AgrupamentoId = agrupamento, Descricao = $"No {id}", EstruturaPaiId = pai,
      NivelHierarquico = pai is null ? "Peca" : "Item", Quantidade = quantidade, QuantidadePorPai = razao,
    });
    for (var i = 0; i < setores.Length; i++)
      Estruturas.Roteiros.Add(new EstruturaRoteiro
      {
        Id = 100 * id + i + 1, EstruturaItemId = id, SetorId = setores[i], Ordem = i + 1,
      });
    return id;
  }

  /// <summary>
  /// Um material no catalogo do fake e gravado no no — o que `EstruturaMaterial` guarda. Entra tambem no
  /// catalogo da separacao: do almoxarifado quando `setorDeEstoqueId` e nulo, senao "fica em" esse Setor.
  /// </summary>
  public int Material(int noId, int materialId, string codigo, string descricao, decimal quantidade = 1m, int? setorDeEstoqueId = null)
  {
    Execucao.CatalogoDeMateriais[materialId] = (codigo, descricao);
    Separacao.Materiais[materialId] = new MaterialDaSeparacao(
        materialId, codigo, descricao, "UN", true, setorDeEstoqueId, null);
    Estruturas.Materiais.Add(new EstruturaMaterial
    {
      Id = 1000 + Estruturas.Materiais.Count, EstruturaItemId = noId, MaterialId = materialId, Quantidade = quantidade,
    });
    return materialId;
  }

  /// <summary>Arranjo direto no livro de separacao: `quantidade` do material separada para o no.</summary>
  public void Separar(int noId, int materialId, decimal quantidade) =>
      Separacao.Linhas.Add(new MaterialSeparacao
      {
        Id = Separacao.Linhas.Count + 1, EstruturaItemId = noId, MaterialId = materialId,
        Tipo = TiposDeSeparacao.Separacao, Quantidade = quantidade, DataHora = DateTime.UtcNow, UsuarioId = Operador,
      });

  /// <summary>
  /// O Agrupamento do cenario vira Kit e a Solda ganha `UtilizaKit` (spec da Fase 3B, D3). A `Setor` do fake
  /// tambem e marcada, para quem le o catalogo ver o mesmo que a execucao.
  /// </summary>
  public void Kit(int agrupamento = AgrupamentoId)
  {
    Execucao.AgrupamentosKit.Add(agrupamento);
    Execucao.SetoresComKit.Add(Solda);
    foreach (var s in Catalogo.Setores.Where(s => s.Id == Solda)) s.UtilizaKit = true;
  }

  /// <summary>Arranjo direto no livro, sem caso de uso.</summary>
  public Movimentacao Mover(int item, string tipo, Local de, Local para, decimal quantidade, int usuario = Operador) =>
      Execucao.Semear(new Movimentacao
      {
        EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
        OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
        DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
        DataHora = DateTime.UtcNow, UsuarioId = usuario,
      });

  public ApontamentoUseCase Apontamento() => new(Execucao, Estruturas, Setores, Catalogo);

  public EntregaUseCase Entrega() => new(Execucao, Estruturas, Catalogo);

  public EstornoUseCase Estorno() => new(Execucao, Estruturas, Catalogo);

  public PausaDePedidoUseCase Pausa() => new(Execucao);

  public AfericaoUseCase Afericao() => new(Execucao, Expedicao, Estruturas, Setores, Catalogo);

  public RoteiroDoNoUseCase Roteiro() => new(Execucao, Estruturas, Catalogo);

  public MontagemDeEstruturaUseCase Estrutura() =>
      new(Estruturas, new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo, new FakePedidoRepo(new Pedido
          {
            Id = PedidoId, Numero = "PED-01", Cliente = "Cliente", Tipo = "Normal",
            Status = Execucao.StatusDoPedido[PedidoId], DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = Pcp,
          }),
          Execucao, Expedicao);

  public ConsultaDeExecucaoUseCase Consulta() =>
      new(Execucao, Estruturas, Setores,
          new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo, Separacao);

  /// <summary>
  /// A calculadora sobre o estado inteiro do cenario, lida direto dos fakes — para os testes afirmarem
  /// o saldo depois de uma operacao sem passar pelo caso de uso que acabaram de exercitar.
  /// </summary>
  public CalculadoraDeExecucao Calcular() =>
      new(Estruturas.Itens.Select(i => new NoDoCalculo(i.Id, i.EstruturaPaiId, i.Quantidade, i.QuantidadePorPai,
              Estruturas.Roteiros.Where(r => r.EstruturaItemId == i.Id).OrderBy(r => r.Ordem)
                  .Select(r => new PassoDoCalculo(r.SetorId, r.Ordem)).ToList(),
              Execucao.AgrupamentosKit.Contains(i.AgrupamentoId))),
          Livro.SomarSaldos(Execucao.Movimentacoes),
          Execucao.Montagens.Where(g => g.EstornadaEm is null).GroupBy(g => g.EstruturaItemId)
              .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade)),
          Execucao.Movimentacoes
              .SelectMany(m => new[] { (m.EstruturaItemId, m.OrigemOrdem), (m.EstruturaItemId, m.DestinoOrdem) })
              .Where(p => p.Item2 is not null)
              .Select(p => (p.Item1, p.Item2!.Value))
              .Distinct()
              .ToList(),
          Execucao.SetoresComKit);
}
