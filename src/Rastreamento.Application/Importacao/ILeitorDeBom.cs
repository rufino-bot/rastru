namespace Rastreamento.Application.Importacao;

/// <summary>
/// Le o arquivo do BOM (CSV ou XLSX) e entrega as linhas cruas — so texto — ou os erros do arquivo.
/// Nao monta receita nem valida o conteudo das celulas: isso e do <see cref="MontadorDeReceitasDoBom"/>.
/// </summary>
public interface ILeitorDeBom
{
  ResultadoDaLeituraDoBom Ler(string nomeDoArquivo, byte[] conteudo);
}

/// <summary>Havendo qualquer erro, <see cref="Linhas"/> volta vazia: ninguem monta rascunho de arquivo recusado.</summary>
public sealed record ResultadoDaLeituraDoBom(IReadOnlyList<LinhaCruaDoBom> Linhas, IReadOnlyList<ErroDoBom> Erros);
