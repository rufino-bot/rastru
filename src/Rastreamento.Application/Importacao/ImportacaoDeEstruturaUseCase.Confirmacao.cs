using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// A confirmacao do rascunho (secao 7 da spec do import): numa transacao so, grava o catalogo (os
/// Componentes novos, as reativacoes, os solidos e as receitas lidas) e cria a Peca, e apaga o rascunho.
/// </summary>
public sealed partial class ImportacaoDeEstruturaUseCase
{
  public const string ErroDeImportacaoComBloqueios = "ImportacaoComBloqueios";
  public const string ErroDeReceitaDoCatalogoMudou = "ReceitaDoCatalogoMudou";

  /// <summary>
  /// Decisao P5 do plano do import: o catalogo e lido e a Peca e planejada FORA da transacao, pela mesma
  /// avaliacao do <c>GET</c>. Ler o catalogo inteiro (<c>LerReceitaCompletaAsync</c>) sob SERIALIZABLE
  /// travaria as tres tabelas da receita por faixa ate o commit e abriria um ciclo de deadlock com
  /// <c>ReceitaPadraoRepository</c> (secao 3.2 da spec do conserto do deadlock na suite de Api), o mesmo
  /// motivo de <c>MontagemDeEstruturaUseCase.CriarPeca</c>. Dentro da transacao so se rele o que e
  /// estreito: a versao do rascunho e a receita de cada pai com escolha (a faixa de
  /// <c>UQ_ComponenteFilhoPadrao</c>, que comeca por <c>ComponentePaiId</c>); os Componentes casados sao
  /// relidos e os pendentes ligados sao desligados por chave. A excecao e a exclusao do rascunho
  /// (<c>ExcluirAsync</c>): ela filtra os registros por <c>ImportacaoId</c>, que nao tem indice proprio, e
  /// sob SERIALIZABLE varre e trava as tabelas do RASCUNHO (nao as do catalogo) ate o commit.
  ///
  /// Residual, o mesmo de <c>CriarPeca</c>: uma mudanca concorrente no catalogo dos Ids alcancados so
  /// pela receita de catalogo, entre a leitura e o commit, nao e vista. Outro: um codigo novo criado no
  /// catalogo por outra requisicao nesse intervalo esbarra em <c>UQ_Componente_Codigo</c> no INSERT, que
  /// nao e 1205/1222 e sobe cru; o <c>GET</c> seguinte mostra o bloqueio <c>CodigoJaExiste</c>.
  /// </summary>
  public async Task<Result<EstruturaItemDto>> Confirmar(int id, string? versao, CancellationToken ct)
  {
    var r = await _importacoes.ObterAsync(id, ct);
    if (r is null)
      return Result<EstruturaItemDto>.Falha(ErroDeImportacaoNaoEncontrada, TipoDeErro.NaoEncontrado);
    if (!TentarLerVersao(versao, out var esperada))
      return Result<EstruturaItemDto>.Falha(ErroDeVersaoInvalida, TipoDeErro.Validacao);
    if (!r.Versao.AsSpan().SequenceEqual(esperada))
      return Result<EstruturaItemDto>.Falha(ErroDeImportacaoDesatualizada, TipoDeErro.Conflito);

    var catalogo = await CarregarCatalogoAsync(r, ct);
    var avaliacao = AvaliadorDeImportacao.Avaliar(r, catalogo);
    // Decisao P15 do plano do import: sem a lista, que a tela rele pelo GET.
    if (avaliacao.Bloqueios.Count > 0 || avaliacao.Plano is null)
      return Result<EstruturaItemDto>.Falha(ErroDeImportacaoComBloqueios, TipoDeErro.Validacao);

    var gravacao = PlanoDeGravacao.De(r, catalogo, avaliacao.Plano);

    // Os registros cuja impressao nao bateu, preenchidos pela tentativa que devolveu a falha.
    var mudaram = new HashSet<int>();
    var resultado = await _execucao.ExecutarAsync(() => GravarAsync(r, esperada, gravacao, mudaram, ct), ct);

    if (resultado.Erro == ErroDeReceitaDoCatalogoMudou)
      await ZerarEscolhasAsync(id, esperada, mudaram, ct);
    return resultado;
  }

