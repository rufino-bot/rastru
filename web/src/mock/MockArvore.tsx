// MOCK DESCARTÁVEL — não commitar. Renderiza a ArvoreDeEstrutura real com uma Peça de 40 nós e
// 8 níveis, para ver o comportamento no celular. Abrir em /mock-arvore.html (?leitura = sem escrita).
import { StrictMode, useState } from 'react'
import { createRoot } from 'react-dom/client'
import '../index.css'
import { ArvoreDeEstrutura } from '../components/ArvoreDeEstrutura'
import { Pagina } from '../components/Pagina'
import { ArvoreProposta } from './ArvoreProposta'
import type { NoDaEstrutura } from '../api/estrutura'
import type { PosicoesDoNoDto } from '../api/execucao'

// [nível, código (null = ad-hoc), descrição, quantidade por pai]
const LINHAS: [number, string | null, string, number][] = [
  [0, 'PH-4000', 'Prensa hidráulica 40 t', 1],
  [1, 'CJ-4100', 'Estrutura principal soldada', 1],
  [2, 'CJ-4110', 'Conjunto mesa inferior', 1],
  [3, 'SC-4111', 'Subconjunto guia lateral', 2],
  [4, 'SC-4112', 'Bloco guia soldado', 1],
  [5, 'PC-4113', 'Suporte usinado do bloco', 2],
  [6, 'PC-4114', 'Bucha de bronze flangeada', 2],
  [7, 'PC-4115', 'Pino trava retificado', 1],
  [7, null, 'Anel elástico DIN 471', 1],
  [6, 'PC-4116', 'Parafuso allen M12x40', 4],
  [5, 'PC-4117', 'Chapa de reforço 12,7 mm', 2],
  [4, 'PC-4118', 'Calço de ajuste', 4],
  [3, 'PC-4119', 'Tampo da mesa usinado', 1],
  [3, 'SC-4120', 'Subconjunto travessa', 2],
  [4, 'PC-4121', 'Viga U 150', 1],
  [4, 'PC-4122', 'Cantoneira de fixação', 4],
  [2, 'CJ-4130', 'Conjunto coluna', 4],
  [3, 'PC-4131', 'Tubo da coluna Ø 120', 1],
  [3, 'SC-4132', 'Flange superior', 1],
  [4, 'PC-4133', 'Disco do flange', 1],
  [4, 'PC-4134', 'Nervura do flange', 4],
  [2, 'CJ-4140', 'Conjunto cabeçote superior', 1],
  [3, 'SC-4141', 'Suporte do cilindro', 1],
  [4, 'SC-4142', 'Berço do cilindro', 1],
  [5, 'SC-4143', 'Mancal do munhão', 2],
  [6, 'SC-4144', 'Caixa do rolamento', 1],
  [7, 'PC-4145', 'Tampa da caixa', 1],
  [7, 'PC-4146', 'Retentor 60x80x8', 1],
  [6, 'PC-4147', 'Graxeiro reto M6', 1],
  [5, 'PC-4148', 'Chapa lateral do berço', 2],
  [3, 'PC-4149', 'Chapa superior 25,4 mm', 1],
  [1, 'CJ-4200', 'Conjunto hidráulico', 1],
  [2, 'SC-4210', 'Bloco manifold', 1],
  [3, 'PC-4211', 'Corpo do manifold usinado', 1],
  [3, 'PC-4212', 'Plug de vedação 1/4"', 6],
  [2, 'SC-4220', 'Reservatório 60 L', 1],
  [3, 'PC-4221', 'Tampa de inspeção', 1],
  [3, null, 'Visor de nível (comprado)', 1],
  [1, 'CJ-4300', 'Proteção frontal', 1],
  [2, 'PC-4310', 'Grade de proteção', 2],
]

const SETORES = ['Corte', 'Dobra', 'Solda', 'Usinagem', 'Rebarbação', 'Pintura', 'Montagem']

