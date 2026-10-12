# Baut den Windows-Installer (Inno Setup) auf Linux/macOS: Inno Setup läuft im Container unter Wine.
#   installer/build.sh            -> dist/SchleimigeScreenshots-Setup-<Version>.exe
# Auf Windows reicht ein installiertes Inno Setup 6 (build.ps1 erkennt ISCC.exe automatisch).
FROM ubuntu:24.04

ARG INNO_VERSION=6.7.3
# SHA-256 des offiziellen Installers (identisch mit dem Digest des GitHub-Releases und der Hersteller-Signatur .issig)
ARG INNO_SHA256=9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732

SHELL ["/bin/bash", "-o", "pipefail", "-c"]
ENV DEBIAN_FRONTEND=noninteractive \
    WINEPREFIX=/opt/wine \
    WINEARCH=win64 \
    WINEDEBUG=-all \
    WINEDLLOVERRIDES="mscoree,mshtml=" \
    LANG=C.UTF-8

# Inno Setup 6 ist ein 32-Bit-Programm -> Wine mit i386-Unterstützung. xdotool/scrot nur für Setup-Tests im Container.
RUN dpkg --add-architecture i386 \
 && apt-get update \
 && apt-get install -y --no-install-recommends \
      wine wine64 wine32:i386 libwine libwine:i386 fonts-wine fonts-liberation \
      xvfb xauth xdotool scrot curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL -o /tmp/innosetup.exe \
      "https://github.com/jrsoftware/issrc/releases/download/is-${INNO_VERSION//./_}/innosetup-${INNO_VERSION}.exe" \
 && echo "${INNO_SHA256}  /tmp/innosetup.exe" | sha256sum -c - \
 && xvfb-run -a wine /tmp/innosetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- '/DIR=C:\InnoSetup' \
 && wineserver -w \
 && rm /tmp/innosetup.exe

COPY iscc.sh /usr/local/bin/iscc
RUN chmod +x /usr/local/bin/iscc
WORKDIR /work
ENTRYPOINT ["iscc"]
