import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { SetoresPage } from './pages/SetoresPage'
import { MateriaisPage } from './pages/MateriaisPage'
import { ComponentesPage } from './pages/ComponentesPage'
import { ComponenteDetalhePage } from './pages/ComponenteDetalhePage'
import { PedidosPage } from './pages/PedidosPage'
import { PedidoDetalhePage } from './pages/PedidoDetalhePage'
import { AgrupamentoDetalhePage } from './pages/AgrupamentoDetalhePage'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { AppShell } from './components/AppShell'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      {/*
        Rota de layout: o shell embrulha TODAS as telas internas de uma vez, em vez de cada tela
        repetir cabeçalho e caminho de volta. O `ProtectedRoute` sobe para cá junto — antes ele
        estava repetido em seis linhas, e uma tela nova podia nascer sem ele por esquecimento.
      */}
      <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
        <Route path="/" element={<HomePage />} />
        <Route path="/setores" element={<SetoresPage />} />
        <Route path="/materiais" element={<MateriaisPage />} />
        <Route path="/componentes" element={<ComponentesPage />} />
        <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
        <Route path="/pedidos" element={<PedidosPage />} />
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
        <Route path="/agrupamentos/:id" element={<AgrupamentoDetalhePage />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
