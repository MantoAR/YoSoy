import argparse
import json
import os
import sys

from faster_whisper import WhisperModel

PISTAS = ("profesional", "paciente")


def parsear_argumentos(argv):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--modelo", required=True)
    parser.add_argument("--hilos", required=True, type=int)
    parser.add_argument("--carpeta-modelos", required=True, dest="carpeta_modelos")
    parser.add_argument("--pista-profesional", dest="pista_profesional", default=None)
    parser.add_argument("--pista-paciente", dest="pista_paciente", default=None)
    return parser.parse_args(argv)


def pistas_a_transcribir(args):
    candidatas = {
        "profesional": args.pista_profesional,
        "paciente": args.pista_paciente,
    }
    return [(pista, ruta) for pista, ruta in candidatas.items() if ruta and os.path.isfile(ruta)]


def transcribir_pista(modelo, pista, ruta):
    segmentos, info = modelo.transcribe(ruta, language="es", beam_size=1, vad_filter=True)
    resultado = []
    for segmento in segmentos:
        texto = segmento.text.strip()
        if texto:
            resultado.append({"pista": pista, "inicio": segmento.start, "fin": segmento.end, "texto": texto})
    return resultado, info.duration


def main(argv):
    args = parsear_argumentos(argv)

    pistas = pistas_a_transcribir(args)
    if not pistas:
        print("ninguna de las pistas indicadas existe", file=sys.stderr)
        return 1

    modelo = WhisperModel(
        args.modelo,
        device="cpu",
        compute_type="int8",
        cpu_threads=args.hilos,
        download_root=args.carpeta_modelos,
    )

    segmentos = []
    duracion_audio_seg = 0.0
    for pista, ruta in pistas:
        segmentos_pista, duracion = transcribir_pista(modelo, pista, ruta)
        segmentos.extend(segmentos_pista)
        duracion_audio_seg = max(duracion_audio_seg, duracion)

    salida = {"duracionAudioSeg": int(round(duracion_audio_seg)), "segmentos": segmentos}
    print(json.dumps(salida, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except Exception as e:
        print(f"{type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
