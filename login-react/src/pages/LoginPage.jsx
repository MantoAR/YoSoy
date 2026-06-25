import { useState, useRef, useEffect } from 'react'
import { useAuth, DASHBOARD_URLS } from '../contexts/AuthContext'
import { requerido } from '../utils/validaciones'

// El backend bloquea la cuenta despues de este numero de intentos fallidos
const MAX_INTENTOS_BACKEND = 3

const ROLES = [
  { id: 'paciente',     label: 'Paciente',     icono: '🧑' },
  { id: 'profesional',  label: 'Profesional',  icono: '👨‍⚕️' },
  { id: 'configurador', label: 'Configurador', icono: '⚙️' },
]

export default function LoginPage() {
  const { login } = useAuth()

  // ── Estado del formulario ───────────────────────────────────────────
  const [rol,          setRol]         = useState('paciente')
  const [username,     setUsername]    = useState('')
  const [password,     setPassword]    = useState('')
  const [mostrarPass,  setMostrarPass] = useState(false)

  // ── Validacion de campos ────────────────────────────────────────────
  const [errUsername, setErrUsername] = useState('')
  const [errPassword, setErrPassword] = useState('')

  // ── Estado de la solicitud ──────────────────────────────────────────
  const [loading,         setLoading]         = useState(false)
  const [error,           setError]           = useState('')
  const [shake,           setShake]           = useState(false)
  const [intentosFallidos, setIntentosFallidos] = useState(0)

  // ── Recuperacion de contraseña ──────────────────────────────────────
  const [mostrarRecup, setMostrarRecup] = useState(false)
  const [userRecup,    setUserRecup]    = useState('')
  const [loadingRecup, setLoadingRecup] = useState(false)
  const [msgRecup,     setMsgRecup]     = useState({ texto: '', ok: true })

  const usernameRef = useRef(null)

  // Auto-foco en el campo usuario al montar el componente
  useEffect(() => {
    usernameRef.current?.focus()
  }, [])

  // ── Validacion en tiempo real ───────────────────────────────────────
  function validarCampos() {
    const eUser = requerido(username, 'El usuario')
    const ePass = requerido(password, 'La contraseña')
    setErrUsername(eUser ?? '')
    setErrPassword(ePass ?? '')
    return !eUser && !ePass
  }

  function mostrarError(msg) {
    setError(msg)
    setShake(true)
    setTimeout(() => setShake(false), 500)
  }

  function cambiarRol(nuevoRol) {
    setRol(nuevoRol)
    setError('')
    setErrUsername('')
    setErrPassword('')
    setIntentosFallidos(0)
    setMostrarRecup(false)
  }

  // ── Submit del login ────────────────────────────────────────────────
  async function handleSubmit(e) {
    e.preventDefault()
    if (!validarCampos()) return

    setLoading(true)
    setError('')

    try {
      const res = await fetch('/login', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ username: username.trim(), password }),
      })

      // 423: cuenta bloqueada | 429: demasiados intentos por IP
      if (res.status === 423 || res.status === 429) {
        const data = await res.json().catch(() => ({}))
        mostrarError(data.mensaje ?? 'Demasiados intentos. Esperá unos minutos.')
        setIntentosFallidos(0)
        return
      }

      if (!res.ok) {
        // Incrementa contador local y calcula intentos restantes
        const nuevosIntentos = intentosFallidos + 1
        setIntentosFallidos(nuevosIntentos)
        const restantes = MAX_INTENTOS_BACKEND - nuevosIntentos
        if (restantes > 0) {
          mostrarError(
            `Usuario o contraseña incorrectos. Te ${restantes === 1 ? 'queda' : 'quedan'} ` +
            `${restantes} intento${restantes === 1 ? '' : 's'} antes del bloqueo.`
          )
        } else {
          mostrarError('Usuario o contraseña incorrectos.')
        }
        return
      }

      const data = await res.json()

      // El rol devuelto por el servidor debe coincidir con el tab seleccionado
      if (data.rol !== rol) {
        mostrarError('Rol incorrecto para este usuario.')
        return
      }

      // Guarda la sesion en el contexto (y en localStorage) y redirige
      login(data)
      window.location.href = DASHBOARD_URLS[data.rol]

    } catch {
      mostrarError('No se pudo conectar con el servidor.')
    } finally {
      setLoading(false)
    }
  }

  // ── Recuperacion de contraseña ──────────────────────────────────────
  async function handleRecuperacion() {
    const eRecup = requerido(userRecup, 'El usuario')
    if (eRecup) {
      setMsgRecup({ texto: eRecup, ok: false })
      return
    }
    setLoadingRecup(true)
    setMsgRecup({ texto: '', ok: true })
    try {
      const res  = await fetch('/recuperacion-password', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ username: userRecup.trim() }),
      })
      const data = await res.json().catch(() => ({}))

      if (res.ok)              setMsgRecup({ texto: data.mensaje ?? 'Te enviamos un link a tu mail.', ok: true })
      else if (res.status === 409) setMsgRecup({ texto: 'Ya tenés una solicitud pendiente.',              ok: false })
      else if (res.status === 404) setMsgRecup({ texto: 'Usuario no encontrado.',                        ok: false })
      else if (res.status === 400) setMsgRecup({ texto: data.mensaje ?? 'El usuario no tiene mail registrado.', ok: false })
      else                         setMsgRecup({ texto: 'Error al enviar el mail. Intentá más tarde.',   ok: false })
    } catch {
      setMsgRecup({ texto: 'No se pudo conectar con el servidor.', ok: false })
    } finally {
      setLoadingRecup(false)
    }
  }

  const esUserFinal = rol === 'paciente' || rol === 'profesional'

  return (
    <div className="login-bg">
      <div className={`card-login${shake ? ' shake' : ''}`}>

        {/* Logo */}
        <img
          src="/pantallas/Pantalla_Usuario/Usuario/img/logo_SINFONDO.png"
          alt="Logo YoSoy"
          className="logo"
          onError={e => { e.target.style.display = 'none' }}
        />
        <div className="titulo">YoSoy</div>
        <div className="subtitulo">Telemedicina en Salud Mental</div>

        {/* Tabs de rol */}
        <div className="roles">
          {ROLES.map(r => (
            <button
              key={r.id}
              type="button"
              className={`rol-btn${rol === r.id ? ' activo' : ''}`}
              onClick={() => cambiarRol(r.id)}
            >
              <span className="icono">{r.icono}</span>
              {r.label}
            </button>
          ))}
        </div>

        {/* Formulario */}
        <form onSubmit={handleSubmit} noValidate>

          {/* Campo usuario */}
          <div className="campo">
            <label>Usuario</label>
            <input
              ref={usernameRef}
              type="text"
              className={`input${errUsername ? ' input-error' : ''}`}
              placeholder="Ingrese su usuario"
              value={username}
              autoComplete="username"
              onChange={e => { setUsername(e.target.value); if (errUsername) setErrUsername('') }}
              onBlur={() => setErrUsername(requerido(username, 'El usuario') ?? '')}
            />
            {errUsername && <span className="field-error">{errUsername}</span>}
          </div>

          {/* Campo contraseña */}
          <div className="campo">
            <label>Contraseña</label>
            <div style={{ position: 'relative' }}>
              <input
                type={mostrarPass ? 'text' : 'password'}
                className={`input${errPassword ? ' input-error' : ''}`}
                placeholder="Ingrese su contraseña"
                value={password}
                autoComplete="current-password"
                style={{ paddingRight: '44px' }}
                onChange={e => { setPassword(e.target.value); if (errPassword) setErrPassword('') }}
                onBlur={() => setErrPassword(requerido(password, 'La contraseña') ?? '')}
              />
              <span
                className="ojo"
                onClick={() => setMostrarPass(v => !v)}
                title={mostrarPass ? 'Ocultar contraseña' : 'Mostrar contraseña'}
              >
                {mostrarPass ? '🙈' : '👁️'}
              </span>
            </div>
            {errPassword && <span className="field-error">{errPassword}</span>}
          </div>

          {/* Mensaje de error global */}
          {error && <div className="error-msg">{error}</div>}

          {/* Botón submit con spinner */}
          <button type="submit" className="boton" disabled={loading}>
            {loading ? <span className="spinner-btn" /> : 'Ingresar'}
          </button>
        </form>

        {/* Links de registro */}
        {rol === 'paciente' && (
          <span className="link-registro">
            ¿No tenés cuenta?{' '}
            <a href="/pantallas/Pantalla_Usuario/Usuario/consentimiento crear_cuenta.html">
              Registrate
            </a>
          </span>
        )}
        {rol === 'profesional' && (
          <span className="link-registro">
            ¿Sos profesional?{' '}
            <a href="/pantallas/Pantalla_Usuario/Usuario/registro_profesional.html">
              Registrate acá
            </a>
          </span>
        )}

        {/* Recuperación de contraseña */}
        {esUserFinal && (
          <div className="seccion-recuperacion">
            <span className="link-registro">
              <a
                href="#"
                onClick={e => {
                  e.preventDefault()
                  setMostrarRecup(v => !v)
                  setMsgRecup({ texto: '', ok: true })
                }}
              >
                ¿Olvidé mi contraseña?
              </a>
            </span>

            {mostrarRecup && (
              <div className="form-recuperacion">
                <div className="campo" style={{ marginTop: '12px' }}>
                  <label>Usuario</label>
                  <input
                    type="text"
                    className="input"
                    placeholder="Tu nombre de usuario"
                    value={userRecup}
                    onChange={e => setUserRecup(e.target.value)}
                  />
                </div>
                <button
                  type="button"
                  className="boton"
                  style={{ height: '42px', fontSize: '15px' }}
                  onClick={handleRecuperacion}
                  disabled={loadingRecup}
                >
                  {loadingRecup
                    ? <span className="spinner-btn" />
                    : 'Enviar link de recuperación'
                  }
                </button>
                {msgRecup.texto && (
                  <div
                    className="recup-msg"
                    style={{ color: msgRecup.ok ? '#27ae60' : '#e74c3c' }}
                  >
                    {msgRecup.texto}
                  </div>
                )}
              </div>
            )}
          </div>
        )}

      </div>
    </div>
  )
}
