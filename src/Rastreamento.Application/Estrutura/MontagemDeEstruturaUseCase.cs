using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Estrutura;

/// <summary>
/// Cria a Peca copiando a receita padrao (Task 2 planeja, aqui grava) e devolve a arvore lida de
/// volta. `_catalogo` (IReceitaPadraoRepository) e usado SO pelos tres lookups em lote de
/// Componente/Material/Setor por Id — nao pelos metodos de receita padrao propriamente ditos
/// (aqueles vivem em `IEstruturaRepository.LerReceitaCompletaAsync`, moldado para o formato que o
/// `PlanejadorDeCopia` consome). Reuso deliberado: evitar duplicar tres consultas em lote que ja
/// existem e ja sao exercitadas por `ReceitaPadraoUseCase`. Nao esta no "Consumes" do brief da
/// Task 3 — ver o relatorio.
/// </summary>
public sealed class MontagemDeEstruturaUseCase
{
  private const string ErroDeAgrupamentoNaoEncontrado = "Agrupamento nao encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  /// <summary>
  /// Fecha o escape de OverflowException nomeado na review da Task 2 (ver XML doc de
  /// `PlanejadorDeCopia`): o planejador multiplica descendo e nao guarda magnitude, entao uma raiz
  /// grande combinada com fatores da receita pode estourar o `decimal` NO MEIO da descida. Sem este
  /// catch, a excecao escaparia por fora do contrato `PlanoDeCopia` (que devolve erro, nao lanca) e
  /// viraria 500 em vez de 400.
  /// </summary>
  private const string ErroDeQuantidadeExcessiva =
      "A quantidade informada, multiplicada pela receita, ultrapassa o que o sistema suporta.";

  private readonly IEstruturaRepository _estruturas;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly IReceitaPadraoRepository _catalogo;

  public MontagemDeEstruturaUseCase(
      IEstruturaRepository estruturas, IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo)
  {
    _estruturas = estruturas;
    _agrupamentos = agrupamentos;
    _catalogo = catalogo;
  }

  public async Task<Result<EstruturaItemDto>> CriarPeca(
      int agrupamentoId, NovaPecaDto nova, CancellationToken ct)
  {
    // DECISAO sobre magnitude/sinal de quantidade (empurrada para ca pela review da Task 2):
    // validar aqui, na Application — nao CHECK no schema (excecao de CHECK vira 500, nao 400; e o
    // mesmo criterio ja usado para Agrupamento.Tipo e Componente.Tipo) e nao dívida silenciosa. So
    // o SINAL: o valor absoluto excessivo e tratado abaixo, no catch de OverflowException, porque
    // um teto fixo aqui teria de ser arbitrario (a explosao depende dos fatores da receita, nao so
    // da raiz) enquanto o catch cobre exatamente o caso em que o valor realmente nao cabe.
    if (nova.Quantidade <= 0)
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeInvalida, TipoDeErro.Validacao);

    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Result<EstruturaItemDto>.Falha(ErroDeAgrupamentoNaoEncontrado, TipoDeErro.NaoEncontrado);

    var (arestasFilhos, arestasMateriais, arestasRoteiro) = await _estruturas.LerReceitaCompletaAsync(ct);
    var receita = new ReceitaDoCatalogo(
        arestasFilhos.ToLookup(f => f.Pai, f => (f.Filho, f.Qtd)),
        arestasMateriais.ToLookup(m => m.Comp, m => (m.Material, m.Qtd)),
        arestasRoteiro.ToLookup(r => r.Comp, r => (r.Setor, r.Ordem)));

