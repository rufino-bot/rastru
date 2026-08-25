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
  private const string ErroDeComponenteNaoEncontrado = "Componente não encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  /// <summary>
  /// A coluna e DECIMAL(18,4) (`specs/02-modelo-de-dados.sql`), entao 4 casas decimais e 14
  /// digitos inteiros. Quantidade fora disso e recusada, nao arredondada — decisao do usuario.
  /// </summary>
  private const string ErroDeQuantidadeForaDaEscala =
      "Quantidade deve ter no máximo 4 casas decimais e no máximo 14 dígitos inteiros.";

  /// <summary>Mensagem do 409 — a gravacao nao aconteceu e refazer o POST e o caminho.</summary>
  private const string ErroDeConflitoDeGravacao =
      "A receita deste componente está sendo alterada por outra gravação. Tente de novo.";

  /// <summary>
  /// Mensagem PROPRIA para A -> A, em vez de deixar a travessia de ciclo responder. A travessia
  /// tambem pegaria, mas diria "criaria um ciclo" onde "nao pode ser filho de si mesmo" e mais
  /// util. Pinado por <c>Auto_referencia_e_recusada_com_mensagem_propria</c>.
  /// </summary>
  private const string ErroDeAutoReferencia = "Um componente não pode ser filho de si mesmo.";

  /// <summary>
  /// As DUAS mensagens de ciclo, e nao uma: a regra e ESTRITA (recusa qualquer ciclo alcancavel a
  /// partir do componente editado), entao existe um caso em que o ciclo NAO passa pelo proprio
  /// componente — ele so se liga a sujeira que ja estava la. Dizer "o componente apareceria dentro
  /// da propria estrutura" nesse caso seria mentira, e mentira que atrapalha: o usuario procuraria
  /// o erro na receita errada.
  ///
  /// As duas NOMEIAM o caminho do ciclo pelo <c>Codigo</c> dos componentes — que e o que a tela
  /// mostra e o que o `SeletorComBusca` procura —, e nao pelo id cru. Foi isso que a decisao pela
  /// regra estrita comprou: sem saber ONDE esta o ciclo, o usuario fica bloqueado por sujeira que
  /// nao criou e nao tem como achar. Mesma forma das mensagens vizinhas de setor e material, que
  /// nomeiam o que esta errado em vez de dizer so "invalido".
  /// </summary>
  private const string ErroDeCicloProprio = "Esta receita criaria um ciclo: {0}.";

  private const string ErroDeCicloAlcancado =
      "Esta receita ligaria este componente a um ciclo que já existe: {0}. "
      + "Corrija a receita desses componentes antes.";

  /// <summary>
  /// Componente da ROTA inativo recusa a ESCRITA nos tres sub-recursos (decisao do usuario,
  /// 2026-08-20). O argumento que pesou foi a assimetria: o sistema ja recusa por em uma receita um
  /// material, setor ou filho INATIVO, e mesmo assim deixava editar a receita inteira de um pai
  /// inativo. E a mesma regra vista do outro lado.
  ///
  /// A LEITURA continua permitida — ver a receita de um componente inativo tem valor historico, e
  /// leitura neste projeto e de qualquer perfil autenticado. Pinado por
  /// <c>Receita_de_componente_inativo_continua_visivel_na_leitura</c>, que morre se alguem
  /// "uniformizar" a guarda para os `Listar*`.
  ///
  /// <c>TipoDeErro.Validacao</c> (400), e nao 409: e a irma da guarda de item inativo, que ja e
  /// validacao, e o 409 destes endpoints ja significa "outra gravacao derrubou a sua, tente de
  /// novo" — dois 409 com semanticas opostas no mesmo POST so se distinguiriam pela string. O
  /// precedente contrario existe e esta relatado (<c>Excluir</c> de Agrupamento usa Conflito para
  /// "Pedido nao Aberto", que tambem e estado do recurso barrando a operacao); se o usuario
  /// preferir aquele, e trocar o tipo e a mensagem, com os testes ja no lugar.
  /// </summary>
  private const string ErroDeComponenteInativo =
      "O componente {0} está inativo: reative-o para alterar a receita.";

  /// <summary>Maior valor que cabe em DECIMAL(18,4): 14 digitos inteiros + 4 decimais.</summary>
  private const decimal MaiorQuantidade = 99_999_999_999_999.9999m;

  /// <summary>
  /// Quantos componentes do ciclo a mensagem nomeia antes de resumir o resto (decisao do usuario,
  /// 2026-08-20: truncar com resumo, em vez de despejar o caminho inteiro).
  ///
  /// SEIS porque o valor de diagnostico esta no comeco do caminho: e por ali que o usuario corta.
  /// Ciclo de receita real tem 2 ou 3 saltos, entao 6 mostra o ciclo INTEIRO em qualquer caso
  /// plausivel e ainda sobra margem; acima disso o que importa e saber que ha mais e para onde o
  /// caminho volta. O no de retorno e SEMPRE nomeado, truncado ou nao — e ele que identifica onde o
  /// ciclo fecha.
  ///
  /// O truncamento so acontece quando ENCURTA (`> Nomeados + 1`): com 7 componentes, a forma
  /// truncada teria o mesmo tamanho da completa, e o resumo diria "+1", que alem de inutil sairia
  /// no plural errado. Assim o "+N" e sempre >= 2.
  /// </summary>
  private const int ComponentesNomeadosNoCiclo = 6;

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
    // campo), e dentro do corpo vem quantidade, escala, duplicata e existencia/atividade — cinco
    // niveis, QUATRO fronteiras adjacentes, todas em `Precedencia_das_validacoes_e_fixa`.
    //
    // Duas delas so entraram no fix pass da review da Task 5: a guarda de escala nao aparecia em
    // caso nenhum do `[Theory]`, entao troca-la com a de `<= 0` ou com a de duplicata deixava a
    // suite INTEIRA verde (medido nas duas direcoes). O molde dos filhos tinha a mesma lacuna.
    //
    // O componente da rota responde por DOIS niveis, nesta ordem: existe (404) e esta ativo (400).
    // Os dois vem antes de qualquer validacao de LINHA — nao adianta apontar erro no corpo de uma
    // gravacao que nao vai acontecer de jeito nenhum.
    var problemaDoPai = await ProblemaComOComponentePai(componenteId, ct);
    if (problemaDoPai is not null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          problemaDoPai.Value.Erro, problemaDoPai.Value.Tipo);

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
    // Mesma precedencia dos materiais: o recurso da ROTA primeiro (404 de inexistente, depois 400
    // de inativo), e toda validacao antes da unica chamada de escrita.
    var problemaDoPai = await ProblemaComOComponentePai(componenteId, ct);
    if (problemaDoPai is not null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          problemaDoPai.Value.Erro, problemaDoPai.Value.Tipo);

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
    // existencia/atividade -> ciclo. Sete niveis, SEIS fronteiras adjacentes: cinco delas estao em
    // `Precedencia_das_validacoes_de_filhos_e_fixa` e a sexta (atividade vs. ciclo) em
    // `Componente_filho_inativo_nao_pode_entrar_na_receita`.
    //
    // A frase que estava aqui — "cruza cinco desses pares e morre a cada reordenacao" — era falsa
    // e a review da Task 5 mediu: trocar esta guarda de `<= 0` com a de escala logo abaixo deixava
    // a suite INTEIRA verde. Faltava o par quantidade x escala, que so uma entrada invalida pelos
    // DOIS motivos distingue (`-0,00001`: negativa E fora da escala; a ordem decide qual mensagem
    // sai). Esse caso entrou no `[Theory]` no fix pass, e agora a reordenacao morre de verdade.
    //
    // O componente da ROTA responde por dois niveis ANTES de todos esses — existe (404) e esta
    // ativo (400) —, presos por `Componente_pai_inativo_recusa_a_gravacao_nos_tres_sub_recursos`.
    var problemaDoPai = await ProblemaComOComponentePai(componenteId, ct);
    if (problemaDoPai is not null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          problemaDoPai.Value.Erro, problemaDoPai.Value.Tipo);

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

    var ciclo = await ProblemaDeCiclo(componenteId, ids, ct);
    if (ciclo is not null) return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ciclo);

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
  /// A mensagem de ciclo, ou <c>null</c> se o grafo resultante estiver limpo.
  ///
  /// A pergunta e sobre o grafo COMO ELE FICARA depois da substituicao, nao sobre o atual: como o
  /// POST substitui a receita inteira, ele pode REMOVER uma aresta, e uma substituicao que desfaz
  /// um ciclo preexistente tem de ser ACEITA. Validar contra o grafo atual deixaria o usuario
  /// preso — a unica saida para consertar um ciclo seria SQL na mao. (§1.3 da spec.) Se o filtro
  /// abaixo virar <c>true</c>, morrem DOIS testes — medido no fix pass:
  /// <c>Substituicao_que_desfaz_um_ciclo_preexistente_e_aceita</c> e
  /// <c>Consertar_o_ciclo_por_dentro_libera_a_gravacao_recusada</c>. Era UM so enquanto a regra era
  /// leniente; foi a regra estrita que tornou o filtro load-bearing num segundo lugar, e o segundo
  /// teste e justamente o que garante que existe caminho de conserto pela API.
  ///
  /// A regra e ESTRITA, por decisao do usuario na review da Task 5: recusa QUALQUER ciclo
  /// alcancavel a partir de <paramref name="componenteId"/>, e nao so o ciclo que passa por ele.
  /// A versao anterior perguntava "ele volta a si mesmo?", e as duas perguntas so coincidem quando
  /// o grafo de partida e aciclico — o que a API preserva por inducao, mas que sujeira vinda de
  /// fora (SQL na mao) ou da corrida descrita abaixo quebra. Sob a regra leniente, ligar um
  /// componente limpo a um ramo ja sujo era ACEITO, e a copia recursiva da Fase 2 partindo dele
  /// giraria para sempre. O preco da regra estrita e o usuario poder ser bloqueado por um ciclo
  /// que nao criou — e por isso a mensagem NOMEIA o caminho: sem saber onde ele esta, nao ha como
  /// consertar. Editar a receita de qualquer componente DE DENTRO do ciclo continua sendo aceito
  /// (a travessia parte do componente editado, cujas arestas antigas ja sairam), entao o caminho
  /// de conserto existe sempre — <c>Consertar_o_ciclo_por_dentro_libera_a_gravacao_recusada</c>.
  /// Custo medido de apertar a regra: DOIS testes (o de ligar-se ao ciclo e o de conserto) morrem
  /// se alguem voltar a leniente, e nenhum outro se mexe.
  ///
  /// TOCTOU, nomeado e NAO fechado aqui: esta leitura do grafo e solta
  /// (<c>ListarTodasAsArestasAsync</c> e <c>AsNoTracking</c>, sem transacao) e acontece FORA da
  /// transacao SERIALIZABLE que o <c>Substituir</c> do repositorio abre so em volta do
  /// apaga-e-grava. Dois POSTs simultaneos em componentes DIFERENTES (um gravando 1 -> 2, outro
  /// 2 -> 1) leem o grafo antes de qualquer escrita, passam os dois na validacao e escrevem em
  /// faixas de chave diferentes — nenhum lock os coloca em conflito, e o ciclo fica gravado. O
  /// <c>CK</c> do banco so pega A -> A. Fechar isso exigiria ler o grafo dentro da mesma transacao
  /// da escrita, e a validacao vive na Application. Consequencia que vale para a Fase 2: esta
  /// barreira e defesa em profundidade, nao garantia — a copia recursiva PRECISA de guarda de
  /// profundidade propria de qualquer jeito.
  /// </summary>
  private async Task<string?> ProblemaDeCiclo(
      int componenteId, IReadOnlyList<int> filhosNovos, CancellationToken ct)
  {
    var resultante = (await _repositorio.ListarTodasAsArestasAsync(ct))
        .Where(a => a.ComponentePaiId != componenteId)          // as antigas deste pai SOMEM
        .Select(a => (Pai: a.ComponentePaiId, Filho: a.ComponenteFilhoId))
        .Concat(filhosNovos.Select(f => (Pai: componenteId, Filho: f)))  // as novas ENTRAM
        .ToLookup(a => a.Pai, a => a.Filho);

    var ciclo = ProcurarCiclo(resultante, componenteId);
    if (ciclo is null) return null;

    // A consulta so acontece no caminho de RECUSA, e e o preco de nomear os componentes: o grafo
    // e de ids, e id cru nao ajuda quem esta olhando codigos na tela.
    var codigos = (await _repositorio.ObterComponentesPorIdAsync(ciclo.Distinct().ToList(), ct))
        .ToDictionary(c => c.Id, c => c.Codigo);

    // O `#{id}` e inalcancavel hoje — a FK de `ComponenteFilhoPadrao` garante que todo no do grafo
    // tem linha em `Componente`, e o proprio pai foi conferido no inicio do metodo. Ele existe para
    // que um grafo sujo NAO troque um 400 legivel por um `KeyNotFoundException` (500), e por isso
    // nao tem teste: nao ha como chegar la pelo repositorio real.
    string Codigo(int id) => codigos.TryGetValue(id, out var codigo) ? codigo : $"#{id}";

    return string.Format(
        ciclo[0] == componenteId ? ErroDeCicloProprio : ErroDeCicloAlcancado,
        ResumirCaminho(ciclo, Codigo));
  }

  /// <summary>
  /// O caminho do ciclo em texto, TRUNCADO quando ele e fundo (decisao do usuario, 2026-08-20 — ver
  /// <see cref="ComponentesNomeadosNoCiclo"/> para o porque do numero).
  ///
  /// <paramref name="ciclo"/> chega FECHADO (o primeiro no aparece tambem no fim), entao o numero de
  /// componentes distintos e <c>Count - 1</c>. A forma truncada nomeia os primeiros N, resume
  /// quantos ficaram de fora e fecha no no de retorno — que e o mesmo primeiro no, e e o que diz ao
  /// usuario para onde a estrutura volta:
  ///
  /// <code>PAI -> C2 -> C3 -> C4 -> C5 -> C6 -> ... (+1995 componentes) -> PAI</code>
  ///
  /// Antes desta decisao a mensagem trazia o caminho inteiro: um ciclo de 2001 componentes gerava
  /// ~14 KB de corpo de 400. O valor de diagnostico estava nos primeiros saltos, e so.
  /// </summary>
  private static string ResumirCaminho(IReadOnlyList<int> ciclo, Func<int, string> codigo)
  {
    var distintos = ciclo.Count - 1;

    if (distintos <= ComponentesNomeadosNoCiclo + 1)
      return string.Join(" -> ", ciclo.Select(codigo));

    var nomeados = string.Join(" -> ", ciclo.Take(ComponentesNomeadosNoCiclo).Select(codigo));
    return $"{nomeados} -> ... (+{distintos - ComponentesNomeadosNoCiclo} componentes) "
           + $"-> {codigo(ciclo[0])}";
  }

  /// <summary>
  /// DFS ITERATIVA com cinza/preto, devolvendo o CAMINHO do ciclo (fechado: o primeiro no aparece
  /// tambem no fim) ou <c>null</c>.
  ///
  /// Iterativa, e nao recursiva, porque a profundidade e do DADO: todo o estado — caminho,
  /// enumeradores — mora no heap. <c>Cadeia_de_20000_niveis_termina_e_e_aceita</c> prende isso, e a
  /// profundidade dele foi medida: uma DFS recursiva equivalente ainda PASSA com 2000 niveis e so
  /// derruba o processo com 20000 (a iterativa leva 43 ms nos mesmos 20000).
  ///
  /// Os DOIS conjuntos carregam peso, e peso DIFERENTE — medido no fix pass, um de cada vez:
  /// - <c>cinza</c> (os nos do caminho atual) e quem DETECTA o ciclo, e com isso quem garante que a
  ///   travessia para. Sem ele a busca reentra no ciclo indefinidamente e
  ///   <c>Ligar_se_a_um_ciclo_preexistente_e_recusado_nomeando_o_ciclo</c> morre com
  ///   <c>OutOfMemoryException</c> em ~30 s: vermelho legivel, ainda que lento, porque cada volta
  ///   empilha mais um enumerador e mais um no no caminho — diferente da travessia anterior a este
  ///   fix pass, cuja pilha alternava com tamanho CONSTANTE e girava para sempre.
  /// - <c>preto</c> (os nos ja fechados) nao muda resposta nenhuma, muda o CUSTO: sem ele um grafo
  ///   aciclico com caminhos paralelos e re-explorado exponencialmente, e
  ///   <c>Grafo_com_caminhos_paralelos_nao_explode_exponencialmente</c> deixa de terminar (medido:
  ///   seguia rodando aos 180 s, morto de fora). ESSE e o que pendura, e para ele nao ha saida
  ///   barata: <c>[Fact(Timeout)]</c> do xUnit v2 nao interrompe laco sincrono dentro de metodo
  ///   async — a review da Task 5 aplicou o atributo e a execucao seguiu presa ate os 180 s.
  ///
  /// DECISAO REGISTRADA (usuario, 2026-08-20): NAO ha teto de iteracoes nesta travessia, e isso e
  /// escolha, nao esquecimento. Um teto transformaria a mutacao acima em vermelho legivel, mas
  /// seria guarda contra bug futuro NOSSO, com numero arbitrario, e um numero arbitrario recusa
  /// estrutura legitima e funda — o grafo real e pequeno e a travessia esta provada. Consequencia
  /// aceita: quem apagar a marcacao <c>preto</c> PENDURA a suite, e isso e sabido. Se voce chegou
  /// aqui depois de uma execucao que nao termina, a causa provavel e essa — nao e descoberta nova,
  /// nao gaste a sessao remedindo.
  /// </summary>
  private static IReadOnlyList<int>? ProcurarCiclo(ILookup<int, int> grafo, int raiz)
  {
    var caminho = new List<int> { raiz };
    var cinza = new HashSet<int> { raiz };
    var preto = new HashSet<int>();
    var pilha = new Stack<IEnumerator<int>>();
    pilha.Push(grafo[raiz].GetEnumerator());

    while (pilha.Count > 0)
    {
      var filhos = pilha.Peek();

      if (!filhos.MoveNext())
      {
        pilha.Pop().Dispose();
        var fechado = caminho[^1];
        caminho.RemoveAt(caminho.Count - 1);
        cinza.Remove(fechado);
        preto.Add(fechado);
        continue;
      }

      var filho = filhos.Current;

      if (cinza.Contains(filho))
      {
        // O ciclo e o trecho do caminho atual que comeca no no reencontrado, fechado nele mesmo:
        // e o que a mensagem precisa para dizer ONDE consertar, e o que descarta os ramos
        // inocentes que a travessia percorreu antes.
        var ciclo = caminho.Skip(caminho.IndexOf(filho)).Append(filho).ToList();
        foreach (var pendente in pilha) pendente.Dispose();
        return ciclo;
      }

      if (preto.Contains(filho)) continue;

      caminho.Add(filho);
      cinza.Add(filho);
      pilha.Push(grafo[filho].GetEnumerator());
    }

    return null;
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

  /// <summary>
  /// Os dois niveis de recusa do componente da ROTA, na ordem, ou <c>null</c> se ele estiver
  /// existente e ativo: 404 para inexistente, 400 para inativo (ver <see cref="ErroDeComponenteInativo"/>).
  ///
  /// Helper compartilhado pelos tres <c>Substituir*</c> de proposito: e a mesma regra nos tres, e
  /// tres copias sao tres chances de a guarda nascer faltando em um deles — foi o que aconteceu com
  /// o <c>catch</c> de conflito, que ficou dois sub-recursos sem cobertura ate a Task 5. Os
  /// <c>Listar*</c> NAO passam por aqui: leitura de receita de componente inativo continua
  /// permitida.
  /// </summary>
  private async Task<(string Erro, TipoDeErro Tipo)?> ProblemaComOComponentePai(
      int componenteId, CancellationToken ct)
  {
    var componente = await _repositorio.ObterComponenteAsync(componenteId, ct);

    if (componente is null) return (ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return componente.Ativo
        ? null
        : (string.Format(ErroDeComponenteInativo, componente.Codigo), TipoDeErro.Validacao);
  }

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
          ? $"O {singular} {ausentes[0]} não existe."
          : $"Os {plural} {string.Join(", ", ausentes)} não existem.";

    var inativos = idsPedidos.Where(id => !ativoPorId[id]).Distinct().ToList();
    if (inativos.Count > 0)
      return inativos.Count == 1
          ? $"O {singular} {inativos[0]} está inativo e não pode entrar na receita."
          : $"Os {plural} {string.Join(", ", inativos)} estão inativos e não podem entrar na receita.";

    return null;
  }
}
