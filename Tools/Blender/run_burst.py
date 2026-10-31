"""Render em batch da explosão de gelo montada:
"C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe" -b --factory-startup --python Tools/Blender/run_burst.py
Gera Logs/cap/burst_compare_side.png, burst_compare_top.png e burst_threequarter_glow.png."""
exec(open(r"C:\Users\Martim\AnimLab\Tools\Blender\ice_hero.py", encoding="utf-8").read())
exec(open(r"C:\Users\Martim\AnimLab\Tools\Blender\ice_spike_one.py", encoding="utf-8").read())
exec(open(r"C:\Users\Martim\AnimLab\Tools\Blender\ice_burst_master.py", encoding="utf-8").read())
build_burst()
render_burst()
