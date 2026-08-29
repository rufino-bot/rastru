namespace Rastreamento.Application.Estrutura;

public sealed record NoPlanejado(
    int? ComponenteId,
    string? Descricao,
    decimal Quantidade,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoPlanejado> Filhos);

public sealed record ReceitaDoCatalogo(
    ILookup<int, (int FilhoId, decimal QuantidadePadrao)> Filhos,
    ILookup<int, (int MaterialId, decimal QuantidadePadrao)> Materiais,
    ILookup<int, (int SetorId, int Ordem)> Roteiro);

public sealed record PlanoDeCopia(NoPlanejado? Raiz, string? Erro, string? CodigoDoErro);

/// <summary>
/// Transforma a receita do catalogo na arvore que sera gravada. PURO: sem I/O, sem repositorio.
/// Quem le o grafo e o caso de uso; aqui so entra dado ja lido, e por isso todo caso de borda desta
/// fase e testavel sem fake nenhum.
///
/// A guarda de ciclo e POR CAMINHO, nao por "ja visto em qualquer lugar", e isso NAO e detalhe: no
/// `seed-demo` gravado ha um Componente pendurado em DOIS pais (medido em 2026-08-29). Uma guarda de
/// "ja visto" recusaria a receita MT-1000 que esta no banco. Diamante vale; ciclo nao. O teste que
/// morre se alguem trocar isso e `Diamante_e_aceito_o_mesmo_componente_sob_dois_pais` — nao o de
/// ciclo, que continuaria verde.
///
/// O teto NAO e regra de negocio, e por isso nao esta em `01-dominio-e-regras-de-negocio.md`: e
/// para-quedas contra transacao desgovernada. O numero nao saiu do `seed-demo` de proposito — o demo
/// tem 3 niveis e 15 nos, o que da PISO e nunca teto, e ele mede o demo, nao a fabrica. Fica alto o
/// bastante para nunca recusar estrutura plausivel; pode DESCER com dado real, porque subir um teto
/// que ja recusou trabalho de cliente e o erro caro.
/// </summary>
public static class PlanejadorDeCopia
{
  public const int ProfundidadeMaxima = 20;
  public const int NosMaximos = 500;

  public const string CodigoDeCiclo = "CicloNaReceita";
  public const string CodigoDeProfundidade = "EstruturaProfundaDemais";
  public const string CodigoDeTamanho = "EstruturaGrandeDemais";

  public static PlanoDeCopia Planejar(
      ReceitaDoCatalogo receita, int componenteRaizId, decimal quantidadeDaRaiz)
  {
    var caminho = new List<int>();
    var noCaminho = new HashSet<int>();
    var nos = 0;

    try
    {
      var raiz = Descer(receita, componenteRaizId, quantidadeDaRaiz, caminho, noCaminho, ref nos);
      return new PlanoDeCopia(raiz, null, null);
    }
    catch (CopiaRecusadaException e)
    {
      return new PlanoDeCopia(null, e.Message, e.Codigo);
    }
  }

  private static NoPlanejado Descer(
      ReceitaDoCatalogo receita,
      int componenteId,
      decimal quantidade,
      List<int> caminho,
      HashSet<int> noCaminho,
      ref int nos)
  {
    if (!noCaminho.Add(componenteId))
    {
      // O ciclo e o trecho do caminho que comeca no no reencontrado, fechado nele mesmo — e o que
      // diz ONDE consertar, descartando os ramos inocentes ja percorridos.
      var trecho = caminho.Skip(caminho.IndexOf(componenteId)).Append(componenteId);
      throw new CopiaRecusadaException(
          CodigoDeCiclo,
          $"A receita tem um ciclo: {string.Join(" -> ", trecho)}. "
              + "Corrija a receita do catalogo antes de criar a Peca.");
    }

    caminho.Add(componenteId);

    if (caminho.Count > ProfundidadeMaxima)
      throw new CopiaRecusadaException(
          CodigoDeProfundidade,
          $"A receita passa de {ProfundidadeMaxima} niveis de profundidade.");

    if (++nos > NosMaximos)
      throw new CopiaRecusadaException(
          CodigoDeTamanho, $"A receita gera mais de {NosMaximos} itens.");

    // `ref int nos` nao atravessa lambda (CS1628): um `.Select(...)` capturando `ref nos` nao
    // compila. `foreach` acumulando na lista evita isso sem trocar o contador por campo estatico —
    // que quebraria em silencio a seguranca para chamadas concorrentes que este metodo precisa ter.
    var filhos = new List<NoPlanejado>();
    foreach (var f in receita.Filhos[componenteId])
      filhos.Add(Descer(receita, f.FilhoId, quantidade * f.QuantidadePadrao, caminho, noCaminho, ref nos));

    caminho.RemoveAt(caminho.Count - 1);
    noCaminho.Remove(componenteId);

    return new NoPlanejado(
        ComponenteId: componenteId,
        Descricao: null,   // NULL herda a descricao do Componente (regra 19)
        Quantidade: quantidade,
        Materiais: receita.Materiais[componenteId]
            .Select(m => (m.MaterialId, quantidade * m.QuantidadePadrao)).ToList(),
        Roteiro: receita.Roteiro[componenteId]
            .OrderBy(r => r.Ordem).Select(r => (r.SetorId, r.Ordem)).ToList(),
        Filhos: filhos);
  }

  private sealed class CopiaRecusadaException(string codigo, string mensagem)
      : Exception(mensagem)
  {
    public string Codigo { get; } = codigo;
  }
}
