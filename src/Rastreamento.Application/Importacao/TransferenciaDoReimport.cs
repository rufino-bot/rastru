using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// O que sobrou do reimport para o chamador apagar: os registros e as linhas que sairam do arquivo. Os
/// registros novos ja estao em <c>Componentes</c>, ligados pelas linhas; os removidos continuam nas colecoes
/// do rascunho ate o <c>SalvarAsync</c> aplicar as exclusoes.
/// </summary>
internal sealed record PlanoDoReimport(
    IReadOnlyList<ImportacaoDeEstruturaComponente> RegistrosRemovidos,
    IReadOnlyList<ImportacaoDeEstruturaFilho> FilhosRemovidos);

/// <summary>
/// A substituicao das receitas lidas do reimport (spec do import, a rota <c>POST /importacoes/{id}/arquivo</c>),
/// por CODIGO (<c>OrdinalIgnoreCase</c>, aparado). Em vez de apagar tudo e recriar, o registro de um codigo
/// que continua no arquivo e MANTIDO (mesmo Id): o casamento, os dados do "criar novo" e o solido pendente
/// seguem com ele sem copia, e so a descricao lida e as linhas mudam. O registro de codigo novo nasce como
/// no <c>Criar</c>; o de codigo que saiu e removido, e o pendente dele e do chamador apagar.
///
/// A raiz (decisao P8 do plano do import) e SEMPRE o mesmo registro. Linha sem codigo, fora a raiz, nao tem
/// identidade para ser reconhecida: sai e entra de novo a cada reimport.
///
/// A escolha de receita (e a impressao dela) so segue quando a receita LIDA do codigo, os codigos dos filhos
/// e as quantidades lidas, e identica a de antes; senao e zerada. As linhas sao reaproveitadas par a par
/// (mesmo pai e mesmo filho) e as quantidades corrigidas na tela voltam ao que o arquivo diz: o arquivo e a
/// forma da arvore (D8 da spec do import).
/// </summary>
internal static class TransferenciaDoReimport
{
  private const string TipoBruto = "Bruto";
  private const string TipoMontagem = "Montagem";
  private const string TipoFabricado = "Fabricado";

  /// <summary>Cria o registro de um codigo que o rascunho ainda nao tem (a regra do <c>Criar</c>).</summary>
  public delegate ImportacaoDeEstruturaComponente CriarRegistro(ComponenteDoBom componente, bool temFilhos, ISet<int> emUso);

