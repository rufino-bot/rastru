/**
 * Erro de API que CARREGA o status. Antes desta classe o status vivia só dentro da string da
 * mensagem (`Falha ao listar setores (403).`), então a tela não tinha como distinguir "seu perfil
 * não pode" de "o servidor caiu" sem fazer parse de texto.
 *
 * Continua sendo `Error`: as funções de `cadastros.ts` não mudam de contrato, e todo
 * `rejects.toThrow()` que já existia segue valendo.
 */
export class ErroDeApi extends Error {
  readonly status: number

  /**
   * Mensagem que veio do SERVIDOR (o campo `erro` do corpo), quando quem chamou se deu ao trabalho
   * de lê-la. Opcional de propósito: quem não popula segue exatamente como antes.
   *
   * Existe porque a receita padrão tem um 400 cuja explicação só o servidor sabe dar — ele nomeia
   * o ciclo (`"Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010."`), e a spec §1.3 da
   * Fase 1C EXIGE que essa mensagem chegue ao usuário: a regra de ciclo é estrita, então alguém
   * pode ser barrado por um ciclo que não criou, e saber ONDE ele está é o que torna a regra
   * praticável. Sem isto, o backend cumpria e o front jogava a mensagem fora.
   *
   * **Só o cliente da receita popula.** É por isso que este campo é opcional em vez de o
   * `mensagemDeErro` passar a expor todo 400: os outros endpoints não escreveram os textos de
   * validação deles pensando em quem lê, e mostrá-los seria decidir por eles.
   */
  readonly detalhe?: string

  /**
   * Código do erro que o SERVIDOR mandou no campo `erro`, quando o endpoint fala por código — a
   * execução da Fase 3 (`{ erro: "SaldoInsuficiente", mensagem: "Só há 6 de ..." }`, spec §8.2).
   * Opcional pelo mesmo motivo de `detalhe`: só o cliente da execução (`api/execucao.ts`) popula.
   * `mensagemDeErro` o traduz por `TRADUCAO_DOS_CODIGOS` quando o servidor não mandou frase.
   */
  readonly codigo?: string

  constructor(status: number, mensagem: string, detalhe?: string, codigo?: string) {
    super(mensagem)
    this.status = status
    this.detalhe = detalhe
    this.codigo = codigo
    this.name = 'ErroDeApi'
  }
}

/** Os códigos de erro da execução (spec da Fase 3 §8.2, mais `OrigemInvalida`, desvio D3 do plano 2). */
export type CodigoDeErroDaExecucao =
  | 'QuantidadeInvalida'
  | 'DestinoIndevido'
  | 'EntregaVazia'
  | 'RoteiroInvalido'
  | 'OrigemInvalida'
  | 'Proibido'
  | 'SemRoteiro'
  | 'NaoEhOPrimeiroPasso'
  | 'SaldoInsuficiente'
  | 'SemFilhos'
  | 'MontagemAcimaDoQueFalta'
  | 'FilhosInsuficientes'
  | 'DestinoForaDoRoteiroDoPai'
  | 'PaiSemRoteiro'
  | 'PassoJaAlcancado'
  | 'QuantidadeAbaixoDoMovimentado'
  | 'EstornoImpossivel'
  | 'JaEstornado'
  | 'PedidoFechado'
  | 'ConflitoDeConcorrencia'

/**
 * A tradução de cada código (spec §8.3: "cada código tem tradução em `mensagemDeErro`"). Só entra
 * quando o servidor NÃO mandou `mensagem` — a frase do servidor nomeia nó, Setor e números
 * ("*D*: 12 aqui, 16 necessários."), e o front não tem como reconstruí-la. `Record` sobre o tipo
 * inteiro: código novo sem frase não compila.
 */
