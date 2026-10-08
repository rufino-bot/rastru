namespace Rastreamento.Application.Importacao;

/// <summary>
/// Uma linha do BOM como o leitor do arquivo a entregou: so texto. <c>Nivel</c> e o "N do item"
/// ("1.2.3"); <c>Quantidade</c> e o texto da celula, ou o numero da celula XLSX formatado com
/// <c>CultureInfo.InvariantCulture</c>.
/// </summary>
public sealed record LinhaCruaDoBom(int NumeroDaLinha, string Nivel, string? Codigo, string Descricao, string Quantidade);

public sealed record ErroDoBom(int? Linha, string Mensagem)
{
  public override string ToString() => Linha is null ? Mensagem : $"Linha {Linha}: {Mensagem}";
}

/// <summary>Chave 0 e a raiz (o proprio arquivo); as demais sao um componente por codigo distinto.</summary>
public sealed record ComponenteDoBom(int Chave, string? Codigo, string Descricao);

public sealed record FilhoDoBom(int PaiChave, int FilhoChave, int Ordem, decimal Quantidade);

public sealed record BomMontado(
    IReadOnlyList<ComponenteDoBom> Componentes,
    IReadOnlyList<FilhoDoBom> Filhos,
    IReadOnlyList<ErroDoBom> Erros);
