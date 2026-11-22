using System.Numerics;
using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Dxf;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Tests.Sinteticos;

namespace BuscaPorFoto.Tests.Comparacao;

public class AgrupadorTests
{
    private static Referencia Ref(string chave, int furos, params double[] valores) =>
        new(chave, [], new AssinaturaGeometrica(furos, valores));

    private static string[][] Grupos(params Referencia[] refs) =>
        AgrupadorDeReferencias.Agrupar(refs).Select(g => g.ToArray()).ToArray();

    [Fact]
    public void Copias_identicas_viram_um_grupo()
    {
        var grupos = Grupos(
            Ref("K0003", 1, 5000, 400, 300, 1e6, 2e5),
            Ref("K0001", 1, 5000 * (1 + 1e-4), 400, 300 * (1 - 1e-4), 1e6, 2e5 * (1 + 1e-4)),
            Ref("K0002", 1, 5000, 400 * (1 - 1e-4), 300, 1e6 * (1 + 1e-4), 2e5));

        Assert.Equal([["K0001", "K0002", "K0003"]], grupos);
    }

    [Fact]
    public void Diferenca_acima_da_tolerancia_separa()
    {
        var grupos = Grupos(
            Ref("K0001", 1, 5000, 400, 300, 1e6, 2e5),
            Ref("K0002", 1, 5050, 400, 300, 1e6, 2e5));

        Assert.Equal([["K0001"], ["K0002"]], grupos);
    }

    [Fact]
    public void Numero_de_furos_diferente_separa_mesmo_com_valores_iguais()
    {
        var grupos = Grupos(
            Ref("K0001", 1, 5000, 400, 300, 1e6, 2e5),
            Ref("K0002", 2, 5000, 400, 300, 1e6, 2e5));

        Assert.Equal(2, grupos.Length);
    }

    [Fact]
    public void Identidade_e_transitiva()
    {
        // A~B (0,40%) e B~C (0,40%), mas A e C diferem 0,79%, acima da tolerância.
        var a = Ref("K0001", 0, 100.0);
        var b = Ref("K0002", 0, 100.4);
        var c = Ref("K0003", 0, 100.8);
        Assert.False(AgrupadorDeReferencias.Identicas(a.Assinatura, c.Assinatura));

        Assert.Equal([["K0001", "K0002", "K0003"]], Grupos(c, a, b));
    }

    [Fact]
    public void Espelho_do_DXF_fica_no_mesmo_grupo()
    {
        var dxf = new DxfSintetico()
            .Bloco("ELE", b => b.Polilinha([(0, 0, 0), (120, 0, 0), (120, 25, 0), (30, 25, 0), (30, 70, 0), (0, 70, 0)], fechada: true))
            .Insert("ELE", 0, 0)
            .Insert("ELE", 400, 0, escalaX: -1);

        var pecas = SeparadorDeNesting.Separar(LeitorDeDxf.Ler(dxf.Gerar()), new HashSet<string>()).Pecas;

        Assert.Equal(2, pecas.Count);
        var refs = pecas.Select(p => new Referencia($"K{p.Numero:0000}", [p.Silhueta], p.Assinatura)).ToArray();
        Assert.Single(AgrupadorDeReferencias.Agrupar(refs));
    }

    [Fact]
    public void Malha_girada_tem_a_mesma_assinatura()
    {
        var malha = MalhaSintetica.ChapaComFuro(100, 50, 3, 20);
        var girada = MalhaSintetica.Transformar(malha, Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f));

        Assert.True(AgrupadorDeReferencias.Identicas(ProjetorDeSilhueta.Assinatura(malha), ProjetorDeSilhueta.Assinatura(girada)));
    }

    [Fact]
    public void Malha_proporcional_tem_assinatura_diferente()
    {
        var a = new Referencia("K0001", [], ProjetorDeSilhueta.Assinatura(MalhaSintetica.ChapaComFuro(100, 50, 3, 20)));
        var b = new Referencia("K0002", [], ProjetorDeSilhueta.Assinatura(MalhaSintetica.ChapaComFuro(104, 52, 3, 20.8f)));

        Assert.Equal(2, AgrupadorDeReferencias.Agrupar([a, b]).Count);
    }
}
