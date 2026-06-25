import { useEffect } from 'react'
import { useAuth, DASHBOARD_URLS } from '../contexts/AuthContext'

/*
  GuestRoute: solo deja pasar si el usuario NO está autenticado.
  Si ya hay sesion activa, redirige al dashboard del rol correspondiente.

  Uso:
    <Route path="/" element={
      <GuestRoute>
        <LoginPage />
      </GuestRoute>
    } />
*/
export default function GuestRoute({ children }) {
  const { user } = useAuth()

  useEffect(() => {
    if (user) {
      const url = DASHBOARD_URLS[user.rol]
      if (url) window.location.href = url
    }
  }, [user])

  // Mientras redirige no renderiza nada para evitar un flash del login
  if (user) return null

  return children
}
