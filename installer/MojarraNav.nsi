; Instalador de MojarraNav (NSIS, por usuario: no pide permisos de administrador)
; Compilar:  makensis -DVERSION=0.2.0 -DSRC=..\dist\app -DOUTFILE=..\dist\MojarraNav-Setup.exe installer\MojarraNav.nsi
; (scripts/compilar.sh y scripts/compilar.ps1 lo hacen todo)
Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define NOMBRE "MojarraNav"
!define EXE "MojarraNav.exe"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NOMBRE}"
!define CLIENTE "Software\Clients\StartMenuInternet\${NOMBRE}"

Name "${NOMBRE}"
Caption "Instalar ${NOMBRE}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${NOMBRE}"
BrandingText "Mojarrium"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NOMBRE}"
VIAddVersionKey "FileDescription" "Instalador de ${NOMBRE}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Mojarrium"
VIAddVersionKey "LegalCopyright" "Mojarrium"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!define MUI_ICON "..\build\icon.ico"
!define MUI_UNICON "..\build\icon.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Bienvenido a ${NOMBRE}"
!define MUI_WELCOMEPAGE_TEXT "El navegador web de Mojarrium, con Claude navegando contigo.$\r$\n$\r$\nSe instalará solo para tu usuario y no necesita permisos de administrador."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir ${NOMBRE}"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Elegirlo como navegador predeterminado"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION Predeterminado
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Spanish"

Function .onInit
  ; WebView2 (motor de Edge) viene con Windows 10/11 actualizados; si falta, se ofrece descargarlo
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ${If} $0 == ""
    ReadRegStr $0 HKCU "Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ${EndIf}
  ${If} $0 == ""
  ${OrIf} $0 == "0.0.0.0"
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "${NOMBRE} necesita el motor de Microsoft Edge (WebView2), que no está en este equipo.$\r$\n$\r$\n¿Abrir la página de descarga de Microsoft? (Después vuelve a ejecutar este instalador)" IDNO +2
      ExecShell "open" "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
    Abort
  ${EndIf}
FunctionEnd

Function Predeterminado
  ExecShell "open" "ms-settings:defaultapps?registeredAppUser=${NOMBRE}"
FunctionEnd

Section "Instalar"
  ; Si está abierto, se cierra para poder actualizarlo
  nsExec::Exec 'taskkill /IM "${EXE}" /F'
  Sleep 300
  ; Carpeta fija (sin elegir) para que desinstalar nunca borre otra cosa
  SetOutPath "$INSTDIR"
  File /r "${SRC}\*.*"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"

  CreateShortcut "$SMPROGRAMS\${NOMBRE}.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$DESKTOP\${NOMBRE}.lnk" "$INSTDIR\${EXE}"

  WriteRegStr HKCU "Software\${NOMBRE}" "InstallDir" "$INSTDIR"
  ; Aplicaciones instaladas (Configuración de Windows)
  WriteRegStr HKCU "${UNKEY}" "DisplayName" "${NOMBRE}"
  WriteRegStr HKCU "${UNKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNKEY}" "Publisher" "Mojarrium"
  WriteRegStr HKCU "${UNKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNKEY}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegDWORD HKCU "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNKEY}" "NoRepair" 1

  ; Registro como navegador: aparece en Configuración > Aplicaciones predeterminadas
  WriteRegStr HKCU "Software\Classes\${NOMBRE}HTML" "" "Documento HTML de ${NOMBRE}"
  WriteRegStr HKCU "Software\Classes\${NOMBRE}HTML\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "Software\Classes\${NOMBRE}HTML\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  WriteRegStr HKCU "Software\Classes\${NOMBRE}URL" "" "Dirección web de ${NOMBRE}"
  WriteRegStr HKCU "Software\Classes\${NOMBRE}URL" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\${NOMBRE}URL\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "Software\Classes\${NOMBRE}URL\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  WriteRegStr HKCU "${CLIENTE}" "" "${NOMBRE}"
  WriteRegStr HKCU "${CLIENTE}\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "${CLIENTE}\shell\open\command" "" '"$INSTDIR\${EXE}"'
  WriteRegStr HKCU "${CLIENTE}\Capabilities" "ApplicationName" "${NOMBRE}"
  WriteRegStr HKCU "${CLIENTE}\Capabilities" "ApplicationDescription" "El navegador web de Mojarrium, con Claude integrado"
  WriteRegStr HKCU "${CLIENTE}\Capabilities" "ApplicationIcon" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\FileAssociations" ".htm" "${NOMBRE}HTML"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\FileAssociations" ".html" "${NOMBRE}HTML"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\FileAssociations" ".pdf" "${NOMBRE}HTML"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\URLAssociations" "http" "${NOMBRE}URL"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\URLAssociations" "https" "${NOMBRE}URL"
  WriteRegStr HKCU "${CLIENTE}\Capabilities\StartMenu" "StartMenuInternet" "${NOMBRE}"
  WriteRegStr HKCU "Software\RegisteredApplications" "${NOMBRE}" "${CLIENTE}\Capabilities"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM "${EXE}" /F'
  Sleep 300
  Delete "$SMPROGRAMS\${NOMBRE}.lnk"
  Delete "$DESKTOP\${NOMBRE}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNKEY}"
  DeleteRegKey HKCU "Software\${NOMBRE}"
  DeleteRegKey HKCU "${CLIENTE}"
  DeleteRegKey HKCU "Software\Classes\${NOMBRE}HTML"
  DeleteRegKey HKCU "Software\Classes\${NOMBRE}URL"
  DeleteRegValue HKCU "Software\RegisteredApplications" "${NOMBRE}"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  ; Tus favoritos, historial y chat se conservan en Documentos\MojarraNav
SectionEnd
