import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from './AuthContext'
import { TelaCarregando } from '../components/TelaCarregando'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { estado } = useAuth()
  // 'carregando' ANTES de 'anonimo' e o que impede o flash de login no F5: enquanto o
  // init-refresh nao volta, mostramos o spinner em vez de redirecionar pro login.
  if (estado.status === 'carregando') return <TelaCarregando />
  if (estado.status === 'anonimo') return <Navigate to="/login" replace />
  return <>{children}</>
}
