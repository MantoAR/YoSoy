(function () {
    const TIMEOUT_MS   = 10 * 60 * 1000;
    const AVISO_MS     = 9 * 60 * 1000;
    const INDEX_URL    = (() => {
        const depth = (window.location.pathname.match(/\//g) || []).length - 1;
        return "../".repeat(depth - 1) + "index.html";
    })();

    let timerExpiracion;
    let timerAviso;

    function crearModal(id, html) {
        if (document.getElementById(id)) return;
        const div = document.createElement("div");
        div.id = id;
        div.innerHTML = html;
        document.body.appendChild(div);
    }

    function iniciarModales() {
        crearModal("sesion-aviso-overlay", `
            <div id="sesion-aviso-overlay" style="
                display:none;position:fixed;inset:0;background:rgba(0,0,0,.45);
                z-index:9998;justify-content:center;align-items:center;">
                <div style="background:#fff;border-radius:16px;padding:32px 28px;
                    max-width:360px;width:90%;text-align:center;box-shadow:0 8px 32px rgba(0,0,0,.2);">
                    <div style="font-size:2rem;margin-bottom:8px">⚠</div>
                    <p style="font-size:15px;color:#444;margin-bottom:20px">
                        Tu sesión expirará en <strong>1 minuto</strong> por inactividad.
                    </p>
                    <button onclick="document.getElementById('sesion-aviso-overlay').style.display='none'"
                        style="background:linear-gradient(to right,#0d6efd,#6A5ACD);color:#fff;border:none;
                        border-radius:10px;padding:10px 28px;font-size:14px;font-weight:bold;cursor:pointer">
                        Seguir conectado
                    </button>
                </div>
            </div>`);

        crearModal("sesion-exp-overlay", `
            <div id="sesion-exp-overlay" style="
                display:none;position:fixed;inset:0;background:rgba(0,0,0,.6);
                z-index:9999;justify-content:center;align-items:center;">
                <div style="background:#fff;border-radius:16px;padding:36px 28px;
                    max-width:360px;width:90%;text-align:center;box-shadow:0 8px 32px rgba(0,0,0,.25);">
                    <div style="font-size:2.2rem;margin-bottom:10px">🔒</div>
                    <p style="font-size:15px;color:#444;margin-bottom:24px">
                        Tu sesión expiró por inactividad.
                    </p>
                    <button onclick="expirarSesion()"
                        style="background:linear-gradient(to right,#0d6efd,#6A5ACD);color:#fff;border:none;
                        border-radius:10px;padding:10px 28px;font-size:14px;font-weight:bold;cursor:pointer">
                        Volver al inicio
                    </button>
                </div>
            </div>`);
    }

    function reiniciarTimer() {
        clearTimeout(timerExpiracion);
        clearTimeout(timerAviso);

        const avisoEl = document.getElementById("sesion-aviso-overlay");
        if (avisoEl) avisoEl.style.display = "none";

        timerAviso = setTimeout(() => {
            const el = document.getElementById("sesion-aviso-overlay");
            if (el) el.style.display = "flex";
        }, AVISO_MS);

        timerExpiracion = setTimeout(() => {
            const el = document.getElementById("sesion-aviso-overlay");
            if (el) el.style.display = "none";
            const exp = document.getElementById("sesion-exp-overlay");
            if (exp) exp.style.display = "flex";
        }, TIMEOUT_MS);
    }

    window.expirarSesion = function () {
        localStorage.clear();
        window.location.href = INDEX_URL;
    };

    document.addEventListener("DOMContentLoaded", () => {
        iniciarModales();
        reiniciarTimer();
        ["mousemove", "keydown", "click", "scroll", "touchstart"].forEach(ev =>
            document.addEventListener(ev, reiniciarTimer, { passive: true }));
    });
})();
