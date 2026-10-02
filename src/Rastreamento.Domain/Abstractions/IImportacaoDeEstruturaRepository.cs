using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Uma linha da lista de rascunhos de um Agrupamento. <c>CriadoPor</c> e o <c>NomeCompleto</c> do
/// autor. Vive junto da interface porque so ela produz este tipo, mesmo padrao de
/// <see cref="MetadadoDeSolido"/>.
/// </summary>
public sealed record ResumoDeImportacao(
    int Id, string NomeDoArquivo, string CriadoPor, DateTime CriadoEm, DateTime AtualizadoEm);

/// <summary>
/// Persistencia do rascunho do import da estrutura a partir do BOM (tres tabelas, spec do import,
/// secao 3) e do solido pendente dos registros.
/// </summary>
public interface IImportacaoDeEstruturaRepository
{
  /// <summary>
  /// O rascunho RASTREADO, com <c>Componentes</c> e <c>Componentes.Filhos</c>: quem escreve muta a
  /// entidade e chama <see cref="SalvarAsync"/>. Null quando nao existe.
  /// </summary>
  Task<ImportacaoDeEstrutura?> ObterAsync(int id, CancellationToken ct);

  /// <summary>Os rascunhos do Agrupamento, do mais recente para o mais antigo (<c>CriadoEm</c>).</summary>
  Task<IReadOnlyList<ResumoDeImportacao>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct);

  /// <summary>
  /// Grava cabecalho, registros e filhos e, so depois, o <c>RaizId</c> (decisao P6 do plano do
  /// import: a FK circular nao deixa gravar tudo de uma vez). A raiz e o objeto apontado por
  /// <see cref="ImportacaoDeEstrutura.Raiz"/>; os filhos apontam o filho pelo objeto
  /// (<see cref="ImportacaoDeEstruturaFilho.Filho"/>). Tudo numa transacao: o rascunho nao existe
  /// pela metade.
  /// </summary>
  Task AdicionarAsync(ImportacaoDeEstrutura importacao, CancellationToken ct);

  /// <summary>
  /// Grava as mudancas da entidade rastreada. Poe <c>AtualizadoEm = UtcNow</c> (e o que faz o
  /// <c>ROWVERSION</c> do cabecalho mudar quando so um registro ou um filho mudou) e usa
  /// <paramref name="versaoEsperada"/> como valor original do token. Lanca
  /// <see cref="ConflitoDeConcorrenciaException"/> quando ela nao bate com a do banco.
  /// </summary>
  Task SalvarAsync(ImportacaoDeEstrutura importacao, byte[] versaoEsperada, CancellationToken ct);

  /// <summary>
  /// Marca registros do rascunho para exclusao no proximo <see cref="SalvarAsync"/> (o reimport tira os
  /// codigos que sairam do arquivo). Sem I/O: quem apaga e o <c>SalvarAsync</c>, na mesma transacao que
  /// o resto da escrita. Os filhos que apontam para eles, como pai ou como filho, tem de ser marcados
  /// por <see cref="RemoverFilhos"/> na mesma escrita: o schema nao tem <c>ON DELETE CASCADE</c>
  /// (decisao P6 do plano do import).
  /// </summary>
  void RemoverRegistros(IEnumerable<ImportacaoDeEstruturaComponente> registros);

  /// <summary>Como <see cref="RemoverRegistros"/>, para as linhas da receita lida.</summary>
  void RemoverFilhos(IEnumerable<ImportacaoDeEstruturaFilho> filhos);

  /// <summary>
  /// Apaga o rascunho numa transacao, na ordem filhos -> <c>RaizId = NULL</c> -> registros ->
  /// cabecalho -> <c>ArquivoDeComponente</c> pendentes (decisao P6 do plano do import: o schema nao
  /// tem <c>ON DELETE CASCADE</c>). Id inexistente nao e erro.
  /// </summary>
  Task ExcluirAsync(int id, CancellationToken ct);

  Task<bool> ExisteNoAgrupamentoAsync(int agrupamentoId, CancellationToken ct);

  /// <summary>
  /// <c>Usuario.NomeCompleto</c> (o <c>IUsuarioRepository</c> so busca por nome de usuario).
  /// </summary>
  Task<string> ObterNomeDoAutorAsync(int usuarioId, CancellationToken ct);

  /// <summary>Grava o arquivo (solido pendente) e devolve o Id. Nenhum Componente aponta para ele.</summary>
  Task<int> GravarArquivoPendenteAsync(ArquivoDeComponente arquivo, CancellationToken ct);

  /// <summary>O arquivo COM o blob, sem rastreamento. Null quando nao existe.</summary>
  Task<ArquivoDeComponente?> ObterArquivoAsync(int arquivoId, CancellationToken ct);

  /// <summary>
  /// Nome e tamanho dos arquivos, chaveados pelo Id do <c>ArquivoDeComponente</c>, SEM carregar o
  /// <c>Conteudo</c> (projecao explicita). Serve ao solido do catalogo e ao pendente com uma consulta
  /// so. Id inexistente simplesmente nao aparece no dicionario.
  /// </summary>
  Task<IReadOnlyDictionary<int, MetadadoDeSolido>> ObterMetadadosAsync(
      IReadOnlyCollection<int> arquivoIds, CancellationToken ct);

  /// <summary>Apaga os arquivos (solidos pendentes descartados ou substituidos).</summary>
  Task ExcluirArquivosAsync(IReadOnlyCollection<int> arquivoIds, CancellationToken ct);
}
