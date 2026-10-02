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