function montar(): { raizes: NoDaEstrutura[]; posicoes: Map<number, PosicoesDoNoDto> } {
  const raizes: NoDaEstrutura[] = []
  const pilha: NoDaEstrutura[] = []
  const posicoes = new Map<number, PosicoesDoNoDto>()
  LINHAS.forEach(([nivel, codigo, descricao, porPai], i) => {
    const id = i + 1
    const no: NoDaEstrutura = {
      id,
      componenteId: codigo ? id : null,
      codigoDoComponente: codigo,
      descricao,
      quantidade: porPai * (nivel === 0 ? 2 : 2),
      nivelHierarquico: nivel === 0 ? 'Peca' : 'Item',
      requerRelatorioDimensional: i % 7 === 3,
      materiais: i % 3 === 0 ? [{ materialId: 1, nome: 'Chapa aço 1020 6,35 mm', quantidade: 2 }] : [],
      roteiro:
        i % 5 === 4
          ? []
          : [0, 1, 2].map((k) => ({ setorId: k + 1, nome: SETORES[(i + k) % SETORES.length], ordem: k + 1 })),
      filhos: [],
      quantidadePorPai: nivel === 0 ? null : porPai,
      semRoteiro: i % 5 === 4,
    } as NoDaEstrutura
    pilha.length = nivel
    if (nivel === 0) raizes.push(no)
    else pilha[nivel - 1].filhos.push(no)
    pilha.push(no)
    if (i % 2 === 0) {
      posicoes.set(id, {
        estruturaItemId: id,
        saldos: [
          { posicao: 'NoSetor', setorId: 1, setorNome: SETORES[i % 7], ordem: 1, quantidade: 1 },
          ...(i % 4 === 0
            ? [{ posicao: 'AguardandoColeta' as const, setorId: 2, setorNome: SETORES[(i + 1) % 7], ordem: 2, quantidade: 1 }]
            : []),
        ],
        totalMontado: null,
      })
    }
  })
  return { raizes, posicoes }
}

const { raizes, posicoes } = montar()
const params = new URLSearchParams(location.search)
const leitura = params.has('leitura')
const proposta = params.has('proposta')
const Arvore = proposta ? ArvoreProposta : ArvoreDeEstrutura
document.body.className = 'bg-fundo'
function Mock() {
  // Sem backend: cada ação só avisa o que o app faria, para o clique ter resposta visível.
  const [aviso, setAviso] = useState<string | null>(null)
  const nome = (no: NoDaEstrutura) => no.codigoDoComponente ?? no.descricao
  const acharPorId = (id: number, nos: NoDaEstrutura[]): NoDaEstrutura | undefined => {
    for (const n of nos) {
      if (n.id === id) return n
      const f = acharPorId(id, n.filhos)
      if (f) return f
    }
  }
  return (
    <Pagina titulo={proposta ? 'Proposta (mock)' : 'Atual (mock)'}>
      <Arvore
        nos={raizes}
        posicoes={posicoes}
        podeEscrever={!leitura}
        onAcrescentarFilho={leitura ? undefined : (id) => setAviso(`Acrescentar filho em ${nome(acharPorId(id, raizes)!)}`)}
        onEditar={leitura ? undefined : (no) => setAviso(`Editar ${nome(no)}`)}
        onExcluir={leitura ? undefined : (no) => setAviso(`Excluir ${nome(no)}`)}
        onDetalhe={(no) => setAviso(`Abriria o painel de detalhe de ${nome(no)}`)}
      />
      {aviso && (
        <div
          role="status"
          className="fixed inset-x-4 bottom-4 flex items-center justify-between gap-3 rounded-lg bg-chrome px-4 py-3 text-sm text-white shadow-lg"
        >
          <span>{`No app: ${aviso}`}</span>
          <button type="button" onClick={() => setAviso(null)} className="font-semibold">
            OK
          </button>
        </div>
      )}
    </Pagina>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Mock />
  </StrictMode>,
)