  /// <summary>
  /// O corpo da transacao, na ordem da secao 7 da spec do import. O retry de deadlock reexecuta tudo do
  /// zero com o <c>ChangeTracker</c> limpo (<c>IExecucaoRepository.EmTransacaoAsync</c>), entao nada
  /// rastreado de uma tentativa e reaproveitado na seguinte: os Componentes sao relidos e criados aqui
  /// dentro, e o rascunho nao e escrito (so apagado, por comando).
  /// </summary>
  private async Task<Result<EstruturaItemDto>> GravarAsync(
      ImportacaoDeEstrutura r, byte[] esperada, PlanoDeGravacao gravacao, ISet<int> mudaram, CancellationToken ct)
  {
    mudaram.Clear();

    // 1-2. A versao do banco, e nao a da instancia: reler pelo `ObterAsync` no mesmo contexto devolveria a
    // instancia rastreada, com a versao de quando foi lida. Sob SERIALIZABLE, a leitura segura a linha do
    // cabecalho ate o commit, e toda escrita no rascunho passa por ela (decisao P7 do plano do import):
    // com a versao igual, o rascunho e o que foi avaliado fora.
    var noBanco = await _importacoes.ObterVersaoAsync(r.Id, ct);
    if (noBanco is null)
      return Result<EstruturaItemDto>.Falha(ErroDeImportacaoNaoEncontrada, TipoDeErro.NaoEncontrado);
    if (!noBanco.AsSpan().SequenceEqual(esperada))
      return Result<EstruturaItemDto>.Falha(ErroDeImportacaoDesatualizada, TipoDeErro.Conflito);

    // 3. A receita de catalogo de cada pai com escolha, contra a impressao vista na escolha. Diferente,
    // a falha desfaz a transacao; a escolha e zerada depois, numa escrita propria (`Confirmar`).
    var codigos = new List<string>();
    foreach (var escolha in gravacao.EscolhasAConferir)
    {
      var atual = await _receitaPadrao.ListarFilhosAsync(escolha.ComponenteId, ct);
      var impressao = AvaliadorDeImportacao.Impressao(atual.Select(f => (f.ComponenteFilhoId, f.QuantidadePadrao)));
      if (escolha.Impressao is null || !impressao.AsSpan().SequenceEqual(escolha.Impressao))
      {
        mudaram.Add(escolha.RegistroId);
        codigos.Add(escolha.Codigo);
      }
    }
    if (codigos.Count > 0)
      return Result<EstruturaItemDto>.Falha(
          ErroDeReceitaDoCatalogoMudou, TipoDeErro.Conflito,
          $"A receita de catalogo de {string.Join(", ", codigos)} mudou depois da escolha: confira de novo.");

    // 4. Os Componentes novos da arvore final, ja com o solido pendente; o mapa Id provisorio -> Id real.
    var reais = new Dictionary<int, Componente>();
    foreach (var novo in gravacao.Novos)
    {
      var componente = new Componente
      {
        Codigo = novo.Codigo, Descricao = novo.Descricao, Tipo = novo.Tipo, Ativo = true,
        ArquivoSolidoId = novo.ArquivoSolidoId,
      };
      await _componentes.AdicionarAsync(componente, ct);
      reais[novo.IdProvisorio] = componente;
    }

    // 5. Os casados: reativa o inativo e liga o solido pendente. O arquivo antigo de quem ja tinha solido
    // fica sem ninguem apontar, como numa troca de solido pelo cadastro.
    foreach (var casado in gravacao.Casados)
    {
      var componente = await _componentes.ObterPorIdAsync(casado.ComponenteId, ct)
          ?? throw new InvalidOperationException($"O Componente {casado.ComponenteId} sumiu do catalogo durante a confirmacao.");
      if (casado.Reativar)
        componente.Ativo = true;
      if (casado.ArquivoSolidoId is int arquivo)
        componente.ArquivoSolidoId = arquivo;
    }
    await _componentes.SalvarAlteracoesAsync(ct);

    int Real(int id) => id < 0 ? reais[id].Id : id;

    // 6. A receita lida, nos Ids reais, onde ela muda o catalogo. NAO passa pelas validacoes de
    // `ReceitaPadraoUseCase`: o ciclo ja foi barrado pelo avaliador sobre a sobreposicao (decisao P4 do
    // plano do import: um ciclo novo passa por uma aresta sobreposta), e os filhos inativos da arvore
    // final foram reativados no passo anterior.
    foreach (var receita in gravacao.Receitas)
    {
      var linhas = receita.Filhos
          .Select(f => new ComponenteFilhoPadrao
          {
            ComponentePaiId = Real(receita.PaiId), ComponenteFilhoId = Real(f.FilhoId), QuantidadePadrao = f.Quantidade,
          })
          .ToList();
      await _receitaPadrao.SubstituirFilhosAsync(Real(receita.PaiId), linhas, ct);
    }

    // 7. A Peca, pelo plano da avaliacao com os Ids traduzidos: a mesma copia que a "Nova Peca" faria.
    var paraGravar = ParaGravar(gravacao.Plano, Real, ehRaiz: true, r.RequerRelatorioDimensional);
    var raizId = await _estruturas.GravarArvoreAsync(r.AgrupamentoId, null, paraGravar, ct);

    // 8. O rascunho. Antes, os pendentes que viraram solido de um Componente deixam de ser pendentes, por
    // chave do registro: o `ExcluirAsync` apaga todo arquivo que o rascunho ainda tem como pendente, e
    // `FK_Componente_ArquivoSolido` derrubaria a transacao. Por chave, e nao por uma consulta a
    // `dbo.Componente` (cuja coluna `ArquivoSolidoId` nao tem indice): sob SERIALIZABLE, essa consulta
    // varreria e travaria o catalogo inteiro ate o commit.
    await _importacoes.DesligarSolidosPendentesAsync(gravacao.RegistrosComSolidoLigado, ct);
    await _importacoes.ExcluirAsync(r.Id, ct);

    // 9. A Peca lida de volta, como em `MontagemDeEstruturaUseCase.CriarPeca`.
    var arvore = await _montador.MontarAsync(r.AgrupamentoId, ct);
    return Result<EstruturaItemDto>.Ok(arvore.Single(i => i.Id == raizId));
  }

