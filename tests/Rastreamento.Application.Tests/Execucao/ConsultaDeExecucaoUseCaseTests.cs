using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>As leituras da Fase 3 (spec secoes 5.2, 6 e 7), no formato do "Contrato JSON" do plano 2.</summary>
public class ConsultaDeExecucaoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>O caso da spec do Kit: P de 10 (Solda); C de 10 (razao 1) e D de 45 (razao 4), no Corte.</summary>
  private static CenarioDeExecucao Kit()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.No(3, 1, 45m, 4m, Corte);
    return c;
  }

  [Fact]
  public async Task A_iniciar_aparece_so_no_Setor_do_primeiro_passo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var corte = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;
    var dobra = (await c.Consulta().Fila(Dobra, ComoOperador, Ct)).Valor!;

    var linha = Assert.Single(corte.AIniciar);
    Assert.Equal((1, 1, 10m), (linha.No.Id, linha.Ordem, linha.Quantidade));
    Assert.Equal("Corte", corte.SetorNome);
    Assert.Empty(dobra.AIniciar);
  }

  [Fact]
  public async Task A_fila_marca_a_pausa_e_poe_os_pausados_no_fim_do_a_iniciar()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.Agrupamentos[2] = ("AG-02", 2, "PED-02");
    c.Execucao.StatusDoPedido[2] = "Aberto";
    c.No(1, null, 10m, null, Corte);                        // Pedido 1 (sera pausado)
    c.Estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 2, Descricao = "No 2", NivelHierarquico = "Peca", Quantidade = 5m,
    });
    c.Estruturas.Roteiros.Add(new EstruturaRoteiro { Id = 201, EstruturaItemId = 2, SetorId = Corte, Ordem = 1 });
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto("PED-02 urgente"), Pcp, Ct);

    var aIniciar = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.AIniciar;

    Assert.Equal(new[] { 2, 1 }, aIniciar.Select(l => l.No.Id).ToArray());
    Assert.Null(aIniciar[0].No.Pausa);
    Assert.Equal(("PED-02 urgente", "PCP"), (aIniciar[1].No.Pausa!.Motivo, aIniciar[1].No.Pausa!.PorUsuarioNome));
  }

  [Fact]
  public async Task Em_trabalho_por_passo_e_o_resto_continua_a_iniciar()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 4m);

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Equal(6m, Assert.Single(fila.AIniciar).Quantidade);
    var emTrabalho = Assert.Single(fila.EmTrabalho);
    Assert.Equal((1, 4m), (emTrabalho.Ordem, emTrabalho.Quantidade));
  }

  [Fact]
  public async Task Aguardando_coleta_mostra_a_tarefa_com_destino_e_a_sobra_a_parte()
  {
    var c = Kit();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 45m);

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    var coleta = Assert.Single(fila.AguardandoColeta);
    Assert.Equal((3, 40m), (coleta.No.Id, coleta.Quantidade));
    Assert.Equal("Montagem", coleta.Destino.Tipo);
    Assert.Equal(1, coleta.Destino.PaiId);
    Assert.Equal((Solda, "Solda"), (coleta.Destino.SetorId, coleta.Destino.SetorNome));
    var sobra = Assert.Single(fila.Sobra);
    Assert.Equal((3, "UltimoPasso", (int?)1, 5m), (sobra.No.Id, sobra.Origem, sobra.Ordem, sobra.Quantidade));
  }

  [Fact]
  public async Task Aguardando_montagem_agrupa_por_pai_com_da_para_montar_e_falta()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null, Solda);
    c.No(2, 1, 10m, 2m, Corte);
    c.No(3, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 1m);

    var fila = (await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!;

    var grupo = Assert.Single(fila.AguardandoMontagem);
    Assert.Equal((1, 5m, 1m), (grupo.Pai.Id, grupo.FaltaMontar, grupo.DaParaMontar));
    Assert.Equal(
        new[] { (2, 2m, 5m, (decimal?)4m, (decimal?)0m), (3, 1m, 1m, (decimal?)2m, (decimal?)1m) },
        grupo.Filhos.Select(f => (f.No.Id, f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToArray());
  }

  [Fact]
  public async Task A_iniciar_nao_lista_no_com_filhos()
  {
    var c = Kit();   // pai 1 na Solda, filhos no Corte

    var solda = (await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!;
    var corte = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Empty(solda.AIniciar);
    Assert.Equal(new[] { 2, 3 }, corte.AIniciar.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task Aguardando_montagem_diz_se_o_pai_inicia_aqui_e_qual_e_o_primeiro_passo_dele()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null, Solda, Pintura);
    c.No(2, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 1m);

    var naSolda = Assert.Single((await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.AguardandoMontagem);
    var naPintura = Assert.Single((await c.Consulta().Fila(Pintura, ComoOperador, Ct)).Valor!.AguardandoMontagem);

    Assert.True(naSolda.IniciaAqui);
    Assert.False(naPintura.IniciaAqui);
    Assert.Equal(new SetorResumoDto(Solda, "Solda"), naPintura.PrimeiroPassoDoPai);
  }

  [Fact]
  public async Task Pai_sem_Roteiro_nao_inicia_em_lugar_nenhum()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null);
    c.No(2, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);

    var grupo = Assert.Single((await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.AguardandoMontagem);

    Assert.False(grupo.IniciaAqui);
    Assert.Null(grupo.PrimeiroPassoDoPai);
  }

  [Fact]
  public async Task Excesso_em_montagem_aparece_na_sobra_e_diz_quando_o_filho_esta_em_mais_de_um_Setor()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(3, 1, 45m, 4m, Corte);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 30m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 15m);

    var fila = (await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!;

    var sobra = Assert.Single(fila.Sobra);
    Assert.Equal(("Montagem", (int?)null, 5m, true), (sobra.Origem, sobra.Ordem, sobra.Quantidade, sobra.EmMaisDeUmSetor));
  }

  [Fact]
  public async Task Resumo_do_no_traz_o_caminho_Pedido_Agrupamento_pai()
  {
    var c = Kit();

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    var no = fila.AIniciar.Single(l => l.No.Id == 3).No;
    Assert.Equal(("No 3", "PED-01", "AG-01", (int?)1, "No 1"), (no.Descricao, no.PedidoNumero, no.AgrupamentoCodigo, no.PaiId, no.PaiDescricao));
  }

  [Fact]
  public async Task Fila_traz_o_cliente_do_Pedido_e_os_materiais_do_no_ordenados_por_codigo()
  {
    var c = Kit();
    c.Material(3, 61, "CH-600", "Chapa 6,00 mm");
    c.Material(3, 30, "CH-300", "Chapa 3,00 mm");

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    var no = fila.AIniciar.Single(l => l.No.Id == 3).No;
    Assert.Equal("Cliente do cenário", no.PedidoCliente);
    Assert.Equal(
        new[] { new MaterialResumoDto(30, "CH-300", "Chapa 3,00 mm"), new MaterialResumoDto(61, "CH-600", "Chapa 6,00 mm") },
        no.Materiais.ToArray());
  }

  [Fact]
  public async Task No_sem_material_traz_lista_vazia_e_nao_nula()
  {
    var c = Kit();
    c.Material(3, 30, "CH-300", "Chapa 3,00 mm");

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    var linha = fila.AIniciar.Single(l => l.No.Id == 2);
    Assert.Empty(linha.No.Materiais);
  }

  [Fact]
  public async Task Tarefas_trazem_os_materiais_do_no()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Material(1, 30, "CH-300", "Chapa 3,00 mm");
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);

    var tarefa = Assert.Single(Assert.Single((await c.Consulta().Tarefas(Ct)).Valor!.Grupos).Itens);

    Assert.Equal(new[] { new MaterialResumoDto(30, "CH-300", "Chapa 3,00 mm") }, tarefa.No.Materiais.ToArray());
    Assert.Equal("Cliente do cenário", tarefa.No.PedidoCliente);
  }

  [Fact]
  public async Task Cartao_de_montagem_traz_os_materiais_do_pai_e_de_cada_filho()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null, Solda);
    c.No(2, 1, 10m, 2m, Corte);
    c.No(3, 1, 5m, 1m, Corte);
    c.Material(1, 10, "PAI-1", "Material do pai");
    c.Material(2, 20, "FIL-2", "Material do filho 2");
    c.Material(3, 30, "FIL-3", "Material do filho 3");
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 1m);

    var grupo = Assert.Single((await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.AguardandoMontagem);

    Assert.Equal(new[] { 10 }, grupo.Pai.Materiais.Select(m => m.Id).ToArray());
    Assert.Equal(
        new[] { (2, 20), (3, 30) },
        grupo.Filhos.Select(f => (f.No.Id, Assert.Single(f.No.Materiais).Id)).ToArray());
  }

  [Fact]
  public async Task Tarefas_agrupam_por_Setor_de_origem_e_a_contagem_bate()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Pintura);
    c.No(2, null, 10m, null, Dobra, Pintura);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Dobra, 1), Local.AguardandoColeta(Dobra, 1), 3m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Dobra, 2), Local.AguardandoColeta(Dobra, 2), 2m);

    var tarefas = (await c.Consulta().Tarefas(Ct)).Valor!.Grupos;
    var contagem = (await c.Consulta().ContagemDeTarefas(Ct)).Valor!;

    Assert.Equal(new[] { Corte, Dobra }, tarefas.Select(t => t.SetorId).ToArray());
    // Na Dobra: o no 1 no passo 2 e o no 2 no passo 1, por Id do no.
    Assert.Equal(new[] { (1, 2), (2, 1) }, tarefas[1].Itens.Select(i => (i.No.Id, i.Ordem)).ToArray());
    Assert.Equal(3, contagem.Total);
  }

  [Fact]
  public async Task Item_pronto_de_pai_sem_Roteiro_aparece_com_paiSemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var tarefa = Assert.Single(Assert.Single((await c.Consulta().Tarefas(Ct)).Valor!.Grupos).Itens);

    Assert.True(tarefa.Destino.PaiSemRoteiro);
    Assert.Null(tarefa.Destino.SetorId);
  }

  [Fact]
  public async Task Pedido_concluido_sai_da_fila_e_das_tarefas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Empty(fila.AIniciar);
    Assert.Empty(fila.AguardandoColeta);
    Assert.Empty((await c.Consulta().Tarefas(Ct)).Valor!.Grupos);
    Assert.Equal(0, (await c.Consulta().ContagemDeTarefas(Ct)).Valor!.Total);
  }

  [Fact]
  public async Task A_fila_traz_a_atividade_do_Setor_ou_nula()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal("solda", (await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.SetorAtividade);
    Assert.Null((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.SetorAtividade);
  }

  [Fact]
  public async Task Em_trabalho_traz_os_inicios_do_operador_do_mais_recente_ao_mais_antigo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var primeiro = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Operador, Ct)).Valor!;
    var segundo = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 3m), Operador, Ct)).Valor!;

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.Equal(new[] { (segundo.Id, 3m), (primeiro.Id, 2m) }, linha.Estornaveis.Select(e => (e.Id, e.Quantidade)).ToArray());
    Assert.All(linha.Estornaveis, e => Assert.Equal((TiposDeMovimentacao.Inicio, "Operador do Corte"), (e.Tipo, e.UsuarioNome)));
  }

  [Fact]
  public async Task O_que_nao_cabe_mais_no_saldo_nao_e_estornavel()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 5m), Operador, Ct);
    await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 4m), Operador, Ct);   // sobra 1 em trabalho

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.Empty(linha.Estornaveis);   // o Inicio de 5 nao cabe no 1 que restou
  }

  [Fact]
  public async Task Registro_alheio_so_aparece_para_PCP()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Movimentador, Ct);   // autor que nao e quem le

    Assert.Empty(Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho).Estornaveis);
    Assert.Single(Assert.Single((await c.Consulta().Fila(Corte, ComoPcp, Ct)).Valor!.EmTrabalho).Estornaveis);
  }

  [Fact]
  public async Task Aguardando_coleta_traz_os_terminos_e_a_sobra_do_mesmo_passo_nao_os_repete()
  {
    var c = Kit();   // 3 (razao 4) de 45 sob pai de 10: 40 e tarefa, 5 e sobra
    await c.Apontamento().Iniciar(3, new InicioDto(Corte, 45m), Operador, Ct);
    var termino = (await c.Apontamento().Terminar(3, new TerminoDto(Corte, 1, 45m), Operador, Ct)).Valor!;

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Equal(termino.Id, Assert.Single(Assert.Single(fila.AguardandoColeta).Estornaveis).Id);
    Assert.Empty(Assert.Single(fila.Sobra).Estornaveis);
  }

  [Fact]
  public async Task Sobra_sem_tarefa_traz_os_terminos()
  {
    var c = Kit();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 10m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });   // o pai ja nao precisa de nada: tudo o que o 3 terminar e sobra
    await c.Apontamento().Iniciar(3, new InicioDto(Corte, 5m), Operador, Ct);
    var termino = (await c.Apontamento().Terminar(3, new TerminoDto(Corte, 1, 5m), Operador, Ct)).Valor!;

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Empty(fila.AguardandoColeta);
    Assert.Equal(termino.Id, Assert.Single(Assert.Single(fila.Sobra).Estornaveis).Id);
  }

  [Fact]
  public async Task Em_trabalho_do_pai_traz_as_montagens_e_nao_o_inicio_avulso()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    var linha = Assert.Single((await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.EmTrabalho);

    var estornavel = Assert.Single(linha.Estornaveis);
    Assert.Equal(("Montagem", c.Execucao.Montagens.Single().Id, 3m), (estornavel.Tipo, estornavel.Id, estornavel.Quantidade));
  }

  [Fact]
  public async Task Estornado_nao_volta_a_aparecer()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var inicio = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Operador, Ct)).Valor!;
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 3m), Operador, Ct);
    await c.Estorno().EstornarMovimentacao(inicio.Id, Operador, false, Ct);

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.DoesNotContain(linha.Estornaveis, e => e.Id == inicio.Id);
  }

  [Fact]
  public async Task Posicoes_trazem_os_saldos_e_o_total_montado_so_de_quem_tem_filhos()
  {
    var c = Kit();
    c.Mover(3, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);

    var posicoes = (await c.Consulta().Posicoes(AgrupamentoId, Ct)).Valor!;

    Assert.Equal(new[] { 1, 2, 3 }, posicoes.Select(p => p.EstruturaItemId).ToArray());
    Assert.Equal(0m, posicoes[0].TotalMontado);
    Assert.Null(posicoes[2].TotalMontado);
    Assert.Equal(
        new[] { new SaldoDto(Posicoes.AIniciar, null, null, null, 40m), new SaldoDto(Posicoes.NoSetor, Corte, "Corte", 1, 5m) },
        posicoes[2].Saldos);
  }

  [Fact]
  public async Task Livro_do_no_traz_os_movimentos_com_a_marca_de_estornado_e_as_montagens_do_pai()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    var inicio = (await c.Apontamento().Iniciar(1, new InicioDto(Solda, 2m), Operador, Ct)).Valor!;
    await c.Estorno().EstornarMontagem(inicio.MontagemId!.Value, Operador, false, Ct);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);
    await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct);

    var livro = (await c.Consulta().LivroDoNo(1, Ct)).Valor!;

    Assert.Equal(
        new[] { (TiposDeMovimentacao.Inicio, true), (TiposDeMovimentacao.Estorno, false), (TiposDeMovimentacao.Inicio, false) },
        livro.Movimentacoes.Select(m => (m.Tipo, m.Estornada)).ToArray());
    Assert.Equal(new[] { true, false }, livro.Montagens.Select(m => m.Estornada).ToArray());
  }

  [Fact]
  public async Task Setor_Agrupamento_ou_no_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().Fila(77, ComoOperador, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().Posicoes(77, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().LivroDoNo(77, Ct)).TipoDoErro);
  }
  /// <summary>
  /// Fix round 4 da Task 11: uma leitura escolhida vitima de deadlock (esgotado o retry de
  /// `IExecucaoRepository.LerAsync`) devolve o mesmo 409 `ConflitoDeConcorrencia` das escritas, em
  /// vez de deixar a excecao subir crua (HTTP 500 medido na suite). Um metodo que NAO passasse por
  /// `LerAsync` ignoraria a chave do fake e devolveria sucesso — e o teste morre.
  /// </summary>
  [Theory]
  [InlineData("Fila")]
  [InlineData("Tarefas")]
  [InlineData("ContagemDeTarefas")]
  [InlineData("Posicoes")]
  [InlineData("LivroDoNo")]
  [InlineData("Roteiro.Obter")]
  public async Task Conflito_na_leitura_vira_ConflitoDeConcorrencia(string leitura)
  {
    var c = Kit();
    c.Execucao.ConflitoNaProximaLeitura = true;

    var (sucesso, erro, tipo, detalhe) = leitura switch
    {
      "Fila" => Desmontar(await c.Consulta().Fila(Corte, ComoOperador, Ct)),
      "Tarefas" => Desmontar(await c.Consulta().Tarefas(Ct)),
      "ContagemDeTarefas" => Desmontar(await c.Consulta().ContagemDeTarefas(Ct)),
      "Posicoes" => Desmontar(await c.Consulta().Posicoes(AgrupamentoId, Ct)),
      "LivroDoNo" => Desmontar(await c.Consulta().LivroDoNo(1, Ct)),
      "Roteiro.Obter" => Desmontar(await c.Roteiro().Obter(1, Ct)),
      _ => throw new ArgumentOutOfRangeException(nameof(leitura)),
    };

    Assert.False(sucesso);
    Assert.Equal((CodigosDaExecucao.ConflitoDeConcorrencia, (TipoDeErro?)TipoDeErro.Conflito), (erro, tipo));
    Assert.Equal(CodigosDaExecucao.MensagemDeConflito, detalhe);
    Assert.False(c.Execucao.ConflitoNaProximaLeitura);
  }

  private static (bool, string?, TipoDeErro?, string?) Desmontar<T>(Result<T> r) =>
      (r.Sucesso, r.Erro, r.TipoDoErro, r.Detalhe);
}
