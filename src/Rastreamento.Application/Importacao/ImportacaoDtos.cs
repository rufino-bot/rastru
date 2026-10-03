namespace Rastreamento.Application.Importacao;

/// <summary>
/// Um no da arvore expandida da conferencia. <c>RegistroId</c> e o registro do rascunho que o no
/// representa (nulo quando o no veio so do catalogo); <c>FilhoId</c> e a linha da receita lida que o
/// pendurou aqui (nula na raiz e quando a aresta e do catalogo), e e por ela que a tela corrige a
/// quantidade.
/// </summary>
public sealed record NoDaImportacaoDto(
    int? RegistroId,
    int? ComponenteId,
    int? FilhoId,
    string Codigo,
    string Descricao,
    decimal? QuantidadePorPai,
    string Origem,
    IReadOnlyList<string> Pendencias,
    IReadOnlyList<NoDaImportacaoDto> Filhos);

/// <summary>Uma linha do comparativo de um nivel de um codigo divergente (D4 da spec do import).</summary>
public sealed record LinhaDoComparativoDto(
    string Codigo, string Descricao, decimal? NoCatalogo, decimal? NoBom, string Situacao);

/// <summary>
/// O que "manter a do catalogo" faria numa ocorrencia do codigo: quantos nos expandidos saem da arvore
/// (os galhos so do BOM) e quantos entram (os so do catalogo).
/// </summary>
public sealed record EfeitoDto(int Retira, int Traz);

/// <summary>
/// A situacao de um registro do rascunho (um por codigo distinto). <c>Tipo</c>, <c>Ativo</c>,
/// <c>CodigoDoCatalogo</c> e <c>DescricaoDoCatalogo</c> sao do Componente casado, e nulos para o novo.
/// </summary>
public sealed record SituacaoDoComponenteDto(
    int RegistroId,
    string? CodigoLido,
    string DescricaoLida,
    int? ComponenteId,
    string? CodigoDoCatalogo,
    string? DescricaoDoCatalogo,
    string? Tipo,
    bool? Ativo,
    bool TemSolido,
    bool TemSolidoPendente,
    string? NomeDoSolido,
    int? TamanhoDoSolidoEmBytes,
    string? CodigoNovo,
    string? DescricaoNova,
    string? TipoNovo,
    bool Divergente,
    string? EscolhaDeReceita,
    IReadOnlyList<LinhaDoComparativoDto> Comparativo,
    EfeitoDto? EfeitoDeManterCatalogo,
    bool NaArvoreFinal);

/// <summary>Um motivo que impede a confirmacao (secao 5.4 da spec do import). A mensagem e texto de tela.</summary>
public sealed record BloqueioDto(string Tipo, int? RegistroId, int? ComponenteId, string Mensagem);

/// <summary>
/// O rascunho inteiro como a tela o le: o estado e CALCULADO a cada leitura (decisao P4 do plano do
/// import) e nunca gravado. <c>Versao</c> e o <c>ROWVERSION</c> do cabecalho; vai como base64 no JSON
/// (decisao P14) e toda escrita a devolve. <c>Raiz</c> e nula quando a expansao e recusada (ciclo,
/// profundidade, tamanho ou quantidade fora da coluna): o motivo esta em <c>Bloqueios</c>.
/// </summary>
public sealed record ImportacaoDto(
    int Id,
    int AgrupamentoId,
    string NomeDoArquivo,
    string CriadoPor,
    DateTime CriadoEm,
    DateTime AtualizadoEm,
    byte[] Versao,
    decimal? QuantidadeDaPeca,
    bool RequerRelatorioDimensional,
    NoDaImportacaoDto? Raiz,
    IReadOnlyList<SituacaoDoComponenteDto> Componentes,
    IReadOnlyList<BloqueioDto> Bloqueios);

/// <summary>Uma linha da lista de rascunhos de um Agrupamento.</summary>
public sealed record ResumoDeImportacaoDto(
    int Id, string NomeDoArquivo, string CriadoPor, DateTime CriadoEm, DateTime AtualizadoEm);

/// <summary>
/// Entradas das escritas do rascunho. <c>Versao</c> e a que a leitura devolveu (base64 do
/// <c>ROWVERSION</c>, decisao P14 do plano do import): toda escrita a manda, e versao velha e 409.
/// </summary>
public sealed record AlteracaoDaPecaDto(string Versao, decimal? QuantidadeDaPeca, bool RequerRelatorioDimensional);

/// <summary>
/// <c>ComponenteId</c> preenchido casa o registro com aquele Componente (e descarta os dados do "criar
/// novo"); nulo deixa-o "criar novo", e ai <c>CodigoNovo</c>, <c>DescricaoNova</c> e <c>TipoNovo</c>, quando
/// preenchidos, sao os dados dele (nulo mantem o que esta). <c>EscolhaDeReceita</c> e o estado inteiro da
/// escolha: nulo a limpa.
/// </summary>
public sealed record AlteracaoDeComponenteDto(
    string Versao, int? ComponenteId, string? CodigoNovo, string? DescricaoNova, string? TipoNovo, string? EscolhaDeReceita);

public sealed record AlteracaoDeFilhoDto(string Versao, decimal Quantidade);

/// <summary>O corpo da confirmacao: so a versao, que tem de ser a do banco.</summary>
public sealed record ConfirmacaoDto(string Versao);

/// <summary>Os valores fechados dos campos de texto dos DTOs da conferencia.</summary>
public static class ValoresDaConferencia
{
  public const string OrigemBom = "Bom";
  public const string OrigemCatalogo = "Catalogo";

  public const string PendenciaNovo = "Novo";
  public const string PendenciaInativo = "Inativo";
  public const string PendenciaDivergente = "Divergente";
  public const string PendenciaSemSolido = "SemSolido";

  public const string EscolhaCatalogo = "Catalogo";
  public const string EscolhaImportada = "Importada";

  public const string LinhaIgual = "Igual";
  public const string LinhaQuantidadeMuda = "QuantidadeMuda";
  public const string LinhaEntra = "Entra";
  public const string LinhaSai = "Sai";

  public const string BloqueioSemSolido = "SemSolido";
  public const string BloqueioDivergenciaSemEscolha = "DivergenciaSemEscolha";
  public const string BloqueioCodigoVazio = "CodigoVazio";
  public const string BloqueioCodigoJaExiste = "CodigoJaExiste";
  public const string BloqueioQuantidadeDaPecaAusente = "QuantidadeDaPecaAusente";

  /// <summary>
  /// A quantidade da Peca, multiplicada pela receita, sai da faixa da coluna. Os outros tres bloqueios
  /// de estrutura usam o codigo do proprio <c>PlanejadorDeCopia</c> (<c>CicloNaReceita</c>,
  /// <c>EstruturaProfundaDemais</c>, <c>EstruturaGrandeDemais</c>).
  /// </summary>
  public const string BloqueioQuantidadeForaDaFaixa = "QuantidadeForaDaFaixa";
}
