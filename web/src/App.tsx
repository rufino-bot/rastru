import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { SetoresPage } from './pages/SetoresPage'
import { MateriaisPage } from './pages/MateriaisPage'
import { PedidosPage } from './pages/PedidosPage'
import { ProtectedRoute } from './auth/ProtectedRoute'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<ProtectedRoute><HomePage /></ProtectedRoute>} />
      <Route path="/setores" element={<ProtectedRoute><SetoresPage /></ProtectedRoute>} />
      <Route path="/materiais" element={<ProtectedRoute><MateriaisPage /></ProtectedRoute>} />
      <Route path="/pedidos" element={<ProtectedRoute><PedidosPage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