    PlanoDeCopia plano;
    try
    {
      plano = PlanejadorDeCopia.Planejar(receita, nova.ComponenteId, nova.Quantidade);
    }
    catch (OverflowException)
    {
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeExcessiva, TipoDeErro.Validacao);
    }

    if (plano.Erro is not null)
      return Result<EstruturaItemDto>.Falha(plano.CodigoDoErro!, TipoDeErro.Conflito);

    // SEM guarda de status, e isso e decisao de negocio, nao omissao: cliente grande pede alteracao
    // de projeto com o Pedido JA em execucao, e acrescentar peca nova ao pedido rodando e o
    // comportamento PADRAO (informacao do usuario, 2026-08-29). O simetrico — o que sai do projeto
    // "so para de ser produzido" — NAO e desta fase: e a Fase 3, onde "Pedido em execucao" passa a
    // existir de fato (hoje nenhum Pedido sai de `Aberto`). Ver §2.3 da spec.
    //
    // A regra 18 tem uma segunda metade que TAMBEM nao e cobrada aqui: `Componente.ArquivoSolido`
    // preenchido. Sem a Fase 2B nao existe upload, entao ninguem consegue preencher pela interface,
    // e cobrar agora travaria a verificacao manual (os 54 Componentes do seed-demo nao tem solido).
    // Quem fecha isso e a 2B.
    var paraGravar = ConverterParaGravar(plano.Raiz!, ehRaiz: true, nova.RequerRelatorioDimensional);
    var raizId = await _estruturas.GravarArvoreAsync(agrupamentoId, null, paraGravar, ct);

    var arvore = await MontarArvore(agrupamentoId, ct);
    return Result<EstruturaItemDto>.Ok(arvore.Single(i => i.Id == raizId));
  }

  /// <summary>
  /// Nao esta nos Passos do brief da Task 3 (que so descreve `CriarPeca`), mas o plano da Fase 2
  /// (linha 1022) mostra a Task 5 chamando `_montagem.ObterArvore` diretamente, sem nenhuma task
  /// intermediaria que a introduza — e o proprio `IEstruturaRepository` da Task 3 ja expoe os
  /// quatro metodos de leitura (`ListarDoAgrupamentoAsync`, `ObterPorIdAsync`,
  /// `ListarMateriaisAsync`, `ListarRoteiroAsync`) que so fazem sentido para montar esta arvore.
  /// Implementada aqui; ver o relatorio da Task 3 para a decisao por extenso.
  /// </summary>
  public async Task<Result<IReadOnlyList<EstruturaItemDto>>> ObterArvore(int agrupamentoId, CancellationToken ct)
  {
    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Result<IReadOnlyList<EstruturaItemDto>>.Falha(
          ErroDeAgrupamentoNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<EstruturaItemDto>>.Ok(await MontarArvore(agrupamentoId, ct));
  }

  /// <summary>
  /// `NoPlanejado` -&gt; `NoParaGravar`. `RequerRelatorioDimensional` e POR PECA (regra 10): so a
  /// raiz recebe o valor do DTO — todo descendente, em qualquer profundidade, recebe `false`.
  /// </summary>
  private static NoParaGravar ConverterParaGravar(NoPlanejado no, bool ehRaiz, bool requerRelatorioDaRaiz) =>
      new(
          ComponenteId: no.ComponenteId,
          Descricao: no.Descricao,
          Quantidade: no.Quantidade,
          RequerRelatorioDimensional: ehRaiz && requerRelatorioDaRaiz,
          Materiais: no.Materiais,
          Roteiro: no.Roteiro,
          Filhos: no.Filhos.Select(f => ConverterParaGravar(f, ehRaiz: false, requerRelatorioDaRaiz)).ToList());

  /// <summary>
  /// Le a arvore inteira do Agrupamento (todas as Pecas e seus descendentes) e monta os DTOs, com
  /// nome de Material/Setor e Codigo/Descricao de Componente resolvidos em lote — nunca um lookup
  /// por no, que faria N+1 numa arvore de centenas de nos.
  /// </summary>
  private async Task<IReadOnlyList<EstruturaItemDto>> MontarArvore(int agrupamentoId, CancellationToken ct)
  {
    var itens = await _estruturas.ListarDoAgrupamentoAsync(agrupamentoId, ct);
    if (itens.Count == 0) return [];

    var ids = itens.Select(i => i.Id).ToList();
    var materiais = await _estruturas.ListarMateriaisAsync(ids, ct);
    var roteiros = await _estruturas.ListarRoteiroAsync(ids, ct);

    var componenteIds = itens.Where(i => i.ComponenteId.HasValue)
        .Select(i => i.ComponenteId!.Value).Distinct().ToList();
    var materialIds = materiais.Select(m => m.MaterialId).Distinct().ToList();
    var setorIds = roteiros.Select(r => r.SetorId).Distinct().ToList();

    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct))
        .ToDictionary(c => c.Id);
    var nomesMateriais = (await _catalogo.ObterMateriaisPorIdAsync(materialIds, ct))
        .ToDictionary(m => m.Id);
    var nomesSetores = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct))
        .ToDictionary(s => s.Id);

    var materiaisPorItem = materiais.ToLookup(m => m.EstruturaItemId);
    var roteirosPorItem = roteiros.ToLookup(r => r.EstruturaItemId);
    var filhosPorPai = itens.ToLookup(i => i.EstruturaPaiId);

    EstruturaItemDto Montar(EstruturaItem item)
    {
      string? codigo = null;
      var descricao = item.Descricao;
      if (item.ComponenteId is int componenteId && componentes.TryGetValue(componenteId, out var componente))
      {
        codigo = componente.Codigo;
        descricao ??= componente.Descricao;   // regra 19: NULL herda a descricao do Componente
      }
      descricao ??= string.Empty;

      var listaDeMateriais = materiaisPorItem[item.Id]
          .Select(m => new MaterialDoNoDto(
              m.MaterialId,
              nomesMateriais.TryGetValue(m.MaterialId, out var material) ? material.Descricao : string.Empty,
              m.Quantidade))
          .ToList();

      var listaDeRoteiro = roteirosPorItem[item.Id]
          .OrderBy(r => r.Ordem)
          .Select(r => new PassoDoRoteiroDto(
              r.SetorId,
              nomesSetores.TryGetValue(r.SetorId, out var setor) ? setor.Nome : string.Empty,
              r.Ordem))
          .ToList();

      var filhos = filhosPorPai[item.Id].Select(Montar).ToList();

      return new EstruturaItemDto(
          item.Id, item.ComponenteId, codigo, descricao, item.Quantidade,
          item.NivelHierarquico, item.RequerRelatorioDimensional, listaDeMateriais, listaDeRoteiro, filhos);
    }

    return filhosPorPai[null].Select(Montar).ToList();
  }
}
