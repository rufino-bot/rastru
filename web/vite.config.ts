import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

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
      // Front (5173) e API (5169) na mesma origem do ponto de vista do navegador. O cookie de
      // refresh e Secure, mas navegadores tratam localhost como contexto seguro mesmo em http,
      // entao o cookie e aceito em dev.
      //
      // UMA entrada, e nenhum tratamento especial. Antes eram cinco (/auth, /me, /setores,
      // /materiais, /pedidos, /agrupamentos) mais um `bypass` que discriminava navegacao de
      // documento por `Accept: text/html` — necessario porque aquelas rotas eram, ao mesmo tempo,
      // rotas do SPA: dar F5 em /pedidos/5 fazia o navegador pedir o DOCUMENTO a API, que
      // respondia 401 por navegacao nao carregar `Authorization: Bearer`. Com a API sob /api a
      // colisao deixa de existir na origem — nenhuma rota do SPA comeca com /api — e o contorno
      // sai junto. Ver `UsePathBase` em `src/Rastreamento.Api/Program.cs`.
      '/api': {
        target: 'http://localhost:5169',
        changeOrigin: true,
      },
    },
  },
})
