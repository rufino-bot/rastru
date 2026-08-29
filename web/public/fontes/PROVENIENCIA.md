# De onde vieram estes arquivos

IBM Plex Sans e IBM Plex Mono, subset `latin`, extraídos dos pacotes npm do Fontsource em
2026-08-28. Os pacotes NÃO são dependência deste projeto — foram usados uma vez, como fonte dos
binários, e os arquivos vivem versionados aqui.

| Arquivo | Pacote | Versão |
|---|---|---|
| `ibm-plex-sans-latin-wght-normal.woff2` | `@fontsource-variable/ibm-plex-sans` | 5.3.0 |
| `ibm-plex-mono-latin-{400,600,700}-normal.woff2` | `@fontsource/ibm-plex-mono` | 5.3.0 |

**Não existe `@fontsource-variable/ibm-plex-mono`** — medido em 2026-08-28, o registro devolve
E404. É por isso que o Sans é um arquivo variável (100–700) e o Mono são três estáticos.

Os pesos do Mono são os três medidos em uso em `web/src/`: 400 (`font-mono`), 600
(`font-mono font-semibold`) e 700 (o único `<strong className="font-mono">`, em
`web/src/pages/PedidoDetalhePage.tsx`). Acrescentar peso de Mono sem um consumidor é baixar
arquivo que ninguém usa.

Para regenerar:

```bash
mkdir -p /tmp/plex && cd /tmp/plex && npm init -y && npm i @fontsource-variable/ibm-plex-sans@5.3.0 @fontsource/ibm-plex-mono@5.3.0
# e copiar os quatro .woff2 de node_modules/**/files/ para cá
```

## Licença

SIL Open Font License 1.1 — `LICENSE-IBM-Plex-Sans.txt` e `LICENSE-IBM-Plex-Mono.txt`, nesta pasta.

**Os dois `.txt` são obrigação da licença, não documentação.** A cláusula 2 da OFL permite
redistribuir a fonte junto de qualquer software *"provided that each copy contains the above
copyright notice and this license"*. Apagá-los para "limpar a pasta" viola a licença.

A cláusula 3 (Reserved Font Name) não se aplica: a IBM **não** reservou o nome — o cabeçalho de
copyright dos dois `LICENSE` não traz a fórmula `with Reserved Font Name` (medido: `grep -c`
devolve 0 nos dois). Por isso o `@font-face` pode declarar `font-family: 'IBM Plex Sans'`.
