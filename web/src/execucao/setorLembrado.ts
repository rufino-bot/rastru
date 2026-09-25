/**
 * O Setor que ESTE aparelho abriu por último na fila (spec §6.1). Conveniência por aparelho — o
 * celular pendurado na Dobra abre direto na fila da Dobra —, não vínculo do usuário ao Setor: o
 * mesmo operador, noutro aparelho, escolhe de novo.
 *
 * Todo acesso ao `localStorage` vai em `try/catch`: navegação privada, cota cheia ou política do
 * navegador fazem o acesso LANÇAR, e a fila tem de abrir mesmo assim — sem lembrança, só isso.
 */
const CHAVE = 'rastru.fila.setorId'

export function setorLembrado(): number | null {
  try {
    const bruto = localStorage.getItem(CHAVE)
    if (bruto === null) return null
    const id = Number(bruto)
    return Number.isInteger(id) && id > 0 ? id : null
  } catch {
    return null
  }
}

export function lembrarSetor(id: number): void {
  try {
    localStorage.setItem(CHAVE, String(id))
  } catch {
    // Sem armazenamento: o aparelho só não lembra.
  }
}

export function esquecerSetor(): void {
  try {
    localStorage.removeItem(CHAVE)
  } catch {
    // Idem.
  }
}
