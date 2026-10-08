namespace Rastreamento.Domain.Entities;

/// <summary>
/// O rascunho de conferencia do import da estrutura a partir do BOM do CAD (spec do import, secao 3):
/// o que foi lido do arquivo, o casamento com o catalogo e as escolhas do usuario, ate a Peca existir.
/// O estado (bloqueios, divergencias) e calculado a cada leitura e nunca gravado (decisao P4 do plano
/// do import). Sem <c>EstruturaItem</c> nem Componente novo ate a confirmacao.
/// </summary>
public class ImportacaoDeEstrutura
{
  public int Id { get; set; }
  public int AgrupamentoId { get; set; }
  public string NomeDoArquivo { get; set; } = string.Empty;

  /// <summary>
  /// O registro da raiz. Nulo so durante a criacao: a FK e circular com
  /// <see cref="ImportacaoDeEstruturaComponente"/>, e <c>AdicionarAsync</c> grava o cabecalho e os
  /// registros primeiro e este Id depois (decisao P6 do plano do import).
  /// </summary>
  public int? RaizId { get; set; }

  /// <summary>
  /// Quem monta o rascunho ainda nao conhece o Id do registro da raiz: aponta o objeto aqui e
  /// <c>AdicionarAsync</c> preenche <see cref="RaizId"/> depois do primeiro save. Nao e mapeada, e
  /// nao e preenchida ao ler do banco -- leitura usa <see cref="RaizId"/>.
  /// </summary>
  public ImportacaoDeEstruturaComponente? Raiz { get; set; }

  public decimal? QuantidadeDaPeca { get; set; }
  public bool RequerRelatorioDimensional { get; set; }
  public int CriadoPorUsuarioId { get; set; }

  /// <summary>Vem do DEFAULT do banco (Database First), nao do C#.</summary>
  public DateTime CriadoEm { get; set; }

  /// <summary>
  /// Toda escrita no rascunho a atualiza (<c>SalvarAsync</c>): e o que faz <see cref="Versao"/> mudar
  /// quando so um registro ou um filho mudou (decisao P7 do plano do import).
  /// </summary>
  public DateTime AtualizadoEm { get; set; }

  /// <summary>ROWVERSION do cabecalho: o token de concorrencia do rascunho inteiro.</summary>
  public byte[] Versao { get; set; } = [];

  public List<ImportacaoDeEstruturaComponente> Componentes { get; set; } = [];
}
