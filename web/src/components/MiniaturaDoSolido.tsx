import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

// Cache de módulo por `caminho + versao`: a miniatura é imutável para uma versão (o hash do que
// ela desenha), então a mesma versão nunca é buscada duas vezes. Guarda a promessa, para duas
// montagens simultâneas dividirem a mesma requisição. A falha sai do cache, para a próxima
// montagem tentar de novo.
const cache = new Map<string, Promise<string | null>>()

/** Só para os testes: zera o cache entre casos. */
export function limparCacheDeMiniaturas(): void {
  cache.clear()
}

function buscar(caminho: string, versao: string): Promise<string | null> {
  const chave = `${caminho}\u0000${versao}`
  const existente = cache.get(chave)
  if (existente) return existente
  const promessa = (async () => {
    try {
      const resposta = await apiFetch(caminho)
      if (!resposta.ok) return null
      return URL.createObjectURL(await resposta.blob())
    } catch {
      return null
    }
  })()
  cache.set(chave, promessa)
  void promessa.then((url) => {
    if (url === null && cache.get(chave) === promessa) cache.delete(chave)
  })
  return promessa
}

const ESPACO = 'aspect-[4/3] border border-borda bg-fundo'

/**
 * A miniatura do sólido. Um `<img src="/api/...">` não mandaria o token, então a imagem vem pelo
 * `apiFetch` e é exibida por URL de `blob:`. Sem versão (o Componente não tem miniatura) ou
 * enquanto carrega ou se a busca falha, mostra o espaço neutro, com o mesmo nome acessível.
 */
export function MiniaturaDoSolido({
  caminho,
  versao,
  alt,
  className = '',
}: {
  caminho: string
  versao: string | null
  alt: string
  className?: string
}) {
  const [resultado, setResultado] = useState<{ chave: string; url: string } | null>(null)
  const chave = versao === null ? null : `${caminho}\u0000${versao}`

  useEffect(() => {
    if (versao === null || chave === null) return
    let vivo = true
    void buscar(caminho, versao).then((url) => {
      if (vivo && url) setResultado({ chave, url })
    })
    return () => {
      vivo = false
    }
  }, [caminho, versao, chave])

  if (resultado && resultado.chave === chave) {
    return <img src={resultado.url} alt={alt} className={`${ESPACO} object-contain ${className}`} />
  }
  return <div role="img" aria-label={alt} className={`${ESPACO} ${className}`} />
}
