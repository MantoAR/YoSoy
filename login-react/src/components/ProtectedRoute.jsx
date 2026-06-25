import { Navigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

/*
  ProtectedRoute: solo deja pasar si el usuario ESTÁ autenticado.
  Si no hay sesion activa, redirige al login ( / ).

  Uso:
    <Route path="/app" element={
      <ProtectedRoute>
        <MiComponenteProtegido />
      </ProtectedRoute>
    } />
*/
export default function ProtectedRoute({ children }) {
  const { user } = useAuth()

  if (!user) {
    return <Navigate to="/" replace />
  }

  return children
}
