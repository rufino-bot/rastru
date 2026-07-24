export interface UsuarioDto {
  id: number
  nomeUsuario: string
  nomeCompleto: string
  perfil: string
}

// accessTokenExpiraEm chega em ISO 8601 com offset -03:00; na Fase 0 nao alimenta logica
// nenhuma (o refresh e reativo ao 401, sem timer proativo). Guardado por fidelidade ao contrato.
export interface LoginResponse {
  accessToken: string
  accessTokenExpiraEm: string
  usuario: UsuarioDto
}
