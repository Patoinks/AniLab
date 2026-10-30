"""Render em batch do pico único (sem a janela do Blender):
"C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe" -b --factory-startup --python Tools/Blender/run_spike_one.py
Gera Logs/cap/spike_one_compare.png (folha | 3 vistas) e Logs/cap/spike_close_compare.png (de perto)."""
exec(open(r"C:\Users\Martim\AnimLab\Tools\Blender\ice_hero.py", encoding="utf-8").read())
exec(open(r"C:\Users\Martim\AnimLab\Tools\Blender\ice_spike_one.py", encoding="utf-8").read())
build_one()
render_views(samples=32)
