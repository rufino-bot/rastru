namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// A seção <c>Banco</c> da configuração, lida pelo <c>banco preparar</c>. As senhas dos usuários do sistema
/// ficam na seção <c>Senhas</c> (chave = nome do usuário), nunca aqui. Tudo vem de variável de ambiente
/// (<c>Banco__Nome</c>, <c>Banco__SenhaDoLogin</c>…), nunca de argumento de linha de comando.
/// </summary>
public sealed class OpcoesDoBanco
{
  /// <summary>Conexão do <c>sa</c>, com o catálogo <c>master</c>: só serve para criar banco, migrar e garantir o login.</summary>
  public string ConexaoAdministrativa { get; set; } = "";

  /// <summary>Nome do banco do ambiente, por exemplo <c>Rastreamento_HML</c>.</summary>
  public string Nome { get; set; } = "";

  /// <summary>Login SQL do ambiente, dono só do próprio banco, por exemplo <c>rastru_hml</c>.</summary>
  public string Login { get; set; } = "";

  public string SenhaDoLogin { get; set; } = "";

  /// <summary>Se o banco novo leva o <c>seed-demo.sql</c> e a <c>massa-vitrine.sql</c>.</summary>
  public bool Vitrine { get; set; }

  /// <summary>Raiz dos scripts SQL; na imagem, <c>/app/sql</c>.</summary>
  public string RaizDosScripts { get; set; } = "/app/sql";
}
