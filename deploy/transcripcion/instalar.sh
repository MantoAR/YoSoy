#!/usr/bin/env bash
set -euo pipefail
umask 077
export LC_ALL=C

SERVICIO="${YS_SERVICIO:-yosoy-api}"
DUENO_FORZADO="${YS_DUENO:-}"
GRUPO_FORZADO="${YS_GRUPO:-}"
SIN_ROOT="${YS_SIN_ROOT:-}"
RAIZ_WHISPER="${YS_RAIZ_WHISPER:-/opt/yosoy/whisper}"
CARPETA_AUDIO="${YS_CARPETA_AUDIO:-/opt/yosoy/grabaciones}"
CONFIG_DIR="${YS_CONFIG_DIR:-/opt/yosoy/config}"
MODELO="${YS_MODELO:-small}"
HILOS="${YS_HILOS:-1}"

VENV="$RAIZ_WHISPER/venv"
CARPETA_MODELOS="$RAIZ_WHISPER/modelos"
VIDEO_ENV="$CONFIG_DIR/video.env"

RAIZ_REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_ORIGEN="$RAIZ_REPO/transcripcion/transcribir.py"
REQUISITOS="$RAIZ_REPO/transcripcion/requirements.txt"
SCRIPT_DESTINO="$RAIZ_WHISPER/transcribir.py"
DROPIN_ORIGEN="$RAIZ_REPO/deploy/yosoy-api.service.d/transcripcion.conf"
DROPIN_DESTINO_DIR="/etc/systemd/system/$SERVICIO.service.d"

FALLOS=0
TEMPORALES=()
CAMBIOS=()
MODO_PRUEBA=0

fallar() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
ok()     { printf '  [ok] %s\n' "$*"; }
mal()    { printf '  [FALLÓ] %s\n' "$*"; FALLOS=$((FALLOS + 1)); }
aviso()  { printf '  [!] %s\n' "$*"; }
info()   { printf '  [i] %s\n' "$*"; }

limpiar() {
    local t
    for t in "${TEMPORALES[@]:-}"; do
        if [ -n "$t" ]; then rm -f "$t"; fi
    done
}
trap limpiar EXIT

if [ -z "$SIN_ROOT" ] && [ "$(id -u)" -ne 0 ]; then
    fallar "Correlo como root: sudo bash $0"
fi
if ! command -v python3 >/dev/null 2>&1; then fallar "Falta python3."; fi
if [ ! -f "$SCRIPT_ORIGEN" ]; then fallar "No está $SCRIPT_ORIGEN (¿rama equivocada?)."; fi
if [ ! -f "$REQUISITOS" ]; then fallar "No está $REQUISITOS."; fi

if [ -n "$DUENO_FORZADO" ]; then
    DUENO="$DUENO_FORZADO"
    GRUPO="${GRUPO_FORZADO:-$DUENO}"
    MODO_PRUEBA=1
else
    if ! command -v systemctl >/dev/null 2>&1; then
        fallar "No hay systemctl. Para probar sin systemd, definí YS_DUENO (y, si hace falta, YS_GRUPO)."
    fi
    if ! systemctl list-unit-files "$SERVICIO.service" --no-legend 2>/dev/null | grep -q .; then
        fallar "No existe el servicio $SERVICIO. Publicá la API antes de correr este script, o definí YS_SERVICIO/YS_DUENO."
    fi
    DUENO="$(systemctl show "$SERVICIO" -p User --value)"
    GRUPO="$(systemctl show "$SERVICIO" -p Group --value)"
    if [ -z "$DUENO" ]; then DUENO=root; fi
    if [ -z "$GRUPO" ]; then GRUPO="$DUENO"; fi
fi
if ! id -u "$DUENO" >/dev/null 2>&1; then fallar "No existe el usuario $DUENO."; fi

dar_permisos() {
    chmod "$2" "$1"
    if [ -z "$SIN_ROOT" ]; then chown "$3:$4" "$1"; fi
}

asegurar_directorio() {
    mkdir -p "$1"
    dar_permisos "$1" 700 "$DUENO" "$GRUPO"
}

