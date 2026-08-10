# Fase 1D — Identidade visual e UX

Spec de design. Escrita em 2026-08-06, a partir do brainstorm da mesma data.
Fase aprovada pelo usuário em 2026-08-05; até esta spec existir, o que havia era um bloco de
decisões no ledger — bom como insumo, insuficiente como origem de plano.

**Pré-requisito:** a Fase 1B mergeada (PR #5). Esta spec conta 7 telas, e a sétima é a
`ComponentesPage`, que nasce na 1B.

---

## 1. A tese da fase, e por que ela é retroativa

O objetivo declarado pelo usuário é "cara de produto" — o projeto vai para o currículo e para a
banca. Mas a fase não existe por estética: existe porque **o produto dela é o padrão**, e há mais
tela pela frente do que para trás.

Hoje são 7 telas. O roadmap ainda traz **8 a 10**: árvore de estrutura e upload de sólido na Fase 2;
apontamento e fila do setor na 3; separação de materiais na 4; dimensional, expedição, perda e
retrabalho na 5; duas de KPI na 6. As duas alternativas a esta fase são piores, e é isso que fecha o
argumento:

- **"UI só daqui para frente"** deixa as 7 telas velhas destoando para sempre.
- **"Passada retroativa no fim"** reestiliza ~15 telas de uma vez e escreve 10 sem padrão nenhum
  no caminho.

A saída é uma passada retroativa **agora e curta, cujo produto é o padrão** — primitivas + retrofit
—, para que "desde o começo" passe a valer para tudo que falta.

**Corolário que vale registrar:** se daqui a três fases o sistema precisar de outra passada de UI,
isso não é uma fase planejada que ficou faltando; é o sinal de que o padrão não pegou. O roadmap
não tem — e não deve ganhar — uma "Fase 1D parte 2".

---

## 2. O ponto de partida, conferido no disco (2026-08-06)

Não há sistema de interface nenhum. O que existe:

- **Seis das sete telas repetem literalmente a mesma linha de container:**
  `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4` — `HomePage`, `SetoresPage`,
  `MateriaisPage`, `PedidosPage`, `PedidoDetalhePage`, `ComponentesPage`. A `LoginPage` é a única
  exceção (cartão centralizado, `max-w-sm`).
- **Um único componente compartilhado:** `web/src/components/TelaCarregando.tsx`. Todo o resto —
  campo, botão, banner de erro, item de lista, pílula, paginação — está copiado caractere a
  caractere entre as telas.
- **Não existe shell de aplicação.** A navegação vive dentro da `HomePage` (`HomePage.tsx:47-54`,
  uma fileira de `<Link>` com borda). As outras seis telas não têm cabeçalho e **não têm caminho de
  volta que não seja o botão do navegador**.
- **Cobertura de tela:** 3 dos 6 arquivos de teste do front são de tela (`ComponentesPage`,
  `MateriaisPage`, `SetoresPage`), para 7 telas. Suíte em 96 testes.

**Consequência de desenho, e é boa notícia:** como não há diversidade de layout, "re-layout" não são
sete problemas de design. É **uma** decisão — o que substitui aquela linha de container — aplicada
seis vezes por uma primitiva de página, mais ajuste onde o conteúdo exige de verdade (§7).

---

## 3. Fundações visuais

Direção escolhida no brainstorm, com mockups das telas reais: **sóbria e espaçada**, não densa. O
custo foi medido e aceito — na mesma altura de tela, a direção espaçada mostra ~3 itens onde a densa
mostraria ~6.

### Paleta

| Papel | Valor | Onde aparece |
|---|---|---|
| Chrome | `#134E4A` petróleo profundo | barra de navegação, cabeçalho |
| Marca | `#5EEAD4` verde-água claro | logo, sempre **sobre** o chrome escuro |
| Ação | `#3E6E68` água fosco | botão primário, link de ação, item de menu ativo |
| Tinta secundária | o **mesmo** `#3E6E68`, sobre fundo `#E8F0EF` | pílulas de categoria (ex.: tipo do componente) |

O valor repetido nas duas últimas linhas **não é engano**: é a mesma tinta em dois contextos — cheia
sobre fundo claro na ação, e sobre um fundo tingido de baixa saturação na pílula. Um segundo tom
para a pílula seria uma cor a mais para manter coerente sem ganho nenhum.
| Estado positivo | `#166534` verde-escuro e `#15803D` verde — os dois passam AA nos dois papéis (fundo ou texto); o que separa é peso visual, não conformidade | **reservado**: aprovado, ativo |
| Estado negativo | `#DC2626` vermelho | **reservado**: reprovado, perda, erro |
| Neutros | escala cinza-esverdeada | texto, bordas, fundos |

**Por que dois tons para o estado positivo:** `#16A34A`, o verde cogitado antes desta medição, dá
3,30:1 contra branco — abaixo dos 4,5:1 de texto normal — e por isso saiu. Os dois tons que entraram
passam AA nos DOIS papéis: `#166534` sobre branco dá 7,130:1, e `#15803D` sobre branco dá 5,016:1 —
razão de contraste é simétrica, então o mesmo número vale com os papéis invertidos. O que separa os
dois não é conformidade — é peso visual: `#166534` é usado como preenchimento cheio (com texto
branco por cima), e `#15803D` como o tom que carrega a cor quando é ele o texto — mais vivo, para
ler pequeno.

### A regra que sustenta a paleta

**Cor de identidade nunca significa estado; cor de estado nunca decora.**

Não é preferência estética — é o que faz a tela de Qualidade da Fase 5 funcionar. Lá, "Aprovado"
(verde) e "Abrir retrabalho" (ação) aparecem na mesma linha. Se a marca fosse verde, os dois
disputariam o mesmo olhar, e o usuário teria de decidir a cada vez se aquele verde significa
"estado bom" ou "clique aqui". Com a marca em petróleo/água, verde e vermelho ficam inteiramente
livres para significar estado.

**Foi por isso que o verde-água entrou por matiz e croma baixos, não por gosto:** ele fica longe do
verde de estado nos dois eixos ao mesmo tempo. Um verde-água vivo teria o problema que a regra
existe para evitar.

### Contraste — medido, não presumido

Contra branco: petróleo `#134E4A` ≈ **9,5:1**; água fosco `#3E6E68` ≈ **5,8:1**. Ambos passam
AA como texto; o petróleo passa AAA.

**Armadilha registrada:** os tons claros das famílias verde-água e âmbar (`#0D9488`, `#D97706`)
reprovam os 4,5:1 de texto normal contra QUALQUER fundo — razão de contraste é simétrica, então
inverter frente e fundo não salva nada; os dois só alcançam os 3:1 de componente de interface /
texto grande. Escolher paleta olhando só o botão é o erro clássico, não porque a inversão muda o
número, mas porque 3:1 não é 4,5:1. **Todo tom novo que a implementação precisar tem de ser medido
antes de entrar.**

### A consequência de a ação ser monocromática

O usuário escolheu a ação na mesma família do chrome (água fosco), e não no ciano contrastante. É
mais coeso, e o custo é real: **botão primário nessa cor fica discreto, e botão discreto custa
clique** — num sistema com ações que precisam ser inequívocas (inativar, abrir retrabalho).

A compensação é de forma, não de cor: o primário ganha peso por **tamanho e densidade tipográfica**,
e o **estilo de contorno neutro fica reservado a ações secundárias**. Nenhuma tela deve ter dois
botões com o mesmo peso visual.

---

## 4. Tipografia e densidade

- **Pilha de fonte do sistema** (`ui-sans-serif` → Segoe UI no Windows, Roboto no Android) e
  `ui-monospace` para códigos de peça e material. Zero dependência nova, zero asset para baixar no
  wifi da fábrica, coerente com a ética do repo (a Task 5 da 1B recusou até `jest-dom` e
  `user-event`). Reversível: trocar por uma fonte própria depois é mudar um token, não reescrever
  telas.
- **Código em monoespaçada** é decisão funcional, não decorativa: alinha na coluna e facilita
  conferir código contra desenho na bancada.

---

## 5. Primitivas

Extraídas do que hoje está duplicado. Cada uma nasce com teste próprio.

`Pagina` — substitui as seis cópias do container; define largura, respiro e cabeçalho de página ·
`Campo` · `Botao` (primário / secundário / perigo) · `BannerDeErro` · `ListaDeCadastro` (o item com
inativo distinguível e ação de (in)ativar) · `Pilula` · `ControlesDePaginacao` · `EstadoVazio`.

**Sem biblioteca de componentes.** Primitivas à mão sobre Tailwind v4, que já está instalado.

**Critério de pronto de uma primitiva:** ela é usada por pelo menos duas telas e não sobrou nenhuma
cópia da forma antiga no repositório. Primitiva com um consumidor só é abstração prematura.

---

## 6. Shell de navegação

Barra superior fixa com os itens principais, que **vira menu-gaveta abaixo de ~768px** (o celular
Android da fábrica é uso declarado). Entra no `App.tsx` como rota de layout.

O shell é onde moram três coisas que hoje não têm casa:

1. **O caminho de volta** — hoje as seis telas internas só voltam pelo botão do navegador.
2. **O gating de navegação por perfil**, uma das três dívidas de UX rastreadas: item que o perfil
   não pode usar não aparece. **Isso é conveniência de navegação, não segurança** — a autorização
   real é do backend (`[Authorize(Roles)]`), continua sendo, e esconder o link não substitui nada.
3. **A identidade de quem está logado** e a saída (logout).

**Com 10+ telas a barra fica apertada** e vai exigir agrupamento (ex.: "Cadastros" com submenu). É
custo conhecido e aceito; o agrupamento entra quando doer, não agora.

---

## 7. As 7 telas

| Tela | O que acontece |
|---|---|
| **Home** | **Muda de papel** — perde o emprego de menu (§8) |
| **Componentes** | **Re-layout**: busca + filtro + seletor de tamanho + paginação não cabem em 448px |
| **PedidoDetalhe** | **Re-layout**: detalhe, modal e lista de agrupamentos na mesma coluna estreita |
| **Login** | Identidade aplicada; layout já adequado |
| **Setores** | Herda container e primitivas. Sem redesenho |
| **Materiais** | Herda container e primitivas. Sem redesenho |
| **Pedidos** | Herda container e primitivas. Sem redesenho |

**Não há tela "pendente de retrofit" ao fim da fase.** As três que não recebem redesenho não estão
sendo adiadas: são formulário + lista, e o container novo mais as primitivas as resolvem por
inteiro. Se durante a execução alguma delas destoar de fato ao lado das novas, **absorver na própria
fase** — são as telas mais simples do sistema — e não abrir fase nova.

---

## 8. Home — números reais sem tocar no backend

A Home vira **cartões de contagem + atalhos por perfil**.

A restrição "zero mudança de domínio" vale, e não há KPI de produção a implementar aqui. Mas
**números verdadeiros já existem de graça** nos endpoints atuais:

- `listarComponentes` devolve `PaginaDe<T>` com **`total` sob o filtro aplicado**. Pedir
  `tamanho: 1` e ler só o `total` custa **uma requisição e nenhum item trafegado**. O campo foi
  feito exatamente para isto.
- `listarPedidos()` devolve o array inteiro: contagem é `.length`, e o status do `PedidoDto` permite
  "N pedidos abertos".
- Idem `listarSetores` / `listarMateriais`.

**Nada de número fake, e nada de rota crua que não retorna nada.** Dois motivos concretos: um número
inventado é uma afirmação falsa numa tela que vai à banca; e o teste de fumaça que esta fase exige
**não distingue número fake de número real** — a suíte ficaria verde provando uma mentira. Mock que
precisa ser removido depois é o tipo de coisa que sobrevive até a defesa.

**Cartões que dependem de domínio inexistente não aparecem.** "Peças em produção" depende do
`EstruturaItem` (Fase 2); "reprovados do dia" depende do Relatório Dimensional (Fase 5). A grade
nasce com o que tem número verdadeiro e **cresce por fase** — o layout fica pronto, e a Fase 6
preenche as lacunas com KPI de verdade em vez de redesenhar a Home.

---

## 9. Dívidas que a fase absorve

Todas são front puro, e todas já estavam rastreadas:

- **`useBuscaPaginada`** — debounce da busca, cancelamento, clamp de página após recarga e reset de
  filtro. Hoje a `ComponentesPage` resolve as quatro à mão, e a review da Task 6 mostrou que parte
  delas estava errada ou sem prova. **Entra cedo, antes do re-layout das telas**: se as telas forem
  reescritas antes do hook existir, elas adotam o padrão velho e alguém reescreve duas vezes.
- **W3** — `setCarregando(true)` sem prova: sem ele, depois da primeira carga o usuário nunca mais
  vê "Carregando…", e a lista antiga fica na tela sem sinal de atividade até a resposta nova trocar
  tudo de repente.
- **Erro global de API** — camada amigável para 401 / falha de rede / 404.
- **Botão desabilitado durante mutação.**
- **Estado vazio explícito** — hoje "nenhum resultado para a busca", "catálogo vazio" e "erro de
  rede" renderizam a mesma lista vazia muda.

---

## 10. Ordem obrigatória: rede antes do markup

**Um teste de fumaça por tela ANTES de qualquer reescrita de markup.** Só 3 das 7 telas têm teste
hoje. Reescrever markup em massa sem rede é regressão silenciosa garantida — e as regressões desta
classe não quebram build nem tipo, aparecem para o usuário.

Sequência da fase:

1. Teste de fumaça nas telas que não têm.
2. `useBuscaPaginada` + as dívidas de comportamento (§9), com teste próprio.
3. Tokens e primitivas (§3, §5), com teste próprio.
4. Shell (§6).
5. Aplicação nas telas (§7, §8).

---

## 11. Critérios de aceite

Verificáveis, um por um. **"Ficou bonito" foi barrado de propósito — não é critério.**

- **Mesma primitiva nas 7 telas**: nenhuma cópia da forma antiga sobrou no repositório (verificável
  por busca: o container antigo e as classes duplicadas não aparecem mais).
- **Estados carregando / vazio / erro** em toda tela que busca dados, cada um com teste que morre se
  o estado sumir.
- **Navegação por teclado e foco visível** em todo controle interativo.
- **Contraste AA** em todo par texto/fundo — tom novo, medição nova.
- **Viewport de celular real**: a barra vira gaveta, nenhuma tela rola na horizontal.
- **Suíte, build e lint verdes**, com a baseline do início da fase medida e registrada (não herdada
  deste documento — medir na hora).

---

## 12. Fora de escopo — nomeado, não esquecido

- **Tema escuro.** Não é uma quinta paleta: dobra os estados a provar em cada primitiva. Decisão
  separada, adiada de propósito.
- **Qualquer mudança em `src/`** (backend). A fase é front puro.
- **KPIs de produção** — Fase 6, e dependem de domínio das Fases 2 e 5.
- **Biblioteca de componentes de terceiros.**
- **As dívidas I2 e I3 da review de branch da 1B** (o `Trim` de `LocalizarDuplicado` sem prova; o
  teste negativo de autorização que fixa um perfil por controller). São backend e têm dono e prazo
  próprios: **fix pass antes da 1C**.

---

## 13. Atualizações de spec que a fase deve fazer

- **`specs/06-roadmap-mvp.md`**: registrar a Fase 1D. Escrever explicitamente que **ela não tem
  aresta de dependência** — não bloqueia nem é bloqueada pela 1C, pode rodar antes ou depois. A
  letra é rótulo cronológico, não ordem obrigatória; sem essa frase alguém lê a sequência como
  dependência.
- **Não renomear 1C.** Já foi decidido por medição: `1C` aponta para a receita padrão em 13 lugares,
  incluindo `specs/05-api-endpoints.md`, e existe a afirmação estrutural "a Fase 2 fica bloqueada até
  a 1C existir". Trocar as letras faria essa frase passar a dizer que a Fase 2 depende da fase de
  interface — falso.
