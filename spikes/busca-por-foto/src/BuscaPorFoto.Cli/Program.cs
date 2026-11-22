using BuscaPorFoto.Marcador;

const string Uso = """
    Uso:
      BuscaPorFoto.Cli marcador <arquivo.pdf>
    """;

switch (args)
{
    case ["marcador", var pdf]:
        File.WriteAllBytes(pdf, PdfDoMarcador.Gerar());
        Console.WriteLine($"Marcador gravado em {Path.GetFullPath(pdf)}");
        return 0;
}

Console.Error.WriteLine(Uso);
return 2;
