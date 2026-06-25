import { useEffect } from 'react'
import { useAuth, DASHBOARD_URLS } from '../contexts/AuthContext'

const ROL_LABELS = {
  paciente:     'Paciente',
  profesional:  'Profesional',
  configurador: 'Configurador',
}

const ROL_ICONOS = {
  paciente:     '🧑',
  profesional:  '👨‍⚕️',
  configurador: '⚙️',
}

/*
  SesionActiva — ruta /app (protegida por ProtectedRoute)

  Demuestra:
  - Lectura del AuthContext con useAuth()
  - Redirección automatica al dashboard HTML correspondiente
  - Botón de logout que limpia el contexto y LocalStorage
*/
export default function SesionActiva() {
  const { user, logout } = useAuth()
  const dashboardUrl = user ? DASHBOARD_URLS[user.rol] : null

  // Redirige al dashboard real despues de 2 segundos
  useEffect(() => {
    if (!dashboardUrl) return
    const timer = setTimeout(() => {
      window.location.href = dashboardUrl
    }, 2000)
    return () => clearTimeout(timer)
  }, [dashboardUrl])

  function handleLogout() {
    logout()
    window.location.href = '/pantallas/index.html'
  }

  return (
    <div className="login-bg">
      <div className="card-login" style={{ textAlign: 'center' }}>
        <div style={{ fontSize: '48px', marginBottom: '8px' }}>
          {ROL_ICONOS[user?.rol] ?? '✅'}
        </div>
        <div className="titulo" style={{ fontSize: '28px' }}>Sesión activa</div>

        <div
          style={{
            background: 'linear-gradient(135deg, #f0eaff, #ddd0f5)',
            borderRadius: '12px',
            padding: '16px',
            margin: '20px 0',
          }}
        >
          <p style={{ margin: 0, fontSize: '14px', color: '#6A5ACD', fontWeight: 'bold' }}>
            ROL
          </p>
          <p style={{ margin: '4px 0 12px', fontSize: '18px', fontWeight: 'bold' }}>
            {ROL_LABELS[user?.rol] ?? user?.rol}
          </p>
          <p style={{ margin: 0, fontSize: '14px', color: '#6A5ACD', fontWeight: 'bold' }}>
            ID DE USUARIO
          </p>
          <p style={{ margin: '4px 0 0', fontSize: '18px', fontWeight: 'bold' }}>
            #{user?.idUsuario}
          </p>
          {user?.idPaciente && (
            <>
              <p style={{ margin: '12px 0 0', fontSize: '14px', color: '#6A5ACD', fontWeight: 'bold' }}>
                ID PACIENTE
              </p>
              <p style={{ margin: '4px 0 0', fontSize: '18px', fontWeight: 'bold' }}>
                #{user.idPaciente}
              </p>
            </>
          )}
          {user?.idProfesional && (
            <>
              <p style={{ margin: '12px 0 0', fontSize: '14px', color: '#6A5ACD', fontWeight: 'bold' }}>
                ID PROFESIONAL
              </p>
              <p style={{ margin: '4px 0 0', fontSize: '18px', fontWeight: 'bold' }}>
                #{user.idProfesional}
              </p>
            </>
          )}
        </div>

        <p style={{ fontSize: '13px', color: '#999', marginBottom: '16px' }}>
          Redirigiendo al panel en 2 segundos…
        </p>

        <a href={dashboardUrl} className="boton" style={{ display: 'block', textDecoration: 'none', lineHeight: '52px' }}>
          Ir al panel ahora
        </a>

        <button
          type="button"
          onClick={handleLogout}
          style={{
            marginTop: '12px',
            background: 'none',
            border: '2px solid #e0d4f5',
            borderRadius: '12px',
            width: '100%',
            height: '44px',
            fontSize: '14px',
            color: '#6A5ACD',
            cursor: 'pointer',
            fontWeight: 'bold',
          }}
        >
          Cerrar sesión
        </button>
      </div>
    </div>
  )
}