valor_de() {
    local archivo=$1 clave=$2 linea valor=""
    if [ -f "$archivo" ]; then
        while IFS= read -r linea || [ -n "$linea" ]; do
            linea=${linea%$'\r'}
            if [[ $linea == "$clave="* ]]; then valor=${linea#*=}; fi
        done < "$archivo"
    fi
    printf '%s' "$valor"
}

temporal_de() {
    TMP="$(mktemp "$(dirname "$1")/.$(basename "$1").XXXXXX")"
    TEMPORALES+=("$TMP")
    dar_permisos "$TMP" "$2" "$3" "$4"
}

poner_clave() {
    local archivo=$1 clave=$2 valor=$3 linea hecho=0
    temporal_de "$archivo" "$4" "$5" "$6"
    {
        while IFS= read -r linea || [ -n "$linea" ]; do
            if [[ ${linea%$'\r'} == "$clave="* ]]; then
                if [ "$hecho" -eq 0 ]; then printf '%s=%s\n' "$clave" "$valor"; hecho=1; fi
                continue
            fi
            printf '%s\n' "$linea"
        done < "$archivo"
        if [ "$hecho" -eq 0 ]; then printf '%s=%s\n' "$clave" "$valor"; fi
    } > "$TMP"
    mv -f "$TMP" "$archivo"
}

agregar_si_falta_video() {
    local clave=$1 valor=$2
    if [ -z "$(valor_de "$VIDEO_ENV" "$clave")" ]; then
        poner_clave "$VIDEO_ENV" "$clave" "$valor" "$MODO_VIDEO" "$DUENO_VIDEO" "$GRUPO_VIDEO"
        CAMBIOS+=("video.env: $clave agregado")
    fi
}

asegurar_directorio "$RAIZ_WHISPER"
asegurar_directorio "$CARPETA_AUDIO"
asegurar_directorio "$CARPETA_MODELOS"

intentar_venv() {
    local salida
    salida="$(mktemp)"
    TEMPORALES+=("$salida")
    if python3 -m venv "$VENV" >"$salida" 2>&1; then return 0; fi
    if [ -z "$SIN_ROOT" ] && command -v apt-get >/dev/null 2>&1 && grep -qi ensurepip "$salida"; then
        apt-get update -y >/dev/null 2>&1 || true
        apt-get install -y python3.12-venv >/dev/null 2>&1 || true
        if python3 -m venv "$VENV" >"$salida" 2>&1; then return 0; fi
    fi
    cat "$salida" >&2
    return 1
}
if [ ! -x "$VENV/bin/python3" ]; then
    if ! intentar_venv; then fallar "No se pudo crear el entorno virtual en $VENV."; fi
    CAMBIOS+=("venv: creado en $VENV")
else
    intentar_venv || true
fi

"$VENV/bin/python3" -m pip install --quiet --disable-pip-version-check -r "$REQUISITOS"

cp "$SCRIPT_ORIGEN" "$SCRIPT_DESTINO.nuevo"
mv -f "$SCRIPT_DESTINO.nuevo" "$SCRIPT_DESTINO"
dar_permisos "$SCRIPT_DESTINO" 600 "$DUENO" "$GRUPO"

cargar_modelo() {
    "$VENV/bin/python3" - "$MODELO" "$CARPETA_MODELOS" <<'PY'
import sys
from faster_whisper import WhisperModel
modelo, carpeta = sys.argv[1], sys.argv[2]
WhisperModel(modelo, device="cpu", compute_type="int8", download_root=carpeta)
PY
}
info "Descargando/verificando el modelo \"$MODELO\" (puede tardar la primera vez)..."
if ! cargar_modelo; then fallar "No se pudo descargar/cargar el modelo \"$MODELO\"."; fi

if [ -z "$SIN_ROOT" ]; then
    chown -R "$DUENO:$GRUPO" "$RAIZ_WHISPER"
fi
dar_permisos "$RAIZ_WHISPER" 700 "$DUENO" "$GRUPO"
dar_permisos "$CARPETA_MODELOS" 700 "$DUENO" "$GRUPO"
dar_permisos "$CARPETA_AUDIO" 700 "$DUENO" "$GRUPO"

if [ ! -f "$VIDEO_ENV" ]; then
    fallar "No existe $VIDEO_ENV. Corré primero deploy/video/generar-secretos.sh (despliegue de video, pasos c/d)."
fi
read -r MODO_VIDEO DUENO_VIDEO GRUPO_VIDEO <<<"$(stat -c '%a %U %G' "$VIDEO_ENV")"

agregar_si_falta_video Video__Transcripcion__Habilitada true
agregar_si_falta_video Video__Transcripcion__RutaPython "$VENV/bin/python3"
agregar_si_falta_video Video__Transcripcion__RutaScript "$SCRIPT_DESTINO"
agregar_si_falta_video Video__Transcripcion__CarpetaAudio "$CARPETA_AUDIO"
agregar_si_falta_video Video__Transcripcion__CarpetaModelos "$CARPETA_MODELOS"
agregar_si_falta_video Video__Transcripcion__Modelo "$MODELO"
agregar_si_falta_video Video__Transcripcion__HilosCpu "$HILOS"
dar_permisos "$VIDEO_ENV" "$MODO_VIDEO" "$DUENO_VIDEO" "$GRUPO_VIDEO"

if [ "$MODO_PRUEBA" -eq 0 ]; then
    install -m 644 "$DROPIN_ORIGEN" "$DROPIN_DESTINO_DIR/transcripcion.conf"
    systemctl daemon-reload
    CAMBIOS+=("systemd: $DROPIN_DESTINO_DIR/transcripcion.conf instalado; daemon-reload hecho")
else
    aviso "Modo de prueba (YS_DUENO definido): no se tocó systemd. Instalá a mano $DROPIN_ORIGEN si corresponde."
fi

echo "YoSoy · instalación de la transcripción local"
echo
echo "Qué se hizo:"
if [ "${#CAMBIOS[@]}" -eq 0 ]; then
    echo "  sin cambios (ya estaba todo)"
else
    for c in "${CAMBIOS[@]}"; do printf '  %s\n' "$c"; done
fi

echo
echo "Verificación:"

if "$VENV/bin/python3" -c "import faster_whisper" >/dev/null 2>&1; then
    ok "El entorno virtual importa faster_whisper."
else
    mal "El entorno virtual no puede importar faster_whisper."
fi

if cargar_modelo >/dev/null 2>&1; then
    ok "El modelo \"$MODELO\" carga desde $CARPETA_MODELOS."
else
    mal "El modelo \"$MODELO\" no carga desde $CARPETA_MODELOS."
fi

puede_escribir() {
    local prueba="$1/.yosoy-prueba-escritura"
    if [ -z "$SIN_ROOT" ]; then
        runuser -u "$DUENO" -- touch "$prueba" 2>/dev/null && runuser -u "$DUENO" -- rm -f "$prueba"
    else
        touch "$prueba" 2>/dev/null && rm -f "$prueba"
    fi
}
if puede_escribir "$CARPETA_AUDIO"; then
    ok "$DUENO puede escribir en $CARPETA_AUDIO."
else
    mal "$DUENO no puede escribir en $CARPETA_AUDIO."
fi
if puede_escribir "$CARPETA_MODELOS"; then
    ok "$DUENO puede escribir en $CARPETA_MODELOS."
else
    mal "$DUENO no puede escribir en $CARPETA_MODELOS."
fi

V_HABILITADA="$(valor_de "$VIDEO_ENV" Video__Transcripcion__Habilitada)"
V_RUTAPYTHON="$(valor_de "$VIDEO_ENV" Video__Transcripcion__RutaPython)"
V_RUTASCRIPT="$(valor_de "$VIDEO_ENV" Video__Transcripcion__RutaScript)"
if [ "$V_HABILITADA" = "true" ]; then
    ok "video.env: Video__Transcripcion__Habilitada=true"
else
    mal "video.env: Video__Transcripcion__Habilitada=\"$V_HABILITADA\" (tiene que ser \"true\")"
fi
if [ -x "$V_RUTAPYTHON" ]; then
    ok "video.env: Video__Transcripcion__RutaPython apunta a un ejecutable ($V_RUTAPYTHON)."
else
    mal "video.env: Video__Transcripcion__RutaPython (\"$V_RUTAPYTHON\") no es un ejecutable."
fi
if [ -f "$V_RUTASCRIPT" ]; then
    ok "video.env: Video__Transcripcion__RutaScript existe ($V_RUTASCRIPT)."
else
    mal "video.env: Video__Transcripcion__RutaScript (\"$V_RUTASCRIPT\") no existe."
fi

echo
if [ "$FALLOS" -gt 0 ]; then
    echo "Hubo $FALLOS verificación(es) que fallaron: revisá lo de arriba antes de reiniciar $SERVICIO."
    exit 1
fi
echo "Listo. Reiniciá $SERVICIO (sudo systemctl restart $SERVICIO) y seguí con docs/DESPLIEGUE_TRANSCRIPCION.md."
