import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Proxy: front (localhost:5173) e API (localhost:5169) na mesma origem do ponto de vista
// do navegador. O cookie de refresh e Secure, mas navegadores tratam localhost como
// contexto seguro mesmo em http, entao o cookie e aceito em dev.
//
// COLISAO DE CAMINHO, e por que existe o `bypass` abaixo:
// as rotas do SPA (/setores, /materiais, /pedidos, /pedidos/:id) tem os MESMOS caminhos dos
// endpoints da API. Sem o bypass, dar F5 em /pedidos/5 faz o navegador pedir o DOCUMENTO
// nessa URL, o Vite proxia para a API, e a API responde 401 — porque navegacao de documento
// nao carrega `Authorization: Bearer`. O sintoma parece bug de autenticacao e NAO e: o
// AuthContext, o ProtectedRoute e o single-flight do client estao corretos. `/` funcionava
// justamente por nao estar nesta lista.
//
// O discriminador e o `Accept`: navegacao de documento manda `text/html,...`; o fetch do app
// manda `*/*` (nunca text/html). Entao HTML cai no index.html e o SPA faz o roteamento; XHR
// segue para a API.
//
// LIMITE: isto conserta o DEV SERVER. Em producao, se o SPA e a API forem servidos na mesma
// origem sob esses mesmos prefixos, a colisao volta — e la nao ha Vite para interceptar. A
// solucao estrutural e prefixar a API com /api. Decisao ainda nao tomada; ver o ledger.
const paraApi = {
  target: 'http://localhost:5169',
  changeOrigin: true,
  bypass(req: { headers: { accept?: string } }) {
    if (req.headers.accept?.includes('text/html')) return '/index.html'
  },
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Escuta nos DOIS stacks (IPv4 + IPv6). Sem isto o Vite liga so em [::1], e como `localhost`
    // resolve para os dois enderecos o navegador tenta 127.0.0.1 primeiro, nao acha ninguem e
    // espera o timeout de conexao (~2s) antes de cair para IPv6. Medido: a fase "Conectando" do
    // DevTools dava 2,08s enquanto "Esperando" dava 35ms — o servidor sempre esteve rapido.
    // So aparecia em requisicao que precisava de conexao NOVA (POST), nunca nas que reaproveitavam
    // uma conexao morna (GET), o que fazia o sintoma parecer "lentidao ao salvar".
    // O Kestrel da API ja liga nos dois (127.0.0.1:5169 E [::1]:5169) — por isso a API nunca sofreu.
    // Bonus alinhado ao projeto: com isto o dev server tambem responde na rede local, que e o que
    // permite testar num tablet Android de verdade (o alvo declarado em CLAUDE.md).
    host: true,
    proxy: {
      // `/auth` e `/me` NAO tem rota de SPA equivalente, mas levam o mesmo tratamento para o
      // dia em que tiverem (ex.: uma tela /me de perfil) — o bypass e inofensivo onde nao ha
      // colisao, ja que o app nunca pede esses caminhos com Accept: text/html.
      '/auth': paraApi,
      '/me': paraApi,
      '/setores': paraApi,
      '/materiais': paraApi,
      '/pedidos': paraApi,
      '/agrupamentos': paraApi,
    },
  },
})