  public static PlanoDoReimport Aplicar(ImportacaoDeEstrutura r, BomMontado bom, CriarRegistro criar)
  {
    var raizId = r.RaizId ?? throw new InvalidOperationException($"O rascunho {r.Id} nao tem raiz.");
    var antigos = r.Componentes.ToList();
    var porId = antigos.ToDictionary(c => c.Id);
    var porCodigo = antigos
        .Where(c => c.Id != raizId && !string.IsNullOrWhiteSpace(c.CodigoLido))
        .ToDictionary(c => c.CodigoLido!.Trim(), StringComparer.OrdinalIgnoreCase);

    // Antes de mexer em qualquer linha: a receita lida de cada registro, como estava.
    var receitaAntiga = antigos.ToDictionary(c => c.Id, c => ReceitaLida(c, porId));
    var receitaNova = ReceitasDoBom(bom);

    var mantidos = new Dictionary<int, ImportacaoDeEstruturaComponente>();
    foreach (var c in bom.Componentes)
    {
      var antigo = c.Chave == 0
          ? porId[raizId]
          : c.Codigo is not null && porCodigo.TryGetValue(c.Codigo.Trim(), out var achado) ? achado : null;
      if (antigo is not null)
        mantidos[c.Chave] = antigo;
    }

    var emUso = mantidos.Values.Where(m => m.ComponenteId is not null).Select(m => m.ComponenteId!.Value).ToHashSet();
    var temFilhos = bom.Filhos.Select(f => f.PaiChave).ToHashSet();
    var registros = new Dictionary<int, ImportacaoDeEstruturaComponente>();
    foreach (var c in bom.Componentes)
    {
      if (mantidos.TryGetValue(c.Chave, out var mantido))
      {
        mantido.CodigoLido = c.Codigo;
        mantido.DescricaoLida = c.Descricao;
        // O tipo do "criar novo" acompanha a forma do arquivo novo (Montagem com filhos, Fabricado sem);
        // o Bruto e escolha do usuario e fica.
        if (mantido.ComponenteId is null && mantido.TipoNovo is not (null or TipoBruto))
          mantido.TipoNovo = temFilhos.Contains(c.Chave) ? TipoMontagem : TipoFabricado;
        if (!MesmaReceita(receitaAntiga[mantido.Id], receitaNova[c.Chave]))
        {
          mantido.EscolhaDeReceita = null;
          mantido.ImpressaoDaReceitaDoCatalogo = null;
        }
        registros[c.Chave] = mantido;
      }
      else
      {
        var novo = criar(c, temFilhos.Contains(c.Chave), emUso);
        r.Componentes.Add(novo);
        registros[c.Chave] = novo;
      }
    }

    var filhosRemovidos = new List<ImportacaoDeEstruturaFilho>();
    foreach (var c in bom.Componentes)
    {
      var pai = registros[c.Chave];
      var aproveitadas = new HashSet<ImportacaoDeEstruturaFilho>();
      foreach (var f in bom.Filhos.Where(f => f.PaiChave == c.Chave).OrderBy(f => f.Ordem))
      {
        var filho = registros[f.FilhoChave];
        // Registro novo ainda nao tem Id; so o mantido pode ter linha de antes.
        var existente = pai.Id == 0 || filho.Id == 0 ? null : pai.Filhos.FirstOrDefault(l => l.FilhoId == filho.Id);
        if (existente is null)
        {
          pai.Filhos.Add(new ImportacaoDeEstruturaFilho
          {
            Filho = filho, Ordem = f.Ordem, QuantidadeLida = f.Quantidade, Quantidade = f.Quantidade,
          });
          continue;
        }
        existente.Ordem = f.Ordem;
        existente.QuantidadeLida = f.Quantidade;
        existente.Quantidade = f.Quantidade;
        aproveitadas.Add(existente);
      }
      // As linhas acrescentadas agora ainda nao tem Id; so as de antes podem sobrar.
      filhosRemovidos.AddRange(pai.Filhos.Where(l => l.Id != 0 && !aproveitadas.Contains(l)));
    }

    var removidos = antigos.Except(mantidos.Values).ToList();
    foreach (var removido in removidos)
      filhosRemovidos.AddRange(removido.Filhos);
    return new PlanoDoReimport(removidos, filhosRemovidos.Distinct().ToList());
  }

  /// <summary>
  /// Codigo do filho -> quantidade lida, ou nulo quando algum filho nao tem codigo: sem codigo nao ha como
  /// dizer que e "o mesmo" filho, entao a receita nunca e igual a nenhuma outra.
  /// </summary>
  private static Dictionary<string, decimal>? ReceitaLida(
      ImportacaoDeEstruturaComponente registro, Dictionary<int, ImportacaoDeEstruturaComponente> porId)
  {
    var receita = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    foreach (var linha in registro.Filhos)
    {
      var codigo = porId[linha.FilhoId].CodigoLido?.Trim();
      if (string.IsNullOrEmpty(codigo))
        return null;
      receita[codigo] = receita.GetValueOrDefault(codigo) + linha.QuantidadeLida;
    }
    return receita;
  }

  private static Dictionary<int, Dictionary<string, decimal>?> ReceitasDoBom(BomMontado bom)
  {
    var codigoDaChave = bom.Componentes.ToDictionary(c => c.Chave, c => c.Codigo?.Trim());
    var receitas = bom.Componentes.ToDictionary(c => c.Chave, _ => (Dictionary<string, decimal>?)new(StringComparer.OrdinalIgnoreCase));
    foreach (var f in bom.Filhos)
    {
      var codigo = codigoDaChave[f.FilhoChave];
      if (string.IsNullOrEmpty(codigo))
        receitas[f.PaiChave] = null;
      else if (receitas[f.PaiChave] is { } receita)
        receita[codigo] = receita.GetValueOrDefault(codigo) + f.Quantidade;
    }
    return receitas;
  }

  private static bool MesmaReceita(Dictionary<string, decimal>? antiga, Dictionary<string, decimal>? nova) =>
      antiga is not null && nova is not null
      && antiga.Count == nova.Count
      && antiga.All(a => nova.TryGetValue(a.Key, out var q) && q == a.Value);
}
