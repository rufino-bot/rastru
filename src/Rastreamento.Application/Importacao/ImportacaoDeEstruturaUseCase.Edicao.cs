using System.Globalization;
using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// As escritas da conferencia: a Peca, o casamento e a escolha de cada registro, a quantidade de cada
/// linha, o solido pendente e o reimport. Toda escrita relê o rascunho, confere a versao, valida, aplica,
/// salva e devolve a projecao (<see cref="ProjetarAsync"/>), por <see cref="Escrever"/>.
/// </summary>
public sealed partial class ImportacaoDeEstruturaUseCase
{
  private const string ErroDeRegistroNaoEncontrado = "Registro nao encontrado no rascunho.";
  private const string ErroDeLinhaNaoEncontrada = "Linha da receita nao encontrada no rascunho.";
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
  private const string ErroDeVersaoInvalida = "Versao do rascunho invalida.";
  private const string ErroDeComponenteJaCasado = "Este componente ja esta casado com outro registro do rascunho.";
  private const string ErroDeSolidoPendenteNaoEncontrado = "Este registro nao tem solido pendente.";
  private const string ErroDeEscolhaComTrocaDeCasamento =
      "Escolha a receita depois de conferir o novo casamento.";
  private const string ErroDeEscolhaSemDivergencia =
      "So ha escolha de receita onde a receita do catalogo diverge da lida do BOM.";

  private static readonly string[] TiposDeComponente = ["Bruto", "Fabricado", "Montagem"];

  private const string ErroDeBrutoComFilhos =
      "So um Componente sem filhos no BOM pode passar a Bruto.";

  /// <summary>Uma recusa que ainda nao e <see cref="Result{T}"/>: as funcoes que aplicam a escrita devolvem uma ou nada.</summary>
  private sealed record Recusa(string Erro, TipoDeErro Tipo, string? Detalhe = null)
  {
    public Result<ImportacaoDto> Resultado() => Result<ImportacaoDto>.Falha(Erro, Tipo, Detalhe);
  }

  private static readonly Recusa Desatualizada = new(ErroDeImportacaoDesatualizada, TipoDeErro.Conflito);

  public Task<Result<ImportacaoDto>> AlterarPeca(int id, AlteracaoDaPecaDto dto, CancellationToken ct) =>
      Escrever(id, dto.Versao, ct, aplicar: r =>
      {
        if (dto.QuantidadeDaPeca is decimal q && ForaDaColuna(q, "A quantidade da Peca") is { } recusa)
          return Task.FromResult<Recusa?>(recusa);
        r.QuantidadeDaPeca = dto.QuantidadeDaPeca;
        r.RequerRelatorioDimensional = dto.RequerRelatorioDimensional;
        return Task.FromResult<Recusa?>(null);
      });

  public Task<Result<ImportacaoDto>> AlterarFilho(int id, int filhoId, AlteracaoDeFilhoDto dto, CancellationToken ct) =>
      Escrever(id, dto.Versao, ct, aplicar: r =>
      {
        var linha = r.Componentes.SelectMany(c => c.Filhos).FirstOrDefault(f => f.Id == filhoId);
        if (linha is null)
          return Task.FromResult<Recusa?>(new Recusa(ErroDeLinhaNaoEncontrada, TipoDeErro.NaoEncontrado));
        if (ForaDaColuna(dto.Quantidade, "A quantidade") is { } recusa)
          return Task.FromResult<Recusa?>(recusa);
        linha.Quantidade = dto.Quantidade;
        return Task.FromResult<Recusa?>(null);
      });

