namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// A ultima linha de `dbo.Embalagem` da Peca (regra 40), com a resposta do terceiro criterio do "some sozinho"
/// ja calculada no banco: `VoltouAProducaoDepois` e verdadeiro quando alguma linha do livro, posterior a esta, tem
/// destino numa das sete posicoes de producao de C18.
/// </summary>
public sealed record UltimaEmbalagem(int EstruturaItemId, string Tipo, DateTime DataHora, int UsuarioId, bool VoltouAProducaoDepois);
