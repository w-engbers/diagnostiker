# Startet den Diagnostiker nach dem Autologin als root auf der ersten Konsole.
# Bewusst keine Punktdatei (~/.profile): Sie geht beim Kopieren und Synchronisieren leicht verloren.
# Der Schutz DIAGNOSTIKER_LAEUFT verhindert, dass eine daraus gestartete Shell das Programm erneut startet.
if [ "$(id -u)" = 0 ] && [ "$(tty 2>/dev/null)" = /dev/tty1 ] && [ -z "${DIAGNOSTIKER_LAEUFT:-}" ]; then
    export DIAGNOSTIKER_LAEUFT=1
    exec /usr/local/bin/diagnostiker-start
fi
