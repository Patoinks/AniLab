"""Gerar espadas, FBX e folha de quatro vistas, sem abrir a janela do Blender:
"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b --factory-startup --python Tools/Blender/run_swords.py
"""
from pathlib import Path
import sys
import traceback

SCRIPT = Path(__file__).resolve().with_name("swords_proto.py")
try:
    exec(compile(SCRIPT.read_text(encoding="utf-8"), str(SCRIPT), "exec"))
    build_swords()
    export_swords()
    render_preview(samples=48)
    save_swords()
except Exception:
    traceback.print_exc()
    sys.exit(1)
