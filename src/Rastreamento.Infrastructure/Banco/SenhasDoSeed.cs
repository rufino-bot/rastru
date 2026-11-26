namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Os sete pares usuário/senha públicos do <c>db/seed.sql</c>. Servem para duas coisas: recusar quem tente
/// definir a senha do repositório num banco de verdade e conferir qual usuário ainda a tem. Os pares já estão
/// no repositório, então listá-los aqui não expõe nada; a lista apodrece se o seed mudar, e por isso o teste
/// <c>A_lista_do_seed_bate_com_o_seed</c> a compara com um banco recém-semeado.
/// </summary>
public static class SenhasDoSeed
{
  public static IReadOnlyList<(string Usuario, string Senha)> Todas { get; } =
  [
    ("admin", "Admin@123"),
    ("pcp", "Pcp@123"),
    ("operador", "Operador@123"),
    ("movimentador", "Movimentador@123"),
    ("separador", "Separador@123"),
    ("qualidade", "Qualidade@123"),
    ("gestao", "Gestao@123"),
  ];
}