export const TRADUCAO_DOS_CODIGOS: Readonly<Record<CodigoDeErroDaExecucao, string>> = {
  QuantidadeInvalida: 'A quantidade precisa ser maior que zero e ter no máximo quatro casas decimais.',
  DestinoIndevido: 'O destino desta entrega não confere com o que o sistema calculou. Atualize a tela e tente de novo.',
  EntregaVazia: 'Marque pelo menos um item para entregar.',
  RoteiroInvalido: 'O Roteiro tem um Setor inexistente ou inativo.',
  OrigemInvalida: 'A origem desta entrega não é válida. Atualize a tela e tente de novo.',
  Proibido: 'Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.',
  SemRoteiro: 'Este item não tem Roteiro. Peça ao PCP para cadastrá-lo.',
  NaoEhOPrimeiroPasso: 'Este item não começa neste Setor.',
  SaldoInsuficiente: 'Não há essa quantidade disponível aqui. Atualize a tela e tente de novo.',
  SemFilhos: 'Este item não tem filhos para montar.',
  MontagemAcimaDoQueFalta: 'Essa quantidade passa do que ainda falta montar.',
  FilhosInsuficientes: 'Não há filhos suficientes aqui para montar essa quantidade.',
  DestinoForaDoRoteiroDoPai: 'Este Setor não está no Roteiro do item pai.',
  PaiSemRoteiro: 'O item pai não tem Roteiro. Peça ao PCP para cadastrá-lo.',
  PassoJaAlcancado: 'Um passo já alcançado não pode ser alterado nem removido, e nenhum passo pode entrar antes dele.',
  QuantidadeAbaixoDoMovimentado: 'A quantidade não pode ficar abaixo do que já andou na produção.',
  EstornoImpossivel: 'Não dá para estornar: a quantidade já foi movida depois deste registro.',
  JaEstornado: 'Este registro já foi estornado.',
  PedidoFechado: 'O pedido deste item está concluído ou cancelado.',
  ConflitoDeConcorrencia: 'Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.',
}

function traducaoDoCodigo(codigo: string | undefined): string | undefined {
  if (codigo === undefined || !Object.hasOwn(TRADUCAO_DOS_CODIGOS, codigo)) return undefined
  return TRADUCAO_DOS_CODIGOS[codigo as CodigoDeErroDaExecucao]
}

/**
 * Traduz o que caiu no `catch` para uma frase que diga ao usuário o que fazer a seguir.
 *
 * `fallback` é o texto específico da tela ("Não foi possível carregar os setores.") e continua
 * sendo o destino de tudo que esta função não sabe explicar melhor — 400 sem código conhecido (um
 * 400 com código traduzido cai em `traducaoDoCodigo`, não aqui), erro de programação, valor que
 * nem erro é. A função nunca INVENTA explicação: ou reconhece o caso, ou devolve o que a tela já
 * diria.
 *
 * O 401 aqui é informativo, não corretivo: quem devolve o usuário ao login é o `onSessionLost` do
 * `client.ts`, depois de o refresh falhar. Esta mensagem cobre a janela em que a tela ainda está
 * montada.
 */
export function mensagemDeErro(e: unknown, fallback: string): string {
  if (e instanceof ErroDeApi) {
    // Mensagem do servidor ganha do texto da tela, e SÓ dela: `detalhe` não é inventado aqui nem
    // preenchido por acidente — quem o popula leu o corpo de propósito. Isto vem ANTES dos ramos
    // por status porque a explicação específica é melhor que a genérica sempre que existe.
    //
    // EXCETO no 401: hoje é inócuo (o backend emite `{erro}` em vários status — 400, 403, 404,
    // 409 —, mas nunca no 401, que vem do middleware com corpo vazio, então `detalhe` nunca
    // populado aqui), mas se algum dia um
    // endpoint responder 401 com corpo, esta guarda impede que ele apague "Sua sessão expirou.
    // Entre novamente." — a única mensagem acionável do conjunto (achado da review Tasks 10-12).
    if (e.status !== 401 && e.detalhe) return e.detalhe
    // Sem frase do servidor, o código traduzido ainda é melhor que o genérico do status — um 403
    // `Proibido` diz QUEM pode estornar, o genérico só diz que "seu perfil" não pode.
    const traduzido = e.status !== 401 ? traducaoDoCodigo(e.codigo) : undefined
    if (traduzido) return traduzido
    if (e.status === 401) return 'Sua sessão expirou. Entre novamente.'
    if (e.status === 403) return 'Seu perfil não tem permissão para esta ação.'
    if (e.status === 404) return 'Este registro não existe mais.'
    if (e.status >= 500) return 'O servidor não respondeu como esperado. Tente de novo em instantes.'
    return fallback
  }
  // `fetch` rejeita com TypeError quando a requisição nem sai (DNS, rede, CORS). É o único erro
  // de rede que chega aqui — o resto do caminho já virou Response.
  if (e instanceof TypeError) return 'Sem conexão com o servidor. Verifique a rede e tente de novo.'
  return fallback
}
