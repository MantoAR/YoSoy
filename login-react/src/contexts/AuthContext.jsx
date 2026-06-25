import { createContext, useContext, useState, useCallback } from 'react'

// URLs de cada dashboard (deben coincidir con las rutas del backend)
export const DASHBOARD_URLS = {
  paciente:     '/pantallas/Pantalla_Paciente/dashboard.html',
  profesional:  '/pantallas/Pantalla_Profesional/nuevo_dashboard.html',
  configurador: '/pantallas/Pantalla_Configurador/dashboard.html',
}

const AuthContext = createContext(null)

// Lee la sesion guardada en localStorage al inicializar el contexto
function leerSesionGuardada() {
  const token = localStorage.getItem('token')
  const rol   = localStorage.getItem('rol')
  if (!token || !rol) return null

  // Verifica que el JWT no haya expirado antes de restaurar la sesion
  try {
    const partes = token.split('.')
    if (partes.length === 3) {
      const payload = JSON.parse(atob(partes[1].replace(/-/g, '+').replace(/_/g, '/')))
      if (payload.exp && payload.exp < Math.floor(Date.now() / 1000)) {
        // Token expirado: limpia localStorage
        ;['token', 'rol', 'idUsuario', 'idPaciente', 'idProfesional'].forEach(k =>
          localStorage.removeItem(k)
        )
        return null
      }
    }
  } catch {
    // Si el token no es parseable, lo ignoramos pero no bloqueamos
  }

  return {
    token,
    rol,
    idUsuario:     Number(localStorage.getItem('idUsuario')),
    idPaciente:    localStorage.getItem('idPaciente')    ? Number(localStorage.getItem('idPaciente'))    : null,
    idProfesional: localStorage.getItem('idProfesional') ? Number(localStorage.getItem('idProfesional')) : null,
  }
}

export function AuthProvider({ children }) {
  // Inicializa leyendo localStorage para que la sesion sobreviva refrescos de pagina
  const [user, setUser] = useState(leerSesionGuardada)

  // Guarda la sesion en el estado Y en localStorage
  const login = useCallback((data) => {
    localStorage.setItem('token',    data.token ?? '')
    localStorage.setItem('rol',      data.rol)
    localStorage.setItem('idUsuario', String(data.idUsuario))
    if (data.idPaciente)    localStorage.setItem('idPaciente',    String(data.idPaciente))
    if (data.idProfesional) localStorage.setItem('idProfesional', String(data.idProfesional))
    setUser(data)
  }, [])

  // Limpia el estado y localStorage
  const logout = useCallback(() => {
    ;['token', 'rol', 'idUsuario', 'idPaciente', 'idProfesional'].forEach(k =>
      localStorage.removeItem(k)
    )
    setUser(null)
  }, [])

  return (
    <AuthContext.Provider value={{ user, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

// Hook para consumir el contexto desde cualquier componente
export const useAuth = () => useContext(AuthContext)
