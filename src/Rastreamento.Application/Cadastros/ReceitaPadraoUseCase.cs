using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Os tres sub-recursos vivem no MESMO caso de uso porque compartilham TRES validacoes
/// (componente pai existe, ids existem, ids estao ativos) e HERDAM do repositorio a mesma
/// propriedade de atomicidade, que nao e validacao — so o ciclo e exclusivo dos filhos. Tres
/// casos de uso duplicariam essas tres ou exigiriam um helper compartilhado que teria a mesma
/// forma deste arquivo, com um nivel a mais de indirecao.
///
/// Toda gravacao SUBSTITUI a receita inteira: "a receita deste componente passa a ser EXATAMENTE
/// estas N linhas". Lista vazia apaga — e o unico caminho de remocao que existe. Quem garante que
/// o apaga-e-grava e atomico e o repositorio (uma transacao explicita), nao este arquivo.
/// </summary>
public sealed class ReceitaPadraoUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  /// <summary>
  /// A coluna e DECIMAL(18,4) (`specs/02-modelo-de-dados.sql`), entao 4 casas decimais e 14
  /// digitos inteiros. Quantidade fora disso e recusada, nao arredondada — decisao do usuario.
  /// </summary>
  private const string ErroDeQuantidadeForaDaEscala =
      "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.";

  /// <summary>Mensagem do 409 — a gravacao nao aconteceu e refazer o POST e o caminho.</summary>
  private const string ErroDeConflitoDeGravacao =
      "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.";

  /// <summary>
  /// Mensagem PROPRIA para A -> A, em vez de deixar a travessia de ciclo responder. A travessia
  /// tambem pegaria, mas diria "criaria um ciclo" onde "nao pode ser filho de si mesmo" e mais
  /// util. Pinado por <c>Auto_referencia_e_recusada_com_mensagem_propria</c>.
  /// </summary>
  private const string ErroDeAutoReferencia = "Um componente nao pode ser filho de si mesmo.";

  private const string ErroDeCiclo =
      "Esta receita criaria um ciclo: o componente apareceria dentro da propria estrutura.";

  /// <summary>Maior valor que cabe em DECIMAL(18,4): 14 digitos inteiros + 4 decimais.</summary>
  private const decimal MaiorQuantidade = 99_999_999_999_999.9999m;

  private readonly IReceitaPadraoRepository _repositorio;

  public ReceitaPadraoUseCase(IReceitaPadraoRepository repositorio) => _repositorio = repositorio;

  // ------------------------------------------------------------------ materiais

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> ListarMateriais(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> SubstituirMateriais(
      int componenteId, IReadOnlyList<LinhaDeMaterialPadraoDto> linhas, CancellationToken ct)
  {
    // Toda validacao acontece ANTES da unica chamada de escrita: recusa que grava metade e pior
    // que recusa nenhuma. `Quantidade_invalida_e_recusada` e as duas de id invalido afirmam
    // `Substituicoes == 0` — mover a escrita para cima mata esses testes.
    //
    // A ORDEM entre as guardas tambem e contrato, e nao estetica: o recurso da ROTA vem primeiro
    // (404 ganha de 400, senao a tela nao sabe se redireciona por peca inexistente ou destaca um
    // campo), e dentro do corpo vem quantidade, depois duplicata, depois existencia/atividade.
    // `Precedencia_das_validacoes_e_fixa` cruza as tres e morre a cada reordenacao.
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    // `> 0` nao basta: 0,00001 e positivo, cabe no decimal do C# e vira 0,0000 em DECIMAL(18,4) —
    // ou seja, o POST responderia 200 com exatamente a linha de quantidade zero que a guarda
    // acima existe para impedir (medido contra o SQL Server na review da Task 3). E valor grande
    // demais estourava como DbUpdateException, virando 500 em vez de 400. Recusar, nao arredondar.
    if (linhas.Any(l => decimal.Round(l.QuantidadePadrao, 4) != l.QuantidadePadrao
                        || l.QuantidadePadrao > MaiorQuantidade))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeForaDaEscala);

    var ids = linhas.Select(l => l.MaterialId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          $"O material {repetido} aparece mais de uma vez na lista.");

    var materiais = await _repositorio.ObterMateriaisPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, materiais.ToDictionary(m => m.Id, m => m.Ativo), "material", "materiais");
    if (problema is not null) return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(problema);

    // O SERIALIZABLE do repositorio derruba o perdedor de duas gravacoes simultaneas do mesmo
    // componente, e isso e desfecho LEGITIMO do desenho — nao erro do servidor. O repositorio
    // traduz o deadlock/lock timeout do banco para `ConflitoDeConcorrenciaException` (mesmo padrao
    // de `RefreshTokenRepository.SalvarAlteracoesAsync`, para a Application nao referenciar o EF
    // Core), e aqui ele vira `TipoDeErro.Conflito` — 409, nao 500.
    try
    {
      await _repositorio.SubstituirMateriaisAsync(componenteId, linhas.Select(l =>
          new ComponenteMaterialPadrao
          {
            ComponenteId = componenteId,
            MaterialId = l.MaterialId,
            QuantidadePadrao = l.QuantidadePadrao,
          }).ToList(), ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeConflitoDeGravacao, TipoDeErro.Conflito);
    }

    // Re-le em vez de devolver o que entrou: e o unico jeito de a resposta trazer o `Id` da linha
    // (identity do banco) e os dados do Material, que o corpo do POST nao tem.
    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  private async Task<IReadOnlyList<MaterialPadraoDto>> ProjetarMateriais(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarMateriaisAsync(componenteId, ct);

    // Este `Distinct()` e defensivo, nao regra: remove-lo nao muda resultado nenhum e nenhum teste
    // morre — mutante EQUIVALENTE (R22 do relatorio de review da Task 3). Os DOIS de
    // `ConferirExistenciaEAtividade` estavam na mesma nota (R23 e R24) e DEIXARAM de ser
    // equivalentes na Task 4, quando o roteiro passou a chamar o helper com ids repetidos — ver o
    // XML doc do helper. A Task 5 (filhos) NAO reabriu a questao: ela mantem a guarda de duplicata,
    // entao entrega ids distintos ao helper, e a carga continua sendo do call site do roteiro
    // (medido: removidos um de cada vez, o de `ausentes` mata `Setor_inexistente_repetido...` e o
    // de `inativos` mata `Setor_inativo_nao_pode_entrar_no_roteiro` — os dois testes de ROTEIRO).
    //
    // Quem garante a ausencia de duplicata na receita GRAVADA e o UNIQUE do banco,
    // `UQ_ComponenteMaterialPadrao (ComponenteId, MaterialId)` — nao a validacao da aplicacao:
    // constraint nao se apaga num refactor, guarda de duplicata sim. E, mesmo com repeticao na
    // entrada, tanto o `WHERE Id IN (...)` real quanto o fake devolvem cada material UMA vez,
    // entao o `ToDictionary` abaixo nao estouraria nem sem o `Distinct()`.
    var materiais = (await _repositorio.ObterMateriaisPorIdAsync(
        linhas.Select(l => l.MaterialId).Distinct().ToList(), ct)).ToDictionary(m => m.Id);

    // Sem filtro por Ativo: linha ja gravada SOBREVIVE a inativacao do material. Inativar catalogo
    // nao pode corromper receita que ja existe.
    return linhas.Select(l =>
    {
      var m = materiais[l.MaterialId];
      return new MaterialPadraoDto(
          l.Id, l.MaterialId, m.Codigo, m.Descricao, m.UnidadeMedida, l.QuantidadePadrao);
    }).ToList();
  }

  // ------------------------------------------------------------------ roteiro

  public async Task<Result<IReadOnlyList<RoteiroPadraoDto>>> ListarRoteiro(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<RoteiroPadraoDto>>.Ok(await ProjetarRoteiro(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<RoteiroPadraoDto>>> SubstituirRoteiro(
      int componenteId, IReadOnlyList<LinhaDeRoteiroPadraoDto> linhas, CancellationToken ct)
  {
    // Mesma precedencia dos materiais: o recurso da ROTA primeiro (404 ganha de 400), e toda
    // validacao antes da unica chamada de escrita.
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    // SEM checagem de repetido, de proposito: o mesmo Setor pode aparecer varias vezes — e o
    // RETORNO AO SETOR, permitido pelo schema (UQ e (ComponenteId, Ordem)). Nao "conserte" isto
    // copiando o `PrimeiroRepetido` dos materiais: `Mesmo_setor_repetido_no_roteiro_e_aceito`
    // existe exatamente para matar essa "correcao".
    var ids = linhas.Select(l => l.SetorId).ToList();
    var setores = await _repositorio.ObterSetoresPorIdAsync(ids.Distinct().ToList(), ct);
    var problema = ConferirExistenciaEAtividade(
        ids, setores.ToDictionary(s => s.Id, s => s.Ativo), "setor", "setores");
    if (problema is not null) return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(problema);

    // Mesma traducao de conflito dos materiais, pelo mesmo motivo: a transacao SERIALIZABLE e do
    // repositorio, entao quem grava roteiro tambem pode ser o perdedor derrubado pelo banco.
    // 409, nao 500 — `Conflito_de_concorrencia_na_gravacao_do_roteiro_vira_erro_de_conflito`.
    //
    // O `try` e ESTREITO de proposito: envolve SO a gravacao, nao a releitura logo abaixo que
    // projeta a resposta. Um erro na releitura nao pode virar 409 "tente de novo" — a gravacao ja
    // aconteceu, e dizer que a operacao falhou seria mentira. Coberto, e nao so por leitura:
    // `Falha_na_releitura_apos_gravar_o_roteiro_nao_vira_conflito` faz a releitura estourar e
    // afirma que a excecao sobe crua.
    try
    {
      // A Ordem sai da POSICAO no array: 1-based, densa por construcao. Nao ha como o cliente
      // produzir buraco nem duplicata na sequencia, porque ele nao envia Ordem nenhuma.
      await _repositorio.SubstituirRoteiroAsync(componenteId, linhas.Select((l, i) =>
          new ComponenteRoteiroPadrao
          {
            ComponenteId = componenteId,
            SetorId = l.SetorId,
            Ordem = i + 1,
          }).ToList(), ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          ErroDeConflitoDeGravacao, TipoDeErro.Conflito);
    }

    return Result<IReadOnlyList<RoteiroPadraoDto>>.Ok(await ProjetarRoteiro(componenteId, ct));
  }

  /// <summary>
  /// A sequencia ja vem ordenada por `Ordem` do repositorio — este metodo nao reordena, so liga
  /// cada passo ao nome do Setor. Sem filtro por Ativo, pela mesma razao dos materiais: passo ja
  /// gravado sobrevive a inativacao do Setor.
  /// </summary>
  private async Task<IReadOnlyList<RoteiroPadraoDto>> ProjetarRoteiro(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarRoteiroAsync(componenteId, ct);

    // Este `Distinct()` e o de `SubstituirRoteiro` sao mutantes EQUIVALENTES, como os tres
    // declarados em `ProjetarMateriais`: com retorno ao setor a lista de ids REALMENTE chega com
    // duplicata aqui (diferente dos materiais, onde a guarda de repetido ja recusou antes), mas
    // tanto o `WHERE Id IN (...)` real quanto o fake devolvem cada Setor UMA vez — o
    // `ToDictionary` abaixo nao estoura nem sem ele. O que o `Distinct()` faz e encurtar a lista
    // do `IN`, e nao evitar erro.
    //
    // Declaracao RE-MEDIDA na Task 5, porque declaracao de equivalencia caduca quando entra
    // chamador novo (foi o que aconteceu com a da Task 3 quando o roteiro chegou): filhos usa
    // `ObterComponentesPorIdAsync`, outro call site, e mantem a guarda de duplicata — os dois
    // `Distinct()` do roteiro continuam sobrevivendo a suite inteira, um de cada vez.
    var setores = (await _repositorio.ObterSetoresPorIdAsync(
        linhas.Select(l => l.SetorId).Distinct().ToList(), ct)).ToDictionary(s => s.Id);

    return linhas
        .Select(l => new RoteiroPadraoDto(l.Id, l.SetorId, setores[l.SetorId].Nome, l.Ordem))
        .ToList();
  }

  // ------------------------------------------------------------------ filhos

  public async Task<Result<IReadOnlyList<FilhoPadraoDto>>> ListarFilhos(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<FilhoPadraoDto>>.Ok(await ProjetarFilhos(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<FilhoPadraoDto>>> SubstituirFilhos(
      int componenteId, IReadOnlyList<LinhaDeFilhoPadraoDto> linhas, CancellationToken ct)
  {
    // A ORDEM das guardas e contrato, nao estetica, e aqui ela tem mais niveis que nos outros dois
    // sub-recursos: 404 do pai -> quantidade -> escala -> auto-referencia -> duplicata ->
    // existencia/atividade -> ciclo. `Precedencia_das_validacoes_de_filhos_e_fixa` cruza cinco
    // desses pares e morre a cada reordenacao; o sexto (atividade vs. ciclo) esta em
    // `Componente_filho_inativo_nao_pode_entrar_na_receita`.
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    // `dbo.ComponenteFilhoPadrao.QuantidadePadrao` e o MESMO DECIMAL(18,4) dos materiais, entao
    // herda as duas armadilhas da coluna: 0,00001 e positivo, passa em `> 0` e chega ao banco como
    // 0,0000 (o POST responderia 200 com a linha de quantidade zero que a guarda acima existe para
    // impedir), e valor grande demais estouraria como erro de banco, virando 500 em vez de 400. O
    // plano desta task nao previa esta guarda; `Quantidade_de_filho_invalida_e_recusada` a cobra.
    if (linhas.Any(l => decimal.Round(l.QuantidadePadrao, 4) != l.QuantidadePadrao
                        || l.QuantidadePadrao > MaiorQuantidade))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeQuantidadeForaDaEscala);

    // Auto-referencia ANTES do ciclo, para a mensagem ser especifica — ver `ErroDeAutoReferencia`.
    if (linhas.Any(l => l.ComponenteFilhoId == componenteId))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeAutoReferencia);

    // Filho repetido e PROIBIDO, ao contrario do setor repetido no roteiro:
    // `UQ_ComponenteFilhoPadrao (ComponentePaiId, ComponenteFilhoId)` recusaria a segunda linha e a
    // violacao sairia como 500. "Duas unidades do mesmo filho" se escreve na quantidade.
    var ids = linhas.Select(l => l.ComponenteFilhoId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          $"O componente {repetido} aparece mais de uma vez na lista.");

    // Sem `Distinct()` na lista do `IN` — e a guarda acima que garante que nao ha repetido aqui,
    // exatamente como do lado dos materiais. Por isso este call site NAO torna load-bearing os
    // `Distinct()` de `ConferirExistenciaEAtividade`: quem os sustenta continua sendo o roteiro,
    // que entrega ids repetidos porque setor repetido e valido.
    var filhos = await _repositorio.ObterComponentesPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, filhos.ToDictionary(c => c.Id, c => c.Ativo), "componente", "componentes");
    if (problema is not null) return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(problema);

    if (await FechariaCiclo(componenteId, ids, ct))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeCiclo);

    // Mesma traducao de conflito dos materiais e do roteiro: a transacao SERIALIZABLE e do
    // repositorio, entao quem grava filhos tambem pode ser o perdedor derrubado pelo banco.
    // 409, nao 500 — `Conflito_de_concorrencia_na_gravacao_dos_filhos_vira_erro_de_conflito`.
    //
    // O `try` e ESTREITO de proposito, e aqui isso pesa mais que no roteiro: acima dele ficam a
    // leitura do grafo INTEIRO e a travessia de ciclo, e abaixo a releitura que monta a resposta.
    // Um `catch` largo transformaria bug real (`KeyNotFoundException`, `NullReference`, erro na
    // montagem do grafo) em 409 "tente de novo" — o usuario reenviaria e falharia de novo, sem
    // sintoma util. Coberto por `Falha_na_releitura_apos_gravar_os_filhos_nao_vira_conflito`.
    try
    {
      await _repositorio.SubstituirFilhosAsync(componenteId, linhas.Select(l =>
          new ComponenteFilhoPadrao
          {
            ComponentePaiId = componenteId,
            ComponenteFilhoId = l.ComponenteFilhoId,
            QuantidadePadrao = l.QuantidadePadrao,
          }).ToList(), ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeConflitoDeGravacao, TipoDeErro.Conflito);
    }

    return Result<IReadOnlyList<FilhoPadraoDto>>.Ok(await ProjetarFilhos(componenteId, ct));
  }

  /// <summary>
  /// A pergunta e sobre o grafo COMO ELE FICARA depois da substituicao, nao sobre o atual: como o
  /// POST substitui a receita inteira, ele pode REMOVER uma aresta, e uma substituicao que desfaz
  /// um ciclo preexistente tem de ser ACEITA. Validar contra o grafo atual deixaria o usuario
  /// preso — a unica saida para consertar um ciclo seria SQL na mao. (§1.3 da spec, e
  /// <c>Substituicao_que_desfaz_um_ciclo_preexistente_e_aceita</c>, que e o UNICO teste que morre
  /// se o filtro abaixo virar <c>true</c>.)
  ///
  /// So arestas SAINDO de <paramref name="componenteId"/> mudam, entao basta perguntar se ele volta
  /// a si mesmo no grafo resultante.
  /// </summary>
  private async Task<bool> FechariaCiclo(
      int componenteId, IReadOnlyList<int> filhosNovos, CancellationToken ct)
  {
    var resultante = (await _repositorio.ListarTodasAsArestasAsync(ct))
        .Where(a => a.ComponentePaiId != componenteId)          // as antigas deste pai SOMEM
        .Select(a => (Pai: a.ComponentePaiId, Filho: a.ComponenteFilhoId))
        .Concat(filhosNovos.Select(f => (Pai: componenteId, Filho: f)))  // as novas ENTRAM
        .ToLookup(a => a.Pai, a => a.Filho);

    // `visitados` nao e otimizacao: sem ele, um ciclo PREEXISTENTE em outro ramo faria esta
    // travessia girar para sempre. Coberto por
    // `Ciclo_preexistente_em_outro_ramo_nao_impede_editar_este_componente`.
    var visitados = new HashSet<int>();
    var pilha = new Stack<int>(resultante[componenteId]);

    while (pilha.Count > 0)
    {
      var atual = pilha.Pop();
      if (atual == componenteId) return true;
      if (!visitados.Add(atual)) continue;
      foreach (var filho in resultante[atual]) pilha.Push(filho);
    }

    return false;
  }

  private async Task<IReadOnlyList<FilhoPadraoDto>> ProjetarFilhos(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarFilhosAsync(componenteId, ct);

    // O `Distinct()` aqui e mutante EQUIVALENTE, como os de `ProjetarMateriais`: o
    // `UQ_ComponenteFilhoPadrao (ComponentePaiId, ComponenteFilhoId)` impede filho repetido na
    // receita GRAVADA, entao a lista nao chega com duplicata; e mesmo se chegasse, tanto o
    // `WHERE Id IN (...)` real quanto o fake devolvem cada Componente uma vez.
    var componentes = (await _repositorio.ObterComponentesPorIdAsync(
        linhas.Select(l => l.ComponenteFilhoId).Distinct().ToList(), ct)).ToDictionary(c => c.Id);

    // Sem filtro por Ativo, pela mesma razao dos materiais e do roteiro: linha ja gravada SOBREVIVE
    // a inativacao do componente filho — filtrar aqui faria a leitura estourar
    // `KeyNotFoundException` (500) numa receita valida. Pinado por
    // `Filho_existente_sobrevive_a_inativacao_do_componente_filho`.
    return linhas.Select(l =>
    {
      var c = componentes[l.ComponenteFilhoId];
      return new FilhoPadraoDto(l.Id, l.ComponenteFilhoId, c.Codigo, c.Descricao, l.QuantidadePadrao);
    }).ToList();
  }

  // ------------------------------------------------------------------ comum

  /// <summary>O primeiro id que aparece duas vezes, ou null. Ordem estavel: a lista dita.</summary>
  private static int? PrimeiroRepetido(IReadOnlyList<int> ids)
  {
    var vistos = new HashSet<int>();
    foreach (var id in ids)
      if (!vistos.Add(id)) return id;
    return null;
  }

  /// <summary>
  /// Uma mensagem para "nao existe" e outra para "esta inativo", NOMEANDO os ids — sem o id, o
  /// usuario com 12 linhas na tela nao sabe qual corrigir. Os quatro ramos (singular e plural de
  /// cada) tem teste proprio em `ReceitaPadraoUseCaseTests`, porque este helper e compartilhado
  /// pelos tres sub-recursos: um ramo quebrado quebraria os tres de uma vez.
  ///
  /// Falha de VALIDACAO, nao 404: o recurso da rota (o Componente) existe — quem esta errado e uma
  /// linha do corpo.
  ///
  /// Os dois <c>Distinct()</c> abaixo NAO sao mais equivalentes, e a nota da Task 3 que os
  /// declarava assim ficou obsoleta quando o roteiro entrou: ela valia porque a guarda de
  /// duplicata dos materiais roda ANTES e nenhum id repetido chegava aqui. O roteiro nao tem
  /// guarda de duplicata — setor repetido e valido — entao um roteiro com o mesmo id invalido
  /// duas vezes chega com a lista repetida, e sem os <c>Distinct()</c> a mensagem sai no plural
  /// nomeando o mesmo id duas vezes ("Os setores 888, 888 nao existem."). Pinado por
  /// <c>Setor_inexistente_repetido_e_nomeado_uma_vez_so</c> (ramo de "nao existe") e por
  /// <c>Setor_inativo_nao_pode_entrar_no_roteiro</c> (ramo de "esta inativo").
  ///
  /// Essa carga (load-bearing-idade) e do CALL SITE, nao deste helper: e porque
  /// <c>SubstituirRoteiro</c> entrega os ids como vieram — sem <c>Distinct()</c> — que a duplicata
  /// chega ate aqui. Se o call site passasse <c>ids.Distinct()</c>, os dois <c>Distinct()</c>
  /// abaixo voltariam a ser equivalentes.
  /// </summary>
  private static string? ConferirExistenciaEAtividade(
      IReadOnlyList<int> idsPedidos,
      IReadOnlyDictionary<int, bool> ativoPorId,
      string singular,
      string plural)
  {
    var ausentes = idsPedidos.Where(id => !ativoPorId.ContainsKey(id)).Distinct().ToList();
    if (ausentes.Count > 0)
      return ausentes.Count == 1
          ? $"O {singular} {ausentes[0]} nao existe."
          : $"Os {plural} {string.Join(", ", ausentes)} nao existem.";

    var inativos = idsPedidos.Where(id => !ativoPorId[id]).Distinct().ToList();
    if (inativos.Count > 0)
      return inativos.Count == 1
          ? $"O {singular} {inativos[0]} esta inativo e nao pode entrar na receita."
          : $"Os {plural} {string.Join(", ", inativos)} estao inativos e nao podem entrar na receita.";

    return null;
  }
}