  /// <summary>
  /// Casa o registro com um Componente, ou o deixa "criar novo", e grava ou limpa a escolha de receita
  /// (ver <see cref="AlteracaoDeComponenteDto"/>). O banco recusa o estado misto (casado COM dados de
  /// novo), entao uma troca limpa um lado e preenche o outro: casado -> novo traz o que o arquivo leu,
  /// com o mesmo preenchimento do <see cref="Criar"/>. Trocar o casamento zera a escolha e a impressao,
  /// que eram de outra receita de catalogo. Recusa (e desfaz o que mexeu) quando a escolha nao cabe.
  /// </summary>
  public Task<Result<ImportacaoDto>> AlterarComponente(
      int id, int registroId, AlteracaoDeComponenteDto dto, CancellationToken ct) =>
      Escrever(id, dto.Versao, ct, aplicar: async r =>
      {
        var registro = r.Componentes.FirstOrDefault(c => c.Id == registroId);
        if (registro is null)
          return new Recusa(ErroDeRegistroNaoEncontrado, TipoDeErro.NaoEncontrado);
        if (ValidarCampos(dto) is { } invalido)
          return invalido;
        // So uma folha passa a Bruto (o Bruto nao exige solido). O Bruto que o reimport manteve num
        // registro que ganhou filhos continua aceito: a recusa e de quem PASSA a Bruto.
        if (dto.ComponenteId is null && dto.TipoNovo == "Bruto"
            && registro.TipoNovo != "Bruto" && registro.Filhos.Count > 0)
          return new Recusa(ErroDeBrutoComFilhos, TipoDeErro.Validacao);
        if (dto.ComponenteId is int alvo)
        {
          if (!(await _receitaPadrao.ObterComponentesPorIdAsync([alvo], ct)).Any())
            return new Recusa(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);
          // O catalogo tem UMA receita por Componente: dois registros casados com ele se pisariam.
          if (r.Componentes.Any(c => c.Id != registro.Id && c.ComponenteId == alvo))
            return new Recusa(ErroDeComponenteJaCasado, TipoDeErro.Validacao);
        }

        // Trocar o casamento (de Componente, ou entre casado e novo) e trocar a receita de catalogo em
        // comparacao: uma escolha enviada junto seria de uma receita que o usuario nao viu. Ela se faz numa
        // escrita seguinte, ja sobre o casamento novo.
        if (dto.ComponenteId != registro.ComponenteId && dto.EscolhaDeReceita is not null)
          return new Recusa(ErroDeEscolhaComTrocaDeCasamento, TipoDeErro.Validacao);

        var antes = Estado.De(registro);
        AplicarCasamento(registro, dto);
        // `escolhaDeReceita` e o estado inteiro da escolha: nulo a limpa, e a troca de casamento so chega
        // aqui com ela nula.
        registro.EscolhaDeReceita = dto.EscolhaDeReceita;
        registro.ImpressaoDaReceitaDoCatalogo = null;

        if (dto.EscolhaDeReceita is not null)
        {
          var impressao = await ImpressaoSeDiverge(r, registro, ct);
          if (impressao is null)
          {
            antes.Restaurar(registro);
            return new Recusa(ErroDeEscolhaSemDivergencia, TipoDeErro.Validacao);
          }
          registro.ImpressaoDaReceitaDoCatalogo = impressao;
        }
        return null;
      });

