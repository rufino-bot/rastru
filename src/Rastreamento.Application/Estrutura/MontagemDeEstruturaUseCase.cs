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

  // Nomeado por entidade, mesmo criterio de `CadastroDeAgrupamentoUseCase`: `AcrescentarFilho`,
  // `EditarNo` e `ExcluirNo` buscam um EstruturaItem por Id, e "no nao encontrado" e ambiguo com
  // "Agrupamento nao encontrado" acima.
  private const string ErroDeNoNaoEncontrado = "No nao encontrado.";

  /// <summary>
  /// Regra 19: `ComponenteId` nulo (no ad-hoc) NAO tem de onde herdar descricao — sem uma
  /// `Descricao` propria ele chegaria ANONIMO a tela do operador, que e exatamente o que a regra
  /// existe para impedir. Vale na criacao (`AcrescentarFilho`) e na edicao (`EditarNo`): um no
  /// ad-hoc nao pode ser esvaziado de volta ao anonimato.
  /// </summary>
  private const string ErroDeDescricaoObrigatoria =
      "Descricao e obrigatoria para um no sem Componente associado (regra 19).";

  private const string StatusAberto = "Aberto";

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
  private readonly MontadorDeArvoreDeEstrutura _montador;

  public MontagemDeEstruturaUseCase(
      IEstruturaRepository estruturas, IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo,
      IPedidoRepository pedidos)
  {
    _estruturas = estruturas;
    _agrupamentos = agrupamentos;
    _catalogo = catalogo;
    _pedidos = pedidos;
    // Instanciado direto, nao via DI: e um colaborador interno, sem estado proprio alem das duas
    // dependencias que o caso de uso ja recebe — nao ha razao para o container conhecer o tipo.
    _montador = new MontadorDeArvoreDeEstrutura(estruturas, catalogo);
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

    var arvore = await _montador.MontarAsync(agrupamentoId, ct);
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

    return Result<IReadOnlyList<EstruturaItemDto>>.Ok(await _montador.MontarAsync(agrupamentoId, ct));
  }

  /// <summary>
  /// Acrescenta um sub-Item sob um no ja existente (`paiId`, Peca ou Item — ambos podem ganhar
  /// filho). Duas formas, discriminadas por `ComponenteId`:
  ///
  /// - Vindo de Componente: mesma maquina de `CriarPeca`/`PlanejadorDeCopia` — a receita do
  ///   catalogo e copiada recursivamente a partir do Componente informado, com TODAS as guardas do
  ///   planejador valendo (ciclo por caminho, profundidade/tamanho maximos, piso e teto de
  ///   quantidade da coluna). `RequerRelatorioDimensional` nunca vale aqui (regra 10: so a RAIZ da
  ///   Peca pode exigir relatorio, e um sub-Item nunca e raiz).
  /// - Ad-hoc (`ComponenteId` nulo): um no solto so — sem receita, sem filhos, sem materiais, sem
  ///   roteiro — e por isso PRECISA de `Descricao` propria (regra 19): sem Componente para herdar
  ///   dela, um no sem descricao chegaria anonimo a tela do operador.
  ///
  /// SEM guarda de status do Pedido, pelo mesmo motivo que `CriarPeca` nao tem: acrescentar
  /// estrutura ao Pedido em execucao e o comportamento PADRAO da fabrica, nao excecao — ver o
  /// comentario em `CriarPeca`. A assimetria com `ExcluirNo` (que CHECA status) e deliberada.
  /// </summary>
  public async Task<Result<EstruturaItemDto>> AcrescentarFilho(int paiId, NovoFilhoDto novo, CancellationToken ct)
  {
    if (novo.Quantidade < PlanejadorDeCopia.QuantidadeMinimaDaColuna)
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeInvalida, TipoDeErro.Validacao);

    var pai = await _estruturas.ObterPorIdAsync(paiId, ct);
    if (pai is null)
      return Result<EstruturaItemDto>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);

    NoParaGravar paraGravar;
    if (novo.ComponenteId is int componenteId)
    {
      var (arestasFilhos, arestasMateriais, arestasRoteiro) = await _estruturas.LerReceitaCompletaAsync(ct);
      var receita = new ReceitaDoCatalogo(
          arestasFilhos.ToLookup(f => f.Pai, f => (f.Filho, f.Qtd)),
          arestasMateriais.ToLookup(m => m.Comp, m => (m.Material, m.Qtd)),
          arestasRoteiro.ToLookup(r => r.Comp, r => (r.Setor, r.Ordem)));

      PlanoDeCopia plano;
      try
      {
        plano = PlanejadorDeCopia.Planejar(receita, componenteId, novo.Quantidade);
      }
      catch (OverflowException)
      {
        return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeExcessiva, TipoDeErro.Validacao);
      }
      catch (QuantidadeExcedeColunaException e)
      {
        return Result<EstruturaItemDto>.Falha(e.Message, TipoDeErro.Validacao);
      }

      if (plano.Erro is not null)
        return Result<EstruturaItemDto>.Falha(plano.CodigoDoErro!, TipoDeErro.Conflito, plano.Erro);

      // ehRaiz sempre false: um sub-Item pendurado num no existente nunca e a raiz da Peca.
      paraGravar = ConverterParaGravar(plano.Raiz!, ehRaiz: false, requerRelatorioDaRaiz: false);
    }
    else
    {
      if (string.IsNullOrWhiteSpace(novo.Descricao))
        return Result<EstruturaItemDto>.Falha(ErroDeDescricaoObrigatoria, TipoDeErro.Validacao);

      paraGravar = new NoParaGravar(
          ComponenteId: null,
          Descricao: novo.Descricao.Trim(),
          Quantidade: novo.Quantidade,
          RequerRelatorioDimensional: false,
          Materiais: [],
          Roteiro: [],
          Filhos: []);
    }

    var novoId = await _estruturas.GravarArvoreAsync(pai.AgrupamentoId, paiId, paraGravar, ct);

    var arvore = await _montador.MontarAsync(pai.AgrupamentoId, ct);
    return Result<EstruturaItemDto>.Ok(BuscarNo(arvore, novoId)!);
  }

  /// <summary>
  /// Edita `Descricao` e `Quantidade` de um no ja existente (Peca ou Item). NAO cascateia a
  /// quantidade para os filhos — decisao de dominio, nao lacuna: a copia da receita e
  /// PRE-PREENCHIMENTO, nao automacao, e nao existe invariante "filho = pai x razao" (um filho pode
  /// legitimamente divergir da proporcao original, por sobra de refugo). `Descricao` vazia/so
  /// espaco grava `null`, que volta a herdar a descricao do Componente (regra 19) — MAS so quando o
  /// no tem Componente para herdar dela: um no ad-hoc (`ComponenteId` nulo) nao pode ser esvaziado
  /// de volta ao anonimato que a regra 19 existe para impedir.
  /// </summary>
  public async Task<Result<EstruturaItemDto>> EditarNo(int id, EdicaoDeNoDto edicao, CancellationToken ct)
  {
    if (edicao.Quantidade < PlanejadorDeCopia.QuantidadeMinimaDaColuna)
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeInvalida, TipoDeErro.Validacao);

    var no = await _estruturas.ObterPorIdAsync(id, ct);
    if (no is null)
      return Result<EstruturaItemDto>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);

    var descricao = string.IsNullOrWhiteSpace(edicao.Descricao) ? null : edicao.Descricao.Trim();
    if (no.ComponenteId is null && descricao is null)
      return Result<EstruturaItemDto>.Falha(ErroDeDescricaoObrigatoria, TipoDeErro.Validacao);

    no.Descricao = descricao;
    no.Quantidade = edicao.Quantidade;
    await _estruturas.SalvarAlteracoesAsync(ct);

    var arvore = await _montador.MontarAsync(no.AgrupamentoId, ct);
    return Result<EstruturaItemDto>.Ok(BuscarNo(arvore, id)!);
  }

  /// <summary>
  /// Apaga um no e a subarvore inteira dele (Material/Roteiro de cada no, filhos antes de pais — a
  /// FK self-referenciada em `EstruturaPaiId` exige). So permitido enquanto o Pedido esta `Aberto`.
  /// </summary>
  public async Task<Result> ExcluirNo(int id, CancellationToken ct)
  {
    // EXCLUIR e CORRECAO DE MONTAGEM, nao descarte — sao operacoes diferentes e tem palavras
    // diferentes. Correcao so existe enquanto nada foi produzido, isto e, Pedido `Aberto`; e a
    // mesma fronteira que `CadastroDeAgrupamentoUseCase.Excluir` ja usa, entao a Fase 2 estende um
    // precedente em vez de inventar regra. DESCARTE ("saiu do projeto, para de ser produzido")
    // preserva a historia e nasce na Fase 3 — ver §2.4 e §6 da spec.
    var no = await _estruturas.ObterPorIdAsync(id, ct);
    if (no is null)
      return Result.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);

    // `no.AgrupamentoId` ja vem denormalizado em TODO EstruturaItem (Peca ou Item — ver o XML doc
    // da entidade), entao chegar no Agrupamento nao exige subir a arvore ate a raiz: uma consulta
    // direta basta.
    var agrupamento = await _agrupamentos.ObterPorIdAsync(no.AgrupamentoId, ct);
    var pedido = agrupamento is null ? null : await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct);
    if (pedido is null || pedido.Status != StatusAberto)
      return Result.Falha("PedidoNaoAberto", TipoDeErro.Conflito);

    await _estruturas.RemoverSubarvoreAsync(id, ct);
    return Result.Ok();
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

  /// <summary>Busca por Id em profundidade na arvore de DTOs ja montada — usado por `AcrescentarFilho`/`EditarNo` para projetar o no alterado, que nao e necessariamente uma raiz (diferente do `raizId` de `CriarPeca`).</summary>
  private static EstruturaItemDto? BuscarNo(IEnumerable<EstruturaItemDto> nos, int id)
  {
    foreach (var no in nos)
    {
      if (no.Id == id) return no;
      var achado = BuscarNo(no.Filhos, id);
      if (achado is not null) return achado;
    }
    return null;
  }
}
