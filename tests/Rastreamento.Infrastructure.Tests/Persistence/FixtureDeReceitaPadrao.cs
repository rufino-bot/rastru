using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Fixture das linhas de apoio (Componente, Material, Setor) que os testes de receita padrao
/// precisam existir no banco antes de gravar qualquer linha de receita — as tres tabelas so tem
/// FK, entao nada entra sem que o alvo exista.
///
/// Extraida na Task 2 pelo mesmo motivo que <see cref="TesteComBanco"/> nasceu na Fase 0: os
/// helpers estavam prestes a virar copia numa segunda classe. Fica so a CRIACAO aqui; a limpeza
/// mora em cada arquivo de teste porque ela depende do formato do teste (o mapeamento cria pares
/// fixos, o repositorio cria conjuntos de tamanho variavel) e um helper unico para as duas formas
/// seria mais generico que o problema.
/// </summary>
public abstract class FixtureDeReceitaPadrao : TesteComBanco
{
  /// <summary>
  /// Codigo unico por linha criada: <c>UQ_Componente_Codigo</c> e <c>UQ_Material_Codigo</c> sao
  /// reais, e o banco de dev e compartilhado entre execucoes.
  /// </summary>
  protected static string NovoCodigo() => $"RP-{Guid.NewGuid():N}"[..12];

  protected static async Task<Componente> UmComponente(RastreamentoDbContext db)
  {
    var componente = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente de teste",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    return componente;
  }

  protected static async Task<(Componente pai, Componente filho)> DoisComponentes(
      RastreamentoDbContext db)
  {
    var pai = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente pai de teste",
      Tipo = "Montagem",
      Ativo = true,
    };
    var filho = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente filho de teste",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.AddRange(pai, filho);
    await db.SaveChangesAsync();
    return (pai, filho);
  }

  protected static async Task<Material> UmMaterial(RastreamentoDbContext db)
  {
    var material = new Material
    {
      Codigo = NovoCodigo(),
      Descricao = "Material de teste",
      UnidadeMedida = "KG",
      Ativo = true,
    };
    db.Materiais.Add(material);
    await db.SaveChangesAsync();
    return material;
  }

  protected static async Task<Setor> UmSetor(RastreamentoDbContext db)
  {
    var setor = new Setor { Nome = $"setor-{Guid.NewGuid():N}", Ativo = true };
    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    return setor;
  }
}
