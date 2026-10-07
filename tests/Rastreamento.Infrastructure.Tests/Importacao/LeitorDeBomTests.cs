using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Rastreamento.Application.Importacao;
using Rastreamento.Infrastructure.Importacao;

namespace Rastreamento.Infrastructure.Tests.Importacao;

/// <summary>Sem banco: os arquivos sao montados em memoria pelo proprio teste.</summary>
public class LeitorDeBomTests
{
  private static readonly LeitorDeBom Leitor = new();

  private static byte[] Utf8(string texto, bool comBom = false) =>
      (comBom ? Encoding.UTF8.GetPreamble() : []).Concat(new UTF8Encoding(false).GetBytes(texto)).ToArray();

  private static byte[] Windows1252(string texto)
  {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    return Encoding.GetEncoding(1252).GetBytes(texto);
  }

  private static ResultadoDaLeituraDoBom LerCsv(string texto) => Leitor.Ler("bom.csv", Utf8(texto));

  // ---- CSV ----

  [Fact]
  public void Csv_ptBR_com_virgula_decimal_le_quantidade_fracionaria()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\r\n1;P-001;PAINEL;2\r\n1.1;P-002;CHAPA;1,5\r\n");

    Assert.Empty(r.Erros);
    Assert.Collection(r.Linhas,
        l =>
        {
          Assert.Equal("1", l.Nivel);
          Assert.Equal("P-001", l.Codigo);
          Assert.Equal("PAINEL", l.Descricao);
          Assert.Equal("2", l.Quantidade);
        },
        l =>
        {
          Assert.Equal("1.1", l.Nivel);
          Assert.Equal("1,5", l.Quantidade);
        });
  }

  [Fact]
  public void Csv_campo_entre_aspas_com_separador_e_aspas_duplas()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;\"CHAPA 1/4\"\", ACO 1020\";1\n2;P-002;\"A;B\";1\n");

    Assert.Empty(r.Erros);
    Assert.Equal("CHAPA 1/4\", ACO 1020", r.Linhas[0].Descricao);
    Assert.Equal("A;B", r.Linhas[1].Descricao);
  }

  [Fact]
  public void Csv_campo_entre_aspas_com_quebra_de_linha_conta_a_linha_fisica()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;\"LINHA 1\nLINHA 2\";1\n2;P-002;X;1\n");

    Assert.Empty(r.Erros);
    Assert.Equal("LINHA 1\nLINHA 2", r.Linhas[0].Descricao);
    Assert.Equal(2, r.Linhas[0].NumeroDaLinha);
    Assert.Equal(4, r.Linhas[1].NumeroDaLinha);
  }

  [Fact]
  public void Csv_com_aspas_nao_fechadas_e_erro_com_a_linha()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;\"SEM FIM;1\n");

    Assert.Empty(r.Linhas);
    var erro = Assert.Single(r.Erros);
    Assert.Equal(2, erro.Linha);
  }

  [Fact]
  public void Csv_windows1252_preserva_acento()
  {
    var bytes = Windows1252("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\r\n1;P-001;Rebarbação;1\r\n");

    var r = Leitor.Ler("bom.csv", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal("Rebarbação", r.Linhas[0].Descricao);
    Assert.Equal(10, r.Linhas[0].Descricao.Length);
  }

  [Fact]
  public void Csv_utf8_com_bom_preserva_acento()
  {
    var bytes = Utf8("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\r\n1;P-001;Rebarbação;1\r\n", comBom: true);

    var r = Leitor.Ler("bom.csv", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal("Rebarbação", r.Linhas[0].Descricao);
    Assert.Equal(10, r.Linhas[0].Descricao.Length);
  }

  [Fact]
  public void Csv_utf8_sem_bom_preserva_acento()
  {
    var r = Leitor.Ler("bom.csv", Utf8("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\r\n1;P-001;Rebarbação;1\r\n"));

    Assert.Empty(r.Erros);
    Assert.Equal("Rebarbação", r.Linhas[0].Descricao);
  }

  [Fact]
  public void Csv_com_virgula_como_separador()
  {
    var r = LerCsv("ITEM NO.,PART NUMBER,DESCRIPTION,QTY.\n1,P-001,\"CHAPA, 3MM\",2\n");

    Assert.Empty(r.Erros);
    Assert.Equal("CHAPA, 3MM", r.Linhas[0].Descricao);
    Assert.Equal("2", r.Linhas[0].Quantidade);
  }

  [Fact]
  public void Cabecalho_em_ingles_do_SolidWorks_e_reconhecido()
  {
    var r = LerCsv("ITEM NO.;PART NUMBER;DESCRIPTION;QTY.\n1;P-001;PAINEL;3\n");

    Assert.Empty(r.Erros);
    var linha = Assert.Single(r.Linhas);
    Assert.Equal("1", linha.Nivel);
    Assert.Equal("P-001", linha.Codigo);
    Assert.Equal("PAINEL", linha.Descricao);
    Assert.Equal("3", linha.Quantidade);
  }

  [Fact]
  public void Cabecalho_com_colunas_fora_de_ordem_e_extras_e_reconhecido()
  {
    var r = LerCsv("Observacao;Quantidade;Descricao;Numero da Peca;Item\nx;4;PAINEL;P-001;1\n");

    Assert.Empty(r.Erros);
    var linha = Assert.Single(r.Linhas);
    Assert.Equal("1", linha.Nivel);
    Assert.Equal("P-001", linha.Codigo);
    Assert.Equal("PAINEL", linha.Descricao);
    Assert.Equal("4", linha.Quantidade);
  }

  [Fact]
  public void Cabecalho_com_apelido_desconhecido_nomeia_a_coluna_que_falta()
  {
    var r = LerCsv("Item;Codigo;Descricao;Quantidade\n1;P-001;PAINEL;1\n");

    Assert.Empty(r.Linhas);
    Assert.Equal("coluna 'N da peca' nao encontrada", Assert.Single(r.Erros).Mensagem);
  }

  [Fact]
  public void Cabecalho_sem_coluna_de_quantidade_e_erro_que_nomeia_a_coluna()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO\n1;P-001;PAINEL\n");

    Assert.Empty(r.Linhas);
    var erro = Assert.Single(r.Erros);
    Assert.Null(erro.Linha);
    Assert.Equal("coluna 'Quantidade' nao encontrada", erro.Mensagem);
  }

  [Fact]
  public void Cabecalho_sem_varias_colunas_traz_um_erro_por_coluna()
  {
    var r = LerCsv("DESCRIÇÃO;OUTRA\n1;2\n");

    Assert.Empty(r.Linhas);
    Assert.Equal(3, r.Erros.Count);
  }

  [Theory]
  [InlineData("Nº DO ITEM")]
  [InlineData("N.º DO ITEM")]
  [InlineData("N° DO ITEM")]
  [InlineData("  item   no. ")]
  [InlineData("Número do Item")]
  public void Cabecalho_de_nivel_e_normalizado(string cabecalho)
  {
    var colunas = ColunasDoBom.Localizar([cabecalho, "N DA PECA", "DESCRICAO", "QTD"], out var erros);

    Assert.Empty(erros);
    Assert.Equal((0, 1, 2, 3), colunas);
  }

  [Fact]
  public void Codigo_vazio_no_conteudo_vira_nulo()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;;PAINEL;1\n");

    Assert.Empty(r.Erros);
    Assert.Null(r.Linhas[0].Codigo);
  }

  [Fact]
  public void Extensao_desconhecida_e_erro()
  {
    var r = Leitor.Ler("bom.pdf", Utf8("qualquer coisa"));

    Assert.Empty(r.Linhas);
    var erro = Assert.Single(r.Erros);
    Assert.Null(erro.Linha);
    Assert.Contains(".csv", erro.Mensagem);
    Assert.Contains(".xlsx", erro.Mensagem);
  }

  [Fact]
  public void Extensao_e_reconhecida_sem_distinguir_caixa()
  {
    var r = Leitor.Ler("BOM.CSV", Utf8("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;PAINEL;1\n"));

    Assert.Empty(r.Erros);
    Assert.Single(r.Linhas);
  }

  [Fact]
  public void Arquivo_acima_do_limite_e_erro()
  {
    var r = Leitor.Ler("bom.csv", new byte[LeitorDeBom.TamanhoMaximoEmBytes + 1]);

    Assert.Empty(r.Linhas);
    Assert.Single(r.Erros);
  }

  [Fact]
  public void Arquivo_vazio_e_erro()
  {
    var r = Leitor.Ler("bom.csv", []);

    Assert.Empty(r.Linhas);
    Assert.Single(r.Erros);
  }

  [Fact]
  public void Linhas_em_branco_no_fim_sao_ignoradas()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;PAINEL;1\n;;;\n\n   ;  ;  ;  \n");

    Assert.Empty(r.Erros);
    Assert.Single(r.Linhas);
  }

  [Fact]
  public void NumeroDaLinha_e_o_do_arquivo_contando_o_cabecalho()
  {
    var r = LerCsv("Nº DO ITEM;Nº DA PEÇA;DESCRIÇÃO;QTD.\n1;P-001;A;1\n\n2;P-002;B;1\n3;P-003;C;1\n");

    Assert.Equal([2, 4, 5], r.Linhas.Select(l => l.NumeroDaLinha).ToArray());
  }

  // ---- XLSX ----

  private enum Tipo { Texto, Numero }

  /// <param name="extras">Celulas de texto com referencia escrita a mao, acrescentadas ao fim da linha
  /// de indice <c>Linha</c> (0 e o cabecalho) — para as colunas longe das quatro do BOM.</param>
  private static byte[] MontarXlsx(IReadOnlyList<IReadOnlyList<(string Valor, Tipo Tipo)?>> linhas,
      bool segundaPlanilha = false, int primeiraLinha = 1,
      IReadOnlyList<(int Linha, string Referencia, string Valor)>? extras = null)
  {
    using var memoria = new MemoryStream();
    using (var doc = SpreadsheetDocument.Create(memoria, SpreadsheetDocumentType.Workbook))
    {
      var livro = doc.AddWorkbookPart();
      livro.Workbook = new Workbook();
      var planilhas = livro.Workbook.AppendChild(new Sheets());
      var tabelaDeTextos = livro.AddNewPart<SharedStringTablePart>();
      tabelaDeTextos.SharedStringTable = new SharedStringTable();
      var indices = new Dictionary<string, int>();

      int Compartilhar(string texto)
      {
        if (indices.TryGetValue(texto, out var i)) return i;
        tabelaDeTextos.SharedStringTable.AppendChild(new SharedStringItem(new Text(texto)));
        return indices[texto] = indices.Count;
      }

      WorksheetPart Planilha(uint id, string nome, IReadOnlyList<IReadOnlyList<(string Valor, Tipo Tipo)?>> conteudo)
      {
        var parte = livro.AddNewPart<WorksheetPart>();
        var dados = new SheetData();
        for (var r = 0; r < conteudo.Count; r++)
        {
          var linha = new Row { RowIndex = (uint)(r + primeiraLinha) };
          for (var c = 0; c < conteudo[r].Count; c++)
          {
            if (conteudo[r][c] is not { } celula) continue;
            var referencia = $"{(char)('A' + c)}{r + primeiraLinha}";
            linha.AppendChild(celula.Tipo == Tipo.Numero
                ? new Cell { CellReference = referencia, CellValue = new CellValue(celula.Valor) }
                : new Cell
                {
                  CellReference = referencia,
                  DataType = CellValues.SharedString,
                  CellValue = new CellValue(Compartilhar(celula.Valor).ToString(CultureInfo.InvariantCulture)),
                });
          }
          foreach (var extra in (extras ?? []).Where(e => e.Linha == r && id == 1))
            linha.AppendChild(new Cell
            {
              CellReference = extra.Referencia,
              DataType = CellValues.SharedString,
              CellValue = new CellValue(Compartilhar(extra.Valor).ToString(CultureInfo.InvariantCulture)),
            });
          dados.AppendChild(linha);
        }
        parte.Worksheet = new Worksheet(dados);
        planilhas.AppendChild(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = id, Name = nome });
        return parte;
      }

      Planilha(1, "BOM", linhas);
      if (segundaPlanilha)
        Planilha(2, "Outra", [[("1", Tipo.Texto), ("X-999", Tipo.Texto), ("OUTRA", Tipo.Texto), ("9", Tipo.Numero)]]);
      livro.Workbook.Save();
    }
    return memoria.ToArray();
  }

  private static (string, Tipo)? T(string valor) => (valor, Tipo.Texto);
  private static (string, Tipo)? N(string valor) => (valor, Tipo.Numero);

  private static IReadOnlyList<(string, Tipo)?> Cabecalho() =>
      [T("Nº DO ITEM"), T("Nº DA PEÇA"), T("DESCRIÇÃO"), T("QTD.")];

  [Fact]
  public void Xlsx_le_primeira_planilha_com_quantidade_numerica()
  {
    var bytes = MontarXlsx(
        [Cabecalho(), [T("1"), T("P-001"), T("Rebarbação"), N("2")], [T("1.1"), T("P-002"), T("CHAPA"), N("1.5")]],
        segundaPlanilha: true);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Collection(r.Linhas,
        l =>
        {
          Assert.Equal(2, l.NumeroDaLinha);
          Assert.Equal("1", l.Nivel);
          Assert.Equal("P-001", l.Codigo);
          Assert.Equal("Rebarbação", l.Descricao);
          Assert.Equal(10, l.Descricao.Length);
          Assert.Equal("2", l.Quantidade);
        },
        l =>
        {
          Assert.Equal(3, l.NumeroDaLinha);
          Assert.Equal("1.1", l.Nivel);
          Assert.Equal("1.5", l.Quantidade);
        });
  }

  [Fact]
  public void Xlsx_le_nivel_salvo_como_texto()
  {
    var bytes = MontarXlsx(
        [Cabecalho(), [T("1"), T("P-001"), T("A"), N("1")], [T("1.10"), T("P-002"), T("B"), N("1")]]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal("1.10", r.Linhas[1].Nivel);
  }

  [Fact]
  public void Xlsx_nivel_numerico_com_casa_decimal_e_erro_que_pede_texto()
  {
    var bytes = MontarXlsx([Cabecalho(), [N("1.1"), T("P-001"), T("A"), N("1")]]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Linhas);
    var erro = Assert.Single(r.Erros);
    Assert.Equal(2, erro.Linha);
    Assert.Contains("texto", erro.Mensagem);
  }

  [Fact]
  public void Xlsx_nivel_numerico_inteiro_e_aceito()
  {
    var bytes = MontarXlsx([Cabecalho(), [N("1"), T("P-001"), T("A"), N("1")], [N("2"), T("P-002"), T("B"), N("1")]]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal(["1", "2"], r.Linhas.Select(l => l.Nivel).ToArray());
  }

  [Fact]
  public void Xlsx_quantidade_numerica_pequena_nao_vira_notacao_cientifica()
  {
    var bytes = MontarXlsx([Cabecalho(), [T("1"), T("P-001"), T("A"), N("1E-05")], [T("2"), T("P-002"), T("B"), N("0.30000000000000004")]]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal("0.00001", r.Linhas[0].Quantidade);
    Assert.Equal("0.3", r.Linhas[1].Quantidade);
  }

  [Fact]
  public void Xlsx_numero_da_linha_e_o_da_planilha_e_celula_vazia_nao_desloca_coluna()
  {
    var bytes = MontarXlsx(
        [Cabecalho(), [T("1"), null, T("SEM CODIGO"), N("1")], [], [T("2"), T("P-002"), T("B"), N("1")]],
        primeiraLinha: 3);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal([4, 6], r.Linhas.Select(l => l.NumeroDaLinha).ToArray());
    Assert.Null(r.Linhas[0].Codigo);
    Assert.Equal("SEM CODIGO", r.Linhas[0].Descricao);
  }

  [Fact]
  public void Xlsx_sem_coluna_de_quantidade_e_erro_que_nomeia_a_coluna()
  {
    var bytes = MontarXlsx([[T("Nº DO ITEM"), T("Nº DA PEÇA"), T("DESCRIÇÃO")], [T("1"), T("P-001"), T("A")]]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    var erro = Assert.Single(r.Erros);
    Assert.Equal("coluna 'Quantidade' nao encontrada", erro.Mensagem);
  }

  [Fact]
  public void Xlsx_com_coluna_XFD_a_ultima_do_Excel_e_aceito()
  {
    var bytes = MontarXlsx(
        [Cabecalho(), [T("1"), T("P-001"), T("A"), N("1")]],
        extras: [(0, "XFD1", "OBS"), (1, "XFD2", "nota")]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Erros);
    Assert.Equal("P-001", Assert.Single(r.Linhas).Codigo);
  }

  [Theory]
  [InlineData("XFE2")]
  [InlineData("AAAA2")]
  [InlineData("ZZZZZZ2")]
  public void Xlsx_com_coluna_alem_da_XFD_e_arquivo_corrompido(string referencia)
  {
    // O Excel para na coluna XFD (16384). Uma referencia alem dela so vem de arquivo forjado, e
    // "ZZZZZZ" e a coluna 321.272.406: preencher as colunas vazias ate ela custaria gigabytes de
    // memoria por um arquivo de 1 KB.
    var bytes = MontarXlsx([Cabecalho(), [T("1"), T("P-001"), T("A"), N("1")]], extras: [(1, referencia, "x")]);

    var r = Leitor.Ler("bom.xlsx", bytes);

    Assert.Empty(r.Linhas);
    Assert.Contains("corrompido", Assert.Single(r.Erros).Mensagem);
  }

  [Fact]
  public void Xlsx_ilegivel_e_erro_sem_excecao()
  {
    var r = Leitor.Ler("bom.xlsx", Utf8("isto nao e um zip"));

    Assert.Empty(r.Linhas);
    Assert.Single(r.Erros);
  }
}
