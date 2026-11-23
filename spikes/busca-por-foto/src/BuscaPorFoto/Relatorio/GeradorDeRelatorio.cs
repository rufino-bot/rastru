using System.Globalization;
using System.Text;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;

namespace BuscaPorFoto.Relatorio;

public static class GeradorDeRelatorio
{
    /// <summary>Acima desta fração de recortes falhos numa execução Otsu, a regra manda reexecutar com GrabCut.</summary>
    public const double FracaoDeRecorteFalhoQueAcionaGrabCut = 1.0 / 3;

    /// <summary>A falha que o executor grava quando o alvo do Corte não saiu separado do nesting. Não entra no denominador.</summary>
    private const string FalhaDeReferenciaNaoSeparada = "referência não separada do nesting";
    private const string FalhaDeRecorteVazio = "recorte vazio";

    private static readonly Condicao[] Condicoes = [Condicao.SemMarcador, Condicao.ComMarcador];

    private sealed record Decisao(
        IReadOnlyList<string> NoDenominador,
        IReadOnlyList<string> NaoSeparados,
        IReadOnlyDictionary<Condicao, int> Acertos,
        Condicao Melhor,
        Veredito? Veredito);

    /// <summary>
    /// O veredito do cenário, ou nulo quando não há (intermediárias, ou nenhum alvo no denominador).
    /// A regra é a da spec §8: só IoU, todos os alvos do cenário de qualquer tipo, top-3, melhor das duas
    /// condições; nenhuma outra informação (pilhas menores, Hu, recorte) entra aqui.
    /// </summary>
    public static Veredito? VereditoDoCenario(Cenario cenario, IReadOnlyList<LinhaDeResultado> linhas) =>
        Decidir(cenario, linhas).Veredito;

    private static Decisao Decidir(Cenario cenario, IReadOnlyList<LinhaDeResultado> linhas)
    {
        var ids = linhas.Select(l => l.Id).Distinct().Order(StringComparer.Ordinal).ToList();
        var naoSeparados = ids
            .Where(id => linhas.Any(l => l.Id == id && l.Falha == FalhaDeReferenciaNaoSeparada))
            .ToList();
        var denominador = ids.Except(naoSeparados).ToList();

        var acertos = Condicoes.ToDictionary(
            c => c,
            c => linhas.Count(l => denominador.Contains(l.Id) && l.Condicao == c
                                   && l.Pontuador == NomeDoPontuador.IoU && l.Posicao is <= 3));
        // Empate entre as condições vai para "sem marcador": é a que não pede nada à fábrica.
        var melhor = acertos[Condicao.ComMarcador] > acertos[Condicao.SemMarcador] ? Condicao.ComMarcador : Condicao.SemMarcador;

        Veredito? veredito = cenario == Cenario.Intermediarias || denominador.Count == 0
            ? null
            : CriterioDeDecisao.Avaliar(acertos[melhor], denominador.Count);
        return new Decisao(denominador, naoSeparados, acertos, melhor, veredito);
    }