  /// <summary>
  /// <c>NoPlanejado</c> -&gt; <c>NoParaGravar</c>, com o Id provisorio (negativo, decisao P1 do plano do
  /// import) trocado pelo real. O relatorio dimensional e so da raiz (regra 10), como em
  /// <c>MontagemDeEstruturaUseCase</c>.
  /// </summary>
  private static NoParaGravar ParaGravar(NoPlanejado no, Func<int, int> real, bool ehRaiz, bool requerRelatorio) =>
      new(
          ComponenteId: no.ComponenteId is int id ? real(id) : null,
          Descricao: no.Descricao,
          Quantidade: no.Quantidade,
          RequerRelatorioDimensional: ehRaiz && requerRelatorio,
          Materiais: no.Materiais,
          Roteiro: no.Roteiro,
          Filhos: no.Filhos.Select(f => ParaGravar(f, real, ehRaiz: false, requerRelatorio)).ToList(),
          QuantidadePorPai: no.QuantidadePorPai);

  /// <summary>
  /// A impressao nao bateu: a escolha e a impressao desses registros voltam a nulo, pela escrita
  /// versionada de sempre, fora da transacao que foi desfeita. Rascunho que mudou ou sumiu nesse meio-tempo
  /// fica como esta: a resposta continua sendo o 409, e a tela rele o rascunho.
  ///
  /// Limpeza de melhor esforco, fora do retry de deadlock: QUALQUER falha aqui e engolida, porque a
  /// resposta certa ja esta decidida (o 409 <c>ReceitaDoCatalogoMudou</c>), e uma excecao a trocaria por
  /// um 500. O pior desfecho e a escolha velha continuar no rascunho, e a proxima confirmacao confere a
  /// impressao de novo e devolve o mesmo 409. O caso de uso nao tem <c>ILogger</c>, entao nao ha onde
  /// registrar a falha.
  /// </summary>
  private async Task ZerarEscolhasAsync(int id, byte[] esperada, ISet<int> registros, CancellationToken ct)
  {
    try
    {
      var r = await _importacoes.ObterAsync(id, ct);
      if (r is null || !r.Versao.AsSpan().SequenceEqual(esperada))
        return;
      foreach (var registro in r.Componentes.Where(c => registros.Contains(c.Id)))
      {
        registro.EscolhaDeReceita = null;
        registro.ImpressaoDaReceitaDoCatalogo = null;
      }
      await _importacoes.SalvarAsync(r, esperada, ct);
    }
    catch (Exception)
    {
      // Ver o XML doc: o conflito de versao (outra escrita chegou antes, e o rascunho dela vale) e qualquer
      // outra falha tem o mesmo desfecho, o 409 que `Confirmar` devolve.
    }
  }

