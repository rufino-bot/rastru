using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Uma fabrica em memoria para os casos de uso da Fase 3: um Pedido `Aberto` com um Agrupamento,
/// cinco Setores (um inativo) e tres usuarios. Os nos se descrevem como "No {id}", o que deixa as
/// mensagens conferiveis por texto. Cada task acrescenta aqui a fabrica do caso de uso que cria.
/// </summary>
internal sealed class CenarioDeExecucao
{
  public const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4, Inativo = 9;
  public const int Operador = 10, Movimentador = 11, Pcp = 12;
  public const int PedidoId = 1, AgrupamentoId = 1;

  public FakeEstruturaRepo Estruturas { get; } = new();
  public FakeExecucaoRepo Execucao { get; }
  public FakeSetorRepo Setores { get; }
  public FakeReceitaPadraoRepo Catalogo { get; } = new();

  public CenarioDeExecucao()
  {
    Execucao = new FakeExecucaoRepo(Estruturas);
    var setores = new[]
    {
      new Setor { Id = Corte, Nome = "Corte", Ativo = true },
      new Setor { Id = Dobra, Nome = "Dobra", Ativo = true },
      new Setor { Id = Solda, Nome = "Solda", Ativo = true },
      new Setor { Id = Pintura, Nome = "Pintura", Ativo = true },
      new Setor { Id = Inativo, Nome = "Serra antiga", Ativo = false },
    };
    Setores = new FakeSetorRepo(setores);
    Catalogo.Setores.AddRange(setores);
    Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-01");
    Execucao.StatusDoPedido[PedidoId] = "Aberto";
    Execucao.Usuarios[Operador] = "Operador do Corte";
    Execucao.Usuarios[Movimentador] = "Movimentador";
    Execucao.Usuarios[Pcp] = "PCP";
  }

  /// <summary>Um no com Roteiro de um passo por Setor, `Ordem` 1, 2, 3... Sem Setor: sem Roteiro.</summary>
  public int No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores)
  {
    Estruturas.Itens.Add(new EstruturaItem
    {
      Id = id, AgrupamentoId = AgrupamentoId, Descricao = $"No {id}", EstruturaPaiId = pai,
      NivelHierarquico = pai is null ? "Peca" : "Item", Quantidade = quantidade, QuantidadePorPai = razao,
    });
    for (var i = 0; i < setores.Length; i++)
      Estruturas.Roteiros.Add(new EstruturaRoteiro
      {
        Id = 100 * id + i + 1, EstruturaItemId = id, SetorId = setores[i], Ordem = i + 1,
      });
    return id;
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

  public RoteiroDoNoUseCase Roteiro() => new(Execucao, Estruturas, Catalogo);

  public MontagemDeEstruturaUseCase Estrutura() =>
      new(Estruturas, new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo, new FakePedidoRepo(new Pedido
          {
            Id = PedidoId, Numero = "PED-01", Cliente = "Cliente", Tipo = "Normal",
            Status = Execucao.StatusDoPedido[PedidoId], DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = Pcp,
          }),
          Execucao);

  public ConsultaDeExecucaoUseCase Consulta() =>
      new(Execucao, Estruturas, Setores,
          new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo);

  /// <summary>
  /// A calculadora sobre o estado inteiro do cenario, lida direto dos fakes — para os testes afirmarem
  /// o saldo depois de uma operacao sem passar pelo caso de uso que acabaram de exercitar.
  /// </summary>
  public CalculadoraDeExecucao Calcular() =>
      new(Estruturas.Itens.Select(i => new NoDoCalculo(i.Id, i.EstruturaPaiId, i.Quantidade, i.QuantidadePorPai,
              Estruturas.Roteiros.Where(r => r.EstruturaItemId == i.Id).OrderBy(r => r.Ordem)
                  .Select(r => new PassoDoCalculo(r.SetorId, r.Ordem)).ToList())),
          Livro.SomarSaldos(Execucao.Movimentacoes),
          Execucao.Montagens.Where(g => g.EstornadaEm is null).GroupBy(g => g.EstruturaItemId)
              .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade)),
          Execucao.Movimentacoes
              .SelectMany(m => new[] { (m.EstruturaItemId, m.OrigemOrdem), (m.EstruturaItemId, m.DestinoOrdem) })
              .Where(p => p.Item2 is not null)
              .Select(p => (p.Item1, p.Item2!.Value))
              .Distinct()
              .ToList());
}