    public static string Gerar(
        Cenario cenario,
        IReadOnlyList<LinhaDeResultado> linhas,
        IReadOnlyList<LinhaDePontuacao> pontuacoes,
        IReadOnlyList<ClassificacaoDoRecorte> classificacao,
        MetodoDeRecorte metodo)
    {
        var s = new StringBuilder();
        void L(string texto = "") => s.Append(texto).Append('\n');

        var ids = linhas.Select(l => l.Id).Distinct().Order(StringComparer.Ordinal).ToList();
        var recorte = classificacao.ToDictionary(c => (c.Id, c.Condicao));
        var decide = cenario != Cenario.Intermediarias;
        var decisao = Decidir(cenario, linhas);

        L($"# Spike — busca de peça por foto: {cenario.NomeDaPasta()}");
        L();
        L($"Recorte: **{metodo}**. Alvos: **{ids.Count}**.");
        L("Posição é contada com empate contra a peça certa. Consulta que falhou (marcador não detectado, recorte vazio, foto ilegível) conta como fora de qualquer top.");
        L();

        if (decide)
        {
            L("## Acertos por tipo, condição e pontuador");
            L();
            L("| Tipo | Condição | Pontuador | Top-1 | Top-3 | Top-5 | Falhas | ms médio |");
            L("|---|---|---|---|---|---|---|---|");
            foreach (var tipo in linhas.Select(l => l.Tipo).Distinct().OrderBy(t => t))
                foreach (var condicao in Condicoes)
                    foreach (var pontuador in new[] { NomeDoPontuador.IoU, NomeDoPontuador.Hu })
                    {
                        var grupo = linhas.Where(l => l.Tipo == tipo && l.Condicao == condicao && l.Pontuador == pontuador).ToList();
                        if (grupo.Count == 0) continue;
                        var consultadas = grupo.Where(l => l.Posicao is not null).ToList();
                        var ms = consultadas.Count == 0 ? "—" : consultadas.Average(l => l.Milissegundos).ToString("F0", CultureInfo.InvariantCulture);
                        L($"| {tipo?.ToString() ?? "—"} | {condicao} | {pontuador} | {Top(grupo, 1)} | {Top(grupo, 3)} | {Top(grupo, 5)} | {grupo.Count(l => l.Posicao is null)} | {ms} |");
                    }
            L();

            L("## Decisão (critério fixado na spec antes da medição)");
            L();
            if (decisao.Veredito is not { } veredito)
            {
                L("Nenhum alvo no denominador: não há decisão.");
            }
            else
            {
                var n = decisao.NoDenominador.Count;
                L($"Pontuador da decisão: **IoU** (Hu é só linha de base). Denominador: **{n}** alvos, de qualquer tipo.");
                L($"Limites para {n}: aprova com ≥ {CriterioDeDecisao.MinimoParaAprovar(n)}, reprova com ≤ {CriterioDeDecisao.MinimoParaNaoReprovar(n) - 1}.");
                L();
                L($"- Sem marcador: **{decisao.Acertos[Condicao.SemMarcador]} de {n}** no top-3");
                L($"- Com marcador: **{decisao.Acertos[Condicao.ComMarcador]} de {n}** no top-3");
                L($"- Melhor condição: **{decisao.Melhor}**");
                L();
                L($"**Veredito: {veredito}.**");
            }
            L();

            if (decisao.NaoSeparados.Count > 0)
            {
                L("## Fora do denominador: referência não separada do nesting");
                L();
                foreach (var id in decisao.NaoSeparados) L($"- {id}");
                L();
            }
        }

        // Recorte e plano B.
        var naoClassificadas = ids.SelectMany(id => Condicoes.Select(c => (id, c)))
            .Count(k => !recorte.TryGetValue(k, out var r) || r.RecorteOk is null);
        L("## Recorte");
        L();
        if (naoClassificadas > 0)
        {
            L($"**Classificação incompleta: {naoClassificadas} consulta(s) sem ok/falho.** A regra do GrabCut e o gatilho do plano B não são avaliados com classificação incompleta.");
        }
        else
        {
            var falhos = classificacao.Count(c => c.RecorteOk == false);
            var total = classificacao.Count;
            L($"Recortes falhos: **{falhos} de {total}**.");
            if (metodo == MetodoDeRecorte.Otsu)
            {
                var aciona = falhos > FracaoDeRecorteFalhoQueAcionaGrabCut * total;
                L(aciona
                    ? "**Regra do recorte: mais de 1/3 falho — reexecute com `--recorte grabcut` numa pasta de saída nova e decida por aquela execução.**"
                    : "Regra do recorte: até 1/3 falho — esta execução Otsu é a que decide.");
            }

            // O plano B só existe na zona cinza: aprovar não precisa dele, e reprovar é reprovar.
            if (decisao.Veredito == Veredito.ZonaCinza)
            {
                var erros = decisao.NoDenominador
                    .SelectMany(id => linhas.Where(l => l.Id == id && l.Condicao == decisao.Melhor
                                                        && l.Pontuador == NomeDoPontuador.IoU && l.Posicao is not <= 3))
                    .ToList();
                var deRecorte = erros.Count(l => l.Falha == FalhaDeRecorteVazio || recorte[(l.Id, l.Condicao)].RecorteOk == false);
                L();
                L($"Erros na melhor condição: **{erros.Count}**, dos quais **{deRecorte}** de recorte.");
                L(deRecorte * 2 > erros.Count
                    ? "**Gatilho do plano B: a maioria dos erros é de recorte.** Acionar é decisão do usuário, com spec própria."
                    : "Plano B **não** se aplica: a maioria dos erros não é de recorte.");
            }
        }
        L();

        if (!decide)
        {
            L("Só informativo (spec §3): 5 fotos não medem taxa de acerto.");
            L();
        }

        L("## Por foto (IoU)");
        L();
        L("| Id | Condição | Posição | Pontuação da certa | Melhor errada | Referências no grupo da certa |");
        L("|---|---|---|---|---|---|");
        foreach (var l in linhas.Where(l => l.Pontuador == NomeDoPontuador.IoU)
                     .OrderBy(l => l.Id, StringComparer.Ordinal).ThenBy(l => l.Condicao))
            L($"| {l.Id} | {l.Condicao} | {l.Posicao?.ToString(CultureInfo.InvariantCulture) ?? l.Falha ?? "—"} | {Numero(l.PontuacaoDaCerta)} | {Numero(l.MelhorErrada)} | {l.ReferenciasNoGrupoDaCerta} |");
        L();

        var comGrupo = linhas.Where(l => l.Pontuador == NomeDoPontuador.IoU && l.ReferenciasNoGrupoDaCerta > 1)
            .GroupBy(l => l.Id).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (comGrupo.Count > 0)
        {
            L("## Grupos com mais de uma referência");
            L();
            L("Cópias do mesmo contorno, ou peças de geometria idêntica, inclusive espelhadas: a foto não as separa.");
            L();
            foreach (var g in comGrupo)
                L($"- {g.Key}: {g.Max(l => l.ReferenciasNoGrupoDaCerta)} referências no grupo da certa");
            L();
        }

        if (decide && pontuacoes.Count > 0)
        {
            L("## Pilhas menores (informativo: não entra na decisão)");
            L();
            var grupos = pontuacoes.Where(p => p.Condicao == decisao.Melhor).GroupBy(p => p.Id).Select(g => g.Count()).DefaultIfEmpty(0).Max();
            var tamanhos = PilhasMenores.Tamanhos.Where(t => t < grupos).ToList();
            if (tamanhos.Count == 0)
            {
                L($"Nenhum tamanho de {string.Join(", ", PilhasMenores.Tamanhos)} é menor que o número de grupos ({grupos}).");
            }
            else
            {
                L($"Na melhor condição ({decisao.Melhor}), {PilhasMenores.Sorteios} sorteios por alvo, semente {PilhasMenores.Semente}: o certo mais (tamanho − 1) grupos errados.");
                L();
                L("| Grupos na pilha | Alvos | Acertos médios no top-3 |");
                L("|---|---|---|");
                foreach (var r in PilhasMenores.Simular(pontuacoes, decisao.Melhor, tamanhos, PilhasMenores.Sorteios, PilhasMenores.Semente))
                    L($"| {r.Tamanho} | {r.Alvos} | {Numero(r.AcertosMedios, 1)} |");
            }
            L();
        }

        return s.ToString();
    }

    private static string Top(IReadOnlyList<LinhaDeResultado> grupo, int n) =>
        $"{grupo.Count(l => l.Posicao is { } p && p <= n)} de {grupo.Count}";

    private static string Numero(double? valor, int casas = 3) =>
        valor is { } v ? v.ToString("F" + casas, CultureInfo.InvariantCulture).Replace('.', ',') : "—";
}
