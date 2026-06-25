import { Routes, Route, Navigate } from 'react-router-dom'
import LoginPage        from './pages/LoginPage'
import SesionActiva     from './pages/SesionActiva'
import GuestRoute       from './components/GuestRoute'
import ProtectedRoute   from './components/ProtectedRoute'

/*
  Rutas:
    /       -> GuestRoute -> LoginPage
              (si ya estás logueado, GuestRoute te manda al dashboard)

    /app    -> ProtectedRoute -> SesionActiva
              (si NO estás logueado, ProtectedRoute te manda al login)

    *       -> redirige a /
*/
export default function App() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <GuestRoute>
            <LoginPage />
          </GuestRoute>
        }
      />
      <Route
        path="/app"
        element={
          <ProtectedRoute>
            <SesionActiva />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
