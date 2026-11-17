/**
 * SHA-256 em hexa minúsculo (64 caracteres), o `solidoSha256` que o `POST /componentes/{id}/miniatura`
 * confere contra o sólido gravado. Devolve `null` sem `crypto.subtle`, que só existe em contexto
 * seguro (HTTPS ou localhost): quem chama decide o que fazer sem o hash (spec, seção 8).
 */
export async function sha256Hex(dados: ArrayBuffer): Promise<string | null> {
  const subtle = globalThis.crypto?.subtle
  if (!subtle) return null
  const resumo = new Uint8Array(await subtle.digest('SHA-256', dados))
  return Array.from(resumo, (b) => b.toString(16).padStart(2, '0')).join('')
}