  public async Task<Result<ImportacaoDto>> EnviarSolidoPendente(
      int id, int registroId, string versao, string nomeOriginal, byte[] conteudo, int usuarioId, CancellationToken ct)
  {
    var nome = SoONome(nomeOriginal);
    if (ExcessoDeTexto(nome, NomeDoArquivoMaximo, "O nome do arquivo") is { } longo)
      return Result<ImportacaoDto>.Falha(longo, TipoDeErro.Validacao);
    // Mesma recusa e mesma mensagem do solido do Componente: o validador e puro, e o pendente vira solido dele.
    if (ValidadorDeArquivoStl.Validar(nome, conteudo) is { } invalido)
      return Result<ImportacaoDto>.Falha(invalido, TipoDeErro.Validacao);

    int? anterior = null;
    int? gravado = null;
    ImportacaoDeEstruturaComponente? alvo = null;
    return await Escrever(id, versao, ct, aplicar: async r =>
    {
      var registro = r.Componentes.FirstOrDefault(c => c.Id == registroId);
      if (registro is null)
        return new Recusa(ErroDeRegistroNaoEncontrado, TipoDeErro.NaoEncontrado);

      alvo = registro;
      anterior = registro.ArquivoSolidoPendenteId;
      gravado = await _importacoes.GravarArquivoPendenteAsync(
          new ArquivoDeComponente { NomeOriginal = nome, Conteudo = conteudo, CriadoPorUsuarioId = usuarioId }, ct);
      registro.ArquivoSolidoPendenteId = gravado;
      return null;
    },
    // O anterior so some depois de o novo estar ligado ao registro: se o salvamento falha, ele continua valendo.
    // As limpezas de depois do salvamento nao usam o token da requisicao: a escrita ja esta confirmada, e uma
    // desconexao agora deixaria arquivos orfaos ou transformaria a escrita feita numa excecao.
    aposSalvar: async () =>
    {
      if (anterior is int velho)
        await _importacoes.ExcluirArquivosAsync([velho], CancellationToken.None);
    },
    aoPerderACorrida: async () =>
    {
      alvo!.ArquivoSolidoPendenteId = anterior;
      if (gravado is int novo)
        await _importacoes.ExcluirArquivosAsync([novo], CancellationToken.None);
    });
  }

  /// <summary>
  /// O solido pendente do registro, com o blob. Registro de outro rascunho, registro sem pendente e
  /// arquivo que sumiu respondem o mesmo <c>NaoEncontrado</c>: nao muda o que o cliente faz.
  /// </summary>
  public async Task<Result<ArquivoDeSolidoDto>> ObterSolidoPendente(int id, int registroId, CancellationToken ct)
  {
    var r = await _importacoes.ObterAsync(id, ct);
    var arquivoId = r?.Componentes.FirstOrDefault(c => c.Id == registroId)?.ArquivoSolidoPendenteId;
    var arquivo = arquivoId is int a ? await _importacoes.ObterArquivoAsync(a, ct) : null;
    return arquivo is null
        ? Result<ArquivoDeSolidoDto>.Falha(ErroDeSolidoPendenteNaoEncontrado, TipoDeErro.NaoEncontrado)
        : Result<ArquivoDeSolidoDto>.Ok(new ArquivoDeSolidoDto(arquivo.NomeOriginal, arquivo.Conteudo));
  }

  /// <summary>
  /// Troca as receitas lidas pelas do arquivo novo (ver <see cref="TransferenciaDoReimport"/> para o que
  /// segue por codigo). Le, monta e confere o arquivo exatamente como o <see cref="Criar"/>, e um arquivo
  /// recusado deixa o rascunho como estava. O pendente de codigo que saiu do arquivo e apagado.
  /// </summary>
  public Task<Result<ImportacaoDto>> Reimportar(
      int id, string versao, string nomeDoArquivo, byte[] conteudo, CancellationToken ct)
  {
    var nome = SoONome(nomeDoArquivo);
    PlanoDoReimport? plano = null;
    return Escrever(id, versao, ct, aplicar: async r =>
    {
      var (bom, erros) = LerEMontar(nome, conteudo);
      if (bom is null)
        return new Recusa(ErroDeBomInvalido, TipoDeErro.Validacao, string.Join('\n', erros.Select(e => e.ToString())));

      var casados = await CasadosPorCodigoAsync(bom, ct);
      plano = TransferenciaDoReimport.Aplicar(r, bom, (c, temFilhos, emUso) => NovoRegistro(c, temFilhos, casados, emUso));
      r.NomeDoArquivo = nome;
      _importacoes.RemoverFilhos(plano.FilhosRemovidos);
      _importacoes.RemoverRegistros(plano.RegistrosRemovidos);
      return null;
    },
    aposSalvar: async () =>
    {
      var arquivos = plano!.RegistrosRemovidos
          .Where(c => c.ArquivoSolidoPendenteId is not null).Select(c => c.ArquivoSolidoPendenteId!.Value).ToList();
      if (arquivos.Count > 0)
        await _importacoes.ExcluirArquivosAsync(arquivos, CancellationToken.None);
    });
  }