  /// <summary>
  /// O que a confirmacao grava, decidido fora da transacao e so com o que a avaliacao ja leu: so o que
  /// esta na arvore final (decisao P3 do plano do import), em pre-ordem.
  /// </summary>
  private sealed record PlanoDeGravacao(
      NoPlanejado Plano,
      IReadOnlyList<PlanoDeGravacao.Escolha> EscolhasAConferir,
      IReadOnlyList<PlanoDeGravacao.Novo> Novos,
      IReadOnlyList<PlanoDeGravacao.Casado> Casados,
      IReadOnlyList<PlanoDeGravacao.Receita> Receitas)
  {
    /// <summary>Os registros cujo solido pendente vira o solido de um Componente (novo ou casado).</summary>
    public IReadOnlyList<int> RegistrosComSolidoLigado { get; } =
        [.. Novos.Where(n => n.ArquivoSolidoId is not null).Select(n => n.RegistroId),
         .. Casados.Where(c => c.ArquivoSolidoId is not null).Select(c => c.RegistroId)];

    public sealed record Escolha(int RegistroId, int ComponenteId, string Codigo, byte[]? Impressao);

    public sealed record Novo(int RegistroId, int IdProvisorio, string Codigo, string Descricao, string Tipo, int? ArquivoSolidoId);

    public sealed record Casado(int RegistroId, int ComponenteId, bool Reativar, int? ArquivoSolidoId);

    /// <summary>A receita lida de um Id da sobreposicao, uma linha por filho (os repetidos somados, como a divergencia os compara).</summary>
    public sealed record Receita(int PaiId, IReadOnlyList<(int FilhoId, decimal Quantidade)> Filhos);

    public static PlanoDeGravacao De(ImportacaoDeEstrutura r, CatalogoParaAvaliacao catalogo, NoPlanejado plano)
    {
      var s = new SobreposicaoDaImportacao(r, catalogo.Receita);
      var escolhas = new List<Escolha>();
      var novos = new List<Novo>();
      var casados = new List<Casado>();
      var receitas = new List<Receita>();

      foreach (var id in s.Alcancaveis)
      {
        // Id alcancado so pela receita de catalogo: nao e registro, nao se cria, nao se reativa (como na
        // "Nova Peca", que copia um filho inativo sem reativa-lo) e a receita dele fica a do catalogo.
        if (s.RegistroDoId(id) is not { } registro)
          continue;

        if (id < 0)
          novos.Add(new Novo(registro.Id, id, registro.CodigoNovo!.Trim(), DescricaoDoNovo(registro), TipoDoNovo(registro),
              registro.ArquivoSolidoPendenteId));
        else
        {
          var reativar = !catalogo.Componentes[id].Ativo;
          if (reativar || registro.ArquivoSolidoPendenteId is not null)
            casados.Add(new Casado(registro.Id, id, reativar, registro.ArquivoSolidoPendenteId));
        }

        // Escolha so conta onde ainda ha divergencia: num codigo que deixou de divergir ela e inerte, e a
        // receita dele e a lida (decisao P2 do plano do import).
        if (s.Diverge(registro) && registro.EscolhaDeReceita is not null)
          escolhas.Add(new Escolha(registro.Id, id, catalogo.Componentes[id].Codigo, registro.ImpressaoDaReceitaDoCatalogo));

        if (s.UsaALida(registro))
        {
          var lida = Somar(s.Lida(registro).Select(l => (l.FilhoId, l.Quantidade)));
          var doCatalogo = Somar(catalogo.Receita.Filhos[id].Select(f => (f.FilhoId, f.QuantidadePadrao)));
          // Igual a do catalogo (o casado que nao diverge, o novo sem filhos): substituir so reescreveria
          // as mesmas linhas, com travas a mais na transacao.
          if (!MesmaReceita(lida, doCatalogo))
            receitas.Add(new Receita(id, lida));
        }
      }
      return new PlanoDeGravacao(plano, escolhas, novos, casados, receitas);
    }

    private static string DescricaoDoNovo(ImportacaoDeEstruturaComponente registro) =>
        string.IsNullOrWhiteSpace(registro.DescricaoNova) ? registro.DescricaoLida : registro.DescricaoNova;

    /// <summary>O <c>TipoNovo</c>; sem ele (nao acontece pelo caso de uso), o mesmo preenchimento do <c>Criar</c>.</summary>
    private static string TipoDoNovo(ImportacaoDeEstruturaComponente registro) =>
        registro.TipoNovo ?? (registro.Filhos.Count > 0 ? TipoMontagem : TipoFabricado);

    private static List<(int FilhoId, decimal Quantidade)> Somar(IEnumerable<(int FilhoId, decimal Quantidade)> linhas) =>
        [.. linhas.GroupBy(l => l.FilhoId).Select(g => (g.Key, g.Sum(l => l.Quantidade)))];

    private static bool MesmaReceita(
        IReadOnlyList<(int FilhoId, decimal Quantidade)> a, IReadOnlyList<(int FilhoId, decimal Quantidade)> b) =>
        a.Count == b.Count && a.OrderBy(l => l.FilhoId).SequenceEqual(b.OrderBy(l => l.FilhoId));
  }
}
