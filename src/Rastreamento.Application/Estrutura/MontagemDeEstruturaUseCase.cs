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

  /// <summary>
  /// Piso e sinal na MESMA mensagem: `QuantidadeMinimaDaColuna` (0,0001) ja e maior que zero, entao
  /// um unico `&lt;` cobre negativo, zero e positivo pequeno demais para a coluna guardar sem
  /// arredondar. Fechado junto do Important 1 da review da Task 3 — a mensagem antiga
  /// ("maior que zero") afirmava menos do que o codigo hoje garante.
  /// </summary>
  private const string ErroDeQuantidadeInvalida =
      "Quantidade deve ser de pelo menos 0,0001 — abaixo disso a coluna do banco arredonda para zero.";

  /// <summary>
  /// Fecha o escape de OverflowException nomeado na review da Task 2 (ver XML doc de
  /// `PlanejadorDeCopia`): o planejador multiplica descendo e nao guarda magnitude, entao uma raiz
  /// grande combinada com fatores da receita pode estourar o `decimal` NO MEIO da descida. Sem este
  /// catch, a excecao escaparia por fora do contrato `PlanoDeCopia` (que devolve erro, nao lanca) e
  /// viraria 500 em vez de 400. Nao cobre a faixa da COLUNA — isso e `QuantidadeExcedeColunaException`,
  /// capturada abaixo (Important 1 da review da Task 3).
  /// </summary>
  private const string ErroDeQuantidadeExcessiva =
      "A quantidade informada, multiplicada pela receita, ultrapassa o que o sistema suporta.";

  private readonly IEstruturaRepository _estruturas;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly IPedidoRepository _pedidos;

  public MontagemDeEstruturaUseCase(
      IEstruturaRepository estruturas, IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo,
      IPedidoRepository pedidos)
  {
    _estruturas = estruturas;
    _agrupamentos = agrupamentos;
    _catalogo = catalogo;
    _pedidos = pedidos;
  }

  public async Task<Result<EstruturaItemDto>> CriarPeca(
      int agrupamentoId, NovaPecaDto nova, CancellationToken ct)
  {
    // DECISAO sobre magnitude/sinal de quantidade (empurrada para ca pela review da Task 2, e
    // corrigida pelo Important 1 da review da Task 3): a faixa guardada e a da COLUNA
    // (`EstruturaItem.Quantidade DECIMAL(18,4)`), nao a do tipo `decimal` do .NET. Piso aqui, na
    // Application — nao CHECK no schema (excecao de CHECK vira 500, nao 400; mesmo criterio ja
    // usado para Agrupamento.Tipo e Componente.Tipo). O teto NAO entra aqui: depende dos fatores da
    // receita, nao so da raiz, entao mora em `PlanejadorDeCopia.Descer` — aplicado a CADA no da
    // descida, capturado abaixo em `QuantidadeExcedeColunaException`.
    if (nova.Quantidade < PlanejadorDeCopia.QuantidadeMinimaDaColuna)
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeInvalida, TipoDeErro.Validacao);

    var agrupamento = await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct);
    if (agrupamento is null)
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
    catch (QuantidadeExcedeColunaException e)
    {
      return Result<EstruturaItemDto>.Falha(e.Message, TipoDeErro.Validacao);
    }

    // Important 2 da review da Task 3: `Erro` carrega o CODIGO estavel (o que o front comuta,
    // ex.: "CicloNaReceita"), `Detalhe` carrega a FRASE que `PlanejadorDeCopia` ja produz (ex.: "A
    // receita tem um ciclo: 2 -> 3 -> 2. ..."). Antes so o codigo ia — a frase, que levou tres
    // rodadas de review na Task 2 para nomear o CAMINHO do ciclo, era descartada.
    if (plano.Erro is not null)
      return Result<EstruturaItemDto>.Falha(plano.CodigoDoErro!, TipoDeErro.Conflito, plano.Erro);

    // SEM guarda de status, e isso e decisao de negocio, nao omissao: cliente grande pede alteracao
    // de projeto com o Pedido JA em execucao, e acrescentar peca nova ao pedido rodando e o
    // comportamento PADRAO (informacao do usuario, 2026-08-29). O simetrico — o que sai do projeto
    // "so para de ser produzido" — NAO e desta fase: e a Fase 3, onde "Pedido em execucao" passa a
    // existir de fato (hoje nenhum Pedido sai de `Aberto`). Ver §2.3 da spec.
    //
    // `_pedidos` existe (Minor 7 da review da Task 3) so para que esta decisao seja TESTAVEL: sem
    // injetar o repositorio, nenhum teste consegue provar que uma guarda futura quebraria — a
    // decisao ficaria documentada so em comentario, sem forma barata de virar guarda executavel
    // (a propria review reconhece isso). O valor lido e DESCARTADO de proposito: ler o Pedido aqui
    // e prova de ALCANCE, nao guarda — se algum dia esta decisao mudar, e este `_ =` que vira o
    // `if (pedido.Status != "Aberto") ...`. Medido em 2026-08-29 (ver relatorio): acrescentar essa
    // guarda no lugar do descarte faz `Criar_Peca_em_Pedido_fora_de_Aberto_e_permitido_...` morrer,
    // sem afetar as outras `CriarPecaTests` (o `FakePedidoRepo` de `Montar()` fica vazio nelas).
    //
    // A regra 18 tem uma segunda metade que TAMBEM nao e cobrada aqui: `Componente.ArquivoSolido`
    // preenchido. Sem a Fase 2B nao existe upload, entao ninguem consegue preencher pela interface,
    // e cobrar agora travaria a verificacao manual (os 54 Componentes do seed-demo nao tem solido).
    // Quem fecha isso e a 2B.
    _ = await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct);

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

    // Minor 2 da review da Task 3: nada ordenava filhos/materiais (so o roteiro, por `Ordem`, que e
    // sequencia de negocio). `ToLookup` preserva a ordem de CHEGADA de `itens`/`materiais`, que e a
    // ordem que o SQL Server devolver — nao garantida entre chamadas sem `ORDER BY` (heap sem
    // indice clusterizado). A ordenacao entra aqui, explicita por `Id`, em vez de confiar so no
    // repositorio: e o que o teste
    // `Filhos_e_materiais_saem_ordenados_por_Id_mesmo_que_o_repositorio_devolva_fora_de_ordem`
    // prova, alimentando o fake fora de ordem de proposito — um teste de Infra contra SQL Server
    // real ficaria verde de qualquer jeito nesta tabela pequena (o heap tende a devolver em ordem
    // de insercao), o que o tornaria fraco.
    var visitados = new HashSet<int>();

    EstruturaItemDto Montar(EstruturaItem item)
    {
      visitados.Add(item.Id);

      string? codigo = null;
      var descricao = item.Descricao;
      if (item.ComponenteId is int componenteId && componentes.TryGetValue(componenteId, out var componente))
      {
        codigo = componente.Codigo;
        descricao ??= componente.Descricao;   // regra 19: NULL herda a descricao do Componente
      }
      descricao ??= string.Empty;

      var listaDeMateriais = materiaisPorItem[item.Id]
          .OrderBy(m => m.Id)
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

      var filhos = filhosPorPai[item.Id].OrderBy(f => f.Id).Select(Montar).ToList();

      return new EstruturaItemDto(
          item.Id, item.ComponenteId, codigo, descricao, item.Quantidade,
          item.NivelHierarquico, item.RequerRelatorioDimensional, listaDeMateriais, listaDeRoteiro, filhos);
    }

    var raizes = filhosPorPai[null].OrderBy(i => i.Id).Select(Montar).ToList();

    // Minor 3 da review da Task 3: um no cujo EstruturaPaiId aponta para fora do proprio
    // Agrupamento nunca e visitado por `Montar` (que so desce a partir de `filhosPorPai[null]`) —
    // sem esta checagem ele SOME da arvore devolvida, sem erro, sem log: perda de dado silenciosa
    // numa LEITURA. Hoje nao deveria acontecer (toda gravacao usa o mesmo AgrupamentoId,
    // `EstruturaRepository.GravarNo`), mas o schema nao impede a inconsistencia — lancar aqui torna
    // o caso AUDIVEL em vez de invisivel, sem inventar recuperacao: quem le a arvore incompleta sem
    // saber vira o operador, e isso e pior que um 500.
    if (visitados.Count != itens.Count)
    {
      var orfaos = itens.Where(i => !visitados.Contains(i.Id)).Select(i => i.Id);
      throw new ArvoreInconsistenteException(
          $"A arvore do Agrupamento {agrupamentoId} tem {itens.Count - visitados.Count} no(s) "
              + "orfao(s) — EstruturaPaiId aponta para fora dos itens lidos deste Agrupamento: "
              + $"{string.Join(", ", orfaos)}.");
    }

    return raizes;
  }
}

/// <summary>
/// Dado inconsistente na leitura da arvore: um <see cref="EstruturaItem"/> cujo
/// <c>EstruturaPaiId</c> nao esta entre os itens lidos do proprio Agrupamento. Ver o comentario em
/// <c>MontagemDeEstruturaUseCase.MontarArvore</c> (Minor 3 da review da Task 3) para o porque de
/// virar excecao em vez de o no simplesmente desaparecer da arvore devolvida.
/// </summary>
public sealed class ArvoreInconsistenteException(string mensagem) : Exception(mensagem);
