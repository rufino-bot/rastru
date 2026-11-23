namespace BuscaPorFoto.Relatorio;

public enum Veredito { Aprova, ZonaCinza, Reprova }

/// <summary>
/// O critério da spec, fixado antes da medição: com 20 alvos, aprova com ≥ 16 no top-3, reprova com ≤ 11.
/// Com outro total, aprova com ≥ 80% e reprova abaixo de 60%, os dois limites arredondados para cima.
/// </summary>
public static class CriterioDeDecisao
{
    /// <summary>⌈0,8·n⌉ em aritmética inteira: em double, 0,8·20 não é garantidamente 16.</summary>
    public static int MinimoParaAprovar(int totalDeAlvos) => (4 * Validar(totalDeAlvos) + 4) / 5;

    /// <summary>⌈0,6·n⌉ em aritmética inteira.</summary>
    public static int MinimoParaNaoReprovar(int totalDeAlvos) => (3 * Validar(totalDeAlvos) + 4) / 5;

    public static Veredito Avaliar(int acertosNoTop3, int totalDeAlvos)
    {
        if (acertosNoTop3 < 0 || acertosNoTop3 > totalDeAlvos)
            throw new ArgumentOutOfRangeException(nameof(acertosNoTop3));
        if (acertosNoTop3 >= MinimoParaAprovar(totalDeAlvos)) return Veredito.Aprova;
        if (acertosNoTop3 >= MinimoParaNaoReprovar(totalDeAlvos)) return Veredito.ZonaCinza;
        return Veredito.Reprova;
    }

    private static int Validar(int total) =>
        total > 0 ? total : throw new ArgumentOutOfRangeException(nameof(total), "Sem alvo não há decisão.");
}
