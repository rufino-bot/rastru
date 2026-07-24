// Estado 'carregando' da sessao. Texto explicito de proposito: numa conexao lenta (wifi de
// chao de fabrica) uma tela em branco faz o usuario recarregar, e o reload no meio do
// init-refresh pode derrubar a sessao. Mostrar que algo acontece tira o incentivo de recarregar.
export function TelaCarregando() {
  return (
    <div className="min-h-screen flex flex-col items-center justify-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-gray-300 border-t-gray-700" />
      <p className="text-gray-600">Restaurando sessão…</p>
    </div>
  )
}
