#!/bin/sh
# Inno-Setup-Compiler unter Wine; alle Argumente gehen an ISCC.exe.
# Xvfb wird bewusst selbst gestartet: xvfb-run haengt, wenn es als PID 1 im Container laeuft.
Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp >/dev/null 2>&1 &
xvfb=$!
export DISPLAY=:99
i=0
while [ $i -lt 100 ] && [ ! -e /tmp/.X11-unix/X99 ]; do sleep 0.1; i=$((i+1)); done

wine 'C:\InnoSetup\ISCC.exe' "$@"
rc=$?

wineserver -k >/dev/null 2>&1
kill "$xvfb" >/dev/null 2>&1
exit $rc