  /// <summary>
  /// O corpo comum de toda escrita: relê, confere a versao (a do corpo tem de ser a do banco) e, so entao,
  /// valida e aplica. <paramref name="aplicar"/> valida e, se quiser, ja muta; devolve a recusa ou nada.
  /// Quem recusa depois de mutar desfaz o que mexeu (ver <see cref="Estado"/>): a recusa nao salva, mas a
  /// instancia rastreada nao deve ficar pela metade. O salvamento troca a versao e toca <c>AtualizadoEm</c> do cabecalho
  /// (decisao P7), mesmo quando so um registro ou uma linha mudou. Versao velha, na checagem ou na corrida
  /// que o banco acusa no <c>SalvarAsync</c>, e <c>ImportacaoDesatualizada</c>.
  /// </summary>
  private async Task<Result<ImportacaoDto>> Escrever(
      int id, string? versao, CancellationToken ct,
      Func<ImportacaoDeEstrutura, Task<Recusa?>> aplicar,
      Func<Task>? aposSalvar = null,
      Func<Task>? aoPerderACorrida = null)
  {
    var r = await _importacoes.ObterAsync(id, ct);
    if (r is null)
      return Result<ImportacaoDto>.Falha(ErroDeImportacaoNaoEncontrada, TipoDeErro.NaoEncontrado);
    if (!TentarLerVersao(versao, out var esperada))
      return Result<ImportacaoDto>.Falha(ErroDeVersaoInvalida, TipoDeErro.Validacao);
    if (!r.Versao.AsSpan().SequenceEqual(esperada))
      return Desatualizada.Resultado();

    if (await aplicar(r) is { } recusa)
      return recusa.Resultado();

    try
    {
      await _importacoes.SalvarAsync(r, esperada, ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      if (aoPerderACorrida is not null)
        await aoPerderACorrida();
      return Desatualizada.Resultado();
    }
    catch
    {
      // Qualquer outra falha do salvamento tambem deixa de lado o que a escrita gravou antes dele.
      if (aoPerderACorrida is not null)
        await aoPerderACorrida();
      throw;
    }

    if (aposSalvar is not null)
      await aposSalvar();
    return Result<ImportacaoDto>.Ok(await ProjetarAsync(r, ct));
  }

  private static bool TentarLerVersao(string? versao, out byte[] bytes)
  {
    bytes = [];
    if (string.IsNullOrEmpty(versao))
      return false;
    try
    {
      bytes = Convert.FromBase64String(versao);
      return true;
    }
    catch (FormatException)
    {
      return false;
    }
  }

  /// <summary>A faixa da coluna <c>DECIMAL(18,4)</c> que o planejador tambem usa, com no maximo 4 casas.</summary>
  private static Recusa? ForaDaColuna(decimal quantidade, string oQue)
  {
    if (quantidade >= PlanejadorDeCopia.QuantidadeMinimaDaColuna
        && quantidade <= PlanejadorDeCopia.QuantidadeMaximaDaColuna
        && decimal.Round(quantidade, 4) == quantidade)
      return null;
    var inv = CultureInfo.InvariantCulture;
    return new Recusa(
        $"{oQue} deve ficar entre {PlanejadorDeCopia.QuantidadeMinimaDaColuna.ToString(inv)} e "
        + $"{PlanejadorDeCopia.QuantidadeMaximaDaColuna.ToString(inv)}, com no maximo 4 casas decimais.",
        TipoDeErro.Validacao);
  }

  private static Recusa? ValidarCampos(AlteracaoDeComponenteDto dto)
  {
    if (dto.EscolhaDeReceita is not null
        && dto.EscolhaDeReceita is not (ValoresDaConferencia.EscolhaCatalogo or ValoresDaConferencia.EscolhaImportada))
      return new Recusa("Escolha de receita invalida: use Catalogo ou Importada.", TipoDeErro.Validacao);
    if (dto.TipoNovo is not null && !TiposDeComponente.Contains(dto.TipoNovo))
      return new Recusa("Tipo invalido: use Bruto, Fabricado ou Montagem.", TipoDeErro.Validacao);

    var texto = ExcessoDeTexto(dto.CodigoNovo?.Trim(), CodigoMaximo, "O codigo")
        ?? ExcessoDeTexto(dto.DescricaoNova?.Trim(), DescricaoMaxima, "A descricao");
    return texto is null ? null : new Recusa(texto, TipoDeErro.Validacao);
  }

  /// <summary>
  /// Casado -> o Componente, com os dados do novo zerados (a escolha e a impressao, quem chama).
  /// Casado -> novo, ou novo ->
  /// novo: os dados do corpo, por cima do que o arquivo leu quando o registro acabou de virar novo.
  /// </summary>
  private static void AplicarCasamento(ImportacaoDeEstruturaComponente registro, AlteracaoDeComponenteDto dto)
  {
    if (dto.ComponenteId is int alvo)
    {
      registro.ComponenteId = alvo;
      registro.CodigoNovo = registro.DescricaoNova = registro.TipoNovo = null;
      return;
    }

    if (registro.ComponenteId is not null)
    {
      registro.ComponenteId = null;
      registro.CodigoNovo = string.IsNullOrWhiteSpace(registro.CodigoLido) ? null : registro.CodigoLido.Trim();
      registro.DescricaoNova = registro.DescricaoLida;
      registro.TipoNovo = registro.Filhos.Count > 0 ? TipoMontagem : TipoFabricado;
    }
    if (dto.CodigoNovo is not null)
      registro.CodigoNovo = dto.CodigoNovo.Trim().Length == 0 ? null : dto.CodigoNovo.Trim();
    if (dto.DescricaoNova is not null)
      registro.DescricaoNova = dto.DescricaoNova.Trim().Length == 0 ? null : dto.DescricaoNova.Trim();
    if (dto.TipoNovo is not null)
      registro.TipoNovo = dto.TipoNovo;
  }

  /// <summary>
  /// A impressao da receita de catalogo que esta sendo comparada agora, ou nulo quando o registro nao
  /// diverge (so ha escolha onde ha divergencia). A divergencia e a do avaliador, sobre o rascunho ja com o
  /// casamento novo.
  /// </summary>
  private async Task<byte[]?> ImpressaoSeDiverge(
      ImportacaoDeEstrutura r, ImportacaoDeEstruturaComponente registro, CancellationToken ct)
  {
    if (registro.ComponenteId is not int componenteId)
      return null;
    var catalogo = await CarregarCatalogoAsync(r, ct);
    var situacao = AvaliadorDeImportacao.Avaliar(r, catalogo).Componentes.Single(s => s.RegistroId == registro.Id);
    return situacao.Divergente
        ? AvaliadorDeImportacao.Impressao(catalogo.Receita.Filhos[componenteId].Select(f => (f.FilhoId, f.QuantidadePadrao)))
        : null;
  }

  /// <summary>O que a edicao do registro pode mudar, para desfazer quando uma validacao tardia recusa.</summary>
  private readonly record struct Estado(
      int? ComponenteId, string? CodigoNovo, string? DescricaoNova, string? TipoNovo,
      string? EscolhaDeReceita, byte[]? Impressao)
  {
    public static Estado De(ImportacaoDeEstruturaComponente c) =>
        new(c.ComponenteId, c.CodigoNovo, c.DescricaoNova, c.TipoNovo, c.EscolhaDeReceita, c.ImpressaoDaReceitaDoCatalogo);

    public void Restaurar(ImportacaoDeEstruturaComponente c)
    {
      c.ComponenteId = ComponenteId;
      c.CodigoNovo = CodigoNovo;
      c.DescricaoNova = DescricaoNova;
      c.TipoNovo = TipoNovo;
      c.EscolhaDeReceita = EscolhaDeReceita;
      c.ImpressaoDaReceitaDoCatalogo = Impressao;
    }
  }
}
