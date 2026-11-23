using System.Globalization;
using System.Text;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;

namespace BuscaPorFoto.Relatorio;

public static class GeradorDeRelatorio
{
    /// <summary>Acima desta fração de recortes falhos numa execução Otsu, a regra manda reexecutar com GrabCut.</summary>
    public const double FracaoDeRecorteFalhoQueAcionaGrabCut = 1.0 / 3;

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
            .Where(id => linhas.Any(l => l.Id == id && l.Falha == Executor.FalhaReferenciaNaoSeparada))
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
        MetodoDeRecorte metodo,
        IReadOnlyList<LinhaDeAvisoPorTipo>? avisosDoDxf = null)
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

            if (cenario == Cenario.Corte) AvisosDoDxf(L, avisosDoDxf);
        }

        // Recorte e plano B. Só as consultas que produziram sobreposição têm o que classificar: "marcador não
        // detectado", "foto ilegível" e "referência não separada" não entram em nenhuma conta do recorte (spec §8.3).
        var comSobreposicao = linhas
            .Where(l => l.Pontuador == NomeDoPontuador.IoU && Executor.TemSobreposicao(l.Falha))
            .Select(l => (l.Id, l.Condicao)).Distinct().ToList();
        // Recorte vazio é falho por definição: não há o que o usuário olhar, e a classificação dele, ok ou em
        // branco, não muda isso (a mesma conta do plano B, mais abaixo).
        var vazios = linhas
            .Where(l => l.Pontuador == NomeDoPontuador.IoU && l.Falha == Executor.FalhaRecorteVazio)
            .Select(l => (l.Id, l.Condicao)).ToHashSet();
        // Verdadeiro: ok; falso: falho; nulo: o usuário ainda não classificou.
        bool? Classe((string Id, Condicao Condicao) k) =>
            vazios.Contains(k) ? false : recorte.TryGetValue(k, out var r) ? r.RecorteOk : null;
        var naoClassificadas = comSobreposicao.Count(k => Classe(k) is null);
        L("## Recorte");
        L();
        if (naoClassificadas > 0)
        {
            L($"**Classificação incompleta: {naoClassificadas} consulta(s) sem ok/falho.** A regra do GrabCut e o gatilho do plano B não são avaliados com classificação incompleta.");
        }
        else
        {
            var falhos = comSobreposicao.Count(k => Classe(k) == false);
            var total = comSobreposicao.Count;
            L($"Recortes falhos: **{falhos} de {total}** consultas com sobreposição."
              + (vazios.Count > 0 ? $" Recorte vazio conta como falho, sem classificar ({vazios.Count} nesta execução)." : ""));
            if (decide) AcertosPorRecorte(L, linhas, comSobreposicao, Classe);
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
                // Erro de recorte: recorte vazio, ou foto recortada e classificada como falha.
                // Marcador não detectado e foto ilegível são erro, mas não de recorte.
                var deRecorte = erros.Count(l => l.Falha == Executor.FalhaRecorteVazio
                                                 || (l.Falha is null && recorte[(l.Id, l.Condicao)].RecorteOk == false));
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
            var grupos = pontuacoes.Where(p => p.Condicao == decisao.Melhor && p.Pontuador == NomeDoPontuador.IoU).GroupBy(p => p.Id).Select(g => g.Count()).DefaultIfEmpty(0).Max();
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

    /// <summary>
    /// O cruzamento "recorte: ok / falho" da spec §7: o top-3 do IoU, por condição, nas consultas que
    /// produziram sobreposição, separado pela classificação do usuário.
    /// </summary>
    private static void AcertosPorRecorte(
        Action<string> L, IReadOnlyList<LinhaDeResultado> linhas,
        IReadOnlyList<(string Id, Condicao Condicao)> comSobreposicao, Func<(string Id, Condicao Condicao), bool?> classe)
    {
        var iou = linhas.Where(l => l.Pontuador == NomeDoPontuador.IoU).ToDictionary(l => (l.Id, l.Condicao));
        L("");
        L("Top-3 (IoU) pelo recorte, nas consultas com sobreposição:");
        L("");
        L("| Condição | Recorte ok | Recorte falho |");
        L("|---|---|---|");
        foreach (var condicao in Condicoes)
        {
            string Celula(bool ok)
            {
                var consultas = comSobreposicao.Where(k => k.Condicao == condicao && classe(k) == ok).ToList();
                return $"{consultas.Count(k => iou[k].Posicao is <= 3)} de {consultas.Count}";
            }
            L($"| {condicao} | {Celula(true)} | {Celula(false)} |");
        }
        L("");
    }

    /// <summary>
    /// Os avisos da leitura do DXF por tipo e por nesting (spec §10.3): entidade não lida, contorno aberto, corte em
    /// linha comum. Só ids e tipos sem nome de bloco nem de camada; o texto inteiro fica na máquina do usuário.
    /// </summary>
    private static void AvisosDoDxf(Action<string> L, IReadOnlyList<LinhaDeAvisoPorTipo>? avisos)
    {
        L("## Avisos da leitura do DXF");
        L("");
        if (avisos is null)
            L($"Esta pasta não tem o {Executor.ArquivoDeAvisosPorTipoCsv}: a contagem de avisos não está disponível.");
        else if (avisos.Count == 0)
            L("Nenhum aviso.");
        else
        {
            L("| Nesting | Aviso | Quantos |");
            L("|---|---|---|");
            foreach (var a in avisos.OrderBy(a => a.Nesting, StringComparer.Ordinal).ThenBy(a => a.Tipo, StringComparer.Ordinal))
                L($"| {a.Nesting} | {a.Tipo} | {a.N} |");
        }
        L("");
    }

    private static string Top(IReadOnlyList<LinhaDeResultado> grupo, int n) =>
        $"{grupo.Count(l => l.Posicao is { } p && p <= n)} de {grupo.Count}";

    private static string Numero(double? valor, int casas = 3) =>
        valor is { } v ? v.ToString("F" + casas, CultureInfo.InvariantCulture).Replace('.', ',') : "—";
}
