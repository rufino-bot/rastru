using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Componente, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa o que criou — UQ_Componente_Codigo nao perdoa sobra de execucao anterior.
/// </summary>
public class ComponentesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _prefixosCriados = [];

  public ComponentesEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    foreach (var prefixo in _prefixosCriados)
      db.Componentes.RemoveRange(
          await db.Componentes.Where(c => c.Codigo.StartsWith(prefixo)).ToListAsync());
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// Prefixo unico por chamada. As consultas de listagem filtram por ele (`?busca=`), entao as
  /// assercoes de paginacao valem mesmo com o catalogo cheio de linhas de outros testes.
  /// </summary>
  private string NovoPrefixo()
  {
    var prefixo = $"cmp{Guid.NewGuid():N}";
    _prefixosCriados.Add(prefixo);
    return prefixo;
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private static object CorpoValido(string codigo) =>
      new { codigo, descricao = "Suporte lateral", tipo = "Fabricado" };

  private static async Task<int> IdDaResposta(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
          .RootElement.GetProperty("id").GetInt32();

  private async Task CriarAsync(HttpClient cliente, string codigo)
  {
    var resposta = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
  }

  [Theory]
  [InlineData("Administrador")]
  [InlineData("PCP")]
  public async Task Perfis_de_escrita_cadastram_componente(string perfil)
  {
    // Os DOIS perfis, nao so o Administrador: /componentes e a primeira entidade de catalogo com
    // dois perfis de escrita, e um teste so do Administrador deixaria remover o "PCP" da string
    // de roles sem quebrar nada.
    var resposta = await ClienteComo(perfil)
        .PostAsJsonAsync("/api/componentes", CorpoValido($"{NovoPrefixo()}-a"));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // ASSERCAO APERTADA em 2026-08-24, que e o que o comentario anterior aqui prometia: com
    // `GET componentes/{id:int}` existindo, o `CreatedAtAction` passou a apontar `Obter`, e o
    // `Location` passa a ser o CAMINHO DO RECURSO CRIADO — nao mais a lista com o id virando
    // query string ignorada (`/api/componentes?id=123`), que era o que a versao `StartsWith`
    // deste teste tolerava.
    //
    // O que ela afirma agora, e que a anterior nao afirmava: (1) o caminho tem o id, entao voltar
    // para `nameof(Listar)` quebra; (2) o id do caminho e o do componente criado, e nao um
    // qualquer, porque ele vem do CORPO da resposta; (3) o prefixo `/api` continua ali, que era o
    // unico ponto da versao anterior. `MateriaisController` e `SetoresController` NAO acompanham
    // — divergencia deliberada, motivo escrito no `Cadastrar` do ComponentesController.
    Assert.NotNull(resposta.Headers.Location);
    var id = await IdDaResposta(resposta);
    Assert.Equal($"/api/componentes/{id}", resposta.Headers.Location!.AbsolutePath);
    Assert.Equal(string.Empty, resposta.Headers.Location.Query);
  }

  [Fact]
  public async Task Obter_componente_devolve_o_que_foi_cadastrado()
  {
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    var resposta = await cliente.GetAsync($"/api/componentes/{id}");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    // Os CINCO campos do ComponenteDto, nao so um: e a unica prova ponta a ponta de que o
    // `Obter` reusa a mesma projecao da listagem. Um campo faltando no JSON quebra a Task 10,
    // que le o cabecalho da tela de detalhe daqui.
    Assert.Equal(id, corpo.GetProperty("id").GetInt32());
    Assert.Equal(codigo, corpo.GetProperty("codigo").GetString());
    Assert.Equal("Suporte lateral", corpo.GetProperty("descricao").GetString());
    Assert.Equal("Fabricado", corpo.GetProperty("tipo").GetString());
    Assert.True(corpo.GetProperty("ativo").GetBoolean());
  }

  [Fact]
  public async Task Operador_le_o_detalhe_do_componente()
  {
    // Leitura e de QUALQUER autenticado: o gate de `Obter` e o `[Authorize]` de classe, sem
    // `Roles`. Sem este teste, acrescentar `[Authorize(Roles = PerfisDeEscrita)]` ao GET novo
    // passaria por toda a suite — inclusive pela guarda de perfis, que so exige que um endpoint
    // com `Roles` esteja NA TABELA, e nao que ele nao devesse ter `Roles` nenhum.
    var criado = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/componentes", CorpoValido($"{NovoPrefixo()}-a"));
    var id = await IdDaResposta(criado);

    var resposta = await ClienteComo("Operador").GetAsync($"/api/componentes/{id}");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Obter_componente_inexistente_responde_404()
  {
    var resposta = await ClienteComo("Administrador").GetAsync("/api/componentes/999999");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_o_detalhe()
  {
    // Par do `Sem_token_nao_le_a_lista`. Ele prova o 401 do `[Authorize]` de classe pela rota de
    // LISTA; apagar o atributo com so aquele teste no lugar deixaria a rota nova descoberta no
    // nivel de comportamento (a guarda de perfis le metadata, nao faz requisicao).
    var resposta = await _factory.CreateClient().GetAsync("/api/componentes/999999");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Theory]
  [InlineData("POST", "/api/componentes")]
  [InlineData("PUT", "/api/componentes/999999")]
  [InlineData("PATCH", "/api/componentes/999999/ativo")]
  public async Task Operador_nao_escreve_em_componente(string metodo, string rota)
  {
    // Cobrir os TRES verbos, e nao so o POST, e o que impede apagar o [Authorize(Roles)] do PUT
    // ou do PATCH em silencio (adendo B5). O filtro de autorizacao roda ANTES do model binding,
    // entao um id inexistente (999999) ainda responde 403 aqui, nunca 404 — por isso o Theory
    // nao precisa cadastrar Componente nenhum, e nada e escrito no banco.
    object corpo = metodo == "PATCH"
        ? new { ativo = false }
        : new { codigo = "operador-nao-pode", descricao = "Suporte", tipo = "Fabricado" };
    var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota)
    {
      Content = JsonContent.Create(corpo)
    };

    var resposta = await ClienteComo("Operador").SendAsync(requisicao);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Operador_le_a_lista_de_componentes()
  {
    var resposta = await ClienteComo("Operador").GetAsync("/api/componentes");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_a_lista()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/componentes");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Listagem_sem_parametros_usa_pagina_1_e_tamanho_20()
  {
    // Mata a mutacao de trocar os defaults da assinatura do controller: sem isto, `tamanho = 5`
    // por engano passaria despercebido.
    var corpo = await ClienteComo("Administrador").GetStringAsync("/api/componentes");
    var raiz = JsonDocument.Parse(corpo).RootElement;

    Assert.Equal(1, raiz.GetProperty("pagina").GetInt32());
    Assert.Equal(20, raiz.GetProperty("tamanho").GetInt32());
  }

  [Fact]
  public async Task Pagina_e_total_respeitam_a_busca()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    await CriarAsync(cliente, $"{prefixo}-c");
    await CriarAsync(cliente, $"{prefixo}-a");
    await CriarAsync(cliente, $"{prefixo}-b");
    // LINHA DE CONTROLE, prefixo DIFERENTE: nao casa com o `?busca=` deste teste. Sem ela, `dbo`
    // vazia fora do teste faz "filtrado" e "sem filtro nenhum" devolverem o mesmo conjunto, e o
    // controller podia descartar o `busca` inteiro com a suite verde (mesma classe do achado B11,
    // o literal `1` que coincidia com o id do unico usuario). Molde copiado de
    // ComponenteMappingTests.Busca_casa_no_codigo_e_na_descricao.
    var controle = $"{NovoPrefixo()}-z";
    await CriarAsync(cliente, controle);

    var pagina1 = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={prefixo}&pagina=1&tamanho=2")).RootElement;
    var pagina2 = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={prefixo}&pagina=2&tamanho=2")).RootElement;
    var semPaginar = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={prefixo}")).RootElement;

    // A afirmacao que discrimina e sobre os CODIGOS devolvidos, e nao so sobre `total`: contagem
    // exata fica refem de linha residual de outro teste. A consulta sem paginar e que da a prova
    // deterministica — com `tamanho=2` a linha de controle poderia cair fora da pagina lida por
    // ordenacao, e a ausencia dela seria acidente em vez de filtro.
    Assert.DoesNotContain(
        controle,
        semPaginar.GetProperty("itens").EnumerateArray()
            .Select(item => item.GetProperty("codigo").GetString()!));
    Assert.Equal(3, pagina1.GetProperty("total").GetInt32());
    Assert.Equal(2, pagina1.GetProperty("itens").GetArrayLength());
    Assert.Equal($"{prefixo}-a", pagina1.GetProperty("itens")[0].GetProperty("codigo").GetString());
    Assert.Equal($"{prefixo}-b", pagina1.GetProperty("itens")[1].GetProperty("codigo").GetString());
    Assert.Equal(1, pagina2.GetProperty("itens").GetArrayLength());
    Assert.Equal($"{prefixo}-c", pagina2.GetProperty("itens")[0].GetProperty("codigo").GetString());
  }

  [Fact]
  public async Task Busca_casa_na_descricao_tambem()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    var marcador = $"desc{Guid.NewGuid():N}";
    await cliente.PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{prefixo}-a", descricao = $"Peca {marcador} especial", tipo = "Bruto" });
    // LINHA DE CONTROLE: mesmo prefixo (a limpeza do DisposeAsync cobre), mas SEM o marcador na
    // descricao — logo nao casa com `?busca={marcador}`. Sem ela o teste afirmava sobre uma tabela
    // em que TODA linha casava, e "filtrado" era indistinguivel de "sem filtro".
    var controle = $"{prefixo}-b";
    await CriarAsync(cliente, controle);

    var corpo = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={marcador}")).RootElement;

    Assert.DoesNotContain(
        controle,
        corpo.GetProperty("itens").EnumerateArray()
            .Select(item => item.GetProperty("codigo").GetString()!));
    Assert.Equal(1, corpo.GetProperty("total").GetInt32());
    Assert.Equal($"{prefixo}-a", corpo.GetProperty("itens")[0].GetProperty("codigo").GetString());
  }

  [Theory]
  [InlineData("pagina=0")]
  [InlineData("tamanho=0")]
  [InlineData("tamanho=101")]
  public async Task Faixa_de_paginacao_invalida_responde_400(string query)
  {
    // Adendo B14: a faixa NAO tem CHECK equivalente no banco, entao a guarda da aplicacao e a
    // unica defesa e merece a prova mais forte (HTTP), nao so a de Application.
    var resposta = await ClienteComo("Administrador").GetAsync($"/api/componentes?{query}");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Tipo_invalido_responde_400_e_nao_500()
  {
    // Prova que a lista fechada e validada no use case, ANTES de o CK_Componente_Tipo estourar
    // como SqlException — a diferenca entre 400 e 500.
    var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{NovoPrefixo()}-a", descricao = "Suporte", tipo = "Qualquer" });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Codigo_duplicado_inativo_responde_409_indicando_reativacao()
  {
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { ativo = false });

    var resposta = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
    Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
    Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Codigo_duplicado_com_espaco_a_esquerda_responde_409_completo()
  {
    // Achado I2 da review da 1B: LocalizarDuplicado normaliza o valor antes de consultar
    // (CadastroDeComponenteUseCase.cs:126), mas nada testava esse Normalizar. Cria "X", inativa,
    // e tenta criar " X" (espaco a esquerda, como sai de copy-paste de planilha/PDF). O 409 tem
    // de vir COMPLETO: sem o Normalizar no localizador, `campo`/`existeInativo`/`idExistente`
    // somem e o front cai no erro generico, sem oferecer a reativacao.
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { ativo = false });

    var resposta = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido($" {codigo}"));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
    Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Editar_para_codigo_de_outro_componente_responde_409()
  {
    // Refinamento do adendo B12: ele dispensa o teste de 409-no-PUT quando o localizador de
    // duplicado e COMPARTILHADO com o POST — mas compartilhar o delegate prova o CORPO dele, nao o
    // CALL SITE. O argumento `Duplicado(alterado.Codigo)` do `Editar` e o ramo `Conflito` do
    // `TraduzirFalha` dele ficavam sem execucao nenhuma. Consequencia real: uma regressao ali
    // devolve 409 sem `campo`/`existeInativo`, e o `lerOuFalhar` do front (adendo F5) lanca erro
    // generico em vez de oferecer a reativacao.
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    var criadoA = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido($"{prefixo}-a"));
    var idDoA = await IdDaResposta(criadoA);
    var criadoB = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido($"{prefixo}-b"));
    var idDoB = await IdDaResposta(criadoB);

    var resposta = await cliente.PutAsJsonAsync(
        $"/api/componentes/{idDoB}", CorpoValido($"{prefixo}-a"));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
    Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
    // O id do OUTRO componente, nao o da rota: e o que prova que o argumento do localizador e o
    // codigo enviado no corpo do PUT, e nao qualquer outro valor a mao.
    Assert.Equal(idDoA, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Componente_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    var codigo = $"{prefixo}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    var patch = await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { ativo = false });

    // O 204 do PATCH de sucesso nao era afirmado em nenhum *EndpointsTests de cadastro: os dois
    // testes que usam o verbo olhavam so o EFEITO, entao trocar o sucesso por `Ok(qualquer coisa)`
    // ficava verde.
    Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
    var padrao = await cliente.GetStringAsync($"/api/componentes?busca={prefixo}");
    var comInativos = await cliente.GetStringAsync(
        $"/api/componentes?busca={prefixo}&incluirInativos=true");

    Assert.DoesNotContain(codigo, padrao);
    Assert.Contains(codigo, comInativos);
  }

  [Fact]
  public async Task Editar_altera_descricao_e_tipo()
  {
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    // Codigo NOVO, nao o mesmo do cadastro (review da Task 2, achado I3): reenviar o
    // mesmo codigo deixa a atribuicao `componente.Codigo = codigo;` sem prova tambem no nivel
    // HTTP — sem isto, apagar aquela linha no use case ainda passaria em toda a suite.
    var codigoNovo = $"{codigo}-novo";
    var resposta = await cliente.PutAsJsonAsync(
        $"/api/componentes/{id}",
        new { codigo = codigoNovo, descricao = "Suporte reforcado", tipo = "Montagem" });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(codigoNovo, corpo.GetProperty("codigo").GetString());
    Assert.Equal("Suporte reforcado", corpo.GetProperty("descricao").GetString());
    Assert.Equal("Montagem", corpo.GetProperty("tipo").GetString());
  }

  [Fact]
  public async Task Editar_componente_inexistente_responde_404()
  {
    // Adendo B7: sem este teste nada exercita o `resultado.Sucesso ? Ok(...) : TraduzirFalha(...)`
    // do controller — trocar o corpo do Editar por `return Ok(resultado.Valor);` ficaria verde.
    var resposta = await ClienteComo("Administrador")
        .PutAsJsonAsync("/api/componentes/999999", CorpoValido($"{NovoPrefixo()}-a"));

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Definir_ativo_em_componente_inexistente_responde_404()
  {
    var resposta = await ClienteComo("Administrador")
        .PatchAsJsonAsync("/api/componentes/999999/ativo", new { ativo = false });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Patch_ativo_sem_o_campo_responde_400()
  {
    // `DefinirAtivoDto.Ativo` era `bool` nao-anulavel e sem `[Required]`: corpo `{}` vinculava
    // `false` sem o validador do [ApiController] reclamar, e o PATCH respondia 204 INATIVANDO o
    // componente — inativacao silenciosa a partir de um corpo que nao pediu nada. Como "catalogo se
    // inativa, nao se exclui", `ativo` e o mais proximo de exclusao que o cadastro tem, e o default
    // silencioso e o lado errado do trade-off. Vale para /setores e /materiais pelo mesmo DTO.
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    var resposta = await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    // O 400 nao basta: o que se quer provar e que a linha NAO foi mexida. Sem esta segunda
    // assercao, um 400 vindo de qualquer outro lugar depois de a linha ja ter sido inativada
    // passaria.
    var lista = await cliente.GetStringAsync($"/api/componentes?busca={codigo}");
    Assert.Contains(codigo, lista);
  }

  [Fact]
  public async Task Descricao_em_branco_responde_400()
  {
    var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{NovoPrefixo()}-a", descricao = " ", tipo = "Fabricado" });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Theory]
  [InlineData("codigo", 51)]
  [InlineData("descricao", 201)]
  public async Task Campo_maior_que_a_coluna_responde_400_e_nao_500(string campo, int tamanho)
  {
    // Um caractere alem de NVARCHAR(50)/(200) de dbo.Componente. Prova que o [MaxLength] de cada
    // parametro de NovoComponenteDto pega ANTES de o insert estourar SqlException.
    //
    // `tipo` fica FORA deste Theory de proposito (adendo B8, "prova falsa em campo de lista
    // fechada"): o [MaxLength(20)] dele nao e testavel neste nivel, porque um valor de 21
    // caracteres tambem esta fora de Bruto|Fabricado|Montagem — o 400 chegaria pela validacao do
    // use case e o teste passaria com o atributo removido. Escrever o InlineData de `tipo` daria
    // ao revisor a impressao de que o atributo esta coberto quando nao esta. O atributo continua
    // no DTO como defesa em profundidade e espelho da coluna, sem teste que o falsifique.
    var valores = new Dictionary<string, object>
    {
      ["codigo"] = $"{NovoPrefixo()}-a",
      ["descricao"] = "Suporte lateral",
      ["tipo"] = "Fabricado",
    };
    valores[campo] = new string('x', tamanho);

    var resposta = await ClienteComo("Administrador").PostAsJsonAsync("/api/componentes", valores);

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }
}
