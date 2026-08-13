import { useEffect, useState, type FormEvent } from 'react'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [nome, setNome] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('setores')

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os setores.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarSetor(nome)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o setor.'))
    } finally {
      setEnviando(false)
    }
  }

  // O `try/catch` continua sendo a fronteira REAL de perfil (F2): esconder o botão é conveniência
  // de interface, e o 403 do backend segue valendo para quem chamar a API por fora da tela.
  async function alternarAtivo(setor: SetorDto) {
    try {
      await definirAtivoSetor(setor.id, !setor.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o setor.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoSetor(id, true)
      setErro(null)
      setIdReativavel(null)
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o setor.'))
    }
  }

  return (
    <Pagina titulo="Setores">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Campo rotulo="Nome do setor">
              {(id) => (
                <input
                  id={id}
                  value={nome}
                  onChange={(e) => setNome(e.target.value)}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…">Adicionar</Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && setores.length === 0 ? (
        // `erro === null` é o que distingue "não há setores" de "a listagem falhou": no `catch`
        // de `carregar`, `setSetores` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum setor cadastrado" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum setor cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {setores.map((s) => (
            <ItemDeCadastro
              key={s.id}
              ativo={s.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(s)}>
                  {s.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              {s.nome}
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
